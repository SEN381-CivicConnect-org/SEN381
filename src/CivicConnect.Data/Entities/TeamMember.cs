namespace CivicConnect.Data.Entities;

/// One membership stint of a user in a team; a past stint is kept, never deleted, once LeftAt is set.
public sealed class TeamMember : IHasTimestamps
{
    public long Id { get; set; }

    public Guid UserId { get; set; }
    public AppUser User { get; set; } = null!;

    public int ServiceTeamId { get; set; }
    public ServiceTeam ServiceTeam { get; set; } = null!;

    public short TeamRoleId { get; set; }
    public TeamRole TeamRole { get; set; } = null!;

    public DateTimeOffset JoinedAt { get; set; }
    public DateTimeOffset? LeftAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
