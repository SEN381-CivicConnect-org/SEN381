namespace CivicConnect.Data.Entities;

/// A system-wide baseline role, such as technician or administrator.
public sealed class Role
{
    public short Id { get; set; }
    public string Name { get; set; } = null!;
    public DateTimeOffset CreatedAt { get; set; }

    public ICollection<UserRole> UserRoles { get; set; } = new List<UserRole>();
}
