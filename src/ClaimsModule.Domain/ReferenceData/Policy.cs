using ClaimsModule.Domain.Claims;
using ClaimsModule.Domain.Common;

namespace ClaimsModule.Domain.ReferenceData;

/// <summary>Policy status (S9.10). Only Cancelled is stored. Active and Expired are computed from today's date (D-34).</summary>
public enum PolicyStatus
{
    Active = 0,
    Expired = 1,
    Cancelled = 2,
}

/// <summary>
/// The simulated policy (S5.5, S9.10). Seeded reference data stands in for a real Policy domain.
/// Coverage types are stored as a JSON array (D-35).
/// </summary>
public sealed class Policy : Entity
{
    public const int MaxPolicyNumberLength = 50;
    public const int MaxClientNameLength = 255;

    private List<string> _coverageTypes = [];

    private Policy()
    {
    }

    private Policy(Guid id)
        : base(id)
    {
    }

    public string PolicyNumber { get; private set; } = string.Empty;

    public string ClientName { get; private set; } = string.Empty;

    public DateOnly EffectiveDate { get; private set; }

    public DateOnly ExpirationDate { get; private set; }

    public bool IsCancelled { get; private set; }

    public IReadOnlyList<string> CoverageTypes => _coverageTypes.AsReadOnly();

    public PolicyPeriod Period => new(EffectiveDate, ExpirationDate);

    public static Policy Create(
        Guid id,
        string policyNumber,
        string clientName,
        DateOnly effectiveDate,
        DateOnly expirationDate,
        IEnumerable<string> coverageTypes,
        bool isCancelled = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(policyNumber);
        ArgumentException.ThrowIfNullOrWhiteSpace(clientName);
        ArgumentNullException.ThrowIfNull(coverageTypes);
        _ = new PolicyPeriod(effectiveDate, expirationDate);

        return new Policy(id)
        {
            PolicyNumber = policyNumber.Trim(),
            ClientName = clientName.Trim(),
            EffectiveDate = effectiveDate,
            ExpirationDate = expirationDate,
            IsCancelled = isCancelled,
            _coverageTypes = coverageTypes.Select(c => c.Trim()).Where(c => c.Length > 0).Distinct(StringComparer.Ordinal).ToList(),
        };
    }

    public PolicyStatus StatusOn(DateOnly today) =>
        IsCancelled ? PolicyStatus.Cancelled
        : today > ExpirationDate ? PolicyStatus.Expired
        : PolicyStatus.Active;

    public PolicySnapshot ToSnapshot() => new(Id, PolicyNumber, ClientName);
}
