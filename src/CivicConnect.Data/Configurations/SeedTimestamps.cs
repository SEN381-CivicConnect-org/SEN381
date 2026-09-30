namespace CivicConnect.Data.Configurations;

/// Fixed CreatedAt used for seed rows, since HasData cannot call now().
internal static class SeedTimestamps
{
    public static readonly DateTimeOffset SeededAt = new(2026, 9, 27, 0, 0, 0, TimeSpan.Zero);
}
