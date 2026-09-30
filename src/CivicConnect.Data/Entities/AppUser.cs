namespace CivicConnect.Data.Entities;

/// A person who can sign in: a reporter, technician, supervisor, manager, or administrator.
public sealed class AppUser : IHasTimestamps
{
    public Guid Id { get; set; }
    public string Email { get; set; } = null!;
    public string FullName { get; set; } = null!;
    public string PasswordHash { get; set; } = null!;
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public ICollection<UserRole> UserRoles { get; set; } = new List<UserRole>();
    public ICollection<TeamMember> TeamMemberships { get; set; } = new List<TeamMember>();
}
