using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using CivicConnect.Api.Services;
using CivicConnect.Data;
using CivicConnect.Data.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

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

/// <summary>
/// Data Transfer Object for supervisor priority override ([FR-06], [NFR-06]).
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

[ApiController]
[Route("api/incidents")]
public sealed class IncidentsController : ControllerBase
{
    private readonly CivicConnectDbContext _dbContext = null!;
    private readonly IResolutionService _resolutionService = null!;

    [ActivatorUtilitiesConstructor]
    public IncidentsController(CivicConnectDbContext dbContext, IResolutionService? resolutionService = null)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        _resolutionService = resolutionService ?? new ResolutionService(dbContext);
    }

    public IncidentsController(IResolutionService resolutionService)
    {
        _resolutionService = resolutionService ?? throw new ArgumentNullException(nameof(resolutionService));
    }

    /// <summary>
    /// Resolves an incident using a resolution code, concise summary, and required asset linkage when the code implies replacement (FR-10, IT-RES-01, IT-RES-02).
    /// </summary>
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

    /// <summary>
    /// Reopens a resolved incident (RESOLVED -> IN_PROGRESS) so a subsequent resolution supersedes the prior active resolution (IT-RES-02).
    /// </summary>
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

    /// <summary>
    /// Returns the Finance Asset Replacement Report over an optional time window (FR-10, AP-10).
    /// </summary>
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

        var wasNew = incident.Status == IncidentStatus.New;
        if (wasNew)
        {
            incident.Status = IncidentStatus.Assigned;
        }

        incident.AssigneeId = staffUserId;
        incident.AssignedTeamId = resolvedTeamId;

        var ownedTransaction = _dbContext.Database.CurrentTransaction is null
            ? await _dbContext.Database.BeginTransactionAsync(cancellationToken)
            : null;
        var activeTransaction = ownedTransaction ?? _dbContext.Database.CurrentTransaction!;
        const string claimSavepoint = "before_incident_claim";

        if (ownedTransaction is null)
        {
            await activeTransaction.CreateSavepointAsync(claimSavepoint, cancellationToken);
        }

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);

            if (wasNew)
            {
                await RecordTransitionToAssignedHistoryAsync(
                    incident,
                    staffUserId,
                    staffUserId,
                    resolvedTeamId,
                    note: null,
                    cancellationToken);

                await _dbContext.SaveChangesAsync(cancellationToken);
            }

            if (ownedTransaction is not null)
            {
                await ownedTransaction.CommitAsync(cancellationToken);
            }
        }
        catch (DbUpdateConcurrencyException)
        {
            if (ownedTransaction is not null)
            {
                await ownedTransaction.RollbackAsync(cancellationToken);
            }
            else
            {
                await activeTransaction.RollbackToSavepointAsync(claimSavepoint, cancellationToken);
            }

            _dbContext.ChangeTracker.Clear();

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
        finally
        {
            if (ownedTransaction is not null)
            {
                await ownedTransaction.DisposeAsync();
            }
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

        var wasNew = incident.Status == IncidentStatus.New;
        if (wasNew)
        {
            incident.Status = IncidentStatus.Assigned;
        }

        incident.AssigneeId = dto.AssigneeId;
        incident.AssignedTeamId = resolvedTeamId;

        var ownedTransaction = _dbContext.Database.CurrentTransaction is null
            ? await _dbContext.Database.BeginTransactionAsync(cancellationToken)
            : null;
        var activeTransaction = ownedTransaction ?? _dbContext.Database.CurrentTransaction!;
        const string assignSavepoint = "before_incident_assign";

        if (ownedTransaction is null)
        {
            await activeTransaction.CreateSavepointAsync(assignSavepoint, cancellationToken);
        }

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);

            if (wasNew)
            {
                await RecordTransitionToAssignedHistoryAsync(
                    incident,
                    supervisorId,
                    dto.AssigneeId,
                    resolvedTeamId,
                    string.IsNullOrWhiteSpace(dto.Note) ? null : dto.Note.Trim(),
                    cancellationToken);

                await _dbContext.SaveChangesAsync(cancellationToken);
            }

            if (ownedTransaction is not null)
            {
                await ownedTransaction.CommitAsync(cancellationToken);
            }
        }
        catch (DbUpdateConcurrencyException)
        {
            if (ownedTransaction is not null)
            {
                await ownedTransaction.RollbackAsync(cancellationToken);
            }
            else
            {
                await activeTransaction.RollbackToSavepointAsync(assignSavepoint, cancellationToken);
            }

            _dbContext.ChangeTracker.Clear();

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
        finally
        {
            if (ownedTransaction is not null)
            {
                await ownedTransaction.DisposeAsync();
            }
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

    /// <summary>
    /// [FR-06] Supervisor Priority Override with Recorded Reason and Optimistic Concurrency (NFR-06, IT-CONC-02).
    /// Updates the effective priority of an <see cref="Incident"/> while preserving <see cref="Incident.ComputedPriority"/>.
    /// </summary>
    [HttpPut("{id:long}/override-priority")]
    [Authorize(Roles = "Supervisor,supervisor,Staff,Management,operations manager,administrator")]
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

        var supervisorClaim = User?.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? User?.FindFirstValue("sub");

        if (string.IsNullOrWhiteSpace(supervisorClaim) || !Guid.TryParse(supervisorClaim, out var supervisorGuid) || supervisorGuid == Guid.Empty)
        {
            return Unauthorized(new { message = "A valid supervisor user ID (UUID) claim was not found in the authentication token." });
        }

        var priorityExists = await _dbContext.PriorityTargets
            .AsNoTracking()
            .AnyAsync(pt => pt.Id == dto.NewPriority, cancellationToken);

        if (!priorityExists)
        {
            return BadRequest(new { message = $"Priority target '{dto.NewPriority}' does not exist." });
        }

        var incident = await _dbContext.Incidents
            .FirstOrDefaultAsync(i => i.Id == id, cancellationToken);

        if (incident is null)
        {
            return NotFound(new { message = $"Incident with ID '{id}' was not found." });
        }

        if (dto.Version.HasValue)
        {
            if (incident.Version != dto.Version.Value)
            {
                return Conflict(new
                {
                    message = "The incident was modified by another user. Please refresh and try again.",
                    incidentId = incident.Id,
                    currentVersion = incident.Version
                });
            }

            _dbContext.Entry(incident).Property(i => i.Version).OriginalValue = dto.Version.Value;
        }

        incident.Priority = dto.NewPriority;
        incident.PriorityOverridden = true;
        incident.OverrideReason = trimmedReason;
        incident.OverrideBy = supervisorGuid;
        incident.OverrideAt = DateTimeOffset.UtcNow;

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);

            if (_dbContext.Database.IsRelational())
            {
                await _dbContext.Entry(incident).ReloadAsync(cancellationToken);
            }
        }
        catch (DbUpdateConcurrencyException)
        {
            _dbContext.ChangeTracker.Clear();

            var currentIncident = await _dbContext.Incidents
                .AsNoTracking()
                .FirstOrDefaultAsync(i => i.Id == id, cancellationToken);

            return Conflict(new
            {
                message = "The incident was modified concurrently by another user. Please reload and try again.",
                incidentId = id,
                currentVersion = currentIncident?.Version
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