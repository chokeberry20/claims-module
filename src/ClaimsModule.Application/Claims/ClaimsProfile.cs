using AutoMapper;
using ClaimsModule.Domain.Claims;
using ClaimsModule.Domain.Claims.Validation;
using ClaimsModule.Domain.Common;

namespace ClaimsModule.Application.Claims;

/// <summary>
/// Claim aggregate → detail DTOs (in-memory, on entities loaded from the database; D-67: flat maps,
/// at most two levels). Reserve totals and the cause-of-loss name come from separate reads.
/// </summary>
public sealed class ClaimsProfile : Profile
{
    public ClaimsProfile()
    {
        CreateMap<Money, decimal>().ConvertUsing(m => m.Amount);

        CreateMap<Claim, ClaimDetailDto>()
            .ForMember(d => d.ClaimId, o => o.MapFrom(c => c.Id))
            .ForMember(d => d.ClaimNumber, o => o.MapFrom(c => c.ClaimNumber.Value))
            .ForMember(d => d.Reserves, o => o.Ignore())
            .ForMember(d => d.Documents, o => o.Ignore())
            .ForMember(d => d.RecentAuditEntries, o => o.Ignore());

        CreateMap<LossEvent, LossEventDto>()
            .ForMember(d => d.CauseOfLossName, o => o.Ignore());

        CreateMap<ClaimParty, PartyDto>()
            .ForMember(d => d.PartyId, o => o.MapFrom(p => p.Id))
            .ForMember(d => d.DisplayName, o => o.MapFrom(p => p.DisplayName));

        CreateMap<ClaimRiskObject, RiskObjectDto>()
            .ForMember(d => d.RiskObjectId, o => o.MapFrom(r => r.Id));

        CreateMap<ClaimValidationIssue, ValidationIssueDto>().ConvertUsing(i => ValidationIssueDto.From(i));
    }
}
