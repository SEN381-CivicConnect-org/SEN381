using CivicConnect.Data;
using CivicConnect.Data.Configuration;
using CivicConnect.Web.Components;
using CivicConnect.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.EntityFrameworkCore;
using Npgsql;

var builder = WebApplication.CreateBuilder(args);

SystemSecretsConfiguration.LoadDotEnvIfPresent();
var systemSecrets = SystemSecretsConfiguration.LoadAndValidate(builder.Configuration, requireJwtSecret: false);
builder.Services.AddSingleton(systemSecrets);
var connectionString = systemSecrets.ConnectionString;

builder.Services.AddRazorComponents().AddInteractiveServerComponents();
builder.Services.AddControllers();
builder.Services.AddAuthorizationCore();
builder.Services.AddSingleton(NpgsqlDataSource.Create(connectionString));
builder.Services.AddDbContext<CivicConnectDbContext>(options =>
    CivicConnectDbContext.Configure((DbContextOptionsBuilder<CivicConnectDbContext>)options, connectionString));
builder.Services.AddScoped<AppAuthenticationStateProvider>();
builder.Services.AddScoped<AuthenticationStateProvider>(sp => sp.GetRequiredService<AppAuthenticationStateProvider>());
builder.Services.AddScoped<WorkspaceNavigation>();
builder.Services.AddSingleton<IPriorityMatrix, PriorityMatrix>();
builder.Services.AddScoped<IResolutionService, ResolutionService>();
builder.Services.AddScoped<ResolutionService>();
builder.Services.AddSingleton<TicketService>();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<CivicConnectDbContext>();
    await db.Database.MigrateAsync();
}

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