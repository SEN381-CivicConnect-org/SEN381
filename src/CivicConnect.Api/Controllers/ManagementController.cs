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

    private readonly IIncidentReportingService _reporting;

    public ManagementController(IIncidentReportingService reporting)
    {
        _reporting = reporting ?? throw new ArgumentNullException(nameof(reporting));
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
}
