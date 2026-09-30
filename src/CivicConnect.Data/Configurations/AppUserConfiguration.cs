using CivicConnect.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CivicConnect.Data.Configurations;

public sealed class AppUserConfiguration : IEntityTypeConfiguration<AppUser>
{
    public void Configure(EntityTypeBuilder<AppUser> builder)
    {
        builder.ToTable("app_user");

        builder.HasKey(u => u.Id);
        builder.Property(u => u.Id).HasDefaultValueSql("gen_random_uuid()").ValueGeneratedOnAdd();

        builder.Property(u => u.Email).HasColumnType("citext").IsRequired();
        builder.HasIndex(u => u.Email).IsUnique();

        builder.Property(u => u.FullName).IsRequired();
        builder.Property(u => u.PasswordHash).IsRequired();
        builder.Property(u => u.IsActive).HasDefaultValue(true);

        builder.Property(u => u.CreatedAt).HasDefaultValueSql("now()").ValueGeneratedOnAdd();
        builder.Property(u => u.UpdatedAt).HasDefaultValueSql("now()").ValueGeneratedOnAdd();
    }
}
