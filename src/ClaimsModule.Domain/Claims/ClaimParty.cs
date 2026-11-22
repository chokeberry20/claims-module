using System.Net.Mail;
using ClaimsModule.Domain.Common;

namespace ClaimsModule.Domain.Claims;

/// <summary>Input for adding a party to a claim (S5.2 step 2, S9.3).</summary>
public sealed record PartyDetails(
    PartyRole Role,
    PartyType Type,
    string? FirstName = null,
    string? LastName = null,
    string? CompanyName = null,
    string? Email = null,
    string? Phone = null,
    string? Notes = null);

/// <summary>
/// A person or organisation involved in a claim (S9.3). It is removed softly with IsActive = false
/// (S10.1), so history stays intact.
/// </summary>
public sealed class ClaimParty : Entity
{
    public const int MaxPersonNameLength = 100;
    public const int MaxCompanyNameLength = 255;
    public const int MaxEmailLength = 255;
    public const int MaxPhoneLength = 50;

    private ClaimParty()
    {
    }

    private ClaimParty(Guid id, Guid claimId)
        : base(id) => ClaimId = claimId;

    public Guid ClaimId { get; private set; }

    public PartyRole Role { get; private set; }

    public PartyType Type { get; private set; }

    public string? FirstName { get; private set; }

    public string? LastName { get; private set; }

    public string? CompanyName { get; private set; }

    public string? Email { get; private set; }

    public string? Phone { get; private set; }

    public string? Notes { get; private set; }

    public bool IsActive { get; private set; }

    public DateTimeOffset? RemovedAt { get; private set; }

    public string DisplayName => Type == PartyType.Company
        ? CompanyName ?? string.Empty
        : $"{FirstName} {LastName}".Trim();

    public bool IsActiveClaimant => IsActive && Role == PartyRole.Claimant;

    internal static ClaimParty Create(Guid claimId, PartyDetails details)
    {
        ArgumentNullException.ThrowIfNull(details);
        var violations = Validate(details);
        if (violations.Count > 0)
        {
            throw new DomainRuleViolationException(violations);
        }

        return new ClaimParty(SequentialGuid.NewGuid(), claimId)
        {
            Role = details.Role,
            Type = details.Type,
            FirstName = Clean(details.FirstName),
            LastName = Clean(details.LastName),
            CompanyName = Clean(details.CompanyName),
            Email = Clean(details.Email),
            Phone = Clean(details.Phone),
            Notes = Clean(details.Notes),
            IsActive = true,
        };
    }

    internal void Deactivate(DateTimeOffset now)
    {
        IsActive = false;
        RemovedAt = now.ToUniversalTime();
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static List<RuleViolation> Validate(PartyDetails d)
    {
        var violations = new List<RuleViolation>();

        if (!Enum.IsDefined(d.Role))
        {
            violations.Add(new("PARTY_ROLE_INVALID", "Party role is not supported.", "PartyRole"));
        }

        if (!Enum.IsDefined(d.Type))
        {
            violations.Add(new("PARTY_TYPE_INVALID", "Party type must be Person or Company.", "PartyType"));
        }
        else if (d.Type == PartyType.Person)
        {
            RequireLength(violations, d.FirstName, MaxPersonNameLength, "FirstName", "First name");
            RequireLength(violations, d.LastName, MaxPersonNameLength, "LastName", "Last name");
        }
        else
        {
            RequireLength(violations, d.CompanyName, MaxCompanyNameLength, "CompanyName", "Company name");
        }

        if (Clean(d.Email) is { } email
            && (email.Length > MaxEmailLength || !MailAddress.TryCreate(email, out var parsed) || parsed.Address != email))
        {
            violations.Add(new("PARTY_EMAIL_INVALID", "Email address is not valid.", "Email"));
        }

        if (Clean(d.Phone) is { Length: > MaxPhoneLength })
        {
            violations.Add(new("PARTY_PHONE_TOO_LONG", $"Phone must be at most {MaxPhoneLength} characters.", "Phone"));
        }

        return violations;
    }

    private static void RequireLength(List<RuleViolation> violations, string? value, int max, string field, string label)
    {
        var cleaned = Clean(value);
        if (cleaned is null)
        {
            violations.Add(new("PARTY_NAME_REQUIRED", $"{label} is required.", field));
        }
        else if (cleaned.Length > max)
        {
            violations.Add(new("PARTY_NAME_TOO_LONG", $"{label} must be at most {max} characters.", field));
        }
    }
}
