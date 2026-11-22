using ClaimsModule.Domain.Common;

namespace ClaimsModule.Domain.ReferenceData;

/// <summary>Peril categories (S9.9). The category of the chosen cause becomes the claim's ClaimType (D-33).</summary>
public enum PerilCategory
{
    Property = 0,
    Auto = 1,
    Liability = 2,
    Weather = 3,
    Equipment = 4,
    Crime = 5,
    General = 6,
}

/// <summary>Cause of loss reference data (S5.6, S9.9), seeded at migration time.</summary>
public sealed class CauseOfLossCode : Entity
{
    public const int MaxCodeLength = 50;
    public const int MaxNameLength = 255;

    private CauseOfLossCode()
    {
    }

    private CauseOfLossCode(Guid id)
        : base(id)
    {
    }

    public string Code { get; private set; } = string.Empty;

    public string Name { get; private set; } = string.Empty;

    public PerilCategory PerilCategory { get; private set; }

    public bool IsActive { get; private set; }

    public int SortOrder { get; private set; }

    public static CauseOfLossCode Create(Guid id, string code, string name, PerilCategory perilCategory, bool isActive, int sortOrder)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (code.Trim().Length > MaxCodeLength)
        {
            throw new ArgumentException($"Code must be at most {MaxCodeLength} characters.", nameof(code));
        }

        return new CauseOfLossCode(id)
        {
            Code = code.Trim(),
            Name = name.Trim(),
            PerilCategory = perilCategory,
            IsActive = isActive,
            SortOrder = sortOrder,
        };
    }
}
