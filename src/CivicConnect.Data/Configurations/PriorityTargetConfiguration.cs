using CivicConnect.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CivicConnect.Data.Configurations;

public sealed class PriorityTargetConfiguration : IEntityTypeConfiguration<PriorityTarget>
{
    /// Baseline priority levels seeded with a placeholder resolve-within target an admin can edit later.
    public static readonly (short Id, string Name, int ResolveWithinMinutes)[] BaselinePriorityTargets =
    {
        (1, "critical", 60),
        (2, "high", 240),
        (3, "medium", 1440),
        (4, "low", 4320),
    };

    public void Configure(EntityTypeBuilder<PriorityTarget> builder)
    {
        builder.ToTable("priority_target");

        builder.HasKey(pt => pt.Id);
        builder.Property(pt => pt.Id).UseIdentityByDefaultColumn();

        builder.Property(pt => pt.Name).IsRequired();
        builder.HasIndex(pt => pt.Name).IsUnique();

        builder.Property(pt => pt.ResolveWithinMinutes).IsRequired();

        builder.Property(pt => pt.CreatedAt).HasDefaultValueSql("now()").ValueGeneratedOnAdd();

        builder.HasData(BaselinePriorityTargets.Select(priorityTarget => new
        {
            priorityTarget.Id,
            priorityTarget.Name,
            priorityTarget.ResolveWithinMinutes,
            CreatedAt = SeedTimestamps.SeededAt,
        }));
    }
}
