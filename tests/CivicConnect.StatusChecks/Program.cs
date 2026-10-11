using System.Security.Claims;
using CivicConnect.Web.Models;
using CivicConnect.Web.Services;

var staff = User("Technician One", "Staff");
var statuses = new[] { "NEW", "ASSIGNED", "IN_PROGRESS", "RESOLVED", "CLOSED" };
for (var from = 0; from < statuses.Length; from++)
{
    for (var to = 0; to < statuses.Length; to++)
    {
        var service = new TicketService();
        var ticket = new Ticket { Id = "test", Status = statuses[from], Updated = "Before" };
        service.Tickets.Add(ticket);
        var before = DateTime.UtcNow;
        var result = service.ChangeStatus(ticket.Id, statuses[to], staff);
        var allowed = to == from + 1;
        Assert(result.Success == allowed, $"Matrix {statuses[from]} -> {statuses[to]}.");
        Assert(ticket.Status == (allowed ? statuses[to] : statuses[from]), "Status preserved or changed correctly.");
        Assert(ticket.StatusHistory.Count == (allowed ? 1 : 0), "Exactly one history entry on success, none on rejection.");
        if (allowed)
        {
            var entry = ticket.StatusHistory.Single();
            Assert(entry.PreviousStatus == statuses[from] && entry.NewStatus == statuses[to], "History statuses.");
            Assert(entry.Actor == "Technician One", "History actor.");
            Assert(entry.ChangedAt.Kind == DateTimeKind.Utc && entry.ChangedAt >= before && entry.ChangedAt <= DateTime.UtcNow, "History UTC timestamp.");
        }
        else
        {
            Assert(!string.IsNullOrWhiteSpace(result.Message), "Explanatory rejection.");
            Assert(ticket.Updated == "Before", "Rejected transition must not change modified time.");
        }
    }
}

var legacyService = new TicketService();
var legacy = legacyService.Find("CC-013")!;
Assert(legacyService.ChangeStatus(legacy.Id, " assigned ", staff).Success, "Legacy Open and trimmed status supported.");
Assert(legacy.Status == "Assigned" && legacy.StatusHistory.Single().PreviousStatus == "NEW", "Display names preserved; history canonical.");
var count = legacy.StatusHistory.Count;
foreach (var target in new string?[] { null, "", "not-a-status", "ON_HOLD", "ASSIGNED" })
{
    Assert(!legacyService.ChangeStatus(legacy.Id, target, staff).Success, "Invalid or repeated status rejected.");
    Assert(legacy.Status == "Assigned" && legacy.StatusHistory.Count == count, "Rejection preserves existing history.");
}
Assert(!legacyService.ChangeStatus("missing", "ASSIGNED", staff).Success, "Missing ticket rejected.");
foreach (var actor in new[] { new ClaimsPrincipal(), User("Reporter", "Requester"), User("Manager", "Management"), User("", "Staff") })
{
    Assert(!legacyService.ChangeStatus(legacy.Id, "IN_PROGRESS", actor).Success, "Unauthorised or unnamed actor rejected.");
}
Assert(legacy.Status == "Assigned" && legacy.StatusHistory.Count == count, "Authorization failures preserve state.");

var concurrentService = new TicketService();
var concurrent = new Ticket { Id = "concurrent", Status = "NEW" };
concurrentService.Tickets.Add(concurrent);
var successes = 0;
Parallel.For(0, 20, _ =>
{
    if (concurrentService.ChangeStatus(concurrent.Id, "ASSIGNED", staff).Success)
    {
        Interlocked.Increment(ref successes);
    }
});
Assert(successes == 1 && concurrent.StatusHistory.Count == 1, "Concurrent duplicate transitions create exactly one entry.");
Console.WriteLine("All FR-07 status transition checks passed.");

static ClaimsPrincipal User(string name, string role) => new(new ClaimsIdentity(
    new[] { new Claim(ClaimTypes.Name, name), new Claim(ClaimTypes.Role, role) }, "Test"));

static void Assert(bool condition, string message)
{
    if (!condition)
    {
        throw new Exception(message);
    }
}
