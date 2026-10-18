using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using CivicConnect.Data.Configuration;

namespace CivicConnect.Data;

/// <summary>
/// Builds the DbContext for `dotnet ef` migrations at design time.
/// Prefers <c>MIGRATION_CONNECTION_STRING</c> (dedicated migration role <c>civic_migrate</c> under NFR-10),
/// falling back to <c>CONNECTION_STRING</c>.
/// </summary>
public sealed class CivicConnectDbContextFactory : IDesignTimeDbContextFactory<CivicConnectDbContext>
{
    public CivicConnectDbContext CreateDbContext(string[] args)
    {
        SystemSecretsConfiguration.LoadDotEnvIfPresent();

        var connectionString = Environment.GetEnvironmentVariable(SystemSecretsConfiguration.MigrationConnectionStringEnvVar)
            ?? Environment.GetEnvironmentVariable(SystemSecretsConfiguration.ConnectionStringEnvVar)
            ?? throw new InvalidOperationException(
                $"{SystemSecretsConfiguration.MigrationConnectionStringEnvVar} or {SystemSecretsConfiguration.ConnectionStringEnvVar} environment variable is not set. Copy .env.example to .env and fill it in.");

        var tlsConnectionString = SystemSecretsConfiguration.EnsureTlsConnectionString(connectionString);

        var optionsBuilder = CivicConnectDbContext.Configure(
            new DbContextOptionsBuilder<CivicConnectDbContext>(),
            tlsConnectionString);

        return new CivicConnectDbContext(optionsBuilder.Options);
    }
}
