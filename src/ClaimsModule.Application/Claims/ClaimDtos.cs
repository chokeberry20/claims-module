using ClaimsModule.Domain.Claims;
using ClaimsModule.Domain.Claims.Validation;
using ClaimsModule.Domain.Common;
using ClaimsModule.Domain.Reserves;

namespace ClaimsModule.Application.Claims;

/// <summary>A party in an FNOL or add-party request (S5.2 step 2, S9.3).</summary>
public sealed record PartyInput(
    PartyRole Role,
    PartyType Type,
    string? FirstName = null,
    string? LastName = null,
    string? CompanyName = null,
    string? Email = null,
    string? Phone = null,
    string? Notes = null)
{
    public PartyDetails ToDetails() => new(Role, Type, FirstName, LastName, CompanyName, Email, Phone, Notes);
}

/// <summary>A risk object in an FNOL or add-risk-object request (S5.2 step 2, S9.4).</summary>
public sealed record RiskObjectInput(
    AssetType AssetType,
    string AssetDescription,
    string? DamageDescription = null,
    string? AssetReference = null,
    bool IsPrimary = false)
{
    public RiskObjectDetails ToDetails() => new(AssetType, AssetDescription, DamageDescription, AssetReference, IsPrimary);
}

/// <summary>A persisted validation issue (D-06) as returned by the API.</summary>
public sealed record ValidationIssueDto(
    Guid IssueId,
    string Code,
    ValidationSeverity Severity,
    string Message,
    string? Field,
    ValidationIssueStatus Status)
{
    public static ValidationIssueDto From(ClaimValidationIssue issue) =>
        new(issue.Id, issue.Code, issue.Severity, issue.Message, issue.Field, issue.Status);
}

/// <summary>A non-blocking warning returned with a successful command (S8 warnings, e.g. the $10M limit).</summary>
public sealed record WarningDto(string Code, string Message, string? Field)
{
    public static WarningDto From(RuleViolation violation) => new(violation.Code, violation.Message, violation.Field);
}

/// <summary>The outcome of a reserve submission (S6.3, S6.4).</summary>
public sealed record ReserveSubmissionDto(
    Guid ReserveComponentId,
    Guid TransactionId,
    ReserveComponentType ComponentType,
    ReserveTransactionType TransactionType,
    decimal Amount,
    decimal PreviousBalance,
    decimal NewBalance,
    ApprovalStatus ApprovalStatus,
    ApprovalLevel RequiredApprovalLevel,
    bool RequiresLimitOverride,
    IReadOnlyList<WarningDto> Warnings)
{
    public static ReserveSubmissionDto From(ClaimReserveComponent component, ReserveSubmissionResult result)
    {
        var t = result.Transaction;
        return new(
            component.Id, t.Id, t.ComponentType, t.TransactionType, t.Amount.Amount, t.PreviousBalance.Amount, t.NewBalance.Amount,
            t.ApprovalStatus, t.RequiredApprovalLevel, t.RequiresLimitOverride, result.Warnings.Select(WarningDto.From).ToList());
    }
}
