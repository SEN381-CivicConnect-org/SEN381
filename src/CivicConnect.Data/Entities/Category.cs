namespace CivicConnect.Data.Entities;

/// A selectable request category: its urgency level and the team it routes to by default.
public sealed class Category : IHasTimestamps
{
    public int Id { get; set; }
    public string Name { get; set; } = null!;
    public string Description { get; set; } = null!;
    public bool IsActive { get; set; } = true;

    /// 1 = low, 2 = medium, 3 = high, 4 = critical.
    public short UrgencyLevel { get; set; }

    public int DefaultServiceTeamId { get; set; }
    public ServiceTeam DefaultServiceTeam { get; set; } = null!;

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
