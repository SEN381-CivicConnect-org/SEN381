using CivicConnect.Data;
using CivicConnect.Data.Entities;
using Microsoft.EntityFrameworkCore;

// Run against a migrated test database. All inserted rows are rolled back.
var connectionString = Environment.GetEnvironmentVariable("TEST_CONNECTION_STRING")
    ?? throw new InvalidOperationException("Set TEST_CONNECTION_STRING to a migrated PostgreSQL test database.");
var options = CivicConnectDbContext.Configure(
    new DbContextOptionsBuilder<CivicConnectDbContext>(), connectionString).Options;
await using var context = new CivicConnectDbContext(options);
await using var transaction = await context.Database.BeginTransactionAsync();

var team = new ServiceTeam { Name = $"FR-09-{Guid.NewGuid()}" };
var category = new Category
{
    Name = team.Name,
    Description = "Historical category details",
    DefaultServiceTeam = team,
    PriorityTargetId = await context.PriorityTargets.Select(p => p.Id).FirstAsync()
};
var location = new Location
{
    Name = "Historical location",
    ImpactLevel = 1,
    LocationKindId = await context.LocationKinds.Select(k => k.Id).FirstAsync()
};
var asset = new Asset
{
    AssetTag = team.Name,
    Description = "Historical asset details",
    Supplier = "Test supplier",
    PurchaseDate = new DateOnly(2026, 1, 1),
    Location = location
};
context.AddRange(category, asset);
await context.SaveChangesAsync();
var categoryId = category.Id;
var locationId = location.Id;
var assetId = asset.Id;
Assert(await context.ActiveCategories.AnyAsync(c => c.Id == categoryId), "Active category selectable.");
Assert(await context.ActiveLocations.AnyAsync(l => l.Id == locationId), "Active location selectable.");
Assert(await context.ActiveAssets.AnyAsync(a => a.Id == assetId), "Active asset selectable.");

context.Remove(category);
context.Remove(location);
await context.SaveChangesAsync();
context.ChangeTracker.Clear();
var historicalAsset = await context.Assets.Include(a => a.Location).SingleAsync(a => a.Id == assetId);
Assert(historicalAsset.LocationId == locationId, "Historical foreign key preserved.");
Assert(historicalAsset.Location is { IsActive: false, Name: "Historical location" }, "Inactive location still loads with its name.");
var historicalCategory = await context.Categories.Include(c => c.DefaultServiceTeam).SingleAsync(c => c.Id == categoryId);
Assert(!historicalCategory.IsActive && historicalCategory.Description == "Historical category details", "Category retained with details.");
Assert(historicalCategory.DefaultServiceTeam.Name == team.Name, "Category routing reference preserved.");

// A key-only deletion must not overwrite stored details with CLR defaults.
context.ChangeTracker.Clear();
context.Remove(new Asset { Id = assetId });
context.SaveChanges();
context.ChangeTracker.Clear();
var historical = await context.Assets.Include(a => a.Location).SingleAsync(a => a.Id == assetId);
Assert(!historical.IsActive && historical.Description == "Historical asset details", "Sync deactivation preserves persisted asset details.");
Assert(historical.LocationId == locationId && historical.Location!.Name == "Historical location", "Asset history still resolves.");
Assert(!await context.ActiveCategories.AnyAsync(c => c.Id == categoryId), "Inactive category excluded.");
Assert(!await context.ActiveLocations.AnyAsync(l => l.Id == locationId), "Inactive location excluded.");
Assert(!await context.ActiveAssets.AnyAsync(a => a.Id == assetId), "Inactive asset excluded.");

historical.IsActive = true;
await context.SaveChangesAsync();
Assert(await context.ActiveAssets.AnyAsync(a => a.Id == assetId), "Active asset remains functional.");
await transaction.RollbackAsync();
Console.WriteLine("All configuration deactivation checks passed.");

static void Assert(bool condition, string message)
{
    if (!condition)
    {
        throw new Exception(message);
    }
}
