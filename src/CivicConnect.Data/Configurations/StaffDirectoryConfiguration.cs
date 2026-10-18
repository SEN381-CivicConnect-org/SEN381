using CivicConnect.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CivicConnect.Data.Configurations;

/// <summary>
/// Maps <see cref="StaffDirectory"/> to the <c>staff_directory</c> read-only view (NFR-04, IT-SEC-01).
/// </summary>
public sealed class StaffDirectoryConfiguration : IEntityTypeConfiguration<StaffDirectory>
{
    public void Configure(EntityTypeBuilder<StaffDirectory> builder)
    {
        builder.HasNoKey();
        builder.ToView("staff_directory");

        builder.Property(s => s.Id).HasColumnName("id");
        builder.Property(s => s.FullName).HasColumnName("full_name");
        builder.Property(s => s.ServiceTeamId).HasColumnName("service_team_id");
        builder.Property(s => s.TeamName).HasColumnName("team_name");
        builder.Property(s => s.TeamRoleId).HasColumnName("team_role_id");
        builder.Property(s => s.TeamRole).HasColumnName("team_role");
        builder.Property(s => s.IsActive).HasColumnName("is_active");
    }
}
