using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
using CivicConnect.Data;
using CivicConnect.Web.Controllers;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;
using Xunit;

namespace CivicConnect.Tests.BlackBox;

/// Infrastructure only: real PostgreSQL, production migrations/controllers, Kestrel and
/// authorization. SQL arranges inputs; it never supplies a test verdict. No service is invoked.
public sealed class OverdueHttpFixture : IAsyncLifetime
{
    private readonly string database = "fr12_bb_" + Guid.NewGuid().ToString("N");
    private string adminConnection = null!;
    private string connection = null!;
    private WebApplication? app;
    private int category;
    private int location;
    public HttpClient Client { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        var input = Environment.GetEnvironmentVariable("FR12_CONNECTION_STRING")
            ?? Environment.GetEnvironmentVariable("CONNECTION_STRING")
            ?? "Host=localhost;Port=5432;Database=postgres;Username=civicconnect;Password=changeme";
        var settings = new NpgsqlConnectionStringBuilder(input) { Pooling = false };
        settings.Database = "postgres";
        adminConnection = settings.ConnectionString;
        await ExecuteAsync(adminConnection, $"CREATE DATABASE {database}");
        settings.Database = database;
        connection = settings.ConnectionString;
        try
        {
            var options = CivicConnectDbContext.Configure(new DbContextOptionsBuilder<CivicConnectDbContext>(), connection);
            await using (var db = new CivicConnectDbContext(options.Options))
                await db.Database.MigrateAsync();

            var team = Convert.ToInt32(await ExecuteAsync(connection,
                "INSERT INTO service_team (name) VALUES ('FR12') RETURNING id"));
            category = Convert.ToInt32(await ExecuteAsync(connection,
                "INSERT INTO category (name, description, urgency_level, default_service_team_id) VALUES ('FR12', 'Black-box fixture', 1, @team) RETURNING id", new NpgsqlParameter("team", team)));
            location = Convert.ToInt32(await ExecuteAsync(connection,
                "INSERT INTO location (name, location_kind_id, impact_level) VALUES ('FR12', 1, 1) RETURNING id"));

            var builder = WebApplication.CreateBuilder();
            builder.Logging.ClearProviders();
            builder.WebHost.ConfigureKestrel(server => server.Listen(IPAddress.Loopback, 0));
            builder.Services.AddDbContext<CivicConnectDbContext>(options =>
                CivicConnectDbContext.Configure((DbContextOptionsBuilder<CivicConnectDbContext>)options, connection));
            builder.Services.AddControllers().AddApplicationPart(typeof(IncidentsController).Assembly);
            builder.Services.AddAuthentication("FR12").AddScheme<AuthenticationSchemeOptions, FixtureIdentity>("FR12", _ => { });
            builder.Services.AddAuthorization();
            app = builder.Build();
            app.UseAuthentication();
            app.UseAuthorization();
            app.MapControllers();
            await app.StartAsync();
            var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
            Client = new HttpClient { BaseAddress = new Uri(address), Timeout = TimeSpan.FromSeconds(10) };
        }
        catch
        {
            await DisposeAsync();
            throw;
        }
    }

    public async Task<long> SeedAsync(DateTimeOffset due, string status = "NEW") =>
        Convert.ToInt64(await ExecuteAsync(connection, """
            INSERT INTO incident (category_id, location_id, title, description, status,
                                  computed_priority, priority, opened_at)
            VALUES (@category, @location, 'FR12 black-box request', 'Fixture input',
                    CAST(@status AS incident_status), 1, 1,
                    @due - (SELECT resolve_within_minutes * INTERVAL '1 minute' FROM priority_target WHERE id = 1))
            RETURNING id
            """, new("category", category), new("location", location), new("status", status), new("due", due)));

    public async Task SetDueAsync(long id, DateTimeOffset due) =>
        await ExecuteAsync(connection, """
            UPDATE incident SET priority = priority, opened_at = @due -
                (SELECT resolve_within_minutes * INTERVAL '1 minute' FROM priority_target WHERE id = priority)
            WHERE id = @id
            """, new("due", due), new("id", id));

    private static async Task<object?> ExecuteAsync(string connectionString, string sql, params NpgsqlParameter[] parameters)
    {
        await using var db = new NpgsqlConnection(connectionString);
        await db.OpenAsync();
        await using var command = new NpgsqlCommand(sql, db);
        command.Parameters.AddRange(parameters);
        return await command.ExecuteScalarAsync();
    }

    public async Task DisposeAsync()
    {
        Client?.Dispose();
        if (app is not null) await app.DisposeAsync();
        if (adminConnection is not null)
            await ExecuteAsync(adminConnection, $"DROP DATABASE IF EXISTS {database} WITH (FORCE)");
    }
}

// Current dev supplies no HTTP login scheme. Only identity establishment is replaced;
// the production endpoint's role policy still runs. This does not validate real login.
public sealed class FixtureIdentity(IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger, UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var role = Request.Headers["X-FR12-Role"].FirstOrDefault() ?? "Management";
        var identity = new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "fr12-manager"),
            new Claim(ClaimTypes.Role, role)], Scheme.Name);
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name)));
    }
}
