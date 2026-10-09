namespace CivicConnect.Data.Entities;

/// <summary>
/// Domain entity representing an automated user notification (FR-13).
/// </summary>
public sealed class Notification
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>
    /// Identifier of the recipient user, matching AppUser.Id.
    /// </summary>
    public Guid UserId { get; set; }

    public string Message { get; set; } = string.Empty;

    public bool IsRead { get; set; } = false;

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public Notification()
    {
    }

    public Notification(Guid userId, string message)
    {
        Id = Guid.NewGuid();
        UserId = userId;
        Message = message;
        IsRead = false;
        CreatedAt = DateTimeOffset.UtcNow;
    }
}
