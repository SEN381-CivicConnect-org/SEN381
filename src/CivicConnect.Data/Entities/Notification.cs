namespace CivicConnect.Data.Entities;

/// A queued notification to one subscriber about one incident event; deduplicated by DedupKey.
public sealed class Notification
{
    public long Id { get; set; }

    public long IncidentId { get; set; }
    public Incident Incident { get; set; } = null!;

    public Guid RecipientId { get; set; }
    public AppUser Recipient { get; set; } = null!;

    public NotificationKind Kind { get; set; }

    /// Unique across the table; queuing again with the same key is a no-op.
    public string DedupKey { get; set; } = null!;

    public DateTimeOffset CreatedAt { get; set; }

    /// Null while unread.
    public DateTimeOffset? ReadAt { get; set; }
}
