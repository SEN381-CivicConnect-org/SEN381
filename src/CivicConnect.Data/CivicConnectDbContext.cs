using CivicConnect.Data.Entities;
using CivicConnect.Data.Interceptors;
using Microsoft.EntityFrameworkCore;

namespace CivicConnect.Data;

/// EF Core context for the CivicConnect schema; entities are added ticket-by-ticket.
public sealed class CivicConnectDbContext : DbContext
{
    public CivicConnectDbContext(DbContextOptions<CivicConnectDbContext> options)
        : base(options)
    {
    }

    public DbSet<AppUser> AppUsers => Set<AppUser>();
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<UserRole> UserRoles => Set<UserRole>();
    public DbSet<ServiceTeam> ServiceTeams => Set<ServiceTeam>();
    public DbSet<TeamRole> TeamRoles => Set<TeamRole>();
    public DbSet<TeamMember> TeamMembers => Set<TeamMember>();
    public DbSet<PriorityTarget> PriorityTargets => Set<PriorityTarget>();
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<LocationKind> LocationKinds => Set<LocationKind>();
    public DbSet<Location> Locations => Set<Location>();
    public DbSet<Asset> Assets => Set<Asset>();
    public DbSet<Incident> Incidents => Set<Incident>();
    public DbSet<Ticket> Tickets => Set<Ticket>();
    public DbSet<SubmissionRequest> SubmissionRequests => Set<SubmissionRequest>();
    public DbSet<IncidentSubscription> IncidentSubscriptions => Set<IncidentSubscription>();
    public DbSet<ServiceRequest> ServiceRequests => Set<ServiceRequest>();
    public DbSet<Notification> Notifications { get; set; } = null!;

    /// Applies the conventions every host (design-time CLI, API, tests) must share: snake_case naming, a snake_case migrations history table, and the updated_at interceptor.
    public static DbContextOptionsBuilder<CivicConnectDbContext> Configure(
        DbContextOptionsBuilder<CivicConnectDbContext> optionsBuilder,
        string connectionString)
    {
        return (DbContextOptionsBuilder<CivicConnectDbContext>)optionsBuilder
            .UseNpgsql(connectionString, npgsql => npgsql
                .MigrationsHistoryTable("__ef_migrations_history")
                .MapEnum<IncidentStatus>("incident_status"))
            .UseSnakeCaseNamingConvention()
            .AddInterceptors(new UpdatedAtInterceptor());
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Case-insensitive text type used for columns such as app_user.email.
        modelBuilder.HasPostgresExtension("citext");

        modelBuilder.HasPostgresEnum(
            "incident_status",
            new[] { "NEW", "ASSIGNED", "IN_PROGRESS", "ON_HOLD", "RESOLVED", "CLOSED", "REJECTED", "MERGED" });

        modelBuilder.ApplyConfigurationsFromAssembly(typeof(CivicConnectDbContext).Assembly);

        // Fluent API configuration for ServiceRequest priority override, SLA, and status properties (FR-06, FR-12, FR-13)
        modelBuilder.Entity<ServiceRequest>(entity =>
        {
            entity.ToTable("service_requests");

            entity.HasKey(sr => sr.Id);

            entity.Property(sr => sr.Title)
                .IsRequired()
                .HasMaxLength(200);

            entity.Property(sr => sr.Description)
                .IsRequired()
                .HasMaxLength(2000);

            entity.Property(sr => sr.Priority)
                .IsRequired()
                .HasConversion<int>();

            entity.Property(sr => sr.Status)
                .IsRequired()
                .HasConversion<int>();

            entity.Property(sr => sr.RequesterId)
                .IsRequired();

            entity.Property(sr => sr.DueAt)
                .HasColumnType("timestamp with time zone")
                .IsRequired(false);

            entity.Property(sr => sr.IsSupervisorOverridden)
                .IsRequired()
                .HasDefaultValue(false);

            entity.Property(sr => sr.OverrideReason)
                .HasMaxLength(500)
                .IsRequired(false);

            entity.Property(sr => sr.OverriddenBySupervisorId)
                .HasMaxLength(128)
                .IsRequired(false);

            entity.Property(sr => sr.OverriddenAt)
                .HasColumnType("timestamp with time zone")
                .IsRequired(false);

            entity.Property(sr => sr.LastModified)
                .HasDefaultValueSql("now()")
                .ValueGeneratedOnAddOrUpdate();

            entity.HasOne(sr => sr.Requester)
                .WithMany()
                .HasForeignKey(sr => sr.RequesterId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.Ignore(sr => sr.IsOverdue);
        });

        // Fluent API configuration for Notification entity (FR-13)
        modelBuilder.Entity<Notification>(entity =>
        {
            entity.ToTable("notifications");

            entity.HasKey(n => n.Id);

            entity.Property(n => n.UserId)
                .IsRequired();

            entity.Property(n => n.Message)
                .IsRequired()
                .HasMaxLength(1000);

            entity.Property(n => n.IsRead)
                .IsRequired()
                .HasDefaultValue(false);

            entity.Property(n => n.CreatedAt)
                .HasDefaultValueSql("now()")
                .ValueGeneratedOnAdd();

            entity.HasIndex(n => n.UserId);
            entity.HasIndex(n => new { n.UserId, n.IsRead });

            entity.HasOne<AppUser>()
                .WithMany()
                .HasForeignKey(n => n.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
