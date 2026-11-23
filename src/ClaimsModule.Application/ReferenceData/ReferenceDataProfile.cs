using AutoMapper;
using ClaimsModule.Domain.ReferenceData;

namespace ClaimsModule.Application.ReferenceData;

/// <summary>
/// Flat entity → DTO maps for reference data (brief §2.4: AutoMapper profiles live in Application).
/// Used with ProjectTo, so they are translated to SQL. The policy status needs "today", which is a
/// ProjectTo parameter; the expression mirrors <see cref="Policy.StatusOn"/> (a test keeps them equal).
/// </summary>
public sealed class ReferenceDataProfile : Profile
{
    public ReferenceDataProfile()
    {
        CreateMap<CauseOfLossCode, CauseOfLossCodeDto>();

        // Parameter for ProjectTo<PolicySummaryDto>(config, new { today }).
        var today = default(DateOnly);
        CreateMap<Policy, PolicySummaryDto>()
            .ForMember(d => d.PolicyId, o => o.MapFrom(p => p.Id))
            .ForMember(d => d.Status, o => o.MapFrom(p =>
                p.IsCancelled ? PolicyStatus.Cancelled
                : today > p.ExpirationDate ? PolicyStatus.Expired
                : PolicyStatus.Active));

        // In-memory map of one loaded policy; the handler supplies the status for today.
        CreateMap<Policy, PolicyCoverageDto>()
            .ForMember(d => d.PolicyId, o => o.MapFrom(p => p.Id))
            .ForMember(d => d.Status, o => o.Ignore());
    }
}
