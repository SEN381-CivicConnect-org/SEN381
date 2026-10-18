namespace CivicConnect.Data.Entities;

/// One recorded change to a configuration row or an incident: who did it, what changed, and when.
public sealed class AuditLog
{
    public long Id { get; set; }

    public Guid ActorId { get; set; }
    public AppUser Actor { get; set; } = null!;

    public AuditEntityType EntityType { get; set; }
    public string EntityId { get; set; } = null!;

    public AuditAction Action { get; set; }

    /// JSON snapshot of the row before the change; null on creation.
    public string? BeforeState { get; set; }

    /// JSON snapshot of the row after the change.
    public string? AfterState { get; set; }

    /// Set when the change is about an incident, for fast per-incident audit reads.
    public long? IncidentId { get; set; }
    public Incident? Incident { get; set; }

    public DateTimeOffset OccurredAt { get; set; }
}
