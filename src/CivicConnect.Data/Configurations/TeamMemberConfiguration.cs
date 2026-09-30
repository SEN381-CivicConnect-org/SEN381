using CivicConnect.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CivicConnect.Data.Configurations;

public sealed class TeamMemberConfiguration : IEntityTypeConfiguration<TeamMember>
{
    public void Configure(EntityTypeBuilder<TeamMember> builder)
    {
        builder.ToTable("team_member");

        builder.HasKey(tm => tm.Id);
        builder.Property(tm => tm.Id).UseIdentityByDefaultColumn();

        builder.Property(tm => tm.JoinedAt).HasDefaultValueSql("now()").ValueGeneratedOnAdd();

        builder.Property(tm => tm.CreatedAt).HasDefaultValueSql("now()").ValueGeneratedOnAdd();
        builder.Property(tm => tm.UpdatedAt).HasDefaultValueSql("now()").ValueGeneratedOnAdd();

        builder.HasOne(tm => tm.User)
            .WithMany(u => u.TeamMemberships)
            .HasForeignKey(tm => tm.UserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(tm => tm.ServiceTeam)
            .WithMany(t => t.Members)
            .HasForeignKey(tm => tm.ServiceTeamId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(tm => tm.TeamRole)
            .WithMany(tr => tr.TeamMembers)
            .HasForeignKey(tm => tm.TeamRoleId)
            .OnDelete(DeleteBehavior.Restrict);

        // A user cannot be an active (non-left) member of the same team more than once at a time.
        builder.HasIndex(tm => new { tm.UserId, tm.ServiceTeamId })
            .IsUnique()
            .HasFilter("left_at IS NULL");
    }
}
