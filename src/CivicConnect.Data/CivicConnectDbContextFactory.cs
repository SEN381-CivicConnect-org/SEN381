using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace CivicConnect.Data;

/// Builds the DbContext for `dotnet ef` at design time; connection string comes only from CONNECTION_STRING.
public sealed class CivicConnectDbContextFactory : IDesignTimeDbContextFactory<CivicConnectDbContext>
{
    public CivicConnectDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("CONNECTION_STRING")
            ?? throw new InvalidOperationException(
                "CONNECTION_STRING environment variable is not set. Copy .env.example to .env and fill it in.");

        var optionsBuilder = CivicConnectDbContext.Configure(
            new DbContextOptionsBuilder<CivicConnectDbContext>(),
            connectionString);

        return new CivicConnectDbContext(optionsBuilder.Options);
    }
}
