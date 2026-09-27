namespace CivicConnect.Data.Entities;

/// A grant of one role to one user; records who granted it and when.
public sealed class UserRole
{
    public Guid UserId { get; set; }
    public AppUser User { get; set; } = null!;

    public short RoleId { get; set; }
    public Role Role { get; set; } = null!;

    public Guid GrantedByUserId { get; set; }
    public AppUser GrantedByUser { get; set; } = null!;

    public DateTimeOffset GrantedAt { get; set; }
}
