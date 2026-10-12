using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using CivicConnect.Data;
using CivicConnect.Data.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CivicConnect.Api.Controllers;

/// <summary>
/// Data Transfer Object for supervisor incident assignment ([FR-04]).
/// </summary>
public sealed class AssignIncidentRequestDto
{
    /// <summary>
    /// The unique identifier (<see cref="AppUser.Id"/>) of the target staff member / technician.
    /// </summary>
    [Required(ErrorMessage = "AssigneeId is required.")]
    public Guid AssigneeId { get; set; }

    /// <summary>
    /// Optional service team identifier (<see cref="ServiceTeam.Id"/>) to assign the incident to.
    /// </summary>
    public int? AssignedTeamId { get; set; }

    /// <summary>
    /// Optional note recorded in the incident status transition history.
    /// </summary>
    [MaxLength(500, ErrorMessage = "Note cannot exceed 500 characters.")]
    public string? Note { get; set; }
}

[ApiController]
[Route("api/incidents")]
public sealed class IncidentsController : ControllerBase
{
    private readonly CivicConnectDbContext _dbContext;

    public IncidentsController(CivicConnectDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    /// <summary>
    /// [FR-04] Claims ownership of an unassigned incident for the authenticated staff member / technician.
    /// Enforces double-claim prevention and optimistic concurrency via <see cref="Incident.Version"/> (NFR-06).
    /// </summary>
    /// <param name="id">The unique database identifier (<c>incident.id</c>) of the incident to claim.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    [HttpPut("{id:long}/claim")]
    [Authorize(Roles = "Staff,Technician,Supervisor,technician,supervisor")]
    public async Task<IActionResult> Claim(long id, CancellationToken cancellationToken = default)
    {
        var staffClaim = User?.FindFirstValue(ClaimTypes.NameIdentifier)
                         ?? User?.FindFirstValue("sub");

        if (string.IsNullOrWhiteSpace(staffClaim) || !Guid.TryParse(staffClaim, out var staffUserId) || staffUserId == Guid.Empty)
        {
            return Unauthorized(new { message = "Valid staff user identity claim (UUID) is missing from the authenticated token." });
        }

        var incident = await _dbContext.Incidents
            .Include(i => i.Assignee)
            .Include(i => i.Category)
            .FirstOrDefaultAsync(i => i.Id == id, cancellationToken);

        if (incident is null)
        {
            return NotFound(new { message = $"Incident with ID '{id}' was not found." });
        }

        if (incident.AssigneeId.HasValue)
        {
            var currentAssigneeName = incident.Assignee?.FullName ?? incident.AssigneeId.Value.ToString();
            return Conflict(new
            {
                message = $"Incident '{incident.ReferenceNumber ?? incident.Id.ToString()}' has already been claimed by {currentAssigneeName}.",
                id = incident.Id,
                referenceNumber = incident.ReferenceNumber,
                currentAssigneeId = incident.AssigneeId,
                currentAssigneeName,
                version = incident.Version
            });
        }

        if (incident.Status != IncidentStatus.New && incident.Status != IncidentStatus.Assigned)
        {
            return BadRequest(new
            {
                message = $"Incident '{incident.ReferenceNumber ?? incident.Id.ToString()}' cannot be claimed while in status '{incident.Status}'."
            });
        }

        var resolvedTeamId = await ResolveTeamIdForUserAsync(
            staffUserId,
            incident.AssignedTeamId,
            incident.Category?.DefaultServiceTeamId,
            cancellationToken);

        if (incident.Status == IncidentStatus.New)
        {
            await RecordTransitionToAssignedHistoryAsync(
                incident,
                staffUserId,
                staffUserId,
                resolvedTeamId,
                note: null,
                cancellationToken);

            incident.Status = IncidentStatus.Assigned;
        }

        incident.AssigneeId = staffUserId;
        incident.AssignedTeamId = resolvedTeamId;

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            var currentIncident = await _dbContext.Incidents
                .AsNoTracking()
                .Include(i => i.Assignee)
                .FirstOrDefaultAsync(i => i.Id == id, cancellationToken);

            var currentAssigneeName = currentIncident?.Assignee?.FullName
                ?? currentIncident?.AssigneeId?.ToString()
                ?? "another staff member";

            return Conflict(new
            {
                message = $"Incident '{currentIncident?.ReferenceNumber ?? id.ToString()}' was claimed concurrently by {currentAssigneeName}.",
                id,
                referenceNumber = currentIncident?.ReferenceNumber,
                currentAssigneeId = currentIncident?.AssigneeId,
                currentAssigneeName,
                version = currentIncident?.Version
            });
        }

        return Ok(new
        {
            message = "Incident successfully claimed.",
            id = incident.Id,
            referenceNumber = incident.ReferenceNumber,
            assigneeId = incident.AssigneeId,
            assignedTeamId = incident.AssignedTeamId,
            status = incident.Status,
            version = incident.Version
        });
    }

    /// <summary>
    /// [FR-04] Supervisor endpoint to assign an incident to a specific active staff member.
    /// Records ownership and status transition history when transitioning from NEW to ASSIGNED.
    /// </summary>
    /// <param name="id">The unique database identifier (<c>incident.id</c>) of the incident to assign.</param>
    /// <param name="dto">Payload specifying the target <see cref="AssignIncidentRequestDto.AssigneeId"/>, optional team, and note.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    [HttpPut("{id:long}/assign")]
    [Authorize(Roles = "Supervisor,supervisor,Management,operations manager,administrator")]
    public async Task<IActionResult> Assign(
        long id,
        [FromBody] AssignIncidentRequestDto? dto,
        CancellationToken cancellationToken = default)
    {
        if (dto is null || dto.AssigneeId == Guid.Empty)
        {
            return BadRequest(new { message = "A valid non-empty AssigneeId is required." });
        }

        var supervisorClaim = User?.FindFirstValue(ClaimTypes.NameIdentifier)
                              ?? User?.FindFirstValue("sub");

        if (string.IsNullOrWhiteSpace(supervisorClaim) || !Guid.TryParse(supervisorClaim, out var supervisorId) || supervisorId == Guid.Empty)
        {
            return Unauthorized(new { message = "Valid supervisor identity claim (UUID) is missing from the authenticated token." });
        }

        var targetAssignee = await _dbContext.AppUsers
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == dto.AssigneeId && u.IsActive, cancellationToken);

        if (targetAssignee is null)
        {
            return BadRequest(new { message = $"Target assignee '{dto.AssigneeId}' does not exist or is inactive." });
        }

        if (dto.AssignedTeamId.HasValue)
        {
            var teamExists = await _dbContext.ServiceTeams
                .AsNoTracking()
                .AnyAsync(st => st.Id == dto.AssignedTeamId.Value, cancellationToken);

            if (!teamExists)
            {
                return BadRequest(new { message = $"Service team '{dto.AssignedTeamId.Value}' does not exist." });
            }
        }

        var incident = await _dbContext.Incidents
            .Include(i => i.Assignee)
            .Include(i => i.Category)
            .FirstOrDefaultAsync(i => i.Id == id, cancellationToken);

        if (incident is null)
        {
            return NotFound(new { message = $"Incident with ID '{id}' was not found." });
        }

        if (incident.Status == IncidentStatus.Closed
            || incident.Status == IncidentStatus.Rejected
            || incident.Status == IncidentStatus.Merged)
        {
            return BadRequest(new
            {
                message = $"Incident '{incident.ReferenceNumber ?? incident.Id.ToString()}' cannot be assigned while in terminal status '{incident.Status}'."
            });
        }

        var resolvedTeamId = dto.AssignedTeamId ?? await ResolveTeamIdForUserAsync(
            dto.AssigneeId,
            incident.AssignedTeamId,
            incident.Category?.DefaultServiceTeamId,
            cancellationToken);

        if (incident.Status == IncidentStatus.New)
        {
            await RecordTransitionToAssignedHistoryAsync(
                incident,
                supervisorId,
                dto.AssigneeId,
                resolvedTeamId,
                string.IsNullOrWhiteSpace(dto.Note) ? null : dto.Note.Trim(),
                cancellationToken);

            incident.Status = IncidentStatus.Assigned;
        }

        incident.AssigneeId = dto.AssigneeId;
        incident.AssignedTeamId = resolvedTeamId;

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            var currentIncident = await _dbContext.Incidents
                .AsNoTracking()
                .Include(i => i.Assignee)
                .FirstOrDefaultAsync(i => i.Id == id, cancellationToken);

            var currentAssigneeName = currentIncident?.Assignee?.FullName
                ?? currentIncident?.AssigneeId?.ToString()
                ?? "another user";

            return Conflict(new
            {
                message = $"Incident '{currentIncident?.ReferenceNumber ?? id.ToString()}' was modified concurrently by {currentAssigneeName}.",
                id,
                referenceNumber = currentIncident?.ReferenceNumber,
                currentAssigneeId = currentIncident?.AssigneeId,
                currentAssigneeName,
                version = currentIncident?.Version
            });
        }

        return Ok(new
        {
            message = "Incident successfully assigned.",
            id = incident.Id,
            referenceNumber = incident.ReferenceNumber,
            assigneeId = incident.AssigneeId,
            assigneeName = targetAssignee.FullName,
            assignedTeamId = incident.AssignedTeamId,
            status = incident.Status,
            version = incident.Version
        });
    }

    private async Task<int?> ResolveTeamIdForUserAsync(
        Guid userId,
        int? currentTeamId,
        int? categoryDefaultTeamId,
        CancellationToken cancellationToken)
    {
        if (currentTeamId.HasValue)
        {
            return currentTeamId.Value;
        }

        var activeTeamIds = await _dbContext.TeamMembers
            .AsNoTracking()
            .Where(tm => tm.UserId == userId && tm.LeftAt == null)
            .Select(tm => tm.ServiceTeamId)
            .ToListAsync(cancellationToken);

        if (categoryDefaultTeamId.HasValue && activeTeamIds.Contains(categoryDefaultTeamId.Value))
        {
            return categoryDefaultTeamId.Value;
        }

        if (activeTeamIds.Count > 0)
        {
            return activeTeamIds[0];
        }

        return categoryDefaultTeamId;
    }

    private async Task RecordTransitionToAssignedHistoryAsync(
        Incident incident,
        Guid actorUserId,
        Guid targetAssigneeId,
        int? assignedTeamId,
        string? note,
        CancellationToken cancellationToken)
    {
        var lastHistory = await _dbContext.IncidentStatusHistories
            .AsNoTracking()
            .Where(h => h.IncidentId == incident.Id)
            .OrderByDescending(h => h.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (lastHistory is null)
        {
            _dbContext.IncidentStatusHistories.Add(new IncidentStatusHistory
            {
                IncidentId = incident.Id,
                FromStatus = null,
                ToStatus = IncidentStatus.New,
                ChangedBy = actorUserId,
                ChangedAt = incident.OpenedAt != default ? incident.OpenedAt : DateTimeOffset.UtcNow
            });

            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        if (lastHistory is null || lastHistory.ToStatus == IncidentStatus.New)
        {
            _dbContext.IncidentStatusHistories.Add(new IncidentStatusHistory
            {
                IncidentId = incident.Id,
                FromStatus = IncidentStatus.New,
                ToStatus = IncidentStatus.Assigned,
                ChangedBy = actorUserId,
                ChangedAt = DateTimeOffset.UtcNow,
                AssigneeId = targetAssigneeId,
                AssignedTeamId = assignedTeamId,
                Note = note
            });
        }
    }
}
