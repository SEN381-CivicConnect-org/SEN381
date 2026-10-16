using System.Security.Claims;
using CivicConnect.Web.Components.Pages.Requester;
using CivicConnect.Web.Components.Pages.Shared;
using CivicConnect.Web.Models;
using CivicConnect.Web.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace CivicConnect.Tests;

/// <summary>
/// Black-box component tests: arrange public ticket data and a signed-in identity,
/// render the real pages, and inspect only their user-visible HTML. Authentication
/// is supplied at the component boundary; credential verification is out of scope.
/// </summary>
public sealed class RequestVisibilityEquivalencePartitionTests
{
    [Theory]
    [InlineData("Bernard Small", "EP-OWN")]
    [InlineData("Other Requester", "EP-OTHER")]
    public async Task EP_RequesterOwnRequest_IsVisibleInHistory(string name, string id)
    {
        var html = await RenderAsync<MyTickets>(name, "Requester");
        Assert.Contains($"/tickets/{id}", html);
    }

    [Theory]
    [InlineData("Bernard Small", "EP-OWN", "Own request title", "Own request description")]
    [InlineData("Other Requester", "EP-OTHER", "Other request title", "Other request description")]
    public async Task EP_RequesterOwnRequest_DetailsAreVisible(
        string name, string id, string title, string description)
    {
        var html = await RenderAsync<TicketDetail>(name, "Requester", id);
        Assert.Contains(title, html);
        Assert.Contains(description, html);
    }

    [Theory]
    [InlineData("Bernard Small", "EP-OTHER", "Other request title")]
    [InlineData("Other Requester", "EP-OWN", "Own request title")]
    public async Task EP_RequesterOtherUsersRequest_IsNotVisibleInHistory(string name, string otherId, string otherTitle)
    {
        var html = await RenderAsync<MyTickets>(name, "Requester");
        Assert.DoesNotContain($"/tickets/{otherId}", html);
        Assert.DoesNotContain(otherTitle, html);
    }

    [Theory]
    [InlineData("Bernard Small", "EP-OTHER", "Other request title", "Other request description")]
    [InlineData("Other Requester", "EP-OWN", "Own request title", "Own request description")]
    public async Task EP_RequesterOtherUsersRequest_DetailsAreNotDisclosed(
        string name, string id, string title, string description)
    {
        var html = await RenderAsync<TicketDetail>(name, "Requester", id);
        // Accept denial or a not-found view without prescribing exact UI wording.
        Assert.DoesNotContain(title, html);
        Assert.DoesNotContain(description, html);
    }

    [Theory]
    [InlineData("Staff", "Assigned Technician")]
    [InlineData("Management", "Test Manager")]
    public async Task EP_PrivilegedUser_RequesterRequest_IsVisible(
        string role, string name)
    {
        // Staff is the assigned technician in IT Support (FR-04/FR-14), rather
        // than assuming that every staff member may access every team's work.
        var html = await RenderAsync<TicketDetail>(name, role, "EP-OTHER");
        Assert.Contains("Other request title", html);
        Assert.Contains("Other request description", html);
    }

    private static async Task<string> RenderAsync<TComponent>(string name, string role, string? id = null)
        where TComponent : IComponent
    {
        var user = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()),
            new Claim(ClaimTypes.Name, name),
            new Claim(ClaimTypes.Email, $"{role}@example.test"),
            new Claim(ClaimTypes.Role, role),
            new Claim("Team", "IT Support")
        }, "Test"));

        var tickets = new TicketService();
        tickets.Tickets.Clear();
        tickets.Tickets.AddRange(new[]
        {
            new Ticket { Id = "EP-OWN", Requester = "Bernard Small", Title = "Own request title",
                Description = "Own request description", Category = "IT Support", Assignee = "Assigned Technician" },
            new Ticket { Id = "EP-OTHER", Requester = "Other Requester", Title = "Other request title",
                Description = "Other request description", Category = "IT Support", Assignee = "Assigned Technician" }
        });

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAuthorizationCore();
        services.AddSingleton<AuthenticationStateProvider>(new SignedInState(user));
        services.AddSingleton(tickets);
        services.AddSingleton<NavigationManager>(new TestNavigation());
        // WorkspaceNavigation requires the concrete provider. No database operation
        // is performed: authentication for the rendered view comes from SignedInState.
        services.AddSingleton(_ => NpgsqlDataSource.Create("Host=localhost;Database=unused"));
        services.AddSingleton<AppAuthenticationStateProvider>();
        services.AddSingleton<WorkspaceNavigation>();
        await using var provider = services.BuildServiceProvider();
        await using var renderer = new HtmlRenderer(provider, provider.GetRequiredService<ILoggerFactory>());
        return await renderer.Dispatcher.InvokeAsync(async () =>
        {
            RenderFragment content = builder =>
            {
                builder.OpenComponent<TComponent>(0);
                if (id is not null) builder.AddAttribute(1, "Id", id);
                builder.CloseComponent();
            };
            var root = await renderer.RenderComponentAsync<CascadingAuthenticationState>(
                ParameterView.FromDictionary(new Dictionary<string, object?> { ["ChildContent"] = content }));
            return root.ToHtmlString();
        });
    }

    private sealed class SignedInState(ClaimsPrincipal user) : AuthenticationStateProvider
    {
        public override Task<AuthenticationState> GetAuthenticationStateAsync() =>
            Task.FromResult(new AuthenticationState(user));
    }

    private sealed class TestNavigation : NavigationManager
    {
        public TestNavigation() => Initialize("http://localhost/", "http://localhost/requester/tickets");
    }
}
