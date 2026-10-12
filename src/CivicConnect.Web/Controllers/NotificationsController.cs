using System.Security.Claims;
using CivicConnect.Data;
using CivicConnect.Data.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CivicConnect.Web.Controllers;

/// <summary>
/// Data Transfer Object representing a notification projected with incident, ticket, and resolution context (FR-13).
/// </summary>
public sealed class NotificationDto
{
    public long Id { get; set; }
    public long IncidentId { get; set; }
    public string IncidentReferenceNumber { get; set; } = string.Empty;
    public string? TicketReferenceNumber { get; set; }
    public Guid RecipientId { get; set; }
    public NotificationKind Kind { get; set; }
    public IncidentStatus CurrentStatus { get; set; }
    public string? ResolutionSummary { get; set; }
    public string Message { get; set; } = string.Empty;
    public bool IsRead { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? ReadAt { get; set; }
}

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
    /// [FR-13] Retrieves all unread notifications for the authenticated user (AP-12).
    /// Extracts the recipient identifier strictly from the authentication claims and queries
    /// unread rows (ReadAt == null) via ix_notification_recipient_unread.
    /// </summary>
    /// <returns>Collection of unread notifications for the caller.</returns>
    [HttpGet("unread")]
    public async Task<IActionResult> GetUnreadNotifications()
    {
        if (!TryGetAuthenticatedUserId(out var recipientId))
        {
            return Unauthorized(new { message = "User identifier claim was not found in the authentication token." });
        }

        var unreadItems = await _context.Notifications
            .AsNoTracking()
            .Where(n => n.RecipientId == recipientId && n.ReadAt == null)
            .OrderByDescending(n => n.CreatedAt)
            .Select(n => new
            {
                n.Id,
                n.IncidentId,
                IncidentReferenceNumber = n.Incident.ReferenceNumber,
                TicketReferenceNumber = _context.Tickets
                    .Where(t => t.IncidentId == n.IncidentId && t.ReporterId == recipientId)
                    .OrderByDescending(t => t.SubmittedAt)
                    .Select(t => t.ReferenceNumber)
                    .FirstOrDefault(),
                n.RecipientId,
                n.Kind,
                CurrentStatus = n.Incident.Status,
                ResolutionSummary = _context.Resolutions
                    .Where(r => r.IncidentId == n.IncidentId && r.SupersededAt == null)
                    .Select(r => r.Summary)
                    .FirstOrDefault(),
                n.CreatedAt,
                n.ReadAt
            })
            .ToListAsync();

        var unreadNotifications = unreadItems
            .Select(n =>
            {
                var reference = !string.IsNullOrWhiteSpace(n.TicketReferenceNumber)
                    ? n.TicketReferenceNumber
                    : (!string.IsNullOrWhiteSpace(n.IncidentReferenceNumber)
                        ? n.IncidentReferenceNumber
                        : $"INC-{n.IncidentId:D6}");

                return new NotificationDto
                {
                    Id = n.Id,
                    IncidentId = n.IncidentId,
                    IncidentReferenceNumber = n.IncidentReferenceNumber,
                    TicketReferenceNumber = n.TicketReferenceNumber,
                    RecipientId = n.RecipientId,
                    Kind = n.Kind,
                    CurrentStatus = n.CurrentStatus,
                    ResolutionSummary = n.ResolutionSummary,
                    Message = BuildNotificationMessage(reference, n.Kind, n.CurrentStatus, n.ResolutionSummary),
                    IsRead = n.ReadAt != null,
                    CreatedAt = n.CreatedAt,
                    ReadAt = n.ReadAt
                };
            })
            .ToList();

        return Ok(unreadNotifications);
    }

    /// <summary>
    /// [FR-13] Toggles the ReadAt status of a specific notification.
    /// Enforces that users can only access and modify their own notifications.
    /// </summary>
    /// <param name="id">The unique identifier (bigint) of the notification.</param>
    /// <returns>HTTP 200 OK with the updated notification status, or 403 Forbidden if not the owner.</returns>
    [HttpPut("{id:long}/mark-read")]
    public async Task<IActionResult> ToggleMarkRead(long id)
    {
        if (!TryGetAuthenticatedUserId(out var recipientId))
        {
            return Unauthorized(new { message = "User identifier claim was not found in the authentication token." });
        }

        var notification = await _context.Notifications.FirstOrDefaultAsync(n => n.Id == id);
        if (notification is null)
        {
            return NotFound(new { message = $"Notification with ID '{id}' was not found." });
        }

        // Security requirement: Users must only be able to modify their own notifications
        if (notification.RecipientId != recipientId)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { message = "You are not authorized to modify this notification." });
        }

        // Toggle the ReadAt timestamp (null = unread, non-null = read)
        notification.ReadAt = notification.ReadAt.HasValue ? null : DateTimeOffset.UtcNow;
        await _context.SaveChangesAsync();

        return Ok(new
        {
            message = $"Notification marked as {(notification.ReadAt.HasValue ? "read" : "unread")}.",
            id = notification.Id,
            incidentId = notification.IncidentId,
            recipientId = notification.RecipientId,
            kind = notification.Kind,
            isRead = notification.ReadAt.HasValue,
            readAt = notification.ReadAt,
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

    private static string BuildNotificationMessage(
        string reference,
        NotificationKind kind,
        IncidentStatus currentStatus,
        string? resolutionSummary)
    {
        var statusDisplay = FormatStatusName(currentStatus);

        if (kind == NotificationKind.Resolution || currentStatus == IncidentStatus.Resolved)
        {
            return !string.IsNullOrWhiteSpace(resolutionSummary)
                ? $"Request {reference} status has changed to {statusDisplay}. Resolution: {resolutionSummary}"
                : $"Request {reference} status has changed to {statusDisplay}.";
        }

        if (kind == NotificationKind.Assignment)
        {
            return $"Request {reference} has been assigned (status: {statusDisplay}).";
        }

        return !string.IsNullOrWhiteSpace(resolutionSummary)
            ? $"Request {reference} status has changed to {statusDisplay}. Resolution: {resolutionSummary}"
            : $"Request {reference} status has changed to {statusDisplay}.";
    }

    private static string FormatStatusName(IncidentStatus status) => status switch
    {
        IncidentStatus.New => "New",
        IncidentStatus.Assigned => "Assigned",
        IncidentStatus.InProgress => "In Progress",
        IncidentStatus.OnHold => "On Hold",
        IncidentStatus.Resolved => "Resolved",
        IncidentStatus.Closed => "Closed",
        IncidentStatus.Rejected => "Rejected",
        IncidentStatus.Merged => "Merged",
        _ => status.ToString()
    };
}
