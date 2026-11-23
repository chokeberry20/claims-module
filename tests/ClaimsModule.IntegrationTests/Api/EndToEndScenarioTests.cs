using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using ClaimsModule.IntegrationTests.Persistence;

namespace ClaimsModule.IntegrationTests.Api;

/// <summary>
/// Task 34: the brief's demo flow over HTTP against the real host, with a real Hangfire server on the test
/// database: sign-in → FNOL → reserve 50k by handler1 → handler1 can't approve (403) → supervisor
/// approves → the GL job posts it → the audit log shows GL_POSTING_SIMULATED → upload and download a
/// document → close checklist → close → reopen.
/// </summary>
[Collection(SqlServerTestGroup.Name)]
public sealed class EndToEndScenarioTests(SqlServerDatabaseFixture db)
{
    private static readonly TimeSpan JobBound = TimeSpan.FromSeconds(60);

    [Fact(Timeout = 180_000)]
    public async Task Fnol_reserve_approval_gl_posting_document_close_and_reopen()
    {
        await using var factory = new ClaimsApiFactory(db, new Dictionary<string, string?>
        {
            ["Hangfire:ServerEnabled"] = "true",
            ["Hangfire:QueuePollInterval"] = "00:00:00.200",
            ["Hangfire:WorkerCount"] = "1",
        });
        using var handler = await factory.SignedInClientAsync("handler1");
        using var supervisor = await factory.SignedInClientAsync("supervisor1");

        // 1. FNOL: a complete claim on an in-force policy is created as Draft with no Critical issues.
        var (policyId, cause, effective) = await handler.ReferenceAsync();
        var created = await (await handler.PostJsonAsync("/api/claims", ApiTestClient.FnolBody(policyId, cause, effective), Guid.NewGuid().ToString()))
            .JsonAsync(HttpStatusCode.Created);
        var claimId = created.GetProperty("claimId").GetGuid();
        var claimUrl = $"/api/claims/{claimId}";
        created.GetProperty("claimNumber").GetString().ShouldStartWith($"CLM-{DateTime.UtcNow.Year}-");
        created.GetProperty("status").GetString().ShouldBe("Draft");
        created.GetProperty("validationIssues").EnumerateArray().ShouldNotContain(i => i.GetProperty("severity").GetString() == "Critical");

        // 2. Draft → Open (the handler is assigned at intake).
        var open = await (await handler.PutJsonAsync($"{claimUrl}/status", new { targetStatus = "Open", acknowledgeWarnings = true })).JsonAsync(HttpStatusCode.OK);
        open.GetProperty("status").GetString().ShouldBe("Open");

        // 3. A 50,000 Indemnity reserve needs a supervisor (BR-R-03).
        var submitted = await (await handler.PostJsonAsync($"{claimUrl}/reserves",
                new { component = "Indemnity", transactionType = "Add", amount = 50_000m, changeReason = "Initial estimate from the repair quote" }))
            .JsonAsync(HttpStatusCode.Created);
        var transactionId = submitted.GetProperty("transactionId").GetGuid();
        submitted.GetProperty("approvalStatus").GetString().ShouldBe("PendingApproval");
        submitted.GetProperty("requiredApprovalLevel").GetString().ShouldBe("Supervisor");

        // 4. The handler can't approve: 403 with the S10.4 body.
        var denied = await handler.PostJsonAsync($"{claimUrl}/reserves/{transactionId}/approve", new { });
        await denied.ErrorAsync(HttpStatusCode.Forbidden, "Forbidden");

        // 5. The supervisor approves; the GL posting job is enqueued after commit and posts it.
        var approved = await (await supervisor.PostJsonAsync($"{claimUrl}/reserves/{transactionId}/approve", new { })).JsonAsync(HttpStatusCode.OK);
        approved.GetProperty("approvalStatus").GetString().ShouldBe("Approved");
        approved.GetProperty("componentBalance").GetDecimal().ShouldBe(50_000m);

        var posted = await WaitForPostingAsync(handler, claimUrl, transactionId);
        posted.GetProperty("postingStatus").GetString().ShouldBe("Posted");
        posted.GetProperty("postingJobId").GetString().ShouldNotBeNullOrWhiteSpace();
        posted.GetProperty("idempotencyKey").GetString().ShouldStartWith("Reserve:");

        // 6. The audit log shows the simulated GL posting (DR/CR journal), newest first.
        var audit = await (await handler.GetAsync($"{claimUrl}/audit?eventType=GL_POSTING_SIMULATED")).JsonAsync(HttpStatusCode.OK);
        var glEntry = audit.GetProperty("items").EnumerateArray().Single();
        glEntry.GetProperty("relatedEntityId").GetGuid().ShouldBe(transactionId);

        // 7. Upload a document and download it through its link (local storage in tests: /uploads/...).
        const string pdf = "%PDF-1.4\n% claims module test document\n";
        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(Encoding.ASCII.GetBytes(pdf));
        file.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        form.Add(file, "file", "police report.pdf");
        form.Add(new StringContent("Police report"), "documentType");
        var document = await (await handler.PostAsync($"{claimUrl}/documents", form)).JsonAsync(HttpStatusCode.Created);
        document.GetProperty("documentName").GetString().ShouldBe("police report.pdf");
        var download = await handler.GetAsync(document.GetProperty("downloadUrl").GetString());
        (await download.Content.ReadAsStringAsync()).ShouldBe(pdf);

        var detail = await (await handler.GetAsync(claimUrl)).JsonAsync(HttpStatusCode.OK);
        detail.GetProperty("documents").GetArrayLength().ShouldBe(1);
        detail.GetProperty("recentAuditEntries").GetArrayLength().ShouldBeGreaterThan(0);
        detail.GetProperty("reserves").GetProperty("approvedGross").GetDecimal().ShouldBe(50_000m);

        // 8. Close checklist: Closed is reachable but asks to confirm the open reserve balance (CC-04).
        var options = await (await handler.GetAsync($"{claimUrl}/transitions")).JsonAsync(HttpStatusCode.OK);
        var close = options.EnumerateArray().Single(o => o.GetProperty("targetStatus").GetString() == "Closed");
        close.GetProperty("blockingConditions").GetArrayLength().ShouldBe(0);
        close.GetProperty("confirmationsRequired").GetArrayLength().ShouldBeGreaterThan(0);

        var unconfirmed = await handler.PutJsonAsync($"{claimUrl}/status", new { targetStatus = "Closed" });
        await unconfirmed.ErrorAsync(HttpStatusCode.UnprocessableEntity, "ValidationError");

        var closed = await (await handler.PutJsonAsync($"{claimUrl}/status",
                new { targetStatus = "Closed", closureJustification = "Repair paid by the insured's garage; reserve kept for the subrogation review." }))
            .JsonAsync(HttpStatusCode.OK);
        closed.GetProperty("status").GetString().ShouldBe("Closed");

        // 9. Reopen needs a supervisor and a reason (BR-ST-04); the claim goes straight back to Open.
        var handlerReopen = await handler.PutJsonAsync($"{claimUrl}/status", new { targetStatus = "Reopened", reason = "New invoice arrived" });
        handlerReopen.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        var reopened = await (await supervisor.PutJsonAsync($"{claimUrl}/status", new { targetStatus = "Reopened", reason = "New invoice arrived" }))
            .JsonAsync(HttpStatusCode.OK);
        reopened.GetProperty("status").GetString().ShouldBe("Open");

        var events = (await (await handler.GetAsync($"{claimUrl}/audit?pageSize=200")).JsonAsync(HttpStatusCode.OK))
            .GetProperty("items").EnumerateArray().Select(e => e.GetProperty("eventType").GetString()).ToList();
        events.ShouldContain("CLAIM_CREATED");
        events.ShouldContain("RESERVE_APPROVED");
        events.ShouldContain("DOCUMENT_UPLOADED");
        events.ShouldContain("CLAIM_CLOSED");
        events.ShouldContain("CLAIM_REOPENED");
    }

    private static async Task<JsonElement> WaitForPostingAsync(HttpClient client, string claimUrl, Guid transactionId)
    {
        var deadline = DateTime.UtcNow + JobBound;
        while (true)
        {
            var reserves = await (await client.GetAsync($"{claimUrl}/reserves")).JsonAsync(HttpStatusCode.OK);
            var transaction = reserves.GetProperty("history").EnumerateArray().Single(t => t.GetProperty("transactionId").GetGuid() == transactionId);
            if (transaction.GetProperty("postingStatus").GetString() != "Pending" || DateTime.UtcNow > deadline)
            {
                return transaction;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(250));
        }
    }
}
