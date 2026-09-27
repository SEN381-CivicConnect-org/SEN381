using CivicConnect.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CivicConnect.Data.Configurations;

public sealed class ServiceTeamConfiguration : IEntityTypeConfiguration<ServiceTeam>
{
    public void Configure(EntityTypeBuilder<ServiceTeam> builder)
    {
        builder.ToTable("service_team");

        builder.HasKey(t => t.Id);
        builder.Property(t => t.Id).UseIdentityByDefaultColumn();

        builder.Property(t => t.Name).IsRequired();
        builder.HasIndex(t => t.Name).IsUnique();

        builder.Property(t => t.CreatedAt).HasDefaultValueSql("now()").ValueGeneratedOnAdd();
        builder.Property(t => t.UpdatedAt).HasDefaultValueSql("now()").ValueGeneratedOnAdd();
    }
}
