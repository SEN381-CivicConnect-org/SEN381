using System.Security.Claims;
using CivicConnect.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CivicConnect.Api.Controllers;

/// <summary>
/// A reporter's own submissions.
/// </summary>
[ApiController]
[Route("api/tickets")]
public sealed class TicketsController : ControllerBase
{
    private readonly IIncidentReportingService _reporting;

    public TicketsController(IIncidentReportingService reporting)
    {
        _reporting = reporting ?? throw new ArgumentNullException(nameof(reporting));
    }

    [HttpGet("mine")]
    [Authorize]
    public async Task<ActionResult<IReadOnlyList<ReporterTicketDto>>> ListMineAsync(
        CancellationToken cancellationToken = default)
    {
        var claimValue = User?.FindFirstValue(ClaimTypes.NameIdentifier) ?? User?.FindFirstValue("sub");
        if (!Guid.TryParse(claimValue, out var reporterId) || reporterId == Guid.Empty)
        {
            return Unauthorized(new { message = "Authenticated user identifier claim is required." });
        }

        return Ok(await _reporting.ListTicketsForReporterAsync(reporterId, cancellationToken));
    }
}
