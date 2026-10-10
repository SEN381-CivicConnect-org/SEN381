using System.Security.Claims;
using CivicConnect.Data;
using CivicConnect.Data.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CivicConnect.Web.Controllers;

[ApiController]
[Route("api/notifications")]
[Authorize]
public sealed class NotificationsController : ControllerBase
{
    private readonly CivicConnectDbContext _context;

    public NotificationsController(CivicConnectDbContext context)
    {
        _context = context;
    }

    /// <summary>
    /// [FR-13] Retrieves all unread notifications for the authenticated user.
    /// Extracts the user identifier strictly from the JWT token.
    /// </summary>
    /// <returns>Collection of unread notifications for the caller.</returns>
    [HttpGet("unread")]
    public async Task<IActionResult> GetUnreadNotifications()
    {
        if (!TryGetAuthenticatedUserId(out var userId))
        {
            return Unauthorized(new { message = "User identifier claim was not found in the authentication token." });
        }

        var unreadNotifications = await _context.Notifications
            .AsNoTracking()
            .Where(n => n.UserId == userId && !n.IsRead)
            .OrderByDescending(n => n.CreatedAt)
            .ToListAsync();

        return Ok(unreadNotifications);
    }

    /// <summary>
    /// [FR-13] Toggles the IsRead status of a specific notification.
    /// Enforces that users can only access and modify their own notifications.
    /// </summary>
    /// <param name="id">The unique identifier of the notification.</param>
    /// <returns>HTTP 200 OK with the updated notification status, or 403 Forbidden if not the owner.</returns>
    [HttpPut("{id}/mark-read")]
    public async Task<IActionResult> ToggleMarkRead(Guid id)
    {
        if (!TryGetAuthenticatedUserId(out var userId))
        {
            return Unauthorized(new { message = "User identifier claim was not found in the authentication token." });
        }

        var notification = await _context.Notifications.FindAsync(id);
        if (notification is null)
        {
            return NotFound(new { message = $"Notification with ID '{id}' was not found." });
        }

        // Security requirement: Users must only be able to modify their own notifications
        if (notification.UserId != userId)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { message = "You are not authorized to modify this notification." });
        }

        // Toggle the IsRead flag
        notification.IsRead = !notification.IsRead;
        await _context.SaveChangesAsync();

        return Ok(new
        {
            message = $"Notification marked as {(notification.IsRead ? "read" : "unread")}.",
            id = notification.Id,
            userId = notification.UserId,
            isRead = notification.IsRead,
            messageText = notification.Message,
            createdAt = notification.CreatedAt
        });
    }

    /// <summary>
    /// Extracts and parses the user identifier GUID from JWT claims.
    /// </summary>
    private bool TryGetAuthenticatedUserId(out Guid userId)
    {
        userId = Guid.Empty;
        var claimValue = User.FindFirstValue(ClaimTypes.NameIdentifier)
                      ?? User.FindFirstValue("sub")
                      ?? User.FindFirstValue("id")
                      ?? User.FindFirstValue("userId");

        return !string.IsNullOrWhiteSpace(claimValue) && Guid.TryParse(claimValue, out userId);
    }
}
