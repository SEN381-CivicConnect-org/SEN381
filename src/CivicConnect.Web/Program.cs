using CivicConnect.Web.Components;
using CivicConnect.Web.Services;
using Microsoft.AspNetCore.Components.Authorization;
using Npgsql;
using Microsoft.AspNetCore.Authorization;

var builder = WebApplication.CreateBuilder(args);

var connectionString = Environment.GetEnvironmentVariable("CONNECTION_STRING")
    ?? throw new InvalidOperationException("CONNECTION_STRING environment variable is not set. Copy .env.example to .env and fill it in.");

builder.Services.AddRazorComponents().AddInteractiveServerComponents();
builder.Services.AddAuthorizationCore();
builder.Services.AddSingleton(NpgsqlDataSource.Create(connectionString));
builder.Services.AddScoped<AppAuthenticationStateProvider>();
builder.Services.AddScoped<AuthenticationStateProvider>(sp => sp.GetRequiredService<AppAuthenticationStateProvider>());
builder.Services.AddSingleton<TicketService>();

var app = builder.Build();

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
app.MapRazorComponents<App>().AddInteractiveServerRenderMode();
app.Run();
