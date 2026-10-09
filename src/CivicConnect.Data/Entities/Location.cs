namespace CivicConnect.Data.Entities;

/// A node in the office location tree (site/floor/room) carrying the impact level used in priority calculation.
public sealed class Location : IHasTimestamps
{
    public int Id { get; set; }
    public string Name { get; set; } = null!;
    public bool IsActive { get; set; } = true;

    /// 1 = low, 2 = medium, 3 = high, 4 = critical.
    public short ImpactLevel { get; set; }

    public short LocationKindId { get; set; }
    public LocationKind LocationKind { get; set; } = null!;

    public int? ParentLocationId { get; set; }
    public Location? ParentLocation { get; set; }
    public ICollection<Location> ChildLocations { get; set; } = new List<Location>();

    public ICollection<Asset> Assets { get; set; } = new List<Asset>();

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
