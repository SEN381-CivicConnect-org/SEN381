using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using CivicConnect.Data;
using CivicConnect.Data.Entities;
using CivicConnect.Web.Controllers;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace CivicConnect.Web.Tests;

public sealed class IncidentPriorityOverrideEndpointTests
{
    [Theory]
    [InlineData("Supervisor", HttpStatusCode.OK)]
    [InlineData("supervisor", HttpStatusCode.OK)]
    [InlineData("Staff", HttpStatusCode.OK)]
    [InlineData("Management", HttpStatusCode.OK)]
    [InlineData("Requester", HttpStatusCode.Forbidden)]
    [InlineData(null, HttpStatusCode.Unauthorized)]
    public async Task PutOverridePriority_EnforcesExistingAuthorizationAndRoute(
        string? role, HttpStatusCode expectedStatus)
    {
        var databaseName = $"fr06-http-{Guid.NewGuid():N}";
        // Host the real controller and MVC/auth middleware without starting the application's
        // development database, demo authentication, or other production startup services.
        using var host = new HostBuilder().ConfigureWebHost(builder => builder.UseTestServer()
            .ConfigureServices(services =>
            {
                services.AddDbContext<CivicConnectDbContext>(options => options.UseInMemoryDatabase(databaseName));
                services.AddControllers().AddApplicationPart(typeof(IncidentsController).Assembly);
                services.AddAuthentication("FR06-test")
                    .AddScheme<AuthenticationSchemeOptions, SupervisorAuthenticationHandler>("FR06-test", _ => { });
                services.AddAuthorization();
            })
            .Configure(app =>
            {
                app.UseRouting();
                app.UseAuthentication();
                app.UseAuthorization();
                app.UseEndpoints(endpoints => endpoints.MapControllers());
            })).Build();
        await host.StartAsync();

        using var scope = host.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<CivicConnectDbContext>();
        await context.Database.EnsureCreatedAsync();
        context.Incidents.Add(new Incident
        {
            Id = 110,
            ReferenceNumber = "INC-000110",
            Title = "Water leak",
            Description = "Community centre leak",
            Priority = 3,
            ComputedPriority = 3,
            Version = 7
        });
        await context.SaveChangesAsync();

        using var client = host.GetTestClient();
        if (role is not null)
            client.DefaultRequestHeaders.Add("X-Test-Role", role);
        using var response = await client.PutAsJsonAsync("/api/incidents/110/override-priority",
            new OverridePriorityRequestDto { NewPriority = 1, Reason = "Safety escalation", Version = 7 });

        Assert.Equal(expectedStatus, response.StatusCode);
        var stored = await context.Incidents.AsNoTracking().SingleAsync();
        Assert.Equal((short)3, stored.ComputedPriority);
        if (expectedStatus == HttpStatusCode.OK)
        {
            Assert.Equal((short)1, stored.Priority);
            Assert.True(stored.PriorityOverridden);
            Assert.Equal(SupervisorAuthenticationHandler.UserId, stored.OverrideBy);
            Assert.Equal("Safety escalation", stored.OverrideReason);
            Assert.NotNull(stored.OverrideAt);
        }
        else
        {
            Assert.Equal((short)3, stored.Priority);
            Assert.False(stored.PriorityOverridden);
            Assert.Null(stored.OverrideReason);
            Assert.Null(stored.OverrideBy);
            Assert.Null(stored.OverrideAt);
            Assert.Equal(7, stored.Version);
        }
        await context.Database.EnsureDeletedAsync();
    }

    private sealed class SupervisorAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
    {
        public static readonly Guid UserId = Guid.Parse("8dbfb2d9-cf5e-42f8-87e0-6e59f72d8710");

        public SupervisorAuthenticationHandler(IOptionsMonitor<AuthenticationSchemeOptions> options,
            ILoggerFactory logger, UrlEncoder encoder) : base(options, logger, encoder) { }

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.TryGetValue("X-Test-Role", out var role))
                return Task.FromResult(AuthenticateResult.NoResult());
            var identity = new ClaimsIdentity(new[]
            {
                new Claim(ClaimTypes.NameIdentifier, UserId.ToString()),
                new Claim(ClaimTypes.Role, role.ToString())
            }, Scheme.Name);
            return Task.FromResult(AuthenticateResult.Success(
                new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name)));
        }
    }
}
