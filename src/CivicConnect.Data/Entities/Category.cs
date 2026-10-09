using CivicConnect.Domain.Enums;

namespace CivicConnect.Data.Entities;

/// A selectable request category: its urgency level and the team it routes to by default.
public sealed class Category : IHasTimestamps
{
    public int Id { get; set; }
    public string Name { get; set; } = null!;
    public string Description { get; set; } = null!;
    public bool IsActive { get; set; } = true;

    /// <summary>Baseline category urgency level (FR-05).</summary>
    public UrgencyLevel DefaultUrgency { get; set; } = UrgencyLevel.Medium;

    public short PriorityTargetId { get; set; }
    public PriorityTarget PriorityTarget { get; set; } = null!;

    public int DefaultServiceTeamId { get; set; }
    public ServiceTeam DefaultServiceTeam { get; set; } = null!;

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
