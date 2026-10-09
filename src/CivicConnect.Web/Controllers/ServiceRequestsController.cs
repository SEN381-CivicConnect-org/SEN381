using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using CivicConnect.Data;
using CivicConnect.Data.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CivicConnect.Web.Controllers;

/// <summary>
/// Data Transfer Object for supervisor priority override.
/// </summary>
public sealed class OverridePriorityRequestDto
{
    [Required(ErrorMessage = "New priority is required.")]
    public PriorityLevel NewPriority { get; set; }

    [Required(ErrorMessage = "Override reason is required.")]
    public string Reason { get; set; } = string.Empty;
}

/// <summary>
/// Data Transfer Object representing an overdue service request.
/// </summary>
public sealed class OverdueServiceRequestDto
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public PriorityLevel Priority { get; set; }
    public RequestStatus Status { get; set; }
    public DateTimeOffset? DueAt { get; set; }
    public bool IsOverdue { get; set; }
    public bool IsSupervisorOverridden { get; set; }
    public string? OverrideReason { get; set; }
    public string? OverriddenBySupervisorId { get; set; }
    public DateTimeOffset? OverriddenAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

[ApiController]
[Route("api/servicerequests")]
public sealed class ServiceRequestsController : ControllerBase
{
    private readonly CivicConnectDbContext _context;

    public ServiceRequestsController(CivicConnectDbContext context)
    {
        _context = context;
    }

    /// <summary>
    /// [FR-06] Supervisor Priority Override with Recorded Reason.
    /// Strictly restricted to authenticated supervisors.
    /// </summary>
    /// <param name="id">The unique identifier of the service request.</param>
    /// <param name="dto">The payload containing the new priority level and recorded reason.</param>
    /// <returns>
    /// 200 OK on success,
    /// 404 Not Found if the service request does not exist,
    /// 400 Bad Request if validation fails,
    /// 401/403 if unauthorized or insufficient privileges.
    /// </returns>
    [HttpPut("{id}/override-priority")]
    [Authorize(Roles = "Supervisor")]
    public async Task<IActionResult> OverridePriority(Guid id, [FromBody] OverridePriorityRequestDto dto)
    {
        if (dto is null)
        {
            return BadRequest(new { message = "Request body cannot be null." });
        }

        if (string.IsNullOrWhiteSpace(dto.Reason))
        {
            return BadRequest(new { message = "An override reason must be provided." });
        }

        // Securely extract the supervisor identifier from the authenticated user's JWT claims
        var supervisorId = User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? User.FindFirstValue("sub");

        if (string.IsNullOrWhiteSpace(supervisorId))
        {
            return Unauthorized(new { message = "Supervisor identifier claim was not found in the authentication token." });
        }

        var serviceRequest = await _context.ServiceRequests.FindAsync(id);
        if (serviceRequest is null)
        {
            return NotFound(new { message = $"Service request with ID '{id}' was not found." });
        }

        try
        {
            serviceRequest.OverridePriority(dto.NewPriority, dto.Reason, supervisorId);
            await _context.SaveChangesAsync();

            return Ok(new
            {
                message = "Priority overridden successfully.",
                serviceRequestId = serviceRequest.Id,
                priority = serviceRequest.Priority,
                isSupervisorOverridden = serviceRequest.IsSupervisorOverridden,
                overrideReason = serviceRequest.OverrideReason,
                overriddenBySupervisorId = serviceRequest.OverriddenBySupervisorId,
                overriddenAt = serviceRequest.OverriddenAt,
                dueAt = serviceRequest.DueAt
            });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary>
    /// [FR-12] Overdue Request Visibility.
    /// Retrieves all service requests where SLA due date has passed and the request is not resolved or closed.
    /// Strictly restricted to supervisors and managers.
    /// </summary>
    /// <returns>Collection of overdue service request DTOs with HTTP 200 OK.</returns>
    [HttpGet("overdue")]
    [Authorize(Roles = "Supervisor,Manager")]
    public async Task<IActionResult> GetOverdueRequests()
    {
        var now = DateTimeOffset.UtcNow;

        var overdueRequests = await _context.ServiceRequests
            .AsNoTracking()
            .Where(r => r.DueAt.HasValue
                     && r.DueAt.Value < now
                     && r.Status != RequestStatus.Resolved
                     && r.Status != RequestStatus.Closed)
            .OrderBy(r => r.DueAt)
            .Select(r => new OverdueServiceRequestDto
            {
                Id = r.Id,
                Title = r.Title,
                Description = r.Description,
                Priority = r.Priority,
                Status = r.Status,
                DueAt = r.DueAt,
                IsOverdue = true,
                IsSupervisorOverridden = r.IsSupervisorOverridden,
                OverrideReason = r.OverrideReason,
                OverriddenBySupervisorId = r.OverriddenBySupervisorId,
                OverriddenAt = r.OverriddenAt,
                CreatedAt = r.CreatedAt
            })
            .ToListAsync();

        return Ok(overdueRequests);
    }
}
