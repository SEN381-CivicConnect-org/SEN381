using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using CivicConnect.Data;
using CivicConnect.Data.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CivicConnect.Web.Controllers;

/// <summary>
/// Data Transfer Object for supervisor priority override on an incident (FR-06).
/// </summary>
public sealed class OverridePriorityRequestDto
{
    /// <summary>
    /// Target priority_target.id (1 = critical, 2 = high, 3 = medium, 4 = low).
    /// </summary>
    [Required(ErrorMessage = "New priority is required.")]
    [Range(1, 4, ErrorMessage = "Priority must be between 1 (critical) and 4 (low).")]
    public short NewPriority { get; set; }

    [Required(ErrorMessage = "Override reason is required.")]
    [MaxLength(300)]
    public string Reason { get; set; } = string.Empty;
}

/// <summary>
/// Data Transfer Object for changing the status of an incident (FR-07 / FR-13).
/// </summary>
public sealed class ChangeStatusRequestDto
{
    public IncidentStatus? NewStatus { get; set; }
    public IncidentStatus? Status { get; set; }
    public string? Note { get; set; }

    public IncidentStatus TargetStatus => NewStatus ?? Status ?? IncidentStatus.New;
}

/// <summary>
/// Data Transfer Object representing an overdue incident (FR-12).
/// </summary>
public sealed class OverdueIncidentDto
{
    public long Id { get; set; }
    public string ReferenceNumber { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public short Priority { get; set; }
    public short ComputedPriority { get; set; }
    public IncidentStatus Status { get; set; }
    public DateTimeOffset DueAt { get; set; }
    public bool IsOverdue { get; set; }
    public bool PriorityOverridden { get; set; }
    public string? OverrideReason { get; set; }
    public Guid? OverrideBy { get; set; }
    public DateTimeOffset? OverrideAt { get; set; }
    public DateTimeOffset OpenedAt { get; set; }
}

/// <summary>
/// Data Transfer Object representing an active incident in the role-specific staff queue (FR-14).
/// </summary>
public sealed class TeamQueueItemDto
{
    public long Id { get; set; }
    public string ReferenceNumber { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public short Priority { get; set; }
    public short ComputedPriority { get; set; }
    public IncidentStatus Status { get; set; }
    public string CategoryName { get; set; } = string.Empty;
    public string? AssignedTeam { get; set; }
    public Guid? AssigneeId { get; set; }
    public DateTimeOffset OpenedAt { get; set; }
    public DateTimeOffset DueAt { get; set; }
    public bool IsOverdue { get; set; }
    public bool PriorityOverridden { get; set; }
    public string? OverrideReason { get; set; }
    public Guid? OverrideBy { get; set; }
    public DateTimeOffset? OverrideAt { get; set; }
}

[ApiController]
[Route("api/incidents")]
public sealed class IncidentsController : ControllerBase
{
    private readonly CivicConnectDbContext _context;

    public IncidentsController(CivicConnectDbContext context)
    {
        _context = context;
    }

    /// <summary>
    /// [FR-07 / FR-13] Updates the status of an incident and records an IncidentStatusHistory row.
    /// The PostgreSQL trigger incident_status_history_notify_trg automatically fans out notifications to subscribers.
    /// </summary>
    [HttpPut("{id:long}/status")]
    [HttpPatch("{id:long}/status")]
    [Authorize]
    public async Task<IActionResult> UpdateStatus(long id, [FromBody] ChangeStatusRequestDto dto)
    {
        if (dto is null)
        {
            return BadRequest(new { message = "Request body cannot be null." });
        }

        if (dto.NewStatus is null && dto.Status is null)
        {
            return BadRequest(new { message = "A valid target status is required." });
        }

        var actorGuid = await ResolveAuthenticatedUserIdAsync();
        if (actorGuid == Guid.Empty)
        {
            return Unauthorized(new { message = "Authenticated user identifier claim was not found." });
        }

        var incident = await _context.Incidents.FindAsync(id);
        if (incident is null)
        {
            return NotFound(new { message = $"Incident with ID '{id}' was not found." });
        }

        var previousStatus = incident.Status;
        var targetStatus = dto.TargetStatus;

        var isTransitionPermitted = await _context.IncidentStatusTransitions
            .AsNoTracking()
            .AnyAsync(t => t.FromStatus == previousStatus && t.ToStatus == targetStatus);

        if (!isTransitionPermitted)
        {
            return BadRequest(new
            {
                message = $"Status transition from '{previousStatus}' to '{targetStatus}' is not permitted."
            });
        }

        var now = DateTimeOffset.UtcNow;
        incident.Status = targetStatus;

        if (targetStatus == IncidentStatus.Resolved)
        {
            incident.ResolvedAt = now;
        }
        else if (targetStatus == IncidentStatus.Closed)
        {
            incident.ClosedAt = now;
        }

        var statusHistory = new IncidentStatusHistory
        {
            IncidentId = incident.Id,
            FromStatus = previousStatus,
            ToStatus = targetStatus,
            ChangedBy = actorGuid,
            ChangedAt = now,
            Note = dto.Note,
            AssignedTeamId = incident.AssignedTeamId,
            AssigneeId = incident.AssigneeId
        };

        await _context.IncidentStatusHistories.AddAsync(statusHistory);
        await _context.SaveChangesAsync();

        return Ok(new
        {
            message = "Incident status updated successfully.",
            incidentId = incident.Id,
            referenceNumber = incident.ReferenceNumber,
            previousStatus,
            status = incident.Status,
            changedAt = now
        });
    }

    /// <summary>
    /// [FR-06] Supervisor Priority Override with Recorded Reason.
    /// Strictly restricted to authenticated supervisors.
    /// </summary>
    [HttpPut("{id:long}/override-priority")]
    [Authorize(Roles = "Supervisor,supervisor")]
    public async Task<IActionResult> OverridePriority(long id, [FromBody] OverridePriorityRequestDto dto)
    {
        if (dto is null)
        {
            return BadRequest(new { message = "Request body cannot be null." });
        }

        if (dto.NewPriority is < 1 or > 4)
        {
            return BadRequest(new { message = "Priority must be between 1 (critical) and 4 (low)." });
        }

        if (string.IsNullOrWhiteSpace(dto.Reason))
        {
            return BadRequest(new { message = "An override reason must be provided." });
        }

        var supervisorGuid = await ResolveAuthenticatedUserIdAsync();
        if (supervisorGuid == Guid.Empty)
        {
            return Unauthorized(new { message = "Supervisor identifier claim was not found in the authentication token." });
        }

        var incident = await _context.Incidents.FindAsync(id);
        if (incident is null)
        {
            return NotFound(new { message = $"Incident with ID '{id}' was not found." });
        }

        var now = DateTimeOffset.UtcNow;
        incident.Priority = dto.NewPriority;
        incident.PriorityOverridden = true;
        incident.OverrideReason = dto.Reason.Trim();
        incident.OverrideBy = supervisorGuid;
        incident.OverrideAt = now;

        await _context.SaveChangesAsync();

        return Ok(new
        {
            message = "Priority overridden successfully.",
            incidentId = incident.Id,
            referenceNumber = incident.ReferenceNumber,
            priority = incident.Priority,
            computedPriority = incident.ComputedPriority,
            priorityOverridden = incident.PriorityOverridden,
            overrideReason = incident.OverrideReason,
            overrideBy = incident.OverrideBy,
            overrideAt = incident.OverrideAt,
            dueAt = incident.DueAt
        });
    }

    /// <summary>
    /// [FR-12] Overdue Incident Visibility.
    /// Retrieves all active incidents where the SLA due date has passed.
    /// Strictly restricted to supervisors and managers.
    /// </summary>
    [HttpGet("overdue")]
    [Authorize(Roles = "Supervisor,Manager,Management,supervisor,operations manager,administrator")]
    public async Task<IActionResult> GetOverdueIncidents()
    {
        var now = DateTimeOffset.UtcNow;

        var overdueIncidents = await _context.Incidents
            .AsNoTracking()
            .Where(i => (i.Status == IncidentStatus.New
                      || i.Status == IncidentStatus.Assigned
                      || i.Status == IncidentStatus.InProgress
                      || i.Status == IncidentStatus.OnHold)
                     && i.DueAt < now)
            .OrderBy(i => i.DueAt)
            .Select(i => new OverdueIncidentDto
            {
                Id = i.Id,
                ReferenceNumber = i.ReferenceNumber,
                Title = i.Title,
                Description = i.Description,
                Priority = i.Priority,
                ComputedPriority = i.ComputedPriority,
                Status = i.Status,
                DueAt = i.DueAt,
                IsOverdue = true,
                PriorityOverridden = i.PriorityOverridden,
                OverrideReason = i.OverrideReason,
                OverrideBy = i.OverrideBy,
                OverrideAt = i.OverrideAt,
                OpenedAt = i.OpenedAt
            })
            .ToListAsync();

        return Ok(overdueIncidents);
    }

    /// <summary>
    /// [FR-14] Role-Specific Staff Incident Queue.
    /// Retrieves active incidents assigned to the authenticated staff member's team(s) or directly to the staff member.
    /// Restricted to staff and supervisors; identity and team assignment are extracted server-side from claims and active team_member rows.
    /// </summary>
    [HttpGet("team-queue")]
    [Authorize(Roles = "Staff,Supervisor,technician,supervisor")]
    public async Task<IActionResult> GetTeamQueue()
    {
        var staffUserId = await ResolveAuthenticatedUserIdAsync();
        var staffTeamClaim = (User.FindFirstValue("Team") ?? User.FindFirstValue("team"))?.Trim();

        var teamIdSet = new HashSet<int>();

        if (staffUserId != Guid.Empty)
        {
            var memberTeamIds = await _context.TeamMembers
                .AsNoTracking()
                .Where(tm => tm.UserId == staffUserId && tm.LeftAt == null)
                .Select(tm => tm.ServiceTeamId)
                .ToListAsync();

            foreach (var teamId in memberTeamIds)
            {
                teamIdSet.Add(teamId);
            }
        }

        if (!string.IsNullOrWhiteSpace(staffTeamClaim))
        {
            var hasNumericId = int.TryParse(staffTeamClaim, out var parsedTeamId);
            var claimTeamIds = await _context.ServiceTeams
                .AsNoTracking()
                .Where(st => st.Name == staffTeamClaim || (hasNumericId && st.Id == parsedTeamId))
                .Select(st => st.Id)
                .ToListAsync();

            foreach (var teamId in claimTeamIds)
            {
                teamIdSet.Add(teamId);
            }
        }

        var hasDirectAssignment = staffUserId != Guid.Empty
            && await _context.Incidents
                .AsNoTracking()
                .AnyAsync(i => i.AssigneeId == staffUserId
                            && (i.Status == IncidentStatus.New
                                || i.Status == IncidentStatus.Assigned
                                || i.Status == IncidentStatus.InProgress
                                || i.Status == IncidentStatus.OnHold));

        if (teamIdSet.Count == 0 && !hasDirectAssignment)
        {
            return StatusCode(
                StatusCodes.Status403Forbidden,
                new { message = "User is not assigned to an active service team or direct incident assignment." });
        }

        var teamIds = teamIdSet.ToArray();
        var now = DateTimeOffset.UtcNow;

        var teamQueue = await _context.Incidents
            .AsNoTracking()
            .Where(i => (i.Status == IncidentStatus.New
                      || i.Status == IncidentStatus.Assigned
                      || i.Status == IncidentStatus.InProgress
                      || i.Status == IncidentStatus.OnHold)
                     && ((i.AssignedTeamId.HasValue && teamIds.Contains(i.AssignedTeamId.Value))
                         || (!i.AssignedTeamId.HasValue && teamIds.Contains(i.Category.DefaultServiceTeamId))
                         || (staffUserId != Guid.Empty && i.AssigneeId == staffUserId)))
            .OrderBy(i => i.Priority)
            .ThenBy(i => i.OpenedAt)
            .Select(i => new TeamQueueItemDto
            {
                Id = i.Id,
                ReferenceNumber = i.ReferenceNumber,
                Title = i.Title,
                Description = i.Description,
                Priority = i.Priority,
                ComputedPriority = i.ComputedPriority,
                Status = i.Status,
                CategoryName = i.Category.Name,
                AssignedTeam = i.AssignedTeam != null ? i.AssignedTeam.Name : i.Category.DefaultServiceTeam.Name,
                AssigneeId = i.AssigneeId,
                OpenedAt = i.OpenedAt,
                DueAt = i.DueAt,
                IsOverdue = i.DueAt < now,
                PriorityOverridden = i.PriorityOverridden,
                OverrideReason = i.OverrideReason,
                OverrideBy = i.OverrideBy,
                OverrideAt = i.OverrideAt
            })
            .ToListAsync();

        return Ok(teamQueue);
    }

    private async Task<Guid> ResolveAuthenticatedUserIdAsync()
    {
        var userIdClaim = User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? User.FindFirstValue("sub");

        if (Guid.TryParse(userIdClaim, out var parsedGuid))
        {
            return parsedGuid;
        }

        var emailClaim = User.FindFirstValue(ClaimTypes.Email);
        if (!string.IsNullOrWhiteSpace(emailClaim))
        {
            var userId = await _context.AppUsers
                .AsNoTracking()
                .Where(u => u.Email == emailClaim && u.IsActive)
                .Select(u => u.Id)
                .FirstOrDefaultAsync();

            return userId;
        }

        return Guid.Empty;
    }
}
