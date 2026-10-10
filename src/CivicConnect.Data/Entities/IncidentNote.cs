namespace CivicConnect.Data.Entities;

/// A working note against an incident; internal notes are hidden from reporter-facing reads.
public sealed class IncidentNote
{
    public long Id { get; set; }

    public long IncidentId { get; set; }
    public Incident Incident { get; set; } = null!;

    public Guid AuthorId { get; set; }
    public AppUser Author { get; set; } = null!;

    public string Body { get; set; } = null!;

    public bool IsInternal { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
