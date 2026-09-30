namespace CivicConnect.Data.Entities;

/// A role a user holds within a specific team, e.g. technician or supervisor.
public sealed class TeamRole
{
    public short Id { get; set; }
    public string Name { get; set; } = null!;
    public DateTimeOffset CreatedAt { get; set; }

    public ICollection<TeamMember> TeamMembers { get; set; } = new List<TeamMember>();
}
