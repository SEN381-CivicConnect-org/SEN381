using CivicConnect.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CivicConnect.Data.Tests;

/// Migrates the test database once per run; connection string comes only from CONNECTION_STRING.
public sealed class CivicConnectDatabaseFixture : IAsyncLifetime
{
    private readonly string _connectionString;

    public CivicConnectDatabaseFixture()
    {
        _connectionString = ResolveConnectionString()
            ?? throw new InvalidOperationException(
                "CONNECTION_STRING environment variable is not set. Copy .env.example to .env and fill it in, then export it before running tests.");
    }

    private static string? ResolveConnectionString()
    {
        var envValue = Environment.GetEnvironmentVariable("CONNECTION_STRING");
        if (!string.IsNullOrWhiteSpace(envValue))
        {
            return envValue;
        }

        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var envFile = Path.Combine(dir.FullName, ".env");
            if (File.Exists(envFile))
            {
                foreach (var line in File.ReadAllLines(envFile))
                {
                    var trimmed = line.Trim();
                    if (trimmed.StartsWith("CONNECTION_STRING=", StringComparison.Ordinal))
                    {
                        var raw = trimmed["CONNECTION_STRING=".Length..].Trim().Trim('"', '\'');
                        return raw.Replace("Host=db;", "Host=localhost;", StringComparison.OrdinalIgnoreCase);
                    }
                }
            }

            dir = dir.Parent;
        }

        return null;
    }

    public async Task InitializeAsync()
    {
        await using var context = CreateContext();
        await context.Database.MigrateAsync();

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();
        await connection.ReloadTypesAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    public CivicConnectDbContext CreateContext()
    {
        var optionsBuilder = CivicConnectDbContext.Configure(
            new DbContextOptionsBuilder<CivicConnectDbContext>(),
            _connectionString);

        return new CivicConnectDbContext(optionsBuilder.Options);
    }
}

[CollectionDefinition("Database")]
public sealed class DatabaseCollection : ICollectionFixture<CivicConnectDatabaseFixture>
{
}
