using CivicConnect.Data;
using CivicConnect.Data.Configuration;
using CivicConnect.Data.Security;
using CivicConnect.Web.Components;
using CivicConnect.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.EntityFrameworkCore;
using Npgsql;

var builder = WebApplication.CreateBuilder(args);

SystemSecretsConfiguration.LoadDotEnvIfPresent();
var systemSecrets = SystemSecretsConfiguration.LoadAndValidate(builder.Configuration, requireJwtSecret: false);
builder.Services.AddSingleton(systemSecrets);
var connectionString = SystemSecretsConfiguration.EnsureTlsConnectionString(systemSecrets.ConnectionString);
SystemSecretsConfiguration.EnsureServerTlsReady(connectionString);

builder.Services.AddRazorComponents().AddInteractiveServerComponents();
builder.Services.AddControllers();
builder.Services.AddAuthorizationCore();
// HTTP requests have no circuit identity. Handle denial without challenging an
// unconfigured HTTP scheme; interactive role checks remain in AuthorizeRouteView.
builder.Services.AddSingleton<IAuthorizationMiddlewareResultHandler, DemoAuthorizationResultHandler>();
builder.Services.AddSingleton(NpgsqlDataSource.Create(connectionString));
builder.Services.AddDbContext<CivicConnectDbContext>(options =>
    CivicConnectDbContext.Configure((DbContextOptionsBuilder<CivicConnectDbContext>)options, connectionString));
builder.Services.AddSingleton<IPasswordHashingService, Argon2idPasswordHasher>();
builder.Services.AddSingleton<Argon2idPasswordHasher>();
builder.Services.AddScoped<AppAuthenticationStateProvider>();
builder.Services.AddScoped<AuthenticationStateProvider>(sp => sp.GetRequiredService<AppAuthenticationStateProvider>());
builder.Services.AddScoped<WorkspaceNavigation>();
builder.Services.AddSingleton<IPriorityMatrix, PriorityMatrix>();
builder.Services.AddScoped<IResolutionService, ResolutionService>();
builder.Services.AddScoped<ResolutionService>();
builder.Services.AddSingleton<AuditService>();
builder.Services.AddSingleton<TicketService>();
builder.Services.AddScoped<WorkspaceNavigation>();

var apiBaseUrl = builder.Configuration["ApiSettings:BaseUrl"]
    ?? builder.Configuration["ApiBaseUrl"]
    ?? Environment.GetEnvironmentVariable("API_BASE_URL")
    ?? "http://backend-api:5060";

builder.Services.AddHttpClient("ApiClient", client =>
{
    client.BaseAddress = new Uri(apiBaseUrl);
});
builder.Services.AddScoped(sp => sp.GetRequiredService<IHttpClientFactory>().CreateClient("ApiClient"));

var app = builder.Build();

var isMigrateOnly = args.Contains("--migrate-only", StringComparer.OrdinalIgnoreCase);
var shouldMigrate = isMigrateOnly || args.Contains("--migrate", StringComparer.OrdinalIgnoreCase) ||
    (app.Environment.IsDevelopment() && !string.IsNullOrWhiteSpace(systemSecrets.MigrationConnectionString));

if (shouldMigrate)
{
    var migrationConn = systemSecrets.MigrationConnectionString ?? connectionString;
    var migrationTlsConn = SystemSecretsConfiguration.EnsureTlsConnectionString(migrationConn);
    SystemSecretsConfiguration.EnsureServerTlsReady(migrationTlsConn);

    var migrationOptions = CivicConnectDbContext.Configure(
        new DbContextOptionsBuilder<CivicConnectDbContext>(),
        migrationTlsConn);

    await using var migrationDb = new CivicConnectDbContext(migrationOptions.Options);
    await migrationDb.Database.MigrateAsync();

    if (isMigrateOnly)
    {
        Console.WriteLine("Database migrations completed successfully.");
        return;
    }
}

// NFR-10 (IT-SEC-02): Verify running application database connection is not a superuser and not the migration role
await SystemSecretsConfiguration.EnforceLeastPrivilegeRoleAsync(connectionString);

await DemoDataSeeder.EnsureDemoUsersAsync(app.Services.GetRequiredService<NpgsqlDataSource>());

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseAntiforgery();
app.MapControllers();
app.MapRazorComponents<App>().AddInteractiveServerRenderMode();
app.Run();