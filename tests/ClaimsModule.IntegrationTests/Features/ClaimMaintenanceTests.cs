using ClaimsModule.Application.Claims;
using ClaimsModule.Application.Common.Exceptions;
using ClaimsModule.Domain.Claims;
using ClaimsModule.Domain.Claims.Validation;
using ClaimsModule.Domain.Common;
using ClaimsModule.IntegrationTests.Persistence;

namespace ClaimsModule.IntegrationTests.Features;

/// <summary>Task 22: claim maintenance commands through the full pipeline on SQL Server.</summary>
[Collection(SqlServerTestGroup.Name)]
[System.Diagnostics.CodeAnalysis.SuppressMessage("Reliability", "CA1001", Justification = "_host is disposed in IAsyncLifetime.DisposeAsync, which xUnit calls after every test.")]
public sealed class ClaimMaintenanceTests(SqlServerDatabaseFixture db) : IAsyncLifetime
{
    private ApplicationHost _host = null!;

    public Task InitializeAsync()
    {
        _host = new ApplicationHost(db);
        return Task.CompletedTask;
    }

    public async Task DisposeAsync() => await _host.DisposeAsync();

    private DateTimeOffset Now => _host.Clock.Now;

    private Task<ClaimDetailDto> DetailAsync(Guid claimId) => _host.SendAsync(new GetClaimQuery(claimId));

    // ------------------------------------------------------------------ loss details, policy, handler, notes, severity

    [Fact]
    public async Task Updating_loss_details_in_draft_revalidates_the_claim()
    {
        var claim = await _host.SendAsync(Fnol.Complete(Now));

        // Move the loss before the policy started (2024-01-01): BR-C-02 warning appears.
        var result = await _host.SendAsync(new UpdateLossDetailsCommand(claim.ClaimId)
        {
            LossDate = new DateTimeOffset(2023, 6, 1, 10, 0, 0, TimeSpan.Zero),
            LossDescription = "Corrected: the collision happened in the depot yard, not on the I-95.",
            CauseOfLossCode = "COL-VEH-COMP",
        });

        result.ValidationIssues.Single().Code.ShouldBe(ValidationIssueCodes.LossDateOutsidePolicyPeriod);
        var detail = await DetailAsync(claim.ClaimId);
        detail.LossEvent.CauseOfLossCode.ShouldBe("COL-VEH-COMP");
        detail.LossEvent.LossDescription.ShouldStartWith("Corrected:");
    }

    [Fact]
    public async Task Updating_loss_details_is_validated_like_fnol()
    {
        var claim = await _host.SendAsync(Fnol.Complete(Now));

        var error = await Should.ThrowAsync<RequestValidationException>(() => _host.SendAsync(new UpdateLossDetailsCommand(claim.ClaimId)
        {
            LossDate = Now.AddDays(1),
            LossDescription = "short",
            CauseOfLossCode = "COL-LEGACY-SUBS",
        }));

        error.Violations.Select(v => v.Code).ShouldBe(
            ["LOSS_DATE_IN_FUTURE", "LOSS_DESCRIPTION_TOO_SHORT", "CAUSE_OF_LOSS_INVALID"], ignoreOrder: true);
    }

    [Fact]
    public async Task Linking_a_policy_resolves_the_no_policy_warning()
    {
        var claim = await _host.SendAsync(Fnol.Complete(Now) with { PolicyId = null });
        claim.ValidationIssues.Single().Code.ShouldBe(ValidationIssueCodes.NoPolicy);

        var result = await _host.SendAsync(new LinkPolicyCommand(claim.ClaimId, Fnol.MeridianPolicyId));

        result.ValidationIssues.Single().Status.ShouldBe(ValidationIssueStatus.Resolved);
        (await DetailAsync(claim.ClaimId)).PolicyNumber.ShouldBe("POL-2024-001001");
        await Should.ThrowAsync<NotFoundException>(() => _host.SendAsync(new LinkPolicyCommand(claim.ClaimId, Guid.NewGuid())));
    }

    [Fact]
    public async Task Supervisors_assign_handlers_from_the_directory_and_handlers_get_403()
    {
        var claim = await _host.SendAsync(Fnol.Complete(Now));

        await Should.ThrowAsync<ForbiddenAccessException>(() =>
            _host.SendAsync(new AssignHandlerCommand(claim.ClaimId, ApplicationHost.Handler2.UserId), ApplicationHost.Handler1));
        var unknown = await Should.ThrowAsync<DomainRuleViolationException>(() =>
            _host.SendAsync(new AssignHandlerCommand(claim.ClaimId, Guid.NewGuid()), ApplicationHost.Supervisor1));
        unknown.Contains("HANDLER_NOT_FOUND").ShouldBeTrue();

        await _host.SendAsync(new AssignHandlerCommand(claim.ClaimId, ApplicationHost.Handler2.UserId), ApplicationHost.Supervisor1);

        var detail = await DetailAsync(claim.ClaimId);
        detail.AssignedHandlerId.ShouldBe(ApplicationHost.Handler2.UserId);
        detail.AssignedHandlerName.ShouldBe(ApplicationHost.Handler2.DisplayName);
    }

    [Fact]
    public async Task Notes_and_severity_are_editable()
    {
        var claim = await _host.SendAsync(Fnol.Complete(Now));

        await _host.SendAsync(new UpdateNotesCommand(claim.ClaimId, "  Called the insured back at 14:05.  "));
        await _host.SendAsync(new ChangeSeverityCommand(claim.ClaimId, ClaimSeverity.Critical));

        var detail = await DetailAsync(claim.ClaimId);
        detail.Notes.ShouldBe("Called the insured back at 14:05.");
        detail.Severity.ShouldBe(ClaimSeverity.Critical);
        (await Should.ThrowAsync<RequestValidationException>(() =>
            _host.SendAsync(new UpdateNotesCommand(claim.ClaimId, new string('x', 4001))))).Violations.Single().Code.ShouldBe("NOTES_TOO_LONG");
    }

    [Fact]
    public async Task Maintenance_on_an_unknown_claim_is_404()
    {
        await Should.ThrowAsync<NotFoundException>(() => _host.SendAsync(new UpdateNotesCommand(Guid.NewGuid(), "x")));
    }

    // ------------------------------------------------------------------ validate, acknowledge, waive (D-07)

    [Fact]
    public async Task Validate_and_acknowledge_warnings()
    {
        var claim = await _host.SendAsync(Fnol.Complete(Now) with { PolicyId = null, RiskObjects = [] });

        var validated = await _host.SendAsync(new ValidateClaimCommand(claim.ClaimId));
        validated.ValidationIssues.Count(i => i.Status == ValidationIssueStatus.Open).ShouldBe(2);

        var acknowledged = await _host.SendAsync(new AcknowledgeWarningsCommand(claim.ClaimId));

        acknowledged.Acknowledged.ShouldBe(2);
        acknowledged.ValidationIssues.ShouldAllBe(i => i.Status == ValidationIssueStatus.Acknowledged);
        (await _host.SendAsync(new ValidateClaimCommand(claim.ClaimId))).ValidationIssues
            .ShouldAllBe(i => i.Status == ValidationIssueStatus.Acknowledged); // not raised again
    }

    [Fact]
    public async Task Supervisor_waives_a_critical_issue_which_is_not_raised_again()
    {
        var claim = await _host.SendAsync(Fnol.Complete(Now) with { CauseOfLossCode = "COL-WIND" });
        await db.ExecuteAsync("UPDATE CauseOfLossCodes SET IsActive = 0 WHERE Code = 'COL-WIND'");
        try
        {
            var issue = (await _host.SendAsync(new ValidateClaimCommand(claim.ClaimId))).ValidationIssues
                .Single(i => i.Code == ValidationIssueCodes.CauseOfLossInactive);
            issue.Severity.ShouldBe(ValidationSeverity.Critical);

            // Handler: 403 from the pipeline; empty reason: 422.
            await Should.ThrowAsync<ForbiddenAccessException>(() =>
                _host.SendAsync(new WaiveValidationIssueCommand(claim.ClaimId, issue.IssueId, "Legacy code accepted"), ApplicationHost.Handler1));
            (await Should.ThrowAsync<RequestValidationException>(() =>
                _host.SendAsync(new WaiveValidationIssueCommand(claim.ClaimId, issue.IssueId, " "), ApplicationHost.Supervisor1)))
                .Violations.Single().Code.ShouldBe("REASON_REQUIRED");

            await _host.SendAsync(new WaiveValidationIssueCommand(claim.ClaimId, issue.IssueId, "Legacy code accepted by underwriting"), ApplicationHost.Supervisor1);

            var revalidated = await _host.SendAsync(new ValidateClaimCommand(claim.ClaimId));
            var waived = revalidated.ValidationIssues.Single(i => i.Code == ValidationIssueCodes.CauseOfLossInactive);
            waived.Status.ShouldBe(ValidationIssueStatus.Waived);
            revalidated.ValidationIssues.Count(i => i.Code == ValidationIssueCodes.CauseOfLossInactive).ShouldBe(1);
        }
        finally
        {
            await db.ExecuteAsync("UPDATE CauseOfLossCodes SET IsActive = 1 WHERE Code = 'COL-WIND'");
        }
    }

    [Fact]
    public async Task No_claimant_cannot_be_waived()
    {
        var claim = await _host.SendAsync(Fnol.Complete(Now) with { Parties = [] });
        var noClaimant = claim.ValidationIssues.Single(i => i.Code == ValidationIssueCodes.NoClaimant);

        var error = await Should.ThrowAsync<DomainRuleViolationException>(() =>
            _host.SendAsync(new WaiveValidationIssueCommand(claim.ClaimId, noClaimant.IssueId, "Claimant will be added later"), ApplicationHost.Supervisor1));

        error.Contains("VALIDATION_ISSUE_NOT_WAIVABLE").ShouldBeTrue();
    }
}
