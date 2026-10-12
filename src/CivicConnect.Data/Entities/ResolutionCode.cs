namespace CivicConnect.Data.Entities;

/// A seeded reason a technician can close an incident with, and whether it implies the asset needs replacing.
public sealed class ResolutionCode
{
    public short Id { get; set; }
    public string Code { get; set; } = null!;
    public string Name { get; set; } = null!;
    public bool ImpliesReplacement { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; }
}
