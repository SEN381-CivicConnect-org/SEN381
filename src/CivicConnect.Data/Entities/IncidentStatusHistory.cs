namespace CivicConnect.Data.Entities;

/// One append-only transition row in an incident's lifecycle.
public sealed class IncidentStatusHistory
{
    public long Id { get; set; }

    public long IncidentId { get; set; }
    public Incident Incident { get; set; } = null!;

    /// Null only on the creation row.
    public IncidentStatus? FromStatus { get; set; }
    public IncidentStatus ToStatus { get; set; }

    public Guid ChangedBy { get; set; }
    public AppUser ChangedByUser { get; set; } = null!;

    public DateTimeOffset ChangedAt { get; set; }

    public string? Note { get; set; }

    /// Team set by this transition, if assignment changed.
    public int? AssignedTeamId { get; set; }
    public ServiceTeam? AssignedTeam { get; set; }

    /// Individual set by this transition, if assignment changed.
    public Guid? AssigneeId { get; set; }
    public AppUser? Assignee { get; set; }
}
