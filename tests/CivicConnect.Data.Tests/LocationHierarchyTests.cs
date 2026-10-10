using CivicConnect.Data.Configurations;
using CivicConnect.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CivicConnect.Data.Tests;

/// Covers the location tree, cycle prevention, asset registration, deactivation, and delete-restrict rules.
[Collection("Database")]
public sealed class LocationHierarchyTests
{
    private readonly CivicConnectDatabaseFixture _fixture;

    public LocationHierarchyTests(CivicConnectDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task BuildThreeLevelTree_AncestorPathIsRootFirst()
    {
        await using var context = _fixture.CreateContext();
        await using var transaction = await context.Database.BeginTransactionAsync();

        var site = await CreateLocationAsync(context, "Head Office", "site", impactLevel: 3);
        var floor = await CreateLocationAsync(context, "Floor 2", "floor", impactLevel: 2, site.Id);
        var room = await CreateLocationAsync(context, "Server Room", "room", impactLevel: 4, floor.Id);

        Assert.Null(site.ParentLocationId);

        var path = await context.GetAncestorPathAsync(room.Id);

        Assert.Equal(new[] { site.Id, floor.Id, room.Id }, path.Select(entry => entry.Id));
        Assert.Equal(new[] { "Head Office", "Floor 2", "Server Room" }, path.Select(entry => entry.Name));
    }

    [Fact]
    public async Task RegisterAsset_AtLocationAndWithNoLocation()
    {
        await using var context = _fixture.CreateContext();
        await using var transaction = await context.Database.BeginTransactionAsync();

        var room = await CreateLocationAsync(context, "Storage Room", "room", impactLevel: 1);

        var placed = await CreateAssetAsync(context, "Projector", room.Id);
        var unplaced = await CreateAssetAsync(context, "Spare Laptop", locationId: null);

        var reloadedPlaced = await context.Assets.AsNoTracking().SingleAsync(a => a.Id == placed.Id);
        var reloadedUnplaced = await context.Assets.AsNoTracking().SingleAsync(a => a.Id == unplaced.Id);

        Assert.Equal(room.Id, reloadedPlaced.LocationId);
        Assert.Null(reloadedUnplaced.LocationId);
    }

    [Fact]
    public async Task RegisterAsset_DuplicateAssetTagIsRejected()
    {
        await using var context = _fixture.CreateContext();
        await using var transaction = await context.Database.BeginTransactionAsync();

        var tag = $"TAG-{Guid.NewGuid():N}";
        await CreateAssetAsync(context, "Projector", locationId: null, assetTag: tag);

        context.Assets.Add(new Asset
        {
            AssetTag = tag,
            Description = "Duplicate",
            Supplier = "Acme",
            PurchaseDate = DateOnly.FromDateTime(DateTime.UtcNow),
        });

        await AssertPostgresFailureAsync(context, PostgresErrorCodes.UniqueViolation);
    }

    [Fact]
    public async Task CreateLocation_DirectSelfParentIsRejected()
    {
        await using var context = _fixture.CreateContext();
        await using var transaction = await context.Database.BeginTransactionAsync();

        var site = await CreateLocationAsync(context, "Branch Office", "site", impactLevel: 2);

        site.ParentLocationId = site.Id;

        await AssertPostgresFailureAsync(context, PostgresErrorCodes.CheckViolation);
    }

    [Fact]
    public async Task UpdateLocation_IndirectCycleThroughDescendantIsRejected()
    {
        await using var context = _fixture.CreateContext();
        await using var transaction = await context.Database.BeginTransactionAsync();

        var site = await CreateLocationAsync(context, "Campus", "site", impactLevel: 2);
        var floor = await CreateLocationAsync(context, "Floor 1", "floor", impactLevel: 2, site.Id);
        var room = await CreateLocationAsync(context, "Lab", "room", impactLevel: 2, floor.Id);

        site.ParentLocationId = room.Id;

        await AssertPostgresFailureAsync(context, PostgresErrorCodes.RaiseException);
    }

    [Fact]
    public async Task CreateLocation_ImpactLevelOutOfRangeIsRejected()
    {
        await using var context = _fixture.CreateContext();
        await using var transaction = await context.Database.BeginTransactionAsync();

        context.Locations.Add(new Location
        {
            Name = "Out Of Range",
            LocationKindId = KindId("site"),
            ImpactLevel = 5,
        });

        await AssertPostgresFailureAsync(context, PostgresErrorCodes.CheckViolation);
    }

    [Fact]
    public async Task DeactivateLocation_SetsFlagAndKeepsRow()
    {
        await using var context = _fixture.CreateContext();
        await using var transaction = await context.Database.BeginTransactionAsync();

        var location = await CreateLocationAsync(context, "Old Annex", "site", impactLevel: 1);

        location.IsActive = false;
        await context.SaveChangesAsync();

        var reloaded = await context.Locations.AsNoTracking().SingleAsync(l => l.Id == location.Id);
        Assert.False(reloaded.IsActive);
    }

    [Fact]
    public async Task DeactivateAsset_SetsFlagAndKeepsRow()
    {
        await using var context = _fixture.CreateContext();
        await using var transaction = await context.Database.BeginTransactionAsync();

        var asset = await CreateAssetAsync(context, "Retired Scanner", locationId: null);

        asset.IsActive = false;
        await context.SaveChangesAsync();

        var reloaded = await context.Assets.AsNoTracking().SingleAsync(a => a.Id == asset.Id);
        Assert.False(reloaded.IsActive);
    }

    [Fact]
    public async Task DeleteLocation_WithChildrenIsRefused()
    {
        await using var context = _fixture.CreateContext();
        await using var transaction = await context.Database.BeginTransactionAsync();

        var site = await CreateLocationAsync(context, "Parent Site", "site", impactLevel: 2);
        var child = await CreateLocationAsync(context, "Child Floor", "floor", impactLevel: 2, site.Id);

        // Untrack the child so EF does not null its FK client-side before deleting the parent;
        // this forces the database's ON DELETE RESTRICT constraint to be the thing under test.
        context.Entry(child).State = EntityState.Detached;

        context.Locations.Remove(site);

        await AssertPostgresFailureAsync(context, PostgresErrorCodes.ForeignKeyViolation);
    }

    [Fact]
    public async Task DeleteLocation_WithAssetIsRefused()
    {
        await using var context = _fixture.CreateContext();
        await using var transaction = await context.Database.BeginTransactionAsync();

        var room = await CreateLocationAsync(context, "Equipped Room", "room", impactLevel: 2);
        var asset = await CreateAssetAsync(context, "Fixed Camera", room.Id);

        // Untrack the asset so EF does not null its FK client-side before deleting the location;
        // this forces the database's ON DELETE RESTRICT constraint to be the thing under test.
        context.Entry(asset).State = EntityState.Detached;

        context.Locations.Remove(room);

        await AssertPostgresFailureAsync(context, PostgresErrorCodes.ForeignKeyViolation);
    }

    /// Saves and asserts the database (not EF) rejected the change with the given Postgres SQLSTATE.
    private static async Task AssertPostgresFailureAsync(CivicConnectDbContext context, string sqlState)
    {
        var ex = await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
        var pgException = Assert.IsType<PostgresException>(ex.InnerException);
        Assert.Equal(sqlState, pgException.SqlState);
    }

    private static short KindId(string name) =>
        LocationKindConfiguration.BaselineLocationKinds.Single(k => k.Name == name).Id;

    private static async Task<Location> CreateLocationAsync(
        CivicConnectDbContext context,
        string name,
        string kind,
        short impactLevel,
        int? parentLocationId = null)
    {
        var location = new Location
        {
            Name = name,
            LocationKindId = KindId(kind),
            ImpactLevel = impactLevel,
            ParentLocationId = parentLocationId,
        };

        context.Locations.Add(location);
        await context.SaveChangesAsync();
        return location;
    }

    private static async Task<Asset> CreateAssetAsync(
        CivicConnectDbContext context,
        string description,
        int? locationId,
        string? assetTag = null)
    {
        var asset = new Asset
        {
            AssetTag = assetTag ?? $"TAG-{Guid.NewGuid():N}",
            Description = description,
            Supplier = "Acme",
            PurchaseDate = DateOnly.FromDateTime(DateTime.UtcNow),
            LocationId = locationId,
        };

        context.Assets.Add(asset);
        await context.SaveChangesAsync();
        return asset;
    }
}
