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
    }
}
