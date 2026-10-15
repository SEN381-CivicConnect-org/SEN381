using CivicConnect.Data;
using CivicConnect.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace CivicConnect.Api.Services;

/// <summary>
/// Management read path for the operations dashboard and staff/reporter incident listing (scope §2.2.1, §2.2.3).
/// </summary>
public interface IIncidentReportingService
{
    /// Open incident counts grouped by status.
    Task<IReadOnlyList<OpenStatusCountDto>> GetOpenStatusCountsAsync(
        CancellationToken cancellationToken = default);

    /// Open incidents past their due date, soonest-overdue first.
    Task<IReadOnlyList<OverdueIncidentDto>> GetOverdueIncidentsAsync(
        int page = 1,
        int pageSize = 50,
        CancellationToken cancellationToken = default);

    /// Resolved/closed counts per category, optionally windowed by opened_at.
    Task<IReadOnlyList<CategoryResolutionBreakdownDto>> GetResolutionBreakdownByCategoryAsync(
        DateTimeOffset? from = null,
        DateTimeOffset? to = null,
        CancellationToken cancellationToken = default);

    /// Resolved/closed counts per service team, optionally windowed by opened_at.
    Task<IReadOnlyList<TeamResolutionBreakdownDto>> GetResolutionBreakdownByTeamAsync(
        DateTimeOffset? from = null,
        DateTimeOffset? to = null,
        CancellationToken cancellationToken = default);

    /// Per-priority turnaround average and met/missed split against each priority's SLA.
    Task<IReadOnlyList<TurnaroundSummaryDto>> GetTurnaroundSummaryAsync(
        DateTimeOffset? from = null,
        DateTimeOffset? to = null,
        CancellationToken cancellationToken = default);

    /// Staff listing filtered by any combination of status, category, team, and location.
    Task<IReadOnlyList<IncidentListItemDto>> ListIncidentsAsync(
        IncidentStatus? status = null,
        int? categoryId = null,
        int? teamId = null,
        int? locationId = null,
        int page = 1,
        int pageSize = 50,
        CancellationToken cancellationToken = default);

    /// A reporter's own tickets with each one's current incident status.
    Task<IReadOnlyList<ReporterTicketDto>> ListTicketsForReporterAsync(
        Guid reporterId,
        CancellationToken cancellationToken = default);
}

/// EF-backed implementation; every query here should hit an index, never incident's full heap.
public sealed class IncidentReportingService : IIncidentReportingService
{
    private static readonly IncidentStatus[] OpenStatuses =
    {
        IncidentStatus.New, IncidentStatus.Assigned, IncidentStatus.InProgress, IncidentStatus.OnHold,
    };

    private static readonly IncidentStatus[] ResolvedOrClosed =
    {
        IncidentStatus.Resolved, IncidentStatus.Closed,
    };

    private readonly CivicConnectDbContext _context;

    public IncidentReportingService(CivicConnectDbContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public async Task<IReadOnlyList<OpenStatusCountDto>> GetOpenStatusCountsAsync(
        CancellationToken cancellationToken = default)
    {
        // status IN (open) narrows to ix_incident_status before the group-by.
        return await _context.Incidents
            .AsNoTracking()
            .Where(i => OpenStatuses.Contains(i.Status))
            .GroupBy(i => i.Status)
            .Select(g => new OpenStatusCountDto(g.Key, g.Count()))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<OverdueIncidentDto>> GetOverdueIncidentsAsync(
        int page = 1,
        int pageSize = 50,
        CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;

        // status+due_at matches ix_incident_due_at_open exactly (open rows only).
        return await _context.Incidents
            .AsNoTracking()
            .Where(i => OpenStatuses.Contains(i.Status) && i.DueAt < now)
            .OrderBy(i => i.DueAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(i => new OverdueIncidentDto(
                i.Id, i.ReferenceNumber, i.Title, i.Status, i.Priority, i.OpenedAt, i.DueAt))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<CategoryResolutionBreakdownDto>> GetResolutionBreakdownByCategoryAsync(
        DateTimeOffset? from = null,
        DateTimeOffset? to = null,
        CancellationToken cancellationToken = default)
    {
        var query = _context.Incidents
            .AsNoTracking()
            .Where(i => ResolvedOrClosed.Contains(i.Status));

        query = ApplyOpenedAtWindow(query, from, to);

        // Leans on ix_incident_status_category_id. The group-by key stays a bare FK column
        // (no Category navigation) because EF can't translate GroupBy-after-join reliably.
        var counts = await query
            .GroupBy(i => i.CategoryId)
            .Select(g => new
            {
                CategoryId = g.Key,
                ResolvedCount = g.Sum(i => i.Status == IncidentStatus.Resolved ? 1 : 0),
                ClosedCount = g.Sum(i => i.Status == IncidentStatus.Closed ? 1 : 0),
            })
            .ToListAsync(cancellationToken);

        var categoryNames = await _context.Categories
            .AsNoTracking()
            .ToDictionaryAsync(c => c.Id, c => c.Name, cancellationToken);

        return counts
            .Select(c => new CategoryResolutionBreakdownDto(
                c.CategoryId, categoryNames.GetValueOrDefault(c.CategoryId, "Unknown"), c.ResolvedCount, c.ClosedCount))
            .OrderByDescending(d => d.ResolvedCount + d.ClosedCount)
            .ToList();
    }

    public async Task<IReadOnlyList<TeamResolutionBreakdownDto>> GetResolutionBreakdownByTeamAsync(
        DateTimeOffset? from = null,
        DateTimeOffset? to = null,
        CancellationToken cancellationToken = default)
    {
        var query = _context.Incidents
            .AsNoTracking()
            .Where(i => ResolvedOrClosed.Contains(i.Status));

        query = ApplyOpenedAtWindow(query, from, to);

        // Leans on ix_incident_status_assigned_team_id; same bare-FK group-by key as the category breakdown.
        var counts = await query
            .GroupBy(i => i.AssignedTeamId)
            .Select(g => new
            {
                TeamId = g.Key,
                ResolvedCount = g.Sum(i => i.Status == IncidentStatus.Resolved ? 1 : 0),
                ClosedCount = g.Sum(i => i.Status == IncidentStatus.Closed ? 1 : 0),
            })
            .ToListAsync(cancellationToken);

        var teamNames = await _context.ServiceTeams
            .AsNoTracking()
            .ToDictionaryAsync(t => t.Id, t => t.Name, cancellationToken);

        return counts
            .Select(c => new TeamResolutionBreakdownDto(
                c.TeamId,
                c.TeamId.HasValue ? teamNames.GetValueOrDefault(c.TeamId.Value, "Unknown") : "Unassigned",
                c.ResolvedCount,
                c.ClosedCount))
            .OrderByDescending(d => d.ResolvedCount + d.ClosedCount)
            .ToList();
    }

    public async Task<IReadOnlyList<TurnaroundSummaryDto>> GetTurnaroundSummaryAsync(
        DateTimeOffset? from = null,
        DateTimeOffset? to = null,
        CancellationToken cancellationToken = default)
    {
        var priorities = await _context.PriorityTargets.AsNoTracking().ToListAsync(cancellationToken);
        var results = new List<TurnaroundSummaryDto>();

        foreach (var priorityTarget in priorities)
        {
            // One query per priority: status alone is too unselective (most incidents end up
            // resolved/closed), but status+priority narrows enough for ix_incident_status_priority.
            var query = _context.Incidents
                .AsNoTracking()
                .Where(i => ResolvedOrClosed.Contains(i.Status) && i.ResolvedAt != null && i.Priority == priorityTarget.Id);

            query = ApplyOpenedAtWindow(query, from, to);

            // TimeSpan arithmetic doesn't translate reliably to SQL, so the raw timestamps are
            // pulled back and the average/met-missed split is computed client-side.
            var rows = await query
                .Select(i => new { i.OpenedAt, i.ResolvedAt, i.DueAt })
                .ToListAsync(cancellationToken);

            if (rows.Count == 0)
            {
                continue;
            }

            results.Add(new TurnaroundSummaryDto(
                priorityTarget.Id,
                priorityTarget.Name,
                rows.Count,
                rows.Count(r => r.ResolvedAt <= r.DueAt),
                rows.Count(r => r.ResolvedAt > r.DueAt),
                rows.Average(r => (r.ResolvedAt!.Value - r.OpenedAt).TotalMinutes)));
        }

        return results.OrderBy(d => d.PriorityId).ToList();
    }

    public async Task<IReadOnlyList<IncidentListItemDto>> ListIncidentsAsync(
        IncidentStatus? status = null,
        int? categoryId = null,
        int? teamId = null,
        int? locationId = null,
        int page = 1,
        int pageSize = 50,
        CancellationToken cancellationToken = default)
    {
        // Each filter is optional; whichever combination is given, Postgres bitmap-ANDs the
        // single-column indexes on status/category_id/assigned_team_id/location_id.
        var query = _context.Incidents.AsNoTracking().AsQueryable();

        if (status.HasValue)
        {
            query = query.Where(i => i.Status == status.Value);
        }

        if (categoryId.HasValue)
        {
            query = query.Where(i => i.CategoryId == categoryId.Value);
        }

        if (teamId.HasValue)
        {
            query = query.Where(i => i.AssignedTeamId == teamId.Value);
        }

        if (locationId.HasValue)
        {
            query = query.Where(i => i.LocationId == locationId.Value);
        }

        return await query
            .OrderByDescending(i => i.OpenedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(i => new IncidentListItemDto(
                i.Id,
                i.ReferenceNumber,
                i.Title,
                i.Status,
                i.CategoryId,
                i.Category.Name,
                i.LocationId,
                i.Location.Name,
                i.AssignedTeamId,
                i.AssignedTeam == null ? null : i.AssignedTeam.Name,
                i.Priority,
                i.OpenedAt,
                i.DueAt,
                i.ResolvedAt,
                i.ClosedAt))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ReporterTicketDto>> ListTicketsForReporterAsync(
        Guid reporterId,
        CancellationToken cancellationToken = default)
    {
        // ticket.reporter_id already carries its FK index.
        return await _context.Tickets
            .AsNoTracking()
            .Where(t => t.ReporterId == reporterId)
            .OrderByDescending(t => t.SubmittedAt)
            .Select(t => new ReporterTicketDto(
                t.Id,
                t.ReferenceNumber,
                t.IncidentId,
                t.Incident.ReferenceNumber,
                t.Incident.Title,
                t.Incident.Status,
                t.SubmittedAt))
            .ToListAsync(cancellationToken);
    }

    /// Narrows a resolved/closed breakdown query to an optional opened_at date range.
    private static IQueryable<Incident> ApplyOpenedAtWindow(
        IQueryable<Incident> query, DateTimeOffset? from, DateTimeOffset? to)
    {
        if (from.HasValue)
        {
            query = query.Where(i => i.OpenedAt >= from.Value);
        }

        if (to.HasValue)
        {
            query = query.Where(i => i.OpenedAt < to.Value);
        }

        return query;
    }
}

/// One status's count among currently-open incidents.
public sealed record OpenStatusCountDto(IncidentStatus Status, int Count);

/// One open incident whose due date has already passed.
public sealed record OverdueIncidentDto(
    long Id,
    string ReferenceNumber,
    string Title,
    IncidentStatus Status,
    short Priority,
    DateTimeOffset OpenedAt,
    DateTimeOffset DueAt);

/// Resolved/closed counts for one category.
public sealed record CategoryResolutionBreakdownDto(
    int CategoryId, string CategoryName, int ResolvedCount, int ClosedCount);

/// Resolved/closed counts for one team (null team id means unassigned).
public sealed record TeamResolutionBreakdownDto(
    int? TeamId, string TeamName, int ResolvedCount, int ClosedCount);

/// Average turnaround and SLA met/missed counts for one priority level.
public sealed record TurnaroundSummaryDto(
    short PriorityId, string PriorityName, int ResolvedCount, int MetCount, int MissedCount, double AvgTurnaroundMinutes);

/// One incident row for the staff listing; turnaround/met-target are derived, not stored.
public sealed record IncidentListItemDto(
    long Id,
    string ReferenceNumber,
    string Title,
    IncidentStatus Status,
    int CategoryId,
    string CategoryName,
    int LocationId,
    string LocationName,
    int? AssignedTeamId,
    string? AssignedTeamName,
    short Priority,
    DateTimeOffset OpenedAt,
    DateTimeOffset DueAt,
    DateTimeOffset? ResolvedAt,
    DateTimeOffset? ClosedAt)
{
    public double? TurnaroundMinutes => ResolvedAt.HasValue ? (ResolvedAt.Value - OpenedAt).TotalMinutes : null;
    public bool? MetTarget => ResolvedAt.HasValue ? ResolvedAt.Value <= DueAt : null;
}

/// One reporter's ticket, carrying its incident's current status.
public sealed record ReporterTicketDto(
    long TicketId,
    string TicketReferenceNumber,
    long IncidentId,
    string IncidentReferenceNumber,
    string Title,
    IncidentStatus Status,
    DateTimeOffset SubmittedAt);
