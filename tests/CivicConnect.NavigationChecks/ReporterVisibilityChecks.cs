using System.Reflection;
using System.Security.Claims;
using System.Text.Encodings.Web;
using CivicConnect.Web.Components.Pages.Requester;
using CivicConnect.Web.Components.Pages.Shared;
using CivicConnect.Web.Models;
using CivicConnect.Web.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

static class ReporterVisibilityChecks
{
    public static async Task RunAsync()
    {
        using var auth = new AuthFixture();
        var tickets = new TicketService();
        var navigation = new TestNavigation();
        using var workspace = new WorkspaceNavigation(navigation, auth.Provider);
        var owners = new Dictionary<string, List<Ticket>>();
        foreach (var email in new[] { "a@example.com", "b@example.com" })
        {
            auth.SignIn(email, "unused");
            var user = (await auth.Provider.GetAuthenticationStateAsync()).User;
            owners[email] = new();
            foreach (var status in new[] { "Open", "In Progress", "Resolved" })
            {
                var model = new Ticket
                {
                    Title = $"{email} {status}",
                    RequesterId = "victim@example.com",
                    Requester = "Forged",
                    Id = "Forged-ID",
                    Status = "Resolved"
                };
                var id = tickets.Create(model, user);
                Check(
                    model.RequesterId == email
                    && model.Requester == "Same display name",
                    "Submitted identity must be replaced with authenticated claims.");
                model.RequesterId = "changed@example.com";
                var created = tickets.GetReporterHistory(user).Single(t => t.Id == id);
                Check(
                    created.RequesterId == email
                    && created.Requester == "Same display name"
                    && id != "Forged-ID",
                    "Stored ownership comes only from claims and cannot change with submitted model.");
                Check(
                    created.Status == "Open"
                    && created.Updated == "Just now"
                    && created.Created == DateTime.Now.ToString("d MMM yyyy"),
                    "Creation preserves status and date behaviour.");
                created.Status = status;
                owners[email].Add(created);
            }
        }

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<NavigationManager>(navigation);
        services.AddSingleton<AuthenticationStateProvider>(auth.Provider);
        services.AddSingleton(tickets);
        services.AddSingleton(workspace);
        using var provider = services.BuildServiceProvider();
        await using var renderer = new HtmlRenderer(provider, provider.GetRequiredService<ILoggerFactory>());
        foreach (var email in owners.Keys)
        {
            auth.SignIn(email, "unused");
            var user = (await auth.Provider.GetAuthenticationStateAsync()).User;
            Check(
                tickets.GetReporterHistory(user).SequenceEqual(owners[email].AsEnumerable().Reverse()),
                "History must include only authenticated owner's tickets, even with identical display names.");
            foreach (var ticket in tickets.Tickets)
            {
                Check(
                    (tickets.FindVisible(ticket.Id, user) is not null) == (ticket.RequesterId == email),
                    "Direct lookup must enforce ownership.");
            }
            foreach (var filter in new[] { "All", "Open", "In Progress", "Resolved" })
            {
                workspace.RequesterFilter = filter;
                var html = await Render<MyTickets>(renderer);
                foreach (var ticket in owners.Values.SelectMany(t => t))
                {
                    Check(
                        html.Contains(HtmlEncoder.Default.Encode(ticket.Title))
                            == (ticket.RequesterId == email
                                && (filter == "All" || ticket.Status == filter)),
                        "Rendered status filters must exclude the other owner's matching statuses.");
                }
            }
            var other = owners.First(p => p.Key != email).Value[0];
            var detail = await Render<TicketDetail>(renderer, other.Id);
            Check(
                detail.Contains("Ticket not found")
                && !detail.Contains(HtmlEncoder.Default.Encode(other.Title)),
                "Other owner's direct detail must render unavailable without content.");
            Check(
                (await Render<TicketDetail>(renderer, owners[email][0].Id))
                    .Contains(HtmlEncoder.Default.Encode(owners[email][0].Title)),
                "Own detail must render.");
        }
        auth.SignIn("A@EXAMPLE.COM", "unused");
        Check(
            tickets.GetReporterHistory((await auth.Provider.GetAuthenticationStateAsync()).User).Count() == 3,
            "Ownership uses case-insensitive email comparison.");

        foreach (var role in new[] { "Staff", "Management" })
        {
            auth.SignIn("employee@example.com", "unused", role);
            var user = (await auth.Provider.GetAuthenticationStateAsync()).User;
            foreach (var ticket in tickets.Tickets)
            {
                Check(
                    tickets.FindVisible(ticket.Id, user) == ticket,
                    "Employees retain cross-owner detail access.");
            }
            Check(tickets.FindVisible("missing", user) is null, "Missing tickets remain unavailable.");
            Check(
                (await Render<TicketDetail>(renderer, owners["a@example.com"][0].Id)).Contains("a@example.com Open"),
                "Employee detail still renders.");
            var html = role == "Staff"
                ? await Render<CivicConnect.Web.Components.Pages.Staff.Queue>(renderer)
                : await Render<CivicConnect.Web.Components.Pages.Management.AllTickets>(renderer);
            foreach (var ticket in owners.Values.SelectMany(t => t))
            {
                Check(
                    html.Contains(HtmlEncoder.Default.Encode(ticket.Title))
                        == (role == "Management" || ticket.Status != "Resolved"),
                    "Employee lists preserve status exclusions and cross-owner visibility.");
            }
            RejectCreate(tickets, user);
        }

        auth.SignIn("form@example.com", "unused");
        navigation.NavigateTo("/requester/new");
        var page = new NewTicket();
        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        typeof(NewTicket).GetProperty("Tickets", flags)!.SetValue(page, tickets);
        typeof(NewTicket).GetProperty("AuthState", flags)!.SetValue(page, auth.Provider);
        typeof(NewTicket).GetProperty("Nav", flags)!.SetValue(page, navigation);
        typeof(NewTicket).GetField("model", flags)!.SetValue(page, new Ticket
        {
            Title = "Form submission",
            RequesterId = "a@example.com",
            Requester = "Forged"
        });
        await (Task)typeof(NewTicket).GetMethod("Submit", flags)!.Invoke(page, null)!;
        var formUser = (await auth.Provider.GetAuthenticationStateAsync()).User;
        var formTicket = tickets.GetReporterHistory(formUser).Single();
        Check(
            formTicket.RequesterId == "form@example.com"
            && formTicket.Requester == "Same display name",
            "New-ticket handler uses authenticated ownership.");
        Check(
            navigation.ToBaseRelativePath(navigation.Uri) == $"tickets/{formTicket.Id}",
            "Submission opens created ticket.");
        workspace.GoBack();
        Check(
            navigation.ToBaseRelativePath(navigation.Uri) == "requester/tickets"
            && (await Render<MyTickets>(renderer)).Contains("Form submission"),
            "Created-ticket Back returns to history containing the new ticket.");
        auth.SignIn("a@example.com", "unused");
        Check(
            !tickets.GetReporterHistory((await auth.Provider.GetAuthenticationStateAsync()).User)
                .Any(t => t.Id == formTicket.Id),
            "Forged form owner must not receive the new ticket.");

        auth.SignIn("a@example.com", "unused", includeEmail: false);
        var missingEmail = (await auth.Provider.GetAuthenticationStateAsync()).User;
        Check(
            !tickets.GetReporterHistory(missingEmail).Any()
            && tickets.FindVisible(formTicket.Id, missingEmail) is null,
            "Missing email fails closed.");
        RejectCreate(tickets, missingEmail);
        auth.SignOut();
        var anonymous = (await auth.Provider.GetAuthenticationStateAsync()).User;
        Check(
            !tickets.GetReporterHistory(anonymous).Any()
            && tickets.FindVisible(formTicket.Id, anonymous) is null,
            "Anonymous access fails closed.");
        RejectCreate(tickets, anonymous);
        auth.SignIn("requester@civicconnect.demo", "unused");
        Check(
            tickets.GetReporterHistory((await auth.Provider.GetAuthenticationStateAsync()).User).Count() == 3,
            "Bernard's demo tickets match seeded email rather than display name.");
    }

    private static async Task<string> Render<T>(
        HtmlRenderer renderer,
        string? id = null) where T : IComponent =>
        await renderer.Dispatcher.InvokeAsync(async () =>
            (await renderer.RenderComponentAsync<T>(
                id is null
                    ? ParameterView.Empty
                    : ParameterView.FromDictionary(new Dictionary<string, object?>
                    {
                        ["Id"] = id
                    }))).ToHtmlString());

    private static void RejectCreate(TicketService tickets, ClaimsPrincipal user)
    {
        try
        {
            tickets.Create(new Ticket(), user);
        }
        catch (UnauthorizedAccessException)
        {
            return;
        }

        throw new Exception("Unauthorized creation must be rejected.");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition)
        {
            throw new Exception(message);
        }
    }
}
