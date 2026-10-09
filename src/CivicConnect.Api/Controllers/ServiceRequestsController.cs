using System.Security.Claims;
using CivicConnect.Data;
using CivicConnect.Domain.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CivicConnect.Api.Controllers;

[ApiController]
[Route("api/servicerequests")]
public class ServiceRequestsController : ControllerBase
{
    private readonly CivicConnectDbContext _dbContext;

    public ServiceRequestsController(CivicConnectDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    /// <summary>
    /// [FR-04] Claims ownership of an unassigned service request for the authenticated staff member.
    /// </summary>
    /// <param name="id">The unique identifier (GUID) of the service request to claim.</param>
    /// <returns>
    /// 200 OK on success.
    /// 404 Not Found if the service request does not exist.
    /// 400 Bad Request if the service request is already claimed/assigned.
    /// </returns>
    [HttpPut("{id}/claim")]
    [Authorize(Roles = "Staff,Supervisor")]
    public async Task<IActionResult> Claim(Guid id)
    {
        // Extract the staffId directly from the authenticated user's JWT token
        var staffId = User.FindFirstValue(ClaimTypes.NameIdentifier)
                      ?? User.FindFirstValue("sub");

        if (string.IsNullOrWhiteSpace(staffId))
        {
            return Unauthorized(new { message = "User identity claim is missing from authenticated token." });
        }

        // Fetch the entity from PostgreSQL
        var serviceRequest = await _dbContext.ServiceRequests
            .FirstOrDefaultAsync(sr => sr.Id == id);

        if (serviceRequest is null)
        {
            return NotFound(new { message = $"Service request with ID '{id}' was not found." });
        }

        try
        {
            // Invoke the rich domain method containing invariant validation logic
            serviceRequest.Claim(staffId);

            // Persist the updated state to PostgreSQL
            await _dbContext.SaveChangesAsync();

            return Ok(new
            {
                message = "Service request successfully claimed.",
                id = serviceRequest.Id,
                assignedStaffId = serviceRequest.AssignedStaffId,
                status = serviceRequest.Status,
                lastModified = serviceRequest.LastModified
            });
        }
        catch (InvalidOperationException ex)
        {
            // 400 Bad Request if already claimed
            return BadRequest(new { message = ex.Message });
        }
        catch (ArgumentException ex)
        {
            // 400 Bad Request on invalid arguments
            return BadRequest(new { message = ex.Message });
        }
    }
}
