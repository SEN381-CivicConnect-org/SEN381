using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using CivicConnect.Data;
using CivicConnect.Data.Entities;
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
/// Data Transfer Object representing an overdue incident ([FR-12]).
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
    /// [FR-12] Overdue Request Visibility.
    /// Retrieves all open incidents (New, Assigned, InProgress, OnHold) where DueAt has passed,
    /// ordered by DueAt ascending, including elapsed overrun.
    /// Restricted to supervisors and management roles.
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
}