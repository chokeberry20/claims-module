namespace ClaimsModule.Domain.Claims;

/// <summary>Claim lifecycle statuses (S4.1). Stored as NVARCHAR(50) names.</summary>
public enum ClaimStatus
{
    Draft = 0,
    Open = 1,
    UnderInvestigation = 2,
    PendingPayment = 3,
    Closed = 4,
    Reopened = 5,
    Withdrawn = 6,
}

/// <summary>Claim severity (S9.1). Optional at intake, defaults to Standard (D-33).</summary>
public enum ClaimSeverity
{
    Standard = 0,
    Minor = 1,
    Critical = 2,
    Catastrophic = 3,
}

/// <summary>Party roles (BR-P-02).</summary>
public enum PartyRole
{
    Claimant = 0,
    Insured = 1,
    ThirdParty = 2,
    Witness = 3,
    Attorney = 4,
}

/// <summary>Party type (S9.3).</summary>
public enum PartyType
{
    Person = 0,
    Company = 1,
}

/// <summary>Risk object asset types (S5.2, S9.4).</summary>
public enum AssetType
{
    Vehicle = 0,
    Property = 1,
    Person = 2,
    Equipment = 3,
    Other = 4,
}
