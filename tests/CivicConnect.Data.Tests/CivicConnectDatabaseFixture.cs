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
        _connectionString = Environment.GetEnvironmentVariable("CONNECTION_STRING")
            ?? throw new InvalidOperationException(
                "CONNECTION_STRING environment variable is not set. Copy .env.example to .env and fill it in, then export it before running tests.");
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
