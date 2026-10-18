using System.Security.Claims;
using CivicConnect.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CivicConnect.Api.Controllers;

/// <summary>
/// Dedicated resolution controller exposing resolution codes, incident resolution, and the Finance Asset Replacement Report (FR-10, AP-10).
/// </summary>
[ApiController]
[Route("api/resolutions")]
public sealed class ResolutionsController : ControllerBase
{
    private readonly IResolutionService _resolutionService;

    public ResolutionsController(IResolutionService resolutionService)
    {
        _resolutionService = resolutionService ?? throw new ArgumentNullException(nameof(resolutionService));
    }

    [HttpGet("codes")]
    [HttpGet("/api/resolution-codes")]
    public async Task<ActionResult<IReadOnlyList<ResolutionCodeDto>>> GetResolutionCodesAsync(
        CancellationToken cancellationToken = default)
    {
        var codes = await _resolutionService.GetActiveResolutionCodesAsync(cancellationToken);
        return Ok(codes);
    }

    [HttpPost("incidents/{id:long}/resolve")]
    [HttpPut("incidents/{id:long}/resolve")]
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

    [HttpGet("reports/asset-replacements")]
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
