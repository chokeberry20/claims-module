using AutoMapper;
using ClaimsModule.Application.Claims;
using ClaimsModule.Domain.Claims;
using ClaimsModule.Domain.Reserves;
using ClaimsModule.Persistence.Conventions;
using Microsoft.EntityFrameworkCore;

namespace ClaimsModule.Persistence.Queries;

/// <summary>
/// Claim reads. The list is one filtered, paged SQL projection; the page's reserve totals come from a
/// second query, because <c>Money</c> uses a value converter that SQL can't aggregate (page ≤ 100 rows).
/// The detail loads the aggregate without tracking and maps it with the Application profiles.
/// </summary>
internal sealed class ClaimReader(ClaimsDbContext context, IMapper mapper) : IClaimReader
{
    public async Task<PagedResult<ClaimSummaryDto>> ListAsync(ClaimListFilter filter, CancellationToken cancellationToken)
    {
        var claims = context.Claims.AsNoTracking();

        if (filter.Statuses.Count > 0)
        {
            claims = claims.Where(c => filter.Statuses.Contains(c.Status));
        }

        if (filter.LossDateFrom is { } from)
        {
            claims = claims.Where(c => c.LossEvent.LossDate >= from);
        }

        if (filter.LossDateTo is { } to)
        {
            claims = claims.Where(c => c.LossEvent.LossDate <= to);
        }

        if (filter.AssignedHandlerId is { } handlerId)
        {
            claims = claims.Where(c => c.AssignedHandlerId == handlerId);
        }

        if (filter.AssignedHandler is { } handlerText)
        {
            claims = claims.Where(c => c.AssignedHandlerName != null && c.AssignedHandlerName.Contains(handlerText));
        }

        if (filter.CauseOfLossCode is { } code)
        {
            claims = claims.Where(c => c.LossEvent.CauseOfLossCode == code);
        }

        if (filter.Search is { } search)
        {
            // S10.1: partial claim number, policy number or client name, as case-insensitive substrings.
            // ClaimNumber is value-converted; ClaimNumberText exposes its stored text to LIKE.
            claims = claims.Where(c =>
                ClaimsDbFunctions.ClaimNumberText(c.ClaimNumber).Contains(search)
                || (c.PolicyNumber != null && c.PolicyNumber.Contains(search))
                || (c.ClientName != null && c.ClientName.Contains(search)));
        }

        var total = await claims.CountAsync(cancellationToken);

        IOrderedQueryable<Claim> ordered = (filter.SortBy, filter.Descending) switch
        {
            (ClaimSortField.LossDate, true) => claims.OrderByDescending(c => c.LossEvent.LossDate),
            (ClaimSortField.LossDate, false) => claims.OrderBy(c => c.LossEvent.LossDate),
            (ClaimSortField.ClaimNumber, true) => claims.OrderByDescending(c => c.ClaimNumber),
            (ClaimSortField.ClaimNumber, false) => claims.OrderBy(c => c.ClaimNumber),
            (ClaimSortField.Status, true) => claims.OrderByDescending(c => c.Status),
            (ClaimSortField.Status, false) => claims.OrderBy(c => c.Status),
            (_, false) => claims.OrderBy(c => c.ReportedDate),
            _ => claims.OrderByDescending(c => c.ReportedDate),
        };

        var rows = await ordered
            .ThenByDescending(c => c.Id)
            .Skip((filter.Page - 1) * filter.PageSize)
            .Take(filter.PageSize)
            .Select(c => new ClaimSummaryDto
            {
                ClaimId = c.Id,
                ClaimNumber = c.ClaimNumber.Value, // evaluated client-side on the materialised value object
                Status = c.Status,
                Severity = c.Severity,
                LossDate = c.LossEvent.LossDate,
                ReportedDate = c.ReportedDate,
                CauseOfLossCode = c.LossEvent.CauseOfLossCode,
                CauseOfLossName = context.CauseOfLossCodes.Where(k => k.Code == c.LossEvent.CauseOfLossCode).Select(k => k.Name).FirstOrDefault(),
                PolicyNumber = c.PolicyNumber,
                ClientName = c.ClientName,
                PrimaryClaimantName = c.Parties
                    .Where(p => p.IsActive && p.Role == PartyRole.Claimant)
                    .OrderBy(p => p.Id)
                    .Select(p => p.Type == PartyType.Company ? p.CompanyName : p.FirstName + " " + p.LastName)
                    .FirstOrDefault(),
                AssignedHandlerId = c.AssignedHandlerId,
                AssignedHandlerName = c.AssignedHandlerName,
                IsSlaBreached = c.IsSlaBreached,
            })
            .ToListAsync(cancellationToken);

        var totals = await GrossTotalsAsync(rows.Select(r => r.ClaimId).ToList(), cancellationToken);
        var items = rows.Select(r => r with { TotalReserves = totals.GetValueOrDefault(r.ClaimId) }).ToList();
        return new PagedResult<ClaimSummaryDto>(items, filter.Page, filter.PageSize, total);
    }

    public async Task<ClaimDetailDto?> GetDetailAsync(Guid claimId, CancellationToken cancellationToken)
    {
        var claim = await context.Claims
            .AsNoTracking()
            .Include(c => c.LossEvent)
            .Include(c => c.Parties)
            .Include(c => c.RiskObjects)
            .Include(c => c.ValidationIssues)
            .AsSplitQuery()
            .SingleOrDefaultAsync(c => c.Id == claimId, cancellationToken);
        if (claim is null)
        {
            return null;
        }

        var causeName = await context.CauseOfLossCodes.AsNoTracking()
            .Where(k => k.Code == claim.LossEvent.CauseOfLossCode).Select(k => k.Name).FirstOrDefaultAsync(cancellationToken);
        var components = await context.ReserveComponents.AsNoTracking()
            .Include(c => c.Transactions)
            .Where(c => c.ClaimId == claimId)
            .ToListAsync(cancellationToken);

        var detail = mapper.Map<ClaimDetailDto>(claim);
        return detail with
        {
            LossEvent = detail.LossEvent with { CauseOfLossName = causeName },
            Parties = detail.Parties.OrderByDescending(p => p.IsActive).ThenBy(p => p.Role).ToList(),
            Reserves = Totals(components),
        };
    }

    internal static ReserveTotalsDto Totals(IReadOnlyCollection<ClaimReserveComponent> components) => new()
    {
        ApprovedGross = ReserveSummary.ApprovedGrossTotal(components).Amount,
        Recoveries = components.Where(c => !c.ComponentType.CountsTowardsAggregateLimit()).Sum(c => c.CurrentAmount.Amount),
        HasPendingApproval = components.Any(c => c.HasPendingApproval),
        Components = components
            .OrderBy(c => c.ComponentType)
            .Select(c => new ReserveComponentSummaryDto
            {
                ReserveComponentId = c.Id,
                ComponentType = c.ComponentType,
                CurrentAmount = c.CurrentAmount.Amount,
                Status = c.Status,
                PendingTransactionId = c.PendingTransaction?.Id,
                PendingAmount = c.PendingTransaction?.Amount.Amount,
            })
            .ToList(),
    };

    private async Task<Dictionary<Guid, decimal>> GrossTotalsAsync(List<Guid> claimIds, CancellationToken cancellationToken)
    {
        if (claimIds.Count == 0)
        {
            return [];
        }

        var balances = await context.ReserveComponents.AsNoTracking()
            .Where(c => claimIds.Contains(c.ClaimId))
            .Select(c => new { c.ClaimId, c.ComponentType, c.CurrentAmount })
            .ToListAsync(cancellationToken);

        return balances
            .Where(b => b.ComponentType.CountsTowardsAggregateLimit())
            .GroupBy(b => b.ClaimId)
            .ToDictionary(g => g.Key, g => g.Sum(b => b.CurrentAmount.Amount));
    }
}

/// <summary>Reserve reads: components with their history, no tracking.</summary>
internal sealed class ReserveReader(ClaimsDbContext context, IMapper mapper) : ClaimsModule.Application.Reserves.IReserveReader
{
    public async Task<ClaimsModule.Application.Reserves.ClaimReservesDto?> GetForClaimAsync(Guid claimId, CancellationToken cancellationToken)
    {
        if (!await context.Claims.AnyAsync(c => c.Id == claimId, cancellationToken))
        {
            return null;
        }

        var components = await context.ReserveComponents.AsNoTracking()
            .Include(c => c.Transactions)
            .Where(c => c.ClaimId == claimId)
            .ToListAsync(cancellationToken);

        var history = components
            .SelectMany(c => c.Transactions)
            .OrderByDescending(t => t.SubmittedAt).ThenByDescending(t => t.ChangeSequence)
            .Select(mapper.Map<ClaimsModule.Application.Reserves.ReserveTransactionDto>)
            .ToList();

        return new ClaimsModule.Application.Reserves.ClaimReservesDto(claimId, ClaimReader.Totals(components), history);
    }
}

/// <summary>ClaimAuditLog reads (tenant-filtered by the context), newest first.</summary>
internal sealed class AuditLogReader(ClaimsDbContext context) : ClaimsModule.Application.Audit.IAuditLogReader
{
    public async Task<PagedResult<ClaimsModule.Application.Audit.AuditLogEntryDto>?> ListAsync(
        Guid claimId, string? eventType, int page, int pageSize, CancellationToken cancellationToken)
    {
        if (!await context.Claims.AnyAsync(c => c.Id == claimId, cancellationToken))
        {
            return null;
        }

        var entries = context.AuditLog.AsNoTracking().Where(a => a.ClaimId == claimId);
        if (eventType is not null)
        {
            entries = entries.Where(a => a.EventType == eventType);
        }

        var total = await entries.CountAsync(cancellationToken);
        var items = await entries
            .OrderByDescending(a => a.CreatedAt).ThenByDescending(a => a.AuditLogId)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(a => new ClaimsModule.Application.Audit.AuditLogEntryDto(
                a.AuditLogId, a.EventType, a.Description, a.OldValue, a.NewValue, a.RelatedEntityId,
                a.RelatedEntityType, a.CorrelationId, a.CreatedByUserId, a.CreatedAt))
            .ToListAsync(cancellationToken);

        return new PagedResult<ClaimsModule.Application.Audit.AuditLogEntryDto>(items, page, pageSize, total);
    }
}

/// <summary>Document metadata for a claim, newest first (no tracking).</summary>
internal sealed class ClaimDocumentReader(ClaimsDbContext context) : ClaimsModule.Application.Documents.IClaimDocumentReader
{
    public async Task<IReadOnlyList<ClaimsModule.Application.Documents.ClaimDocumentRecord>?> ListAsync(Guid claimId, CancellationToken cancellationToken)
    {
        if (!await context.Claims.AnyAsync(c => c.Id == claimId, cancellationToken))
        {
            return null;
        }

        return await context.ClaimDocuments.AsNoTracking()
            .Where(d => d.ClaimId == claimId)
            .OrderByDescending(d => d.UploadedAt).ThenByDescending(d => d.Id)
            .Select(d => new ClaimsModule.Application.Documents.ClaimDocumentRecord(
                d.Id, d.ClaimId, d.DocumentType, d.DocumentName, d.BlobPath, d.ContentType, d.FileSizeBytes,
                d.UploadedAt, d.UploadedByUserId, d.UploadedByName, d.Notes))
            .ToListAsync(cancellationToken);
    }
}
