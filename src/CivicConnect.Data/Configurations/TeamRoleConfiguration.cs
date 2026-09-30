using CivicConnect.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CivicConnect.Data.Configurations;

public sealed class TeamRoleConfiguration : IEntityTypeConfiguration<TeamRole>
{
    /// Baseline team roles seeded so every team can assign a technician and a supervisor.
    public static readonly (short Id, string Name)[] BaselineTeamRoles =
    {
        (1, "technician"),
        (2, "supervisor"),
    };

    public void Configure(EntityTypeBuilder<TeamRole> builder)
    {
        builder.ToTable("team_role");

        builder.HasKey(tr => tr.Id);
        builder.Property(tr => tr.Id).UseIdentityByDefaultColumn();

        builder.Property(tr => tr.Name).IsRequired();
        builder.HasIndex(tr => tr.Name).IsUnique();

        builder.Property(tr => tr.CreatedAt).HasDefaultValueSql("now()").ValueGeneratedOnAdd();

        builder.HasData(BaselineTeamRoles.Select(teamRole => new
        {
            teamRole.Id,
            teamRole.Name,
            CreatedAt = SeedTimestamps.SeededAt,
        }));
    }
}
