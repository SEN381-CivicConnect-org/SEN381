namespace CivicConnect.Data.Entities;

/// A level in the location tree, e.g. site, floor, or room.
public sealed class LocationKind
{
    public short Id { get; set; }
    public string Name { get; set; } = null!;
    public DateTimeOffset CreatedAt { get; set; }

    public ICollection<Location> Locations { get; set; } = new List<Location>();
}
