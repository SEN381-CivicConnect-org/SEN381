using CivicConnect.Data.Configurations;
using CivicConnect.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CivicConnect.Data.Tests;

/// Covers 03-service-catalogue: create, deactivate, reactivate, and the refusal to delete a referenced category.
[Collection("Database")]
public sealed class ServiceCatalogueTests
{
    private readonly CivicConnectDatabaseFixture _fixture;

    public ServiceCatalogueTests(CivicConnectDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task CreateCategory_StoresNameDescriptionUrgencyAndDefaultTeam()
    {
        await using var context = _fixture.CreateContext();
        await using var transaction = await context.Database.BeginTransactionAsync();

        var team = await CreateTeamAsync(context, "Facilities");

        var category = new Category
        {
            Name = $"Leaking tap {Guid.NewGuid():N}",
            Description = "Plumbing issue reported by a reporter",
            UrgencyLevel = 2,
            DefaultServiceTeamId = team.Id,
        };

        context.Categories.Add(category);
        await context.SaveChangesAsync();

        Assert.True(category.IsActive);

        var reloaded = await context.Categories.AsNoTracking().SingleAsync(c => c.Id == category.Id);
        Assert.Equal(category.Name, reloaded.Name);
        Assert.Equal("Plumbing issue reported by a reporter", reloaded.Description);
        Assert.Equal((short)2, reloaded.UrgencyLevel);
        Assert.Equal(team.Id, reloaded.DefaultServiceTeamId);
    }

    [Fact]
    public async Task CreateCategory_WithNonexistentDefaultTeam_IsRejected()
    {
        await using var context = _fixture.CreateContext();
        await using var transaction = await context.Database.BeginTransactionAsync();

        var category = new Category
        {
            Name = $"Orphan category {Guid.NewGuid():N}",
            Description = "Default team does not exist",
            UrgencyLevel = 1,
            DefaultServiceTeamId = -1,
        };

        context.Categories.Add(category);

        var dbEx = await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
        Assert.Contains(
            "fk_category_service_teams_default_service_team_id",
            dbEx.InnerException?.Message ?? dbEx.Message,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CategoryName_UniqueOnlyAmongActiveCategories()
    {
        await using var context = _fixture.CreateContext();
        await using var transaction = await context.Database.BeginTransactionAsync();

        var team = await CreateTeamAsync(context, "IT Support");
        var name = $"Network outage {Guid.NewGuid():N}";

        var first = new Category
        {
            Name = name,
            Description = "First category",
            UrgencyLevel = 3,
            DefaultServiceTeamId = team.Id,
        };
        context.Categories.Add(first);
        await context.SaveChangesAsync();

        // Same name, still active: rejected by the filtered unique index.
        await transaction.CreateSavepointAsync("before_duplicate_active_name");
        context.Categories.Add(new Category
        {
            Name = name,
            Description = "Duplicate while first is still active",
            UrgencyLevel = 3,
            DefaultServiceTeamId = team.Id,
        });

        var dbEx = await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
        Assert.Contains(
            "category_active_name_uq",
            dbEx.InnerException?.Message ?? dbEx.Message,
            StringComparison.OrdinalIgnoreCase);

        await transaction.RollbackToSavepointAsync("before_duplicate_active_name");
        context.ChangeTracker.Clear();

        // Retire the first category, then the name becomes reusable.
        var toRetire = await context.Categories.SingleAsync(c => c.Id == first.Id);
        toRetire.IsActive = false;
        await context.SaveChangesAsync();

        var reused = new Category
        {
            Name = name,
            Description = "Reuses the retired name",
            UrgencyLevel = 3,
            DefaultServiceTeamId = team.Id,
        };
        context.Categories.Add(reused);
        await context.SaveChangesAsync();

        Assert.NotEqual(first.Id, reused.Id);
    }

    [Fact]
    public async Task PriorityTargets_AreSeededForEveryLevelWithAResolveWithinMinutesValue()
    {
        await using var context = _fixture.CreateContext();

        var targets = await context.PriorityTargets.AsNoTracking().ToListAsync();

        Assert.Equal(
            PriorityTargetConfiguration.BaselinePriorityTargets.Length,
            targets.Count);

        foreach (var (id, name, resolveWithinMinutes) in PriorityTargetConfiguration.BaselinePriorityTargets)
        {
            var target = Assert.Single(targets, t => t.Id == id);
            Assert.Equal(name, target.Name);
            Assert.Equal(resolveWithinMinutes, target.ResolveWithinMinutes);
            Assert.True(target.ResolveWithinMinutes > 0);
        }
    }

    [Fact]
    public async Task DeactivateCategory_SetsIsActiveFalse_ReferencingIncidentUntouched()
    {
        await using var context = _fixture.CreateContext();
        await using var transaction = await context.Database.BeginTransactionAsync();

        var (team, category, location) = await CreateCatalogueAsync(context);
        var incident = await CreateIncidentAsync(context, category.Id, location.Id);

        var toDeactivate = await context.Categories.SingleAsync(c => c.Id == category.Id);
        toDeactivate.IsActive = false;
        await context.SaveChangesAsync();

        var reloadedCategory = await context.Categories.AsNoTracking().SingleAsync(c => c.Id == category.Id);
        Assert.False(reloadedCategory.IsActive);

        var reloadedIncident = await context.Incidents.AsNoTracking().SingleAsync(i => i.Id == incident.Id);
        Assert.Equal(category.Id, reloadedIncident.CategoryId);
        Assert.Equal(incident.Title, reloadedIncident.Title);
    }

    [Fact]
    public async Task InactiveCategory_CannotBeSelectedForANewIncident_ButStillResolvesForAnExistingOne()
    {
        await using var context = _fixture.CreateContext();
        await using var transaction = await context.Database.BeginTransactionAsync();

        var (team, category, location) = await CreateCatalogueAsync(context);
        var existingIncident = await CreateIncidentAsync(context, category.Id, location.Id);

        var toDeactivate = await context.Categories.SingleAsync(c => c.Id == category.Id);
        toDeactivate.IsActive = false;
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        await transaction.CreateSavepointAsync("before_insert_against_inactive_category");

        var dbEx = await Assert.ThrowsAsync<DbUpdateException>(
            () => CreateIncidentAsync(context, category.Id, location.Id));
        Assert.Contains(
            "is not active and cannot be selected",
            dbEx.InnerException?.Message ?? dbEx.Message,
            StringComparison.OrdinalIgnoreCase);

        await transaction.RollbackToSavepointAsync("before_insert_against_inactive_category");
        context.ChangeTracker.Clear();

        var stillResolves = await context.Incidents
            .Include(i => i.Category)
            .AsNoTracking()
            .SingleAsync(i => i.Id == existingIncident.Id);
        Assert.Equal(category.Id, stillResolves.CategoryId);
        Assert.False(stillResolves.Category.IsActive);
    }

    [Fact]
    public async Task RemovingCategory_ThroughEntityFramework_SoftDeactivatesRatherThanDeleting()
    {
        // CivicConnectDbContext.DeactivateDeletedConfiguration() redirects Remove() on a
        // Category/Location/Asset into an IsActive=false update, so application code can never
        // hard-delete a referenced category through the ORM.
        await using var context = _fixture.CreateContext();
        await using var transaction = await context.Database.BeginTransactionAsync();

        var (team, category, location) = await CreateCatalogueAsync(context);
        await CreateIncidentAsync(context, category.Id, location.Id);
        context.ChangeTracker.Clear();

        var toRemove = await context.Categories.SingleAsync(c => c.Id == category.Id);
        context.Categories.Remove(toRemove);
        await context.SaveChangesAsync();

        var reloaded = await context.Categories.AsNoTracking().SingleAsync(c => c.Id == category.Id);
        Assert.False(reloaded.IsActive);
    }

    [Fact]
    public async Task DeleteCategory_ReferencedByAnIncident_IsRefusedByTheDatabase()
    {
        // Bypasses the EF soft-delete redirect with a raw statement to exercise the
        // ON DELETE RESTRICT foreign key itself, the actual safety net the ticket describes.
        await using var context = _fixture.CreateContext();
        await using var transaction = await context.Database.BeginTransactionAsync();

        var (team, category, location) = await CreateCatalogueAsync(context);
        await CreateIncidentAsync(context, category.Id, location.Id);

        await transaction.CreateSavepointAsync("before_restricted_delete");

        var pgEx = await Assert.ThrowsAsync<PostgresException>(
            () => context.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM category WHERE id = {category.Id}"));
        Assert.Equal("fk_incident_category_category_id", pgEx.ConstraintName);

        await transaction.RollbackToSavepointAsync("before_restricted_delete");

        var stillThere = await context.Categories.AsNoTracking().SingleOrDefaultAsync(c => c.Id == category.Id);
        Assert.NotNull(stillThere);
    }

    [Fact]
    public async Task ReactivateCategory_AllowsItToBeSelectedAgain()
    {
        await using var context = _fixture.CreateContext();
        await using var transaction = await context.Database.BeginTransactionAsync();

        var (team, category, location) = await CreateCatalogueAsync(context);

        var toToggle = await context.Categories.SingleAsync(c => c.Id == category.Id);
        toToggle.IsActive = false;
        await context.SaveChangesAsync();

        toToggle.IsActive = true;
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        var incident = await CreateIncidentAsync(context, category.Id, location.Id);

        var reloaded = await context.Incidents.AsNoTracking().SingleAsync(i => i.Id == incident.Id);
        Assert.Equal(category.Id, reloaded.CategoryId);
    }

    private static async Task<ServiceTeam> CreateTeamAsync(CivicConnectDbContext context, string namePrefix)
    {
        var team = new ServiceTeam { Name = $"{namePrefix} {Guid.NewGuid():N}" };
        context.ServiceTeams.Add(team);
        await context.SaveChangesAsync();
        return team;
    }

    private static async Task<(ServiceTeam Team, Category Category, Location Location)> CreateCatalogueAsync(
        CivicConnectDbContext context)
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];

        var team = new ServiceTeam { Name = $"Catalogue Team {suffix}" };
        context.ServiceTeams.Add(team);
        await context.SaveChangesAsync();

        var category = new Category
        {
            Name = $"Catalogue Category {suffix}",
            Description = "Used by service catalogue tests",
            UrgencyLevel = 3,
            DefaultServiceTeamId = team.Id,
            IsActive = true,
        };
        var location = new Location
        {
            Name = $"Catalogue Location {suffix}",
            LocationKindId = LocationKindConfiguration.BaselineLocationKinds.Single(k => k.Name == "room").Id,
            ImpactLevel = 3,
            IsActive = true,
        };
        context.Categories.Add(category);
        context.Locations.Add(location);
        await context.SaveChangesAsync();

        return (team, category, location);
    }

    private static async Task<Incident> CreateIncidentAsync(
        CivicConnectDbContext context,
        int categoryId,
        int locationId)
    {
        var incident = new Incident
        {
            CategoryId = categoryId,
            LocationId = locationId,
            Title = $"Catalogue incident {Guid.NewGuid():N}",
            Description = "Created by service catalogue tests",
            Status = IncidentStatus.New,
            ComputedPriority = 1,
            Priority = 1,
            PriorityOverridden = false,
        };
        context.Incidents.Add(incident);
        await context.SaveChangesAsync();
        return incident;
    }
}
