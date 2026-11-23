using System.Text.Json;
using ClaimsModule.Application.Abstractions.Audit;
using ClaimsModule.Application.Abstractions.Persistence;
using ClaimsModule.Application.Audit;
using ClaimsModule.Application.Claims;
using ClaimsModule.Application.Common.Exceptions;
using ClaimsModule.Application.Reserves;
using ClaimsModule.Domain.Claims;
using ClaimsModule.Domain.Claims.Validation;
using ClaimsModule.IntegrationTests.Persistence;

namespace ClaimsModule.IntegrationTests.Features;

/// <summary>
/// Task 28: each command writes exactly the expected audit event types (S14.1), in the same
/// transaction, with actor and correlation id. Also the task 26 check: one GL_POSTING_SIMULATED per
/// transaction, even when the job runs twice or concurrently.
/// </summary>
[Collection(SqlServerTestGroup.Name)]
[System.Diagnostics.CodeAnalysis.SuppressMessage("Reliability", "CA1001", Justification = "_host is disposed in IAsyncLifetime.DisposeAsync, which xUnit calls after every test.")]
public sealed class AuditTrailTests(SqlServerDatabaseFixture db) : IAsyncLifetime
{
    private ApplicationHost _host = null!;

    public Task InitializeAsync()
    {
        _host = new ApplicationHost(db);
        return Task.CompletedTask;
    }

    public async Task DisposeAsync() => await _host.DisposeAsync();

    private DateTimeOffset Now => _host.Clock.Now;

    private async Task<IReadOnlyList<AuditLogEntryDto>> AuditAsync(Guid claimId) =>
        (await _host.SendAsync(new GetAuditLogQuery(claimId) { PageSize = 200 })).Items;

    /// <summary>The event types a command adds to the claim's audit log, sorted.</summary>
    private async Task<string[]> AddedBy(Guid claimId, Func<Task> command)
    {
        var before = (await AuditAsync(claimId)).Select(a => a.AuditLogId).ToHashSet();
        await command();
        return (await AuditAsync(claimId)).Where(a => !before.Contains(a.AuditLogId)).Select(a => a.EventType).Order().ToArray();
    }

    private static string[] Sorted(params string[] types) => types.Order().ToArray();

    [Fact]
    public async Task Fnol_writes_creation_party_risk_object_and_reserve_entries()
    {
        var created = await _host.SendAsync(Fnol.WithReserve(Now, 5_000m));

        (await AuditAsync(created.ClaimId)).Select(a => a.EventType).Order().ShouldBe(Sorted(
            AuditEventTypes.ClaimCreated, AuditEventTypes.PartyAdded, AuditEventTypes.ClaimUpdated,
            AuditEventTypes.ReserveCreated, AuditEventTypes.ReserveAutoApproved));
    }

    [Fact]
    public async Task Fnol_with_findings_writes_one_validation_entry_per_issue()
    {
        var created = await _host.SendAsync(Fnol.Complete(Now) with { PolicyId = null, Parties = [], RiskObjects = [] });

        var types = (await AuditAsync(created.ClaimId)).Select(a => a.EventType).ToList();
        types.Count(t => t == AuditEventTypes.ValidationIssueAdded).ShouldBe(3);
        types.ShouldContain(AuditEventTypes.ClaimCreated);
    }

    [Fact]
    public async Task Lifecycle_commands_write_their_entries()
    {
        var claimId = (await _host.SendAsync(Fnol.WithReserve(Now, 5_000m) with { RiskObjects = [] })).ClaimId;

        (await AddedBy(claimId, () => _host.SendAsync(new TransitionClaimStatusCommand(claimId, ClaimStatus.Open) { AcknowledgeWarnings = true })))
            .ShouldBe(Sorted(AuditEventTypes.WarningsAcknowledged, AuditEventTypes.StatusChanged));
        (await AddedBy(claimId, () => _host.SendAsync(new TransitionClaimStatusCommand(claimId, ClaimStatus.Closed) { ClosureJustification = "Held for recovery" })))
            .ShouldBe(Sorted(AuditEventTypes.StatusChanged, AuditEventTypes.ClaimClosed));
        // D-12: Closed → Reopened → Open is two STATUS_CHANGED entries plus CLAIM_REOPENED.
        (await AddedBy(claimId, () => _host.SendAsync(new TransitionClaimStatusCommand(claimId, ClaimStatus.Reopened) { Reason = "Late invoice" }, ApplicationHost.Supervisor1)))
            .ShouldBe(Sorted(AuditEventTypes.StatusChanged, AuditEventTypes.StatusChanged, AuditEventTypes.ClaimReopened));
    }

    [Fact]
    public async Task Maintenance_and_party_commands_write_their_entries()
    {
        var claimId = (await _host.SendAsync(Fnol.Complete(Now) with { PolicyId = null })).ClaimId;

        (await AddedBy(claimId, () => _host.SendAsync(new LinkPolicyCommand(claimId, Fnol.MeridianPolicyId)))).ShouldBe([AuditEventTypes.PolicyLinked]);
        (await AddedBy(claimId, () => _host.SendAsync(new AssignHandlerCommand(claimId, ApplicationHost.Handler2.UserId), ApplicationHost.Supervisor1)))
            .ShouldBe([AuditEventTypes.HandlerAssigned]);
        (await AddedBy(claimId, () => _host.SendAsync(new UpdateNotesCommand(claimId, "Called back")))).ShouldBe([AuditEventTypes.ClaimUpdated]);
        (await AddedBy(claimId, () => _host.SendAsync(new SetReserveLimitOverrideCommand(claimId, true), ApplicationHost.Manager1)))
            .ShouldBe([AuditEventTypes.ReserveLimitOverrideSet]);

        var witness = await _host.SendAsync(new AddPartyCommand(claimId, new PartyInput(Domain.Claims.PartyRole.Witness, Domain.Claims.PartyType.Person, "Walt", "Witness")));
        (await AddedBy(claimId, () => _host.SendAsync(new RemovePartyCommand(claimId, witness.ChildId)))).ShouldBe([AuditEventTypes.PartyRemoved]);
    }

    [Fact]
    public async Task Waiving_a_critical_issue_writes_a_waiver_entry()
    {
        var claimId = (await _host.SendAsync(Fnol.Complete(Now) with { CauseOfLossCode = "COL-WIND" })).ClaimId;
        await db.ExecuteAsync("UPDATE CauseOfLossCodes SET IsActive = 0 WHERE Code = 'COL-WIND'");
        try
        {
            (await AddedBy(claimId, () => _host.SendAsync(new ValidateClaimCommand(claimId)))).ShouldBe([AuditEventTypes.ValidationIssueAdded]);
            var issue = (await _host.SendAsync(new GetClaimQuery(claimId))).ValidationIssues.Single(i => i.Code == ValidationIssueCodes.CauseOfLossInactive);

            (await AddedBy(claimId, () => _host.SendAsync(new WaiveValidationIssueCommand(claimId, issue.IssueId, "Accepted by underwriting"), ApplicationHost.Supervisor1)))
                .ShouldBe([AuditEventTypes.ValidationIssueWaived]);
        }
        finally
        {
            await db.ExecuteAsync("UPDATE CauseOfLossCodes SET IsActive = 1 WHERE Code = 'COL-WIND'");
        }
    }

    [Fact]
    public async Task Reserve_approval_workflow_writes_its_entries_with_the_rejection_reason()
    {
        var claimId = (await _host.SendAsync(Fnol.Complete(Now))).ClaimId;
        ReserveSubmissionDto pending = null!;

        (await AddedBy(claimId, async () => pending = await _host.SendAsync(new SubmitReserveTransactionCommand(
            claimId, Domain.Reserves.ReserveComponentType.Indemnity, Domain.Reserves.ReserveTransactionType.Add, 50_000m, "Estimate"))))
            .ShouldBe([AuditEventTypes.ReserveCreated]);
        (await AddedBy(claimId, () => _host.SendAsync(new RejectReserveTransactionCommand(claimId, pending.TransactionId, "No photos"), ApplicationHost.Supervisor1)))
            .ShouldBe([AuditEventTypes.ReserveRejected]);
        (await AuditAsync(claimId)).First(a => a.EventType == AuditEventTypes.ReserveRejected).OldValue.ShouldNotBeNull().ShouldContain("No photos");

        var second = await _host.SendAsync(new SubmitReserveTransactionCommand(
            claimId, Domain.Reserves.ReserveComponentType.Indemnity, Domain.Reserves.ReserveTransactionType.Add, 40_000m, "Revised"));
        (await AddedBy(claimId, () => _host.SendAsync(new RetractReserveTransactionCommand(claimId, second.TransactionId)))).ShouldBe([AuditEventTypes.ReserveRetracted]);

        var third = await _host.SendAsync(new SubmitReserveTransactionCommand(
            claimId, Domain.Reserves.ReserveComponentType.Indemnity, Domain.Reserves.ReserveTransactionType.Add, 30_000m, "Final"));
        (await AddedBy(claimId, () => _host.SendAsync(new ApproveReserveTransactionCommand(claimId, third.TransactionId), ApplicationHost.Supervisor1)))
            .ShouldBe([AuditEventTypes.ReserveApproved]);
    }

    [Fact]
    public async Task Gl_posting_is_audited_exactly_once_even_when_the_job_runs_twice_or_concurrently()
    {
        var sequential = await _host.SendAsync(Fnol.WithReserve(Now, 5_000m));
        await _host.SendAsync(new SimulateGlPostingCommand(sequential.InitialReserve!.TransactionId, "job-1"));
        await _host.SendAsync(new SimulateGlPostingCommand(sequential.InitialReserve.TransactionId, "job-2"));
        (await AuditAsync(sequential.ClaimId)).Count(a => a.EventType == AuditEventTypes.GlPostingSimulated).ShouldBe(1);

        var concurrent = await _host.SendAsync(Fnol.WithReserve(Now, 5_000m));
        await Task.WhenAll(Enumerable.Range(1, 3).Select(async i =>
        {
            try
            {
                await _host.SendAsync(new SimulateGlPostingCommand(concurrent.InitialReserve!.TransactionId, $"job-{i}"));
            }
            catch (ConcurrencyException)
            {
                // the losing writer; Hangfire would retry and find it Posted
            }
        }));
        (await AuditAsync(concurrent.ClaimId)).Count(a => a.EventType == AuditEventTypes.GlPostingSimulated).ShouldBe(1);
    }

    [Fact]
    public async Task Gl_journal_is_balanced_and_failure_and_retry_are_audited()
    {
        var created = await _host.SendAsync(Fnol.WithReserve(Now, 5_000m));
        var txn = created.InitialReserve!.TransactionId;

        (await AddedBy(created.ClaimId, () => _host.SendAsync(new MarkGlPostingFailedCommand(txn, "GL timeout")))).ShouldBe([AuditEventTypes.GlPostingFailed]);
        (await AddedBy(created.ClaimId, () => _host.SendAsync(new RetryGlPostingCommand(created.ClaimId, txn), ApplicationHost.Supervisor1)))
            .ShouldBe([AuditEventTypes.GlPostingRetryRequested]);
        (await AddedBy(created.ClaimId, () => _host.SendAsync(new SimulateGlPostingCommand(txn, "job-9")))).ShouldBe([AuditEventTypes.GlPostingSimulated]);

        var posted = (await AuditAsync(created.ClaimId)).First(a => a.EventType == AuditEventTypes.GlPostingSimulated);
        using var json = JsonDocument.Parse(posted.NewValue!);
        var journal = json.RootElement.GetProperty("journal").EnumerateArray().ToList();
        journal.Sum(l => l.GetProperty("debit").GetDecimal()).ShouldBe(5_000m);
        journal.Sum(l => l.GetProperty("credit").GetDecimal()).ShouldBe(5_000m);
        json.RootElement.GetProperty("idempotencyKey").GetString().ShouldStartWith("Reserve:");
    }

    [Fact]
    public async Task Sla_breach_is_audited()
    {
        var created = await _host.SendAsync(Fnol.Complete(Now));
        _host.Clock.Now += TimeSpan.FromHours(49);

        (await AddedBy(created.ClaimId, () => _host.SendAsync(new RunSlaScanCommand()))).ShouldBe([AuditEventTypes.SlaBreachDetected]);
    }

    [Fact]
    public async Task Entries_carry_the_actor_the_correlation_id_and_utc_time_newest_first()
    {
        _host.Correlation.CorrelationId = Guid.NewGuid();
        var created = await _host.SendAsync(Fnol.Complete(Now), ApplicationHost.Handler2);

        var entries = await AuditAsync(created.ClaimId);
        entries.ShouldAllBe(e => e.CorrelationId == _host.Correlation.CorrelationId);
        entries.ShouldAllBe(e => e.CreatedByUserId == ApplicationHost.Handler2.UserId);
        entries.ShouldAllBe(e => e.CreatedAt.Offset == TimeSpan.Zero);
        entries.Select(e => e.CreatedAt).ShouldBeInOrder(SortDirection.Descending);
        await Should.ThrowAsync<NotFoundException>(() => _host.SendAsync(new GetAuditLogQuery(Guid.NewGuid())));
    }

    [Fact]
    public async Task A_failed_command_writes_no_audit_entries()
    {
        var claimId = (await _host.SendAsync(Fnol.Complete(Now) with { Parties = [] })).ClaimId;
        var before = (await AuditAsync(claimId)).Count;

        await Should.ThrowAsync<Domain.Common.DomainRuleViolationException>(() =>
            _host.SendAsync(new TransitionClaimStatusCommand(claimId, ClaimStatus.Open)));

        (await AuditAsync(claimId)).Count.ShouldBe(before);
    }
}
