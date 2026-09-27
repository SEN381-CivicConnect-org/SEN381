namespace CivicConnect.Data.Entities;

/// A team that owns tickets, e.g. IT support or facilities.
public sealed class ServiceTeam : IHasTimestamps
{
    public int Id { get; set; }
    public string Name { get; set; } = null!;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public ICollection<TeamMember> Members { get; set; } = new List<TeamMember>();
}
