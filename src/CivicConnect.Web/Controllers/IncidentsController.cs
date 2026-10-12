using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using CivicConnect.Data;
using CivicConnect.Data.Entities;
using CivicConnect.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CivicConnect.Web.Controllers;

/// <summary>
/// Data Transfer Object for supervisor priority override ([FR-06]).
/// </summary>
public sealed class OverridePriorityRequestDto
{
    /// <summary>
    /// Target priority ID (1 = critical, 2 = high, 3 = medium, 4 = low) corresponding to <see cref="PriorityTarget.Id"/>.
    /// </summary>
    [Required(ErrorMessage = "New priority is required.")]
    [Range(1, 4, ErrorMessage = "New priority must be between 1 (critical) and 4 (low).")]
    public short NewPriority { get; set; }

    /// <summary>
    /// Mandatory recorded justification for overriding the computed priority.
    /// </summary>
    [Required(AllowEmptyStrings = false, ErrorMessage = "Override reason is required.")]
    [MaxLength(300, ErrorMessage = "Override reason cannot exceed 300 characters.")]
    public string Reason { get; set; } = string.Empty;

    /// <summary>
    /// Optional optimistic concurrency version token of the incident being updated.
    /// </summary>
    public int? Version { get; set; }
}

/// <summary>
/// Data Transfer Object for changing the status of an incident (FR-07 / FR-13).
/// </summary>
public sealed class ChangeStatusRequestDto
{
    public IncidentStatus? NewStatus { get; set; }
    public IncidentStatus? Status { get; set; }
    public string? Note { get; set; }
    public string? ResolutionSummary { get; set; }
    public short? ResolutionCodeId { get; set; }
    public int? AssetId { get; set; }

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
    public int CategoryId { get; set; }
    public int LocationId { get; set; }
    public short Priority { get; set; }
    public short ComputedPriority { get; set; }
    public IncidentStatus Status { get; set; }
    public DateTimeOffset OpenedAt { get; set; }
    public DateTimeOffset DueAt { get; set; }
    public bool IsOverdue { get; set; }
    public TimeSpan Overrun { get; set; }
    public double OverrunMinutes { get; set; }
    public bool PriorityOverridden { get; set; }
    public string? OverrideReason { get; set; }
    public Guid? OverrideBy { get; set; }
    public DateTimeOffset? OverrideAt { get; set; }
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
    private readonly IResolutionService _resolutionService;

    public IncidentsController(CivicConnectDbContext context, IResolutionService? resolutionService = null)
    {
        _context = context;
        _resolutionService = resolutionService ?? new ResolutionService(context);
    }

    [HttpPost("{id:long}/resolve")]
    [HttpPut("{id:long}/resolve")]
    [Authorize(Roles = "Staff,Technician,Supervisor,technician,supervisor")]
    public async Task<IActionResult> ResolveIncidentAsync(
        long id,
        [FromBody] ResolveIncidentRequestDto request,
        CancellationToken cancellationToken = default)
    {
        if (request is null)
        {
            return BadRequest(new { message = "Resolution request payload is required." });
        }

        var resolvedByUserId = ResolveAuthenticatedUserId(request.ResolvedBy);
        if (resolvedByUserId == Guid.Empty)
        {
            return Unauthorized(new { message = "Authenticated user identifier claim is required to resolve an incident." });
        }

        try
        {
            var result = await _resolutionService.ResolveIncidentAsync(
                incidentId: id,
                request: request,
                resolvedByUserId: resolvedByUserId,
                cancellationToken: cancellationToken);

            return Ok(result);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPost("{id:long}/reopen")]
    [Authorize(Roles = "Staff,Technician,Supervisor,technician,supervisor")]
    public async Task<IActionResult> ReopenIncidentAsync(
        long id,
        [FromBody] ReopenIncidentRequestDto? request = null,
        CancellationToken cancellationToken = default)
    {
        var changedByUserId = ResolveAuthenticatedUserId(request?.ChangedBy);
        if (changedByUserId == Guid.Empty)
        {
            return Unauthorized(new { message = "Authenticated user identifier claim is required to reopen an incident." });
        }

        try
        {
            var incident = await _resolutionService.ReopenIncidentAsync(
                incidentId: id,
                changedByUserId: changedByUserId,
                note: request?.Note,
                cancellationToken: cancellationToken);

            return Ok(new
            {
                incident.Id,
                incident.ReferenceNumber,
                incident.Status,
                incident.ResolvedAt
            });
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpGet("reports/asset-replacements")]
    [HttpGet("/api/reports/asset-replacements")]
    [Authorize(Roles = "Management,Finance,Supervisor,operations manager,administrator,supervisor")]
    public async Task<ActionResult<IReadOnlyList<FinanceAssetReplacementReportItemDto>>> GetAssetReplacementReportAsync(
        [FromQuery] DateTimeOffset? from = null,
        [FromQuery] DateTimeOffset? to = null,
        [FromQuery] bool activeOnly = true,
        CancellationToken cancellationToken = default)
    {
        var items = await _resolutionService.GetFinanceReplacementReportAsync(
            from: from,
            to: to,
            activeOnly: activeOnly,
            cancellationToken: cancellationToken);

        return Ok(items);
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

        var actorId = await ResolveAuthenticatedUserIdAsync();
        if (actorId == Guid.Empty)
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

        var hasHistory = await _context.IncidentStatusHistories
            .AsNoTracking()
            .AnyAsync(h => h.IncidentId == incident.Id);

        if (!hasHistory)
        {
            var creationHistory = new IncidentStatusHistory
            {
                IncidentId = incident.Id,
                FromStatus = null,
                ToStatus = IncidentStatus.New,
                ChangedBy = actorId,
                ChangedAt = incident.OpenedAt != default ? incident.OpenedAt : now,
                Note = "Initial incident creation.",
                AssignedTeamId = incident.AssignedTeamId,
                AssigneeId = incident.AssigneeId
            };

            await _context.IncidentStatusHistories.AddAsync(creationHistory);
            await _context.SaveChangesAsync();
        }

        var statusHistory = new IncidentStatusHistory
        {
            IncidentId = incident.Id,
            FromStatus = previousStatus,
            ToStatus = targetStatus,
            ChangedBy = actorId,
            ChangedAt = now,
            Note = dto.Note,
            AssignedTeamId = incident.AssignedTeamId,
            AssigneeId = incident.AssigneeId
        };

        await _context.IncidentStatusHistories.AddAsync(statusHistory);

        incident.Status = targetStatus;
        if (targetStatus == IncidentStatus.Resolved)
        {
            incident.ResolvedAt = now;

            if (dto.ResolutionCodeId.HasValue || !string.IsNullOrWhiteSpace(dto.ResolutionSummary))
            {
                var resolution = new Resolution
                {
                    IncidentId = incident.Id,
                    ResolutionCodeId = dto.ResolutionCodeId ?? 1,
                    Summary = !string.IsNullOrWhiteSpace(dto.ResolutionSummary)
                        ? dto.ResolutionSummary.Trim()
                        : (!string.IsNullOrWhiteSpace(dto.Note) ? dto.Note.Trim() : "Incident resolved."),
                    AssetId = dto.AssetId,
                    ResolvedBy = actorId,
                    ResolvedAt = now
                };

                await _context.Resolutions.AddAsync(resolution);
            }
        }
        else if (targetStatus == IncidentStatus.Closed)
        {
            incident.ClosedAt = now;
        }

        await _context.SaveChangesAsync();

        return Ok(new
        {
            message = "Incident status updated successfully.",
            incidentId = incident.Id,
            referenceNumber = incident.ReferenceNumber,
            previousStatus,
            status = incident.Status,
            resolvedAt = incident.ResolvedAt,
            closedAt = incident.ClosedAt,
            changedAt = now
        });
    }

    /// <summary>
    /// [FR-06] Supervisor Priority Override with Recorded Reason.
    /// Updates the effective priority of an <see cref="Incident"/> while preserving <see cref="Incident.ComputedPriority"/>.
    /// </summary>
    /// <param name="id">The database identifier (<c>incident.id</c>) of the incident.</param>
    /// <param name="dto">The payload containing the new priority target ID, mandatory reason, and optional version.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    [HttpPut("{id:long}/override-priority")]
    [Authorize(Roles = "Supervisor,supervisor,Staff,Management")]
    public async Task<IActionResult> OverridePriority(
        long id,
        [FromBody] OverridePriorityRequestDto? dto,
        CancellationToken cancellationToken = default)
    {
        if (dto is null)
        {
            return BadRequest(new { message = "Request body cannot be null." });
        }

        if (string.IsNullOrWhiteSpace(dto.Reason))
        {
            return BadRequest(new { message = "An override reason must be provided." });
        }

        var trimmedReason = dto.Reason.Trim();
        if (trimmedReason.Length > 300)
        {
            return BadRequest(new { message = "Override reason cannot exceed 300 characters." });
        }

        if (dto.NewPriority < 1 || dto.NewPriority > 4)
        {
            return BadRequest(new { message = "New priority must be between 1 (critical) and 4 (low)." });
        }

        // Extract and validate the authenticated supervisor's user ID (UUID / Guid in app_user.id)
        var supervisorClaim = User?.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? User?.FindFirstValue("sub");

        if (string.IsNullOrWhiteSpace(supervisorClaim) || !Guid.TryParse(supervisorClaim, out var supervisorGuid))
        {
            return Unauthorized(new { message = "A valid supervisor user ID (UUID) claim was not found in the authentication token." });
        }

        var priorityExists = await _context.PriorityTargets
            .AnyAsync(pt => pt.Id == dto.NewPriority, cancellationToken);

        if (!priorityExists)
        {
            return BadRequest(new { message = $"Priority target '{dto.NewPriority}' does not exist." });
        }

        var incident = await _context.Incidents
            .FirstOrDefaultAsync(i => i.Id == id, cancellationToken);

        if (incident is null)
        {
            return NotFound(new { message = $"Incident with ID '{id}' was not found." });
        }

        if (dto.Version.HasValue && incident.Version != dto.Version.Value)
        {
            return Conflict(new
            {
                message = "The incident was modified by another user. Please refresh and try again.",
                incidentId = incident.Id,
                currentVersion = incident.Version
            });
        }

        // Preserve incident.ComputedPriority untouched so the original calculated priority stays visible.
        incident.Priority = dto.NewPriority;
        incident.PriorityOverridden = true;
        incident.OverrideReason = trimmedReason;
        incident.OverrideBy = supervisorGuid;
        incident.OverrideAt = DateTimeOffset.UtcNow;

        try
        {
            await _context.SaveChangesAsync(cancellationToken);

            if (_context.Database.IsRelational())
            {
                await _context.Entry(incident).ReloadAsync(cancellationToken);
            }
        }
        catch (DbUpdateConcurrencyException)
        {
            return Conflict(new
            {
                message = "The incident was modified concurrently by another user. Please reload and try again.",
                incidentId = id
            });
        }

        return Ok(new
        {
            message = "Priority overridden successfully.",
            incidentId = incident.Id,
            referenceNumber = incident.ReferenceNumber,
            computedPriority = incident.ComputedPriority,
            priority = incident.Priority,
            priorityOverridden = incident.PriorityOverridden,
            overrideReason = incident.OverrideReason,
            overrideBy = incident.OverrideBy,
            overrideAt = incident.OverrideAt,
            openedAt = incident.OpenedAt,
            dueAt = incident.DueAt,
            version = incident.Version
        });
    }

    /// <summary>
    /// [FR-12] Overdue Incident Visibility.
    /// Retrieves all active incidents where the SLA due date has passed.
    /// Strictly restricted to supervisors and managers.
    /// </summary>
    [HttpGet("overdue")]
    [Authorize(Roles = "Supervisor,Manager,Operations Manager,Administrator,supervisor,operations manager,administrator,Management")]
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
            .ToListAsync();

        var response = overdueIncidents
            .Select(i => new OverdueIncidentDto
            {
                Id = i.Id,
                ReferenceNumber = i.ReferenceNumber,
                Title = i.Title,
                Description = i.Description,
                CategoryId = i.CategoryId,
                LocationId = i.LocationId,
                Priority = i.Priority,
                ComputedPriority = i.ComputedPriority,
                Status = i.Status,
                OpenedAt = i.OpenedAt,
                DueAt = i.DueAt,
                IsOverdue = true,
                Overrun = now - i.DueAt,
                OverrunMinutes = (now - i.DueAt).TotalMinutes,
                PriorityOverridden = i.PriorityOverridden,
                OverrideReason = i.OverrideReason,
                OverrideBy = i.OverrideBy,
                OverrideAt = i.OverrideAt
            })
            .ToList();

        return Ok(response);
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

    private Guid ResolveAuthenticatedUserId(Guid? fallbackUserId)
    {
        var claimValue = User?.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? User?.FindFirstValue("sub");

        if (Guid.TryParse(claimValue, out var parsedClaimUserId) && parsedClaimUserId != Guid.Empty)
        {
            return parsedClaimUserId;
        }

        if (fallbackUserId.HasValue && fallbackUserId.Value != Guid.Empty)
        {
            return fallbackUserId.Value;
        }

        return Guid.Empty;
    }

    private async Task<Guid> ResolveAuthenticatedUserIdAsync()
    {
        var userIdClaim = User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? User.FindFirstValue("sub")
            ?? User.FindFirstValue("id")
            ?? User.FindFirstValue("userId");

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