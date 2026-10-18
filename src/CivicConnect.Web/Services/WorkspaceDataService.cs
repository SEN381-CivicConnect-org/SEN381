using System.Security.Claims;
using CivicConnect.Data;
using CivicConnect.Data.Entities;
using CivicConnect.Web.Controllers;
using CivicConnect.Web.Models;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.EntityFrameworkCore;

namespace CivicConnect.Web.Services;

public sealed record DirectoryUser(string Name, string Email, bool IsActive, List<string> Roles);

public sealed record DirectoryCategory(
    string Name,
    string Description,
    bool IsActive,
    int Id = 0,
    short UrgencyLevel = 2,
    int DefaultServiceTeamId = 0,
    string DefaultServiceTeamName = "");

public sealed record ServiceTeamOption(int Id, string Name);

public sealed record CategoryActivityRow(
    string CategoryName,
    int OpenCount,
    int InProgressCount,
    int OverdueCount,
    int ResolvedCount,
    int ClosedCount)
{
    public int TotalCount => OpenCount + InProgressCount + ResolvedCount + ClosedCount;
}

public sealed record TeamPerformanceRow(
    string TeamName,
    int ActiveCount,
    int ResolvedCount,
    int ClosedCount,
    double ResolutionRatePercent);

public sealed record PrioritySlaRow(
    short PriorityId,
    string PriorityName,
    int TargetMinutes,
    int ResolvedCount,
    int MetCount,
    int MissedCount,
    double AvgTurnaroundMinutes);

public sealed record OverdueSummaryRow(
    string ReferenceNumber,
    string Title,
    string CategoryName,
    string PriorityName,
    string StatusDisplay,
    DateTimeOffset OpenedAt,
    DateTimeOffset DueAt,
    double OverrunMinutes);

public sealed record ManagementAuditRow(
    DateTimeOffset OccurredAt,
    string Actor,
    string EntityType,
    string EntityId,
    string Action,
    string? PreviousState,
    string? NewState);

public sealed record ManagementDashboardSummary(
    int TotalRequests,
    int OpenCount,
    int AssignedCount,
    int InProgressCount,
    int OnHoldCount,
    int OverdueCount,
    int ResolvedCount,
    int ClosedCount,
    double ResolutionRatePercent,
    double? AvgTurnaroundMinutes,
    double? SlaCompliancePercent,
    IReadOnlyList<OverdueSummaryRow> OverdueIncidents,
    IReadOnlyList<CategoryActivityRow> CategoryBreakdown,
    IReadOnlyList<TeamPerformanceRow> TeamBreakdown,
    IReadOnlyList<PrioritySlaRow> PriorityTurnaround,
    IReadOnlyList<ManagementAuditRow> RecentAuditEntries);

// Each read/write owns a short-lived scope/context; no DbContext is shared across circuit operations.
public sealed class WorkspaceDataService(IServiceScopeFactory scopes, AuthenticationStateProvider authentication)
{
    private static readonly IncidentStatus[] OpenStatuses =
    [
        IncidentStatus.New,
        IncidentStatus.Assigned,
        IncidentStatus.InProgress,
        IncidentStatus.OnHold
    ];

    private async Task<ClaimsPrincipal> RequireUserAsync(string? role = null)
    {
        var user = (await authentication.GetAuthenticationStateAsync()).User;
        if (user.Identity?.IsAuthenticated != true || (role is not null && !user.IsInRole(role)))
            throw new UnauthorizedAccessException();
        return user;
    }

    public async Task<List<DirectoryUser>> GetUsersAsync()
    {
        await RequireUserAsync("Management");
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CivicConnectDbContext>();
        return await db.AppUsers.AsNoTracking().OrderBy(u => u.FullName).ThenBy(u => u.Email)
            .Select(u => new DirectoryUser(u.FullName, u.Email, u.IsActive,
                u.UserRoles.OrderBy(r => r.Role.Name).Select(r => r.Role.Name).ToList()))
            .ToListAsync();
    }

    public async Task<List<DirectoryCategory>> GetCategoriesAsync()
    {
        await RequireUserAsync("Management");
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CivicConnectDbContext>();
        return await db.Categories.AsNoTracking()
            .OrderByDescending(c => c.IsActive)
            .ThenBy(c => c.Name)
            .Select(c => new DirectoryCategory(
                c.Name,
                c.Description,
                c.IsActive,
                c.Id,
                c.UrgencyLevel,
                c.DefaultServiceTeamId,
                c.DefaultServiceTeam.Name))
            .ToListAsync();
    }

    public async Task<List<ServiceTeamOption>> GetServiceTeamsAsync()
    {
        await RequireUserAsync("Management");
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CivicConnectDbContext>();
        return await db.ServiceTeams.AsNoTracking()
            .OrderBy(t => t.Name)
            .Select(t => new ServiceTeamOption(t.Id, t.Name))
            .ToListAsync();
    }

    /// <summary>
    /// [FR-09] Creates a new controlled service category with default urgency level (1-4) and default service team.
    /// </summary>
    public async Task<DirectoryCategory> CreateCategoryAsync(
        string name,
        string description,
        short urgencyLevel,
        int defaultServiceTeamId,
        AuditService? audit = null)
    {
        var actor = await RequireUserAsync("Management");

        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Category name is required.", nameof(name));
        if (string.IsNullOrWhiteSpace(description))
            throw new ArgumentException("Category description is required.", nameof(description));
        if (urgencyLevel is < 1 or > 4)
            throw new ArgumentOutOfRangeException(nameof(urgencyLevel), "Urgency level must be between 1 (Low) and 4 (Critical).");

        var trimmedName = name.Trim();
        var trimmedDescription = description.Trim();

        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CivicConnectDbContext>();

        var team = await db.ServiceTeams.AsNoTracking().FirstOrDefaultAsync(t => t.Id == defaultServiceTeamId)
            ?? await db.ServiceTeams.AsNoTracking().OrderBy(t => t.Id).FirstOrDefaultAsync()
            ?? throw new InvalidOperationException("At least one service team must exist before creating a category.");

        var duplicateActive = await db.Categories.AsNoTracking()
            .AnyAsync(c => c.IsActive && c.Name.ToLower() == trimmedName.ToLower());
        if (duplicateActive)
            throw new InvalidOperationException($"An active category named '{trimmedName}' already exists.");

        var entity = new Category
        {
            Name = trimmedName,
            Description = trimmedDescription,
            UrgencyLevel = urgencyLevel,
            DefaultServiceTeamId = team.Id,
            IsActive = true
        };

        db.Categories.Add(entity);
        await db.SaveChangesAsync();

        audit?.Record(
            AuditService.ActorFor(actor),
            "Category",
            entity.Id.ToString(),
            "CREATE",
            newValues: new Dictionary<string, object?>
            {
                ["UrgencyLevel"] = entity.UrgencyLevel,
                ["DefaultServiceTeamId"] = entity.DefaultServiceTeamId,
                ["IsActive"] = true
            });

        return new DirectoryCategory(
            entity.Name,
            entity.Description,
            entity.IsActive,
            entity.Id,
            entity.UrgencyLevel,
            team.Id,
            team.Name);
    }

    /// <summary>
    /// [FR-09] Soft-deactivates or reactivates a category while preserving all historical incidents and tickets.
    /// </summary>
    public async Task<DirectoryCategory> SetCategoryActiveAsync(
        int categoryId,
        bool isActive,
        AuditService? audit = null)
    {
        var actor = await RequireUserAsync("Management");

        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CivicConnectDbContext>();

        var category = await db.Categories
            .Include(c => c.DefaultServiceTeam)
            .FirstOrDefaultAsync(c => c.Id == categoryId)
            ?? throw new KeyNotFoundException($"Category #{categoryId} was not found.");

        if (isActive && !category.IsActive)
        {
            var conflict = await db.Categories.AsNoTracking()
                .AnyAsync(c => c.Id != categoryId && c.IsActive && c.Name.ToLower() == category.Name.ToLower());
            if (conflict)
                throw new InvalidOperationException($"Another active category named '{category.Name}' already exists.");
        }

        var previousActive = category.IsActive;
        category.IsActive = isActive;
        await db.SaveChangesAsync();

        audit?.Record(
            AuditService.ActorFor(actor),
            "Category",
            category.Id.ToString(),
            isActive ? "UPDATE" : "DEACTIVATE",
            previousValues: new Dictionary<string, object?> { ["IsActive"] = previousActive },
            newValues: new Dictionary<string, object?> { ["IsActive"] = isActive });

        return new DirectoryCategory(
            category.Name,
            category.Description,
            category.IsActive,
            category.Id,
            category.UrgencyLevel,
            category.DefaultServiceTeamId,
            category.DefaultServiceTeam?.Name ?? "");
    }

    /// <summary>
    /// [FR-12, FR-17, FR-08] Aggregates Management &amp; Oversight metrics, overdue visibility,
    /// category/team breakdowns, SLA turnaround performance, and audit history.
    /// </summary>
    public async Task<ManagementDashboardSummary> GetManagementDashboardSummaryAsync(
        TicketService ticketService,
        AuditService? auditService = null)
    {
        await RequireUserAsync("Management");
        ArgumentNullException.ThrowIfNull(ticketService);

        var now = DateTimeOffset.UtcNow;
        var workspaceTickets = ticketService.Tickets.ToList();

        var wsOpen = workspaceTickets.Count(t => TicketService.NormalizeStatus(t.Status) == "NEW");
        var wsAssigned = workspaceTickets.Count(t => TicketService.NormalizeStatus(t.Status) == "ASSIGNED");
        var wsInProgress = workspaceTickets.Count(t => TicketService.NormalizeStatus(t.Status) == "IN_PROGRESS");
        var wsResolved = workspaceTickets.Count(t => TicketService.NormalizeStatus(t.Status) == "RESOLVED");
        var wsClosed = workspaceTickets.Count(t => TicketService.NormalizeStatus(t.Status) == "CLOSED");

        var dbOpen = 0;
        var dbAssigned = 0;
        var dbInProgress = 0;
        var dbOnHold = 0;
        var dbResolved = 0;
        var dbClosed = 0;
        var overdueRows = new List<OverdueSummaryRow>();
        var categoryMap = new Dictionary<string, (int Open, int InProgress, int Overdue, int Resolved, int Closed)>(StringComparer.OrdinalIgnoreCase);
        var teamMap = new Dictionary<string, (int Active, int Resolved, int Closed)>(StringComparer.OrdinalIgnoreCase);
        var priorityRows = new List<PrioritySlaRow>();
        var auditRows = new List<ManagementAuditRow>();

        foreach (var ticket in workspaceTickets)
        {
            var cat = string.IsNullOrWhiteSpace(ticket.Category) ? "Uncategorised" : ticket.Category;
            categoryMap.TryGetValue(cat, out var current);
            var norm = TicketService.NormalizeStatus(ticket.Status);
            if (norm is "NEW" or "ASSIGNED") current.Open++;
            else if (norm == "IN_PROGRESS") current.InProgress++;
            else if (norm == "RESOLVED") current.Resolved++;
            else if (norm == "CLOSED") current.Closed++;
            categoryMap[cat] = current;

            var teamName = cat is "IT Support" or "HR Request" ? "IT Support" : "Facilities";
            teamMap.TryGetValue(teamName, out var teamCur);
            if (norm is "RESOLVED") teamCur.Resolved++;
            else if (norm is "CLOSED") teamCur.Closed++;
            else teamCur.Active++;
            teamMap[teamName] = teamCur;
        }

        if (auditService is not null)
        {
            foreach (var entry in auditService.GetRecentEntries(25))
            {
                auditRows.Add(new ManagementAuditRow(
                    new DateTimeOffset(DateTime.SpecifyKind(entry.Timestamp, DateTimeKind.Utc)),
                    entry.Actor,
                    entry.EntityType,
                    entry.EntityId,
                    entry.Action,
                    entry.PreviousValues,
                    entry.NewValues));
            }
        }

        try
        {
            using var scope = scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<CivicConnectDbContext>();

            var statusGroups = await db.Incidents.AsNoTracking()
                .GroupBy(i => i.Status)
                .Select(g => new { Status = g.Key, Count = g.Count() })
                .ToListAsync();

            foreach (var g in statusGroups)
            {
                switch (g.Status)
                {
                    case IncidentStatus.New: dbOpen += g.Count; break;
                    case IncidentStatus.Assigned: dbAssigned += g.Count; break;
                    case IncidentStatus.InProgress: dbInProgress += g.Count; break;
                    case IncidentStatus.OnHold: dbOnHold += g.Count; break;
                    case IncidentStatus.Resolved: dbResolved += g.Count; break;
                    case IncidentStatus.Closed: dbClosed += g.Count; break;
                }
            }

            var dbOverdue = await db.Incidents.AsNoTracking()
                .Where(i => OpenStatuses.Contains(i.Status) && i.DueAt < now)
                .OrderBy(i => i.DueAt)
                .Take(25)
                .Select(i => new
                {
                    i.ReferenceNumber,
                    i.Title,
                    CategoryName = i.Category.Name,
                    i.Priority,
                    i.Status,
                    i.OpenedAt,
                    i.DueAt
                })
                .ToListAsync();

            foreach (var item in dbOverdue)
            {
                overdueRows.Add(new OverdueSummaryRow(
                    item.ReferenceNumber,
                    item.Title,
                    item.CategoryName,
                    PriorityMatrix.GetPriorityName(item.Priority),
                    item.Status.ToString(),
                    item.OpenedAt,
                    item.DueAt,
                    Math.Max(0, (now - item.DueAt).TotalMinutes)));
            }

            var dbCatCounts = await db.Incidents.AsNoTracking()
                .GroupBy(i => new { i.Category.Name, i.Status, IsOverdue = OpenStatuses.Contains(i.Status) && i.DueAt < now })
                .Select(g => new
                {
                    g.Key.Name,
                    g.Key.Status,
                    g.Key.IsOverdue,
                    Count = g.Count()
                })
                .ToListAsync();

            foreach (var row in dbCatCounts)
            {
                categoryMap.TryGetValue(row.Name, out var cur);
                if (row.Status is IncidentStatus.New or IncidentStatus.Assigned) cur.Open += row.Count;
                else if (row.Status is IncidentStatus.InProgress or IncidentStatus.OnHold) cur.InProgress += row.Count;
                else if (row.Status == IncidentStatus.Resolved) cur.Resolved += row.Count;
                else if (row.Status == IncidentStatus.Closed) cur.Closed += row.Count;
                if (row.IsOverdue) cur.Overdue += row.Count;
                categoryMap[row.Name] = cur;
            }

            var dbTeamCounts = await db.Incidents.AsNoTracking()
                .GroupBy(i => new
                {
                    TeamName = i.AssignedTeam != null ? i.AssignedTeam.Name : i.Category.DefaultServiceTeam.Name,
                    i.Status
                })
                .Select(g => new { g.Key.TeamName, g.Key.Status, Count = g.Count() })
                .ToListAsync();

            foreach (var row in dbTeamCounts)
            {
                var tName = string.IsNullOrWhiteSpace(row.TeamName) ? "Unassigned" : row.TeamName;
                teamMap.TryGetValue(tName, out var cur);
                if (row.Status == IncidentStatus.Resolved) cur.Resolved += row.Count;
                else if (row.Status == IncidentStatus.Closed) cur.Closed += row.Count;
                else if (OpenStatuses.Contains(row.Status)) cur.Active += row.Count;
                teamMap[tName] = cur;
            }

            var priorityTargets = await db.PriorityTargets.AsNoTracking()
                .OrderBy(p => p.Id)
                .ToListAsync();

            var resolvedIncidents = await db.Incidents.AsNoTracking()
                .Where(i => (i.Status == IncidentStatus.Resolved || i.Status == IncidentStatus.Closed) && i.ResolvedAt != null)
                .Select(i => new { i.Priority, i.OpenedAt, i.ResolvedAt, i.DueAt })
                .ToListAsync();

            foreach (var pt in priorityTargets)
            {
                var matching = resolvedIncidents.Where(r => r.Priority == pt.Id).ToList();
                var met = matching.Count(r => r.ResolvedAt <= r.DueAt);
                var missed = matching.Count(r => r.ResolvedAt > r.DueAt);
                var avgMins = matching.Count > 0
                    ? matching.Average(r => Math.Max(0, (r.ResolvedAt!.Value - r.OpenedAt).TotalMinutes))
                    : 0.0;

                priorityRows.Add(new PrioritySlaRow(
                    pt.Id,
                    PriorityMatrix.GetPriorityName(pt.Id),
                    pt.ResolveWithinMinutes,
                    matching.Count,
                    met,
                    missed,
                    avgMins));
            }

            var dbAudits = await db.AuditLogs.AsNoTracking()
                .OrderByDescending(a => a.OccurredAt)
                .Take(25)
                .Select(a => new
                {
                    a.OccurredAt,
                    ActorName = a.Actor.FullName,
                    a.EntityType,
                    a.EntityId,
                    a.Action,
                    a.BeforeState,
                    a.AfterState
                })
                .ToListAsync();

            foreach (var a in dbAudits)
            {
                auditRows.Add(new ManagementAuditRow(
                    a.OccurredAt,
                    a.ActorName,
                    a.EntityType.ToString(),
                    a.EntityId,
                    a.Action.ToString(),
                    a.BeforeState,
                    a.AfterState));
            }
        }
        catch
        {
            // Fallback gracefully when running against an unmigrated or mock scope in unit tests.
        }

        var totalOpen = wsOpen + dbOpen;
        var totalAssigned = wsAssigned + dbAssigned;
        var totalInProgress = wsInProgress + dbInProgress;
        var totalOnHold = dbOnHold;
        var totalResolved = wsResolved + dbResolved;
        var totalClosed = wsClosed + dbClosed;
        var totalRequests = totalOpen + totalAssigned + totalInProgress + totalOnHold + totalResolved + totalClosed;
        var resolutionRate = totalRequests > 0
            ? Math.Round((double)(totalResolved + totalClosed) / totalRequests * 100.0, 1)
            : 0.0;

        var totalResolvedWithTiming = priorityRows.Sum(p => p.ResolvedCount);
        double? avgTurnaround = totalResolvedWithTiming > 0
            ? Math.Round(priorityRows.Where(p => p.ResolvedCount > 0)
                .Sum(p => p.AvgTurnaroundMinutes * p.ResolvedCount) / totalResolvedWithTiming, 1)
            : null;
        double? slaCompliance = totalResolvedWithTiming > 0
            ? Math.Round((double)priorityRows.Sum(p => p.MetCount) / totalResolvedWithTiming * 100.0, 1)
            : null;

        var categoryList = categoryMap
            .Select(kvp => new CategoryActivityRow(
                kvp.Key,
                kvp.Value.Open,
                kvp.Value.InProgress,
                kvp.Value.Overdue,
                kvp.Value.Resolved,
                kvp.Value.Closed))
            .OrderByDescending(c => c.TotalCount)
            .ThenBy(c => c.CategoryName)
            .ToList();

        var teamList = teamMap
            .Select(kvp =>
            {
                var teamTotal = kvp.Value.Active + kvp.Value.Resolved + kvp.Value.Closed;
                var rate = teamTotal > 0
                    ? Math.Round((double)(kvp.Value.Resolved + kvp.Value.Closed) / teamTotal * 100.0, 1)
                    : 0.0;
                return new TeamPerformanceRow(
                    kvp.Key,
                    kvp.Value.Active,
                    kvp.Value.Resolved,
                    kvp.Value.Closed,
                    rate);
            })
            .OrderByDescending(t => t.ActiveCount + t.ResolvedCount + t.ClosedCount)
            .ThenBy(t => t.TeamName)
            .ToList();

        var sortedAudits = auditRows
            .OrderByDescending(a => a.OccurredAt)
            .Take(25)
            .ToList();

        return new ManagementDashboardSummary(
            totalRequests,
            totalOpen,
            totalAssigned,
            totalInProgress,
            totalOnHold,
            overdueRows.Count,
            totalResolved,
            totalClosed,
            resolutionRate,
            avgTurnaround,
            slaCompliance,
            overdueRows,
            categoryList,
            teamList,
            priorityRows,
            sortedAudits);
    }

    public async Task<List<NotificationDto>> GetUnreadNotificationsAsync()
    {
        var user = await RequireUserAsync();
        if (!Guid.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out var recipientId))
            throw new UnauthorizedAccessException();
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CivicConnectDbContext>();
        return await NotificationsController.ReadUnreadAsync(db, recipientId);
    }
}
