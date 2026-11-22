namespace ClaimsModule.Domain.Claims.Validation;

/// <summary>S5.4: Critical blocks Draft→Open. Warning does not block, but must be acknowledged (BR-C-02, D-07).</summary>
public enum ValidationSeverity
{
    Critical = 0,
    Warning = 1,
}

/// <summary>Lifecycle of a persisted validation issue (D-06, D-07).</summary>
public enum ValidationIssueStatus
{
    /// <summary>The condition still holds and nobody has handled it yet.</summary>
    Open = 0,

    /// <summary>A Warning that a user has acknowledged. It no longer blocks Open.</summary>
    Acknowledged = 1,

    /// <summary>A Critical waived by Supervisor+ with a reason (S4.2 "or all waived").</summary>
    Waived = 2,

    /// <summary>The condition no longer holds: the data was fixed.</summary>
    Resolved = 3,
}
