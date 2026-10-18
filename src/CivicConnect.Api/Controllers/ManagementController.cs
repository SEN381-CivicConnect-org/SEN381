using System.Security.Claims;
using CivicConnect.Api.Services;
using CivicConnect.Data.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CivicConnect.Api.Controllers;

/// <summary>
/// Operations dashboard and staff incident listing (scope §2.2.1, §2.2.3).
/// </summary>
[ApiController]
[Route("api/management")]
public sealed class ManagementController : ControllerBase
{
    private const string ManagementRoles = "Management,Supervisor,operations manager,administrator,supervisor";
    private const string StaffRoles = "Staff,Technician,Supervisor,operations manager,technician,supervisor,administrator";
    private const string AdminOrManagerRoles = "Administrator,operations manager,administrator";

    private readonly IIncidentReportingService _reporting;
    private readonly IRetentionSweepService? _retentionSweepService;
    private readonly IUserDeIdentificationService? _deIdentificationService;

    public ManagementController(
        IIncidentReportingService reporting,
        IRetentionSweepService? retentionSweepService = null,
        IUserDeIdentificationService? deIdentificationService = null)
    {
        _reporting = reporting ?? throw new ArgumentNullException(nameof(reporting));
        _retentionSweepService = retentionSweepService;
        _deIdentificationService = deIdentificationService;
    }

    [HttpGet("incidents/open-status-counts")]
    [Authorize(Roles = ManagementRoles)]
    public async Task<ActionResult<IReadOnlyList<OpenStatusCountDto>>> GetOpenStatusCountsAsync(
        CancellationToken cancellationToken = default)
    {
        return Ok(await _reporting.GetOpenStatusCountsAsync(cancellationToken));
    }

    [HttpGet("incidents/overdue")]
    [Authorize(Roles = ManagementRoles)]
    public async Task<ActionResult<IReadOnlyList<OverdueIncidentDto>>> GetOverdueIncidentsAsync(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken cancellationToken = default)
    {
        return Ok(await _reporting.GetOverdueIncidentsAsync(page, pageSize, cancellationToken));
    }

    [HttpGet("incidents/resolution-breakdown/category")]
    [Authorize(Roles = ManagementRoles)]
    public async Task<ActionResult<IReadOnlyList<CategoryResolutionBreakdownDto>>> GetResolutionBreakdownByCategoryAsync(
        [FromQuery] DateTimeOffset? from = null,
        [FromQuery] DateTimeOffset? to = null,
        CancellationToken cancellationToken = default)
    {
        return Ok(await _reporting.GetResolutionBreakdownByCategoryAsync(from, to, cancellationToken));
    }

    [HttpGet("incidents/resolution-breakdown/team")]
    [Authorize(Roles = ManagementRoles)]
    public async Task<ActionResult<IReadOnlyList<TeamResolutionBreakdownDto>>> GetResolutionBreakdownByTeamAsync(
        [FromQuery] DateTimeOffset? from = null,
        [FromQuery] DateTimeOffset? to = null,
        CancellationToken cancellationToken = default)
    {
        return Ok(await _reporting.GetResolutionBreakdownByTeamAsync(from, to, cancellationToken));
    }

    [HttpGet("incidents/turnaround")]
    [Authorize(Roles = ManagementRoles)]
    public async Task<ActionResult<IReadOnlyList<TurnaroundSummaryDto>>> GetTurnaroundSummaryAsync(
        [FromQuery] DateTimeOffset? from = null,
        [FromQuery] DateTimeOffset? to = null,
        CancellationToken cancellationToken = default)
    {
        return Ok(await _reporting.GetTurnaroundSummaryAsync(from, to, cancellationToken));
    }

    /// Staff triage view; any combination of filters is optional.
    [HttpGet("incidents")]
    [Authorize(Roles = StaffRoles)]
    public async Task<ActionResult<IReadOnlyList<IncidentListItemDto>>> ListIncidentsAsync(
        [FromQuery] IncidentStatus? status = null,
        [FromQuery] int? categoryId = null,
        [FromQuery] int? teamId = null,
        [FromQuery] int? locationId = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        [FromQuery] bool onlyOpen = false,
        CancellationToken cancellationToken = default)
    {
        return Ok(await _reporting.ListIncidentsAsync(
            status, categoryId, teamId, locationId, page, pageSize, onlyOpen, cancellationToken));
    }

    /// Dedicated staff queue for a service team (NFR-07, AP-04).
    [HttpGet("teams/{teamId:int}/queue")]
    [Authorize(Roles = StaffRoles)]
    public async Task<ActionResult<IReadOnlyList<IncidentListItemDto>>> GetTeamQueueAsync(
        [FromRoute] int teamId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken cancellationToken = default)
    {
        return Ok(await _reporting.GetTeamQueueAsync(teamId, page, pageSize, cancellationToken));
    }

    /// <summary>
    /// Executes an on-demand retention sweep (NFR-05, POPIA s14(1), IT-RET-01).
    /// Purges idempotency records expired or older than 7 days, and read notifications older than 12 months.
    /// </summary>
    [HttpPost("retention/sweep")]
    [Authorize(Roles = "Administrator,operations manager")]
    public async Task<ActionResult<RetentionSweepSummaryDto>> RunRetentionSweepAsync(
        [FromServices] IRetentionSweepService? retentionSweepService = null,
        CancellationToken cancellationToken = default)
    {
        var service = _retentionSweepService ?? retentionSweepService
            ?? throw new InvalidOperationException("Retention sweep service is not available.");
        var summary = await service.SweepAsync(cancellationToken: cancellationToken);
        return Ok(summary);
    }

    /// <summary>
    /// Executes POPIA-compliant irreversible soft de-identification of user personal information (NFR-05, IT-RET-02).
    /// </summary>
    [HttpPost("users/{id}/de-identify")]
    [Authorize(Roles = "Administrator,operations manager")]
    public async Task<ActionResult<UserDeIdentificationResultDto>> DeIdentifyUserAsync(
        Guid id,
        [FromServices] IUserDeIdentificationService? deIdentificationService = null,
        CancellationToken cancellationToken = default)
    {
        var service = _deIdentificationService ?? deIdentificationService
            ?? throw new InvalidOperationException("User de-identification service is not available.");
        try
        {
            var actorClaim = User?.FindFirstValue(ClaimTypes.NameIdentifier) ?? User?.FindFirstValue("sub");
            Guid? actorId = Guid.TryParse(actorClaim, out var parsedActorId) && parsedActorId != Guid.Empty ? parsedActorId : null;

            var result = await service.DeIdentifyUserAsync(id, actorId, cancellationToken);
            return Ok(result);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }
}
