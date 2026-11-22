using System.Globalization;
using ClaimsModule.Domain.Common;

namespace ClaimsModule.Domain.Reserves;

/// <summary>Reserve authority thresholds. Defaults are the brief A.3 / spec S6.3 values.</summary>
public sealed record ReserveAuthorityLimits
{
    public ReserveAuthorityLimits(decimal autoApprovalLimit, decimal supervisorApprovalLimit, decimal claimAggregateLimit)
    {
        if (autoApprovalLimit <= 0 || supervisorApprovalLimit <= autoApprovalLimit || claimAggregateLimit <= supervisorApprovalLimit)
        {
            throw new ArgumentException("Limits must be positive and strictly increasing: auto < supervisor < aggregate.");
        }

        AutoApprovalLimit = autoApprovalLimit;
        SupervisorApprovalLimit = supervisorApprovalLimit;
        ClaimAggregateLimit = claimAggregateLimit;
    }

    public static ReserveAuthorityLimits Default { get; } = new(10_000m, 100_000m, 10_000_000m);

    /// <summary>Transactions at or below this are auto-approved (≤ $10,000).</summary>
    public decimal AutoApprovalLimit { get; }

    /// <summary>Supervisors may approve up to and including this amount (≤ $100,000).</summary>
    public decimal SupervisorApprovalLimit { get; }

    /// <summary>The approved gross total per claim, above which a Manager override is needed (BR-R-05).</summary>
    public decimal ClaimAggregateLimit { get; }
}

/// <summary>
/// What a reserve transaction needs before it counts toward the balance and GL posting starts.
/// <see cref="AmountLevel"/> comes from the transaction's absolute amount (S6.3, D-16).
/// <see cref="RequiresLimitOverride"/> is set when it would push the claim over the aggregate limit (D-19).
/// </summary>
public sealed record ApprovalRequirement(ApprovalLevel AmountLevel, bool RequiresLimitOverride)
{
    /// <summary>An aggregate breach always needs a Manager and the claim's override flag (D-19).</summary>
    public ApprovalLevel EffectiveLevel => RequiresLimitOverride ? ApprovalLevel.Manager : AmountLevel;

    public bool IsAutoApproved => EffectiveLevel == ApprovalLevel.Auto;
}

/// <summary>
/// Reserve authority rules (S6.3, BR-R-02, BR-R-05). The threshold applies to the individual
/// transaction, not the running total. The aggregate limit is gross: Indemnity + Expense + ALAE,
/// with SubrogationRecoverable excluded (D-19).
/// </summary>
public static class ReserveAuthorityPolicy
{
    public static ApprovalLevel LevelFor(Money transactionAmount, ReserveAuthorityLimits? limits = null)
    {
        limits ??= ReserveAuthorityLimits.Default;
        var absolute = transactionAmount.Abs().Amount;

        if (absolute <= limits.AutoApprovalLimit)
        {
            return ApprovalLevel.Auto;
        }

        return absolute <= limits.SupervisorApprovalLimit ? ApprovalLevel.Supervisor : ApprovalLevel.Manager;
    }

    /// <summary>
    /// True when this transaction would push the approved gross total above the aggregate limit.
    /// Only increases to gross components count. Decreases and recoveries never breach.
    /// </summary>
    public static bool WouldExceedAggregateLimit(
        ReserveComponentType componentType,
        Money delta,
        Money approvedGrossTotal,
        ReserveAuthorityLimits? limits = null)
    {
        limits ??= ReserveAuthorityLimits.Default;
        return componentType.CountsTowardsAggregateLimit()
            && delta.IsPositive
            && approvedGrossTotal.Amount + delta.Amount > limits.ClaimAggregateLimit;
    }

    public static ApprovalRequirement Determine(
        ReserveComponentType componentType,
        Money delta,
        Money approvedGrossTotal,
        ReserveAuthorityLimits? limits = null) =>
        new(LevelFor(delta, limits), WouldExceedAggregateLimit(componentType, delta, approvedGrossTotal, limits));

    public static UserRole MinimumRoleFor(ApprovalLevel level) => level switch
    {
        ApprovalLevel.Auto => UserRole.Handler,
        ApprovalLevel.Supervisor => UserRole.Supervisor,
        ApprovalLevel.Manager => UserRole.Manager,
        _ => throw new ArgumentOutOfRangeException(nameof(level), level, "Unknown approval level."),
    };

    public static bool CanApprove(UserRole role, ApprovalLevel level) => role >= MinimumRoleFor(level);

    /// <summary>The S8 warning text: "Total reserves will exceed $10,000,000. Manager override required."</summary>
    public static string AggregateLimitWarning(ReserveAuthorityLimits? limits = null) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"Total reserves will exceed ${(limits ?? ReserveAuthorityLimits.Default).ClaimAggregateLimit:#,0}. Manager override required.");
}
