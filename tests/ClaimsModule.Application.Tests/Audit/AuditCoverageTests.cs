using ClaimsModule.Application.Abstractions.Audit;
using ClaimsModule.Application.Common.Events;
using ClaimsModule.Domain.Claims.Events;
using ClaimsModule.Domain.Common;
using ClaimsModule.Domain.Reserves.Events;
using MediatR;

namespace ClaimsModule.Application.Tests.Audit;

/// <summary>Task 28: every domain event either has a before-commit audit handler or a documented reason not to.</summary>
public sealed class AuditCoverageTests
{
    /// <summary>Deliberately not audited on their own (see AuditTrailHandlers).</summary>
    private static readonly Type[] NotAudited = [typeof(ClaimWithdrawn), typeof(ReservePostingRequeued)];

    [Fact]
    public void Every_domain_event_is_audited_or_explicitly_excluded()
    {
        var domainEvents = typeof(DomainEvent).Assembly.GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false } && typeof(IDomainEvent).IsAssignableFrom(t))
            .ToHashSet();

        var audited = typeof(AssemblyReference).Assembly.GetTypes()
            .Where(t => t.Namespace == "ClaimsModule.Application.Audit")
            .SelectMany(t => t.GetInterfaces())
            .Where(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(INotificationHandler<>))
            .Select(i => i.GetGenericArguments()[0])
            .Where(n => n.IsGenericType && n.GetGenericTypeDefinition() == typeof(BeforeCommit<>))
            .Select(n => n.GetGenericArguments()[0])
            .ToHashSet();

        domainEvents.Except(audited).Except(NotAudited).Select(t => t.Name).ShouldBeEmpty("events with neither an audit handler nor an exclusion");
        audited.Intersect(NotAudited).ShouldBeEmpty();
        domainEvents.Count.ShouldBe(28);
    }

    [Fact]
    public void All_sixteen_spec_event_types_are_defined()
    {
        // S14.1 lists 16 audited event types (FR-71, corrected in the docs review).
        string[] spec =
        [
            AuditEventTypes.ClaimCreated, AuditEventTypes.StatusChanged, AuditEventTypes.PartyAdded, AuditEventTypes.PartyRemoved,
            AuditEventTypes.ReserveCreated, AuditEventTypes.ReserveAutoApproved, AuditEventTypes.ReserveApproved, AuditEventTypes.ReserveRejected,
            AuditEventTypes.ReserveRetracted, AuditEventTypes.GlPostingSimulated, AuditEventTypes.GlPostingFailed, AuditEventTypes.DocumentUploaded,
            AuditEventTypes.ClaimClosed, AuditEventTypes.ClaimReopened, AuditEventTypes.SlaBreachDetected, AuditEventTypes.ValidationIssueAdded,
        ];

        spec.Distinct().Count().ShouldBe(16);
    }
}
