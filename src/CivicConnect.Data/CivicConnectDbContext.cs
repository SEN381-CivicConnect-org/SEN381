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
    public DbSet<IncidentStatusTransition> IncidentStatusTransitions => Set<IncidentStatusTransition>();
    public DbSet<IncidentStatusHistory> IncidentStatusHistories => Set<IncidentStatusHistory>();
    public DbSet<ResolutionCode> ResolutionCodes => Set<ResolutionCode>();
    public DbSet<Resolution> Resolutions => Set<Resolution>();
    public DbSet<IncidentNote> IncidentNotes => Set<IncidentNote>();
    public DbSet<Notification> Notifications => Set<Notification>();

    // Use these for new selections; the DbSets remain unfiltered for history and management.
    public IQueryable<Category> ActiveCategories => Categories.Where(c => c.IsActive);
    public IQueryable<Location> ActiveLocations => Locations.Where(l => l.IsActive);
    public IQueryable<Asset> ActiveAssets => Assets.Where(a => a.IsActive);

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        DeactivateDeletedConfiguration();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(
        bool acceptAllChangesOnSuccess,
        CancellationToken cancellationToken = default)
    {
        DeactivateDeletedConfiguration();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    private void DeactivateDeletedConfiguration()
    {
        foreach (var entry in ChangeTracker.Entries().Where(e => e.State == EntityState.Deleted))
        {
            if (entry.Entity is not (Category or Location or Asset))
            {
                continue;
            }

            // Only update lifecycle fields, including when Remove was called on a key-only entity.
            // Do not overwrite names, details, or foreign keys needed by historical references.
            entry.State = EntityState.Unchanged;
            entry.Property("IsActive").CurrentValue = false;
            entry.Property("IsActive").IsModified = true;
            entry.Property("UpdatedAt").CurrentValue = DateTimeOffset.UtcNow;
            entry.Property("UpdatedAt").IsModified = true;
        }
    }

    /// Applies the conventions every host (design-time CLI, API, tests) must share: snake_case naming, a snake_case migrations history table, and the updated_at interceptor.
    public static DbContextOptionsBuilder<CivicConnectDbContext> Configure(
        DbContextOptionsBuilder<CivicConnectDbContext> optionsBuilder,
        string connectionString)
    {
        return (DbContextOptionsBuilder<CivicConnectDbContext>)optionsBuilder
            .UseNpgsql(connectionString, npgsql => npgsql
                .MigrationsHistoryTable("__ef_migrations_history")
                .MapEnum<IncidentStatus>("incident_status")
                .MapEnum<NotificationKind>("notification_kind"))
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

        modelBuilder.HasPostgresEnum(
            "notification_kind",
            new[] { "STATUS_CHANGE", "ASSIGNMENT", "RESOLUTION" });

        modelBuilder.ApplyConfigurationsFromAssembly(typeof(CivicConnectDbContext).Assembly);
    }
}
