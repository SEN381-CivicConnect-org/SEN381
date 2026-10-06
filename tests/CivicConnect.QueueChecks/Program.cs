using CivicConnect.Web.Models;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using CivicConnect.Web.Services;

var service = new TicketService();
service.Tickets.Clear();
service.Tickets.AddRange(new[]
{
    Ticket("CC-001", "Wi-Fi laptop", "IT Support", "Open", "Low", 5),
    Ticket("CC-002", "Wi-Fi office", "IT Support", "Open", "High", 2),
    Ticket("CC-003", "Wi-Fi meeting room", "IT Support", "In Progress", "Medium", 4),
    Ticket("CC-004", "Air conditioner", "Facilities", "Open", "Medium", 3),
    Ticket("CC-005", "Wi-Fi resolved", "IT Support", "Resolved", "High", 6),
    Ticket("CC-006", "Wi-Fi closed", "IT Support", "Closed", "High", 7),
    Ticket("CC-007", "Wi-Fi guest", "IT Support", "Open", "High", 1)
});
service.Tickets[3].Description = "Cooling failure";
service.Tickets[3].Requester = "Ayesha Khan";

Expect(new(), "CC-001", "CC-003", "CC-004", "CC-002", "CC-007");
Expect(new() { Search = "  WI-FI  " }, "CC-001", "CC-003", "CC-002", "CC-007");
Expect(new() { Search = "cc-004" }, "CC-004");
Expect(new() { Search = "cooling" }, "CC-004");
Expect(new() { Search = "ayesha" }, "CC-004");
Expect(new() { Search = "facilities" }, "CC-004");
Expect(new() { Category = "Facilities" }, "CC-004");
Expect(new() { Status = "in progress" }, "CC-003");
Expect(new() { Status = "Open" }, "CC-001", "CC-004", "CC-002", "CC-007");
Expect(new() { Sort = TicketSort.PriorityHighFirst }, "CC-002", "CC-007", "CC-003", "CC-004", "CC-001");
Expect(new() { Sort = TicketSort.PriorityLowFirst }, "CC-001", "CC-003", "CC-004", "CC-002", "CC-007");
Expect(new() { Sort = TicketSort.OldestFirst }, "CC-007", "CC-002", "CC-004", "CC-003", "CC-001");
Expect(new() { Search = "wi-fi", Category = "IT Support", Status = "Open", Sort = TicketSort.PriorityHighFirst },
    "CC-002", "CC-007", "CC-001");
Expect(new() { Search = "   ", Category = "", Status = "" }, "CC-001", "CC-003", "CC-004", "CC-002", "CC-007");
Expect(new() { Search = "missing" });
Expect(new() { Category = "Unknown" });
Expect(new() { Status = "Resolved" });
Expect(new() { Status = "Closed" });
Check(service.GetStaffQueueCategories().SequenceEqual(new[] { "Facilities", "IT Support" }), "Existing active categories.");
Check(service.GetStaffQueueStatuses().SequenceEqual(new[] { "In Progress", "Open" }), "Existing active statuses.");
Check(service.Tickets.Select(ticket => ticket.Id).SequenceEqual(Enumerable.Range(1, 7).Select(n => $"CC-{n:000}")),
    "Queries do not mutate the shared ticket list.");
Check(service.Tickets[0].Priority == "Low" && service.Tickets[4].Status == "Resolved", "Queries preserve ticket values.");

var created = new Ticket { Title = "New request", Category = "Facilities" };
var id = service.Create(created);
Check(created.SubmittedAt > service.Tickets[1].SubmittedAt, "Creation records a sortable timestamp.");
Check(service.GetStaffQueue(new())[0].Id == id, "New requests appear first in newest-first sorting.");
Check(service.Find("CC-005") is not null, "Shared detail lookup remains unchanged.");
var services = new ServiceCollection();
services.AddLogging();
services.AddSingleton(new TicketService());
services.AddSingleton<NavigationManager, PreviewNavigation>();
await using var provider = services.BuildServiceProvider();
await using var renderer = new HtmlRenderer(provider, provider.GetRequiredService<ILoggerFactory>());
var html = await renderer.Dispatcher.InvokeAsync(async () =>
{
    var output = await renderer.RenderComponentAsync<CivicConnect.Web.Components.Pages.Staff.Queue>();
    return output.ToHtmlString();
});
Check(html.Contains("#CC-014") && html.Contains("#CC-013") && html.Contains("#CC-010") && !html.Contains("#CC-012"),
    "Queue renders the active service result.");
Check(html.Contains("queue-search") && html.Contains("queue-category") && html.Contains("queue-status") && html.Contains("queue-sort"),
    "Queue renders all labelled FR-15 controls.");
if (args.Length == 1)
    await File.WriteAllTextAsync(args[0], "<!doctype html><html><head><meta name=\"viewport\" content=\"width=device-width, initial-scale=1\"><link rel=\"stylesheet\" href=\"http://localhost:5186/css/app.css\"></head><body><main class=\"content\">" + html + "</main></body></html>");

Console.WriteLine("All FR-15 search, filter, sort, combined-query, empty-state, and creation checks passed.");

void Expect(TicketQuery query, params string[] ids) =>
    Check(service.GetStaffQueue(query).Select(ticket => ticket.Id).SequenceEqual(ids), $"Query failed: {query}");

static Ticket Ticket(string id, string title, string category, string status, string priority, int day) => new()
{
    Id = id, Title = title, Category = category, Status = status, Priority = priority,
    SubmittedAt = new DateTimeOffset(2026, 1, day, 0, 0, 0, TimeSpan.Zero)
};

static void Check(bool condition, string message)
{
    if (!condition)
        throw new Exception(message);
}

sealed class PreviewNavigation : NavigationManager
{
    public PreviewNavigation() => Initialize("http://localhost:5186/", "http://localhost:5186/staff/queue");
    protected override void NavigateToCore(string uri, bool forceLoad) => Uri = ToAbsoluteUri(uri).AbsoluteUri;
}
