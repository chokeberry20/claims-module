namespace ClaimsModule.Domain.Reserves;

/// <summary>Reserve component types (S6.2, D-02).</summary>
public enum ReserveComponentType
{
    Indemnity = 0,
    Expense = 1,
    ALAE = 2,
    SubrogationRecoverable = 3,
}

/// <summary>Reserve component status (S9.5). It closes with the claim and reactivates on reopen (D-21).</summary>
public enum ReserveComponentStatus
{
    Active = 0,
    Closed = 1,
}

/// <summary>
/// Reserve transaction types (S9.6, D-16):
/// Add opens a component with an initial balance, Adjust sets a new target balance,
/// Reverse brings the balance to zero.
/// </summary>
public enum ReserveTransactionType
{
    Add = 0,
    Adjust = 1,
    Reverse = 2,
}

/// <summary>Approval status of a reserve transaction (S9.6).</summary>
public enum ApprovalStatus
{
    AutoApproved = 0,
    PendingApproval = 1,
    Approved = 2,
    Rejected = 3,
    Cancelled = 4,
}

/// <summary>GL posting status of a reserve transaction (S9.6, S12.1).</summary>
public enum PostingStatus
{
    Pending = 0,
    Posted = 1,
    Failed = 2,
    Cancelled = 3,
}

/// <summary>The authority needed for a transaction, based on its absolute amount (S6.3).</summary>
public enum ApprovalLevel
{
    Auto = 0,
    Supervisor = 1,
    Manager = 2,
}

public static class ReserveComponentTypeExtensions
{
    /// <summary>BR-R-01: only SubrogationRecoverable may be negative.</summary>
    public static bool AllowsNegativeAmounts(this ReserveComponentType type) =>
        type == ReserveComponentType.SubrogationRecoverable;

    /// <summary>D-19: the $10M aggregate is gross. Indemnity, Expense and ALAE count; recoveries are excluded.</summary>
    public static bool CountsTowardsAggregateLimit(this ReserveComponentType type) =>
        type != ReserveComponentType.SubrogationRecoverable;
}
