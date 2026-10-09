namespace CivicConnect.Data.Entities;

/// A physical asset, optionally registered against a location in the tree.
public sealed class Asset : IHasTimestamps
{
    public int Id { get; set; }
    public string AssetTag { get; set; } = null!;
    public string Description { get; set; } = null!;
    public string Supplier { get; set; } = null!;
    public DateOnly PurchaseDate { get; set; }
    public bool IsActive { get; set; } = true;

    public int? LocationId { get; set; }
    public Location? Location { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
