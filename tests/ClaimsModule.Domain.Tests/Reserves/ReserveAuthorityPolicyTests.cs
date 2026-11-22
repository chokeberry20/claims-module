using System.Globalization;
using ClaimsModule.Domain.Common;
using ClaimsModule.Domain.Reserves;

namespace ClaimsModule.Domain.Tests.Reserves;

public sealed class ReserveAuthorityPolicyTests
{
    private static Money M(string amount) => Money.Of(decimal.Parse(amount, CultureInfo.InvariantCulture));

    [Theory]
    [InlineData("0.0001", ApprovalLevel.Auto)]
    [InlineData("10000", ApprovalLevel.Auto)]
    [InlineData("10000.01", ApprovalLevel.Supervisor)]
    [InlineData("10000.0001", ApprovalLevel.Supervisor)]
    [InlineData("100000", ApprovalLevel.Supervisor)]
    [InlineData("100000.01", ApprovalLevel.Manager)]
    [InlineData("10000000", ApprovalLevel.Manager)]
    [InlineData("-10000", ApprovalLevel.Auto)]
    [InlineData("-10000.01", ApprovalLevel.Supervisor)]
    [InlineData("-100000.01", ApprovalLevel.Manager)]
    public void S6_3_level_depends_on_the_absolute_transaction_amount(string amount, ApprovalLevel expected) =>
        ReserveAuthorityPolicy.LevelFor(M(amount)).ShouldBe(expected);

    [Theory]
    [InlineData(UserRole.Handler, ApprovalLevel.Supervisor, false)]
    [InlineData(UserRole.Handler, ApprovalLevel.Manager, false)]
    [InlineData(UserRole.Supervisor, ApprovalLevel.Supervisor, true)]
    [InlineData(UserRole.Supervisor, ApprovalLevel.Manager, false)]
    [InlineData(UserRole.Manager, ApprovalLevel.Supervisor, true)]
    [InlineData(UserRole.Manager, ApprovalLevel.Manager, true)]
    public void S3_approval_authority_by_role(UserRole role, ApprovalLevel level, bool expected) =>
        ReserveAuthorityPolicy.CanApprove(role, level).ShouldBe(expected);

    [Theory]
    [InlineData("9990000", "10000", false)]    // lands exactly on $10M: allowed
    [InlineData("9990000", "10000.01", true)]
    [InlineData("0", "10000000.01", true)]     // a single transaction over $10M
    [InlineData("10500000", "-600000", false)] // decreases never breach
    public void D_19_aggregate_limit_counts_gross_increases(string approvedGross, string delta, bool expected) =>
        ReserveAuthorityPolicy.WouldExceedAggregateLimit(ReserveComponentType.Indemnity, M(delta), M(approvedGross)).ShouldBe(expected);

    [Fact]
    public void D_19_subrogation_recoveries_never_breach_the_gross_limit() =>
        ReserveAuthorityPolicy.WouldExceedAggregateLimit(
            ReserveComponentType.SubrogationRecoverable, M("50000"), M("9999999")).ShouldBeFalse();

    [Theory]
    [InlineData(ReserveComponentType.Indemnity)]
    [InlineData(ReserveComponentType.Expense)]
    [InlineData(ReserveComponentType.ALAE)]
    public void D_19_indemnity_expense_and_alae_count_towards_the_limit(ReserveComponentType type) =>
        ReserveAuthorityPolicy.WouldExceedAggregateLimit(type, M("2"), M("9999999")).ShouldBeTrue();

    [Fact]
    public void D_19_a_small_breaching_transaction_is_not_auto_approved_and_needs_a_manager()
    {
        var requirement = ReserveAuthorityPolicy.Determine(ReserveComponentType.Expense, M("500"), M("9999800"));

        requirement.AmountLevel.ShouldBe(ApprovalLevel.Auto);
        requirement.RequiresLimitOverride.ShouldBeTrue();
        requirement.EffectiveLevel.ShouldBe(ApprovalLevel.Manager);
        requirement.IsAutoApproved.ShouldBeFalse();
    }

    [Fact]
    public void Non_breaching_small_transaction_is_auto_approved()
    {
        var requirement = ReserveAuthorityPolicy.Determine(ReserveComponentType.Indemnity, M("10000"), M("0"));

        requirement.IsAutoApproved.ShouldBeTrue();
    }

    [Fact]
    public void Warning_text_matches_S8() =>
        ReserveAuthorityPolicy.AggregateLimitWarning().ShouldBe("Total reserves will exceed $10,000,000. Manager override required.");

    [Fact]
    public void Limits_must_be_strictly_increasing() =>
        Should.Throw<ArgumentException>(() => new ReserveAuthorityLimits(10_000m, 10_000m, 10_000_000m));
}
