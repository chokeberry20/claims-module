namespace ClaimsModule.Domain.Claims;

/// <summary>
/// The simulated policy as linked to a claim. Policy number and client name are denormalised onto
/// the claim for display (S9.1).
/// </summary>
public sealed record PolicySnapshot
{
    public const int MaxPolicyNumberLength = 50;
    public const int MaxClientNameLength = 255;

    public PolicySnapshot(Guid policyId, string policyNumber, string clientName)
    {
        if (policyId == Guid.Empty)
        {
            throw new ArgumentException("Policy id must not be empty.", nameof(policyId));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(policyNumber);
        ArgumentException.ThrowIfNullOrWhiteSpace(clientName);

        PolicyId = policyId;
        PolicyNumber = policyNumber.Trim();
        ClientName = clientName.Trim();
    }

    public Guid PolicyId { get; }

    public string PolicyNumber { get; }

    public string ClientName { get; }
}

/// <summary>A policy's effective period. Both bounds are inclusive (BR-C-02, D-15).</summary>
public sealed record PolicyPeriod
{
    public PolicyPeriod(DateOnly effectiveDate, DateOnly expirationDate)
    {
        if (expirationDate < effectiveDate)
        {
            throw new ArgumentException("Expiration date must not be before effective date.", nameof(expirationDate));
        }

        EffectiveDate = effectiveDate;
        ExpirationDate = expirationDate;
    }

    public DateOnly EffectiveDate { get; }

    public DateOnly ExpirationDate { get; }

    public bool Contains(DateOnly date) => date >= EffectiveDate && date <= ExpirationDate;

    /// <summary>Loss timestamps are stored in UTC, so the comparison uses the UTC calendar date (D-15).</summary>
    public bool Contains(DateTimeOffset instant) => Contains(DateOnly.FromDateTime(instant.UtcDateTime));
}
