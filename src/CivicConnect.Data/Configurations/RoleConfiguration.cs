using CivicConnect.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CivicConnect.Data.Configurations;

public sealed class RoleConfiguration : IEntityTypeConfiguration<Role>
{
    /// Baseline roles seeded so every environment starts with the same five roles.
    public static readonly (short Id, string Name)[] BaselineRoles =
    {
        (1, "reporter"),
        (2, "technician"),
        (3, "supervisor"),
        (4, "operations manager"),
        (5, "administrator"),
    };

    public void Configure(EntityTypeBuilder<Role> builder)
    {
        builder.ToTable("role");

        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).UseIdentityByDefaultColumn();

        builder.Property(r => r.Name).IsRequired();
        builder.HasIndex(r => r.Name).IsUnique();

        builder.Property(r => r.CreatedAt).HasDefaultValueSql("now()").ValueGeneratedOnAdd();

        builder.HasData(BaselineRoles.Select(role => new
        {
            role.Id,
            role.Name,
            CreatedAt = SeedTimestamps.SeededAt,
        }));
    }
}
