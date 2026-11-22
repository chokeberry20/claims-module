using ClaimsModule.Domain.Common;

namespace ClaimsModule.Domain.Claims;

/// <summary>Input for linking an affected asset to a claim (S5.2 step 2, S9.4).</summary>
public sealed record RiskObjectDetails(
    AssetType AssetType,
    string AssetDescription,
    string? DamageDescription = null,
    string? AssetReference = null,
    bool IsPrimary = false);

/// <summary>
/// An asset affected by the loss (S9.4). The model is simplified: asset type plus description.
/// The Claim keeps exactly one primary risk object whenever any exist.
/// </summary>
public sealed class ClaimRiskObject : Entity
{
    public const int MaxAssetDescriptionLength = 500;
    public const int MaxAssetReferenceLength = 255;

    private ClaimRiskObject()
    {
    }

    private ClaimRiskObject(Guid id, Guid claimId)
        : base(id) => ClaimId = claimId;

    public Guid ClaimId { get; private set; }

    public AssetType AssetType { get; private set; }

    public string AssetDescription { get; private set; } = string.Empty;

    public string? DamageDescription { get; private set; }

    public string? AssetReference { get; private set; }

    public bool IsPrimary { get; private set; }

    internal static ClaimRiskObject Create(Guid claimId, RiskObjectDetails details)
    {
        ArgumentNullException.ThrowIfNull(details);
        var violations = new List<RuleViolation>();

        if (!Enum.IsDefined(details.AssetType))
        {
            violations.Add(new("ASSET_TYPE_INVALID", "Asset type is not supported.", "AssetType"));
        }

        if (string.IsNullOrWhiteSpace(details.AssetDescription))
        {
            violations.Add(new("ASSET_DESCRIPTION_REQUIRED", "Asset description is required.", "AssetDescription"));
        }
        else if (details.AssetDescription.Trim().Length > MaxAssetDescriptionLength)
        {
            violations.Add(new(
                "ASSET_DESCRIPTION_TOO_LONG",
                $"Asset description must be at most {MaxAssetDescriptionLength} characters.",
                "AssetDescription"));
        }

        if (details.AssetReference?.Trim() is { Length: > MaxAssetReferenceLength })
        {
            violations.Add(new(
                "ASSET_REFERENCE_TOO_LONG",
                $"Asset reference must be at most {MaxAssetReferenceLength} characters.",
                "AssetReference"));
        }

        if (violations.Count > 0)
        {
            throw new DomainRuleViolationException(violations);
        }

        return new ClaimRiskObject(SequentialGuid.NewGuid(), claimId)
        {
            AssetType = details.AssetType,
            AssetDescription = details.AssetDescription.Trim(),
            DamageDescription = string.IsNullOrWhiteSpace(details.DamageDescription) ? null : details.DamageDescription.Trim(),
            AssetReference = string.IsNullOrWhiteSpace(details.AssetReference) ? null : details.AssetReference.Trim(),
            IsPrimary = details.IsPrimary,
        };
    }

    internal void SetPrimary(bool isPrimary) => IsPrimary = isPrimary;
}
