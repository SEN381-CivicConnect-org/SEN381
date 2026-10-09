using CivicConnect.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CivicConnect.Data.Configurations;

public sealed class CategoryConfiguration : IEntityTypeConfiguration<Category>
{
    public void Configure(EntityTypeBuilder<Category> builder)
    {
        builder.ToTable("category", table =>
        {
            table.HasCheckConstraint("category_urgency_level_range_ck", "urgency_level BETWEEN 1 AND 4");
        });

        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).UseIdentityByDefaultColumn();

        builder.Property(c => c.Name).IsRequired();
        builder.Property(c => c.Description).IsRequired();
        builder.Property(c => c.IsActive).HasDefaultValue(true);
        builder.Property(c => c.DefaultUrgency).HasConversion<int>().HasDefaultValue(CivicConnect.Domain.Enums.UrgencyLevel.Medium);
        builder.Property(c => c.UrgencyLevel).IsRequired();

        // A name is unique only among active categories, so a retired name can be reused.
        builder.HasIndex(c => c.Name)
            .IsUnique()
            .HasDatabaseName("category_active_name_uq")
            .HasFilter("is_active");

        builder.Property(c => c.CreatedAt).HasDefaultValueSql("now()").ValueGeneratedOnAdd();
        builder.Property(c => c.UpdatedAt).HasDefaultValueSql("now()").ValueGeneratedOnAdd();

        builder.HasOne(c => c.DefaultServiceTeam)
            .WithMany(t => t.DefaultCategories)
            .HasForeignKey(c => c.DefaultServiceTeamId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
