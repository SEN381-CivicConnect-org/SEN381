namespace CivicConnect.Data.Entities;

/// Marks entities with created_at/updated_at columns.
public interface IHasTimestamps
{
    DateTimeOffset CreatedAt { get; set; }
    DateTimeOffset UpdatedAt { get; set; }
}
