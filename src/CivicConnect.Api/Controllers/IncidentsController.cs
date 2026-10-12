using System.Security.Claims;
using CivicConnect.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CivicConnect.Api.Controllers;

/// <summary>
/// Endpoints for resolving incidents with asset linkage (FR-10) and querying the Finance Asset Replacement Report (AP-10).
/// </summary>
[ApiController]
[Route("api/incidents")]
public sealed class IncidentsController : ControllerBase
{
    private readonly IResolutionService _resolutionService;

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
}
