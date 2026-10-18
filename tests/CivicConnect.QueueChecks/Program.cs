using CivicConnect.Web.Models;
using CivicConnect.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Queue = CivicConnect.Web.Components.Queue;

var now = new DateTime(2026, 10, 8, 16, 0, 0);
var tickets = new[]
{
    new Ticket
    {
        Id = "A",
        Title = "WiFi outage",
        Description = "Router offline",
        Category = "IT Support",
        Status = "Open",
        Priority = "Low",
        Updated = "9 Sep 2026"
    },
    new Ticket
    {
        Id = "CC-TEST-B",
        Title = "WiFi slow",
        Category = "IT Support",
        Status = "In Progress",
        Priority = "High",
        Updated = "Today, 14:20"
    },
    new Ticket
    {
        Id = "C",
        Title = "Cooling",
        Category = "Facilities",
        Status = "Open",
        Priority = "Medium",
        Updated = "Yesterday"
    },
    new Ticket
    {
        Id = "D",
        Title = "WiFi fixed",
        Category = "IT Support",
        Status = "Resolved",
        Priority = "High"
    },
    new Ticket
    {
        Id = "E",
        Title = "WiFi closed",
        Category = "IT Support",
        Status = "Closed",
        Priority = "High"
    }
};

var activeTickets = tickets
    .Where(ticket => ticket.Status != "Resolved" && ticket.Status != "Closed")
    .ToArray();

IEnumerable<Ticket> Filter(
    string search = "",
    string category = "",
    string status = "",
    string sort = "date-newest")
{
    return TicketQueueFilter.Apply(activeTickets, search, category, status, sort, now);
}

Check(
    Filter("  WIFI  ")
        .Select(ticket => ticket.Id)
        .SequenceEqual(new[] { "CC-TEST-B", "A" }),
    "Trimmed case-insensitive title search");
Check(Filter("router").Single().Id == "A", "Description search");
Check(Filter("CC-TEST-B").Single().Id == "CC-TEST-B", "ID search");
Check(Filter().Count() == 3, "Cleared search restores active collection");

Check(Filter(category: "IT Support").Count() == 2, "Category filter");
Check(Filter(category: "").Count() == 3, "All categories");

Check(Filter(status: "Open").All(ticket => ticket.Status == "Open"), "Status filter");
Check(Filter(status: "").Count() == 3, "All statuses");

Check(
    Filter(sort: "priority-high")
        .Select(ticket => ticket.Priority)
        .SequenceEqual(new[] { "High", "Medium", "Low" }),
    "Priority descending");
Check(
    Filter(sort: "priority-low")
        .Select(ticket => ticket.Priority)
        .SequenceEqual(new[] { "Low", "Medium", "High" }),
    "Priority ascending");

Check(
    Filter("wifi", "IT Support", "Open", "priority-high").Single().Id == "A",
    "Combined criteria");
Check(
    !Filter(status: "Resolved").Any() && !Filter(status: "Closed").Any(),
    "Filters cannot expand active visibility");
Check(
    !TicketQueueFilter.Apply(activeTickets.Take(1), "slow", "", "", "date-newest", now).Any(),
    "Restricted source cannot expand");

Check(
    Filter(sort: "date-newest")
        .Select(ticket => ticket.Id)
        .SequenceEqual(new[] { "CC-TEST-B", "C", "A" }),
    "Dates newest first");
Check(
    Filter(sort: "date-oldest")
        .Select(ticket => ticket.Id)
        .SequenceEqual(new[] { "A", "C", "CC-TEST-B" }),
    "Dates oldest first");
Check(!Filter("missing").Any(), "Empty results");

var dateTickets = new[]
{
    new Ticket
    {
        Id = "unknown",
        Updated = "invalid"
    },
    new Ticket
    {
        Id = "created",
        Updated = "invalid",
        Created = "7 Sep 2026"
    },
    new Ticket
    {
        Id = "now",
        Updated = "Just now"
    },
    new Ticket
    {
        Id = "today",
        Updated = "Today"
    }
};

Check(
    TicketQueueFilter.Apply(dateTickets, "", "", "", "date-newest", now)
        .Select(ticket => ticket.Id)
        .SequenceEqual(new[] { "now", "today", "created", "unknown" }),
    "Relative dates and Created fallback");
Check(
    TicketQueueFilter.Apply(dateTickets, "", "", "", "date-oldest", now).Last().Id == "unknown",
    "Unknown dates last ascending");

Check(
    typeof(Queue)
        .GetCustomAttributes(typeof(AuthorizeAttribute), true)
        .Cast<AuthorizeAttribute>()
        .Single().Roles == "Staff",
    "Staff authorization retained");

// Render the actual Queue and shared table without starting the database-backed app.
var services = new ServiceCollection();
services.AddLogging();
services.AddSingleton<TicketService>();
services.AddSingleton<NavigationManager, TestNavigation>();
await using var provider = services.BuildServiceProvider();
await using var renderer = new HtmlRenderer(
    provider,
    provider.GetRequiredService<ILoggerFactory>());

await renderer.Dispatcher.InvokeAsync(async () =>
{
    var component = await renderer.RenderComponentAsync<Queue>();
    var html = component.ToHtmlString();

    Check(
        html.Contains("queue-search") &&
        html.Contains("queue-category") &&
        html.Contains("queue-status") &&
        html.Contains("queue-sort"),
        "Queue controls render");
    Check(
        html.Contains("/tickets/CC-014") && !html.Contains("/tickets/CC-012"),
        "Shared table shows active tickets only");
    Check(!html.Contains(">Resolved</option>"), "Status options exclude resolved tickets");

    provider.GetRequiredService<TicketService>().Tickets.Clear();
    var empty = await renderer.RenderComponentAsync<Queue>();
    Check(
        empty.ToHtmlString().Contains("No tickets match your search or filters."),
        "Queue empty state renders");
});

Console.WriteLine("All Staff Queue search, filter, sort, visibility, and rendering checks passed.");

static void Check(bool condition, string message)
{
    if (!condition)
    {
        throw new Exception(message);
    }
}

sealed class TestNavigation : NavigationManager
{
    public TestNavigation()
    {
        Initialize("http://localhost/", "http://localhost/staff/queue");
    }
}
