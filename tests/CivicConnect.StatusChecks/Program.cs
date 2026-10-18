using System.Security.Claims;
using CivicConnect.Web.Models;
using CivicConnect.Web.Services;

var staff = User("Technician One", "Staff");
var statuses = new[] { "NEW", "ASSIGNED", "IN_PROGRESS", "RESOLVED", "CLOSED" };
for (var index = 0; index < statuses.Length; index++)
{
    Assert(TicketService.GetNextStatus(statuses[index]) == (index + 1 < statuses.Length ? statuses[index + 1] : null), "UI offers only the next legal status.");
}
Assert(TicketService.GetNextStatus("Open") == "ASSIGNED", "Existing Open vocabulary supported by UI.");
Assert(TicketService.GetNextStatus("unknown") is null, "Unknown status offers no transition.");

var creationService = new TicketService();
var created = new Ticket { Title = "New request", Requester = "Reporter", Assignee = "Technician", Description = "Details", Status = "Closed" };
var createdId = creationService.Create(created);
var stored = creationService.Find(createdId)!;
Assert(createdId == "CC-015" && !ReferenceEquals(stored, created), "Dev creation identifiers and isolated snapshot preserved.");
Assert(ReferenceEquals(creationService.Tickets[0], stored) && stored.Status == "Open" && stored.Updated == "Just now" && !string.IsNullOrWhiteSpace(stored.Created), "Dev creation defaults and ordering preserved.");
Assert(stored.Requester == "Reporter" && stored.Assignee == "Technician" && stored.Description == "Details" && stored.StatusHistory.Count == 0, "Dev ticket fields preserved without fabricated history.");
var originalPriority = (stored.ComputedPriorityId, stored.PriorityId, stored.Priority);
var internalNote = creationService.AddNote(createdId, "  Investigating  ", true, staff);
var publicNote = creationService.AddNote(createdId, "Assigned to a technician", false, staff);
Assert(stored.StatusHistory.Count == 0 && stored.Status == "Open", "Notes do not change status or append transition history.");
foreach (var target in statuses.Skip(1))
{
    Assert(creationService.ChangeStatus(createdId, target, staff).Success, "Created ticket follows the complete forward lifecycle.");
}
Assert(stored.Status == "Closed" && stored.StatusHistory.Count == 4, "Full lifecycle records four successful transitions.");
Assert((stored.ComputedPriorityId, stored.PriorityId, stored.Priority) == originalPriority, "Status changes preserve dev priority snapshot.");
Assert(creationService.GetNotes(createdId, staff).SequenceEqual(new[] { publicNote, internalNote }), "Status changes preserve dev notes and ordering.");
Assert(creationService.GetNotes(createdId, User("Reporter", "Requester")).SequenceEqual(new[] { publicNote }), "Status changes preserve internal-note visibility rules.");
Assert(internalNote.Text == "Investigating" && internalNote.Timestamp.Offset == TimeSpan.Zero, "Dev note trimming and UTC timestamps preserved.");
Assert(!creationService.ChangeStatus(createdId, "NEW", staff).Success && stored.StatusHistory.Count == 4, "Backward transition preserves recorded history.");
creationService.AddNote(createdId, "Resolution confirmed", false, staff);
Assert(stored.Status == "Closed" && stored.StatusHistory.Count == 4 && creationService.GetNotes(createdId, staff).Count == 3, "Notes still work after the complete lifecycle without changing history.");
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
