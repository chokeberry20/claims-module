using ClaimsModule.Application.Abstractions.Persistence;
using ClaimsModule.Application.Claims;
using ClaimsModule.Application.Common.Exceptions;
using ClaimsModule.Domain.Claims;
using ClaimsModule.Domain.Claims.Validation;
using ClaimsModule.Domain.Common;
using ClaimsModule.Domain.Reserves;
using ClaimsModule.IntegrationTests.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace ClaimsModule.IntegrationTests.Features;

/// <summary>Task 20 (+25a for the initial reserve): FNOL intake through the full pipeline on SQL Server.</summary>
[Collection(SqlServerTestGroup.Name)]
[System.Diagnostics.CodeAnalysis.SuppressMessage("Reliability", "CA1001", Justification = "_host is disposed in IAsyncLifetime.DisposeAsync, which xUnit calls after every test.")]
public sealed class CreateClaimTests(SqlServerDatabaseFixture db) : IAsyncLifetime
{
    private ApplicationHost _host = null!;

    public Task InitializeAsync()
    {
        _host = new ApplicationHost(db);
        return Task.CompletedTask;
    }

    public async Task DisposeAsync() => await _host.DisposeAsync();

    private DateTimeOffset Now => _host.Clock.Now;

    private Task<Claim?> LoadAsync(Guid claimId) =>
        _host.InScopeAsync(sp => sp.GetRequiredService<IClaimRepository>().GetByIdAsync(claimId));

    private Task<int> CountAsync(string sql) => db.ScalarAsync<int>(sql);

    // ------------------------------------------------------------------ happy path

    [Fact]
    public async Task Complete_fnol_creates_a_draft_claim_with_children_and_no_open_issues()
    {
        var result = await _host.SendAsync(Fnol.Complete(Now));

        result.ClaimNumber.ShouldMatch(@"^CLM-2026-\d{7}$");
        result.Status.ShouldBe(ClaimStatus.Draft);
        result.ValidationIssues.ShouldBeEmpty();
        result.InitialReserve.ShouldBeNull();

        var claim = (await LoadAsync(result.ClaimId)).ShouldNotBeNull();
        claim.ClaimType.ShouldBe("Auto"); // D-33: from the cause code's peril category
        claim.AssignedHandlerId.ShouldBe(ApplicationHost.Handler1.UserId); // D-10: the creator
        claim.PolicyNumber.ShouldBe("POL-2024-001001");
        claim.Parties.Single().IsActiveClaimant.ShouldBeTrue();
        claim.RiskObjects.Single().IsPrimary.ShouldBeTrue();
        claim.LossEvent.CauseOfLossCode.ShouldBe("COL-VEH-COL");
    }

    [Fact]
    public async Task Consecutive_claims_get_consecutive_numbers()
    {
        var first = await _host.SendAsync(Fnol.Complete(Now));
        var second = await _host.SendAsync(Fnol.Complete(Now));

        ClaimNumber.Parse(second.ClaimNumber).Sequence.ShouldBe(ClaimNumber.Parse(first.ClaimNumber).Sequence + 1);
    }

    [Fact]
    public async Task State_level_findings_are_saved_on_the_draft_claim_not_rejected()
    {
        // D-05 layer b: no claimant (Critical), no policy (Warning), no risk objects (Warning).
        var result = await _host.SendAsync(Fnol.Complete(Now) with { PolicyId = null, Parties = [], RiskObjects = [] });

        result.Status.ShouldBe(ClaimStatus.Draft);
        result.ValidationIssues.Select(i => (i.Code, i.Severity)).OrderBy(i => i.Code).ShouldBe(
        [
            (ValidationIssueCodes.NoClaimant, ValidationSeverity.Critical),
            (ValidationIssueCodes.NoPolicy, ValidationSeverity.Warning),
            (ValidationIssueCodes.NoRiskObjects, ValidationSeverity.Warning),
        ]);
        (await CountAsync($"SELECT COUNT(*) FROM ClaimValidationIssues WHERE ClaimId = '{result.ClaimId}'")).ShouldBe(3);
    }

    [Fact]
    public async Task Loss_outside_the_policy_period_is_a_warning()
    {
        // BR-C-02: POL-2024-001002 expired 2026-05-31; the loss is in November 2026.
        var result = await _host.SendAsync(Fnol.Complete(Now) with { PolicyId = Fnol.HarborviewPolicyId });

        result.ValidationIssues.Single().Code.ShouldBe(ValidationIssueCodes.LossDateOutsidePolicyPeriod);
        result.ValidationIssues.Single().Severity.ShouldBe(ValidationSeverity.Warning);
    }

    // ------------------------------------------------------------------ request validation (422, nothing saved)

    [Theory]
    [InlineData("future", "LOSS_DATE_IN_FUTURE")]
    [InlineData("short", "LOSS_DESCRIPTION_TOO_SHORT")]
    [InlineData("unknown-code", "CAUSE_OF_LOSS_INVALID")]
    [InlineData("inactive-code", "CAUSE_OF_LOSS_INVALID")]
    [InlineData("unknown-policy", "POLICY_NOT_FOUND")]
    [InlineData("nameless-person", "PARTY_NAME_REQUIRED")]
    [InlineData("bad-email", "PARTY_EMAIL_INVALID")]
    [InlineData("negative-reserve", "RESERVE_AMOUNT_NOT_POSITIVE")]
    [InlineData("reserve-without-reason", "CHANGE_REASON_REQUIRED")]
    public async Task Request_level_rules_give_422_and_save_nothing(string variant, string expectedCode)
    {
        var valid = Fnol.Complete(Now);
        var command = variant switch
        {
            "future" => valid with { LossDate = Now.AddHours(1) },
            "short" => valid with { LossDescription = "Too short." },
            "unknown-code" => valid with { CauseOfLossCode = "COL-NOPE" },
            "inactive-code" => valid with { CauseOfLossCode = "COL-LEGACY-SUBS" },
            "unknown-policy" => valid with { PolicyId = Guid.NewGuid() },
            "nameless-person" => valid with { Parties = [new PartyInput(PartyRole.Claimant, PartyType.Person, "Jane")] },
            "bad-email" => valid with { Parties = [Fnol.Claimant with { Email = "not-an-email" }] },
            "negative-reserve" => Fnol.WithReserve(Now, -5_000m),
            "reserve-without-reason" => valid with { InitialReserve = new InitialReserveInput(ReserveComponentType.Expense, 1_000m, "") },
            _ => throw new ArgumentOutOfRangeException(nameof(variant)),
        };
        var claimsBefore = await CountAsync("SELECT COUNT(*) FROM Claims");

        var error = await Should.ThrowAsync<RequestValidationException>(() => _host.SendAsync(command));

        error.Violations.Select(v => v.Code).ShouldContain(expectedCode);
        (await CountAsync("SELECT COUNT(*) FROM Claims")).ShouldBe(claimsBefore);
    }

    [Fact]
    public async Task Loss_date_within_the_five_minute_clock_skew_tolerance_is_accepted()
    {
        var result = await _host.SendAsync(Fnol.Complete(Now) with { LossDate = Now.AddMinutes(4) }); // D-15

        result.Status.ShouldBe(ClaimStatus.Draft);
    }

    [Fact]
    public async Task Subrogation_reserve_may_be_negative()
    {
        var result = await _host.SendAsync(Fnol.WithReserve(Now, -2_000m, ReserveComponentType.SubrogationRecoverable));

        result.InitialReserve.ShouldNotBeNull().NewBalance.ShouldBe(-2_000m);
    }

    [Fact]
    public async Task Anonymous_caller_gets_401_before_any_validation()
    {
        // Authorization runs before validation, so even an invalid command reports 401, not 422.
        await Should.ThrowAsync<UnauthenticatedException>(() => _host.SendAnonymousAsync(Fnol.Complete(Now) with { LossDescription = "" }));
    }

    // ------------------------------------------------------------------ initial reserve (S5.2 step 3, task 25a)

    [Fact]
    public async Task Small_initial_reserve_is_auto_approved_with_the_claim()
    {
        var result = await _host.SendAsync(Fnol.WithReserve(Now, 8_000m));

        var reserve = result.InitialReserve.ShouldNotBeNull();
        reserve.ApprovalStatus.ShouldBe(ApprovalStatus.AutoApproved);
        reserve.NewBalance.ShouldBe(8_000m);
        (await CountAsync($"SELECT COUNT(*) FROM ReserveHistory WHERE ClaimId = '{result.ClaimId}'")).ShouldBe(1);
        (await LoadAsync(result.ClaimId)).ShouldNotBeNull().LastReserveActivityAt.ShouldBe(Now);
    }

    [Fact]
    public async Task Large_initial_reserve_waits_for_supervisor_approval()
    {
        var result = await _host.SendAsync(Fnol.WithReserve(Now, 50_000m));

        result.InitialReserve.ShouldNotBeNull().ApprovalStatus.ShouldBe(ApprovalStatus.PendingApproval);
        result.InitialReserve.RequiredApprovalLevel.ShouldBe(ApprovalLevel.Supervisor);
    }

    [Fact]
    public async Task Initial_reserve_without_a_policy_rolls_back_the_whole_fnol_and_releases_the_number()
    {
        var before = await _host.SendAsync(Fnol.Complete(Now));
        var claimsBefore = await CountAsync("SELECT COUNT(*) FROM Claims");

        var error = await Should.ThrowAsync<DomainRuleViolationException>(() =>
            _host.SendAsync(Fnol.WithReserve(Now, 5_000m) with { PolicyId = null }));

        error.Contains("RESERVES_REQUIRE_POLICY").ShouldBeTrue(); // BR-C-06
        (await CountAsync("SELECT COUNT(*) FROM Claims")).ShouldBe(claimsBefore);
        (await CountAsync("SELECT COUNT(*) FROM ClaimAuditLog WHERE EventType = 'CLAIM_CREATED' AND ClaimId NOT IN (SELECT ClaimId FROM Claims)")).ShouldBe(0);

        var after = await _host.SendAsync(Fnol.Complete(Now));
        ClaimNumber.Parse(after.ClaimNumber).Sequence.ShouldBe(ClaimNumber.Parse(before.ClaimNumber).Sequence + 1); // no gap
    }

    // ------------------------------------------------------------------ Idempotency-Key (S10, D-66)

    [Fact]
    public async Task Same_idempotency_key_replays_the_first_result_and_creates_one_claim()
    {
        var key = Guid.NewGuid().ToString();
        var command = Fnol.WithReserve(Now, 8_000m) with { IdempotencyKey = key };

        var first = await _host.SendAsync(command);
        var replay = await _host.SendAsync(command);

        replay.ClaimId.ShouldBe(first.ClaimId);
        replay.ClaimNumber.ShouldBe(first.ClaimNumber);
        replay.InitialReserve.ShouldNotBeNull().TransactionId.ShouldBe(first.InitialReserve!.TransactionId);
        (await CountAsync($"SELECT COUNT(*) FROM Claims WHERE ClaimNumber = '{first.ClaimNumber}'")).ShouldBe(1);
        (await CountAsync($"SELECT COUNT(*) FROM IdempotencyRecords WHERE IdempotencyKey = '{key}'")).ShouldBe(1);
    }

    [Fact]
    public async Task Same_idempotency_key_with_a_different_payload_is_a_conflict()
    {
        var key = Guid.NewGuid().ToString();
        await _host.SendAsync(Fnol.Complete(Now) with { IdempotencyKey = key });

        await Should.ThrowAsync<IdempotencyConflictException>(() =>
            _host.SendAsync(Fnol.Complete(Now) with { IdempotencyKey = key, LossLocation = "Somewhere else entirely" }));
    }

    [Fact]
    public async Task Idempotency_keys_are_per_user()
    {
        var key = Guid.NewGuid().ToString();
        var first = await _host.SendAsync(Fnol.Complete(Now) with { IdempotencyKey = key }, ApplicationHost.Handler1);
        var other = await _host.SendAsync(Fnol.Complete(Now) with { IdempotencyKey = key }, ApplicationHost.Handler2);

        other.ClaimId.ShouldNotBe(first.ClaimId);
    }
}
