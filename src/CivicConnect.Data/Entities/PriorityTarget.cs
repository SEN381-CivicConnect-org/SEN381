namespace CivicConnect.Data.Entities;

/// A priority level and the resolution time it allows, e.g. critical within 60 minutes.
public sealed class PriorityTarget
{
    public short Id { get; set; }
    public string Name { get; set; } = null!;
    public int ResolveWithinMinutes { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
