using CivicConnect.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CivicConnect.Data.Configurations;

public sealed class ResolutionCodeConfiguration : IEntityTypeConfiguration<ResolutionCode>
{
    /// Baseline resolution codes; the replacement ones require an asset link and flag it for finance.
    public static readonly (short Id, string Code, string Name, bool ImpliesReplacement)[] BaselineResolutionCodes =
    {
        (1, "FIXED", "Fixed on site", false),
        (2, "NO_FAULT_FOUND", "No fault found", false),
        (3, "DUPLICATE", "Duplicate of another incident", false),
        (4, "HARDWARE_REPLACED", "Faulty hardware replaced", true),
        (5, "BEYOND_REPAIR", "Beyond repair, replacement required", true),
    };

    public void Configure(EntityTypeBuilder<ResolutionCode> builder)
    {
        builder.ToTable("resolution_code");

        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).UseIdentityByDefaultColumn();

        builder.Property(c => c.Code).IsRequired();
        builder.HasIndex(c => c.Code).IsUnique();

        builder.Property(c => c.Name).IsRequired();
        builder.Property(c => c.ImpliesReplacement).IsRequired();
        builder.Property(c => c.IsActive).HasDefaultValue(true);

        builder.Property(c => c.CreatedAt).HasDefaultValueSql("now()").ValueGeneratedOnAdd();

        builder.HasData(BaselineResolutionCodes.Select(code => new
        {
            code.Id,
            code.Code,
            code.Name,
            code.ImpliesReplacement,
            IsActive = true,
            CreatedAt = SeedTimestamps.SeededAt,
        }));
    }
}
