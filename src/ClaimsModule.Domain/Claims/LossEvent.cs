using ClaimsModule.Domain.Common;

namespace ClaimsModule.Domain.Claims;

/// <summary>Input for creating or correcting a loss event (S5.2 step 1).</summary>
public sealed record LossEventDetails(
    DateTimeOffset LossDate,
    string LossDescription,
    string CauseOfLossCode,
    string? LossLocation = null,
    decimal? EstimatedLossAmount = null,
    string? PoliceReportNumber = null);

/// <summary>
/// The loss incident behind a claim (S9.2). It is part of the Claim aggregate (1:1, own table).
/// The invariants here duplicate the request-level FluentValidation rules on purpose
/// (brief §3.4: rules are enforced in validators AND in domain logic).
/// </summary>
public sealed class LossEvent : Entity
{
    public const int MinDescriptionLength = 20;
    public const int MaxLocationLength = 500;
    public const int MaxCauseOfLossCodeLength = 50;
    public const int MaxPoliceReportNumberLength = 100;

    /// <summary>Allowed clock skew for the "not in the future" rule (D-15).</summary>
    public static readonly TimeSpan FutureDateTolerance = TimeSpan.FromMinutes(5);

    private LossEvent()
    {
    }

    private LossEvent(Guid id, Guid claimId)
        : base(id) => ClaimId = claimId;

    public Guid ClaimId { get; private set; }

    /// <summary>Stored in UTC (S14.2, S15.1).</summary>
    public DateTimeOffset LossDate { get; private set; }

    public string LossDescription { get; private set; } = string.Empty;

    public string? LossLocation { get; private set; }

    public string CauseOfLossCode { get; private set; } = string.Empty;

    public Money? EstimatedLossAmount { get; private set; }

    /// <summary>When the claim was formally reported. Set at intake and never changed.</summary>
    public DateTimeOffset ReportDate { get; private set; }

    public string? PoliceReportNumber { get; private set; }

    internal static LossEvent Create(Guid claimId, LossEventDetails details, DateTimeOffset now)
    {
        var lossEvent = new LossEvent(SequentialGuid.NewGuid(), claimId) { ReportDate = now.ToUniversalTime() };
        lossEvent.Apply(details, now);
        return lossEvent;
    }

    internal void Update(LossEventDetails details, DateTimeOffset now) => Apply(details, now);

    private void Apply(LossEventDetails details, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(details);
        var violations = Validate(details, now);
        if (violations.Count > 0)
        {
            throw new DomainRuleViolationException(violations);
        }

        LossDate = details.LossDate.ToUniversalTime();
        LossDescription = details.LossDescription.Trim();
        CauseOfLossCode = details.CauseOfLossCode.Trim();
        LossLocation = string.IsNullOrWhiteSpace(details.LossLocation) ? null : details.LossLocation.Trim();
        EstimatedLossAmount = details.EstimatedLossAmount is { } amount ? Money.Of(amount) : null;
        PoliceReportNumber = string.IsNullOrWhiteSpace(details.PoliceReportNumber) ? null : details.PoliceReportNumber.Trim();
    }

    private static List<RuleViolation> Validate(LossEventDetails details, DateTimeOffset now)
    {
        var violations = new List<RuleViolation>();

        if (details.LossDate == default)
        {
            violations.Add(new("LOSS_DATE_REQUIRED", "Loss date is required.", "LossDate"));
        }
        else if (details.LossDate > now + FutureDateTolerance)
        {
            // BR-C-01
            violations.Add(new("LOSS_DATE_IN_FUTURE", "Loss date cannot be in the future.", "LossDate"));
        }

        if (string.IsNullOrWhiteSpace(details.LossDescription) || details.LossDescription.Trim().Length < MinDescriptionLength)
        {
            // BR-C-07
            violations.Add(new(
                "LOSS_DESCRIPTION_TOO_SHORT",
                "Loss description is required and must be at least 20 characters.",
                "LossDescription"));
        }

        if (string.IsNullOrWhiteSpace(details.CauseOfLossCode) || details.CauseOfLossCode.Trim().Length > MaxCauseOfLossCodeLength)
        {
            // BR-C-05 (existence and active state are checked against reference data by the caller)
            violations.Add(new(
                "CAUSE_OF_LOSS_INVALID",
                "Cause of loss code is not recognised or is inactive.",
                "CauseOfLossCode"));
        }

        if (details.LossLocation is { Length: > MaxLocationLength })
        {
            violations.Add(new("LOSS_LOCATION_TOO_LONG", $"Loss location must be at most {MaxLocationLength} characters.", "LossLocation"));
        }

        if (details.EstimatedLossAmount is < 0m)
        {
            violations.Add(new("ESTIMATED_LOSS_NEGATIVE", "Estimated loss amount cannot be negative.", "EstimatedLossAmount"));
        }

        if (details.PoliceReportNumber is { Length: > MaxPoliceReportNumberLength })
        {
            violations.Add(new(
                "POLICE_REPORT_NUMBER_TOO_LONG",
                $"Police report number must be at most {MaxPoliceReportNumberLength} characters.",
                "PoliceReportNumber"));
        }

        return violations;
    }
}
