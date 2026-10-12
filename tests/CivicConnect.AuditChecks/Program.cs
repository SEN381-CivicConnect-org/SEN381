using System.Text.Json;
using System.Security.Claims;
using CivicConnect.Web.Models;
using CivicConnect.Web.Services;

var audit = new AuditService();
var tickets = new TicketService(audit: audit);
var ticket = new Ticket
{
    Category = "IT Support",
    ImpactLevel = PriorityMatrix.LevelMedium,
    UrgencyLevel = PriorityMatrix.LevelHigh,
    Title = "Call +27821234567 or person@example.com",
    Description = "Private contact information"
};
var before = DateTime.UtcNow;
var id = tickets.Create(ticket, "Test Reporter");
var entry = audit.GetForEntity("Ticket", id).Single();
Assert(entry.Actor == "Test Reporter" && entry.Action == "CREATE", "Creation actor/action.");
Assert(entry.EntityType == "Ticket" && entry.EntityId == id, "Creation entity.");
Assert(entry.Timestamp.Kind == DateTimeKind.Utc && entry.Timestamp >= before && entry.Timestamp <= DateTime.UtcNow, "UTC timestamp.");
using (var values = JsonDocument.Parse(entry.NewValues!))
{
    Assert(values.RootElement.GetProperty("Priority").GetString() == "High", "Created priority.");
    Assert(values.RootElement.GetProperty("Status").GetString() == "Open", "Created status.");
    Assert(!values.RootElement.TryGetProperty("Title", out _), "Free text excluded.");
}
Assert(entry.PreviousValues is null, "Creation has no previous values.");
try
{
    tickets.Create(ticket, "Test Reporter");
    throw new Exception("Duplicate creation should be rejected.");
}
catch (InvalidOperationException)
{
    Assert(audit.GetForEntity("Ticket", id).Count == 1, "Rejected operation creates no audit.");
}
try
{
    tickets.Create(new Ticket(), " ");
    throw new Exception("Missing actor should be rejected.");
}
catch (ArgumentException)
{
    Assert(tickets.Tickets.Count == 5, "Missing actor creates no ticket.");
}

// Exercise the reusable audit contract without adding a priority override business feature.
audit.Record("Test Supervisor", "Incident", "incident-1", "OVERRIDE",
    new Dictionary<string, object?> { ["Priority"] = 3 },
    new Dictionary<string, object?> { ["Priority"] = 1 });
var priority = audit.GetForEntity("Incident", "incident-1").Single();
Assert(priority.Action == "OVERRIDE" && priority.Actor == "Test Supervisor", "Override contract.");
using (var previous = JsonDocument.Parse(priority.PreviousValues!))
using (var next = JsonDocument.Parse(priority.NewValues!))
{
    Assert(previous.RootElement.GetProperty("Priority").GetInt32() == 3, "Previous priority.");
    Assert(next.RootElement.GetProperty("Priority").GetInt32() == 1, "New priority.");
}

var sensitive = new Dictionary<string, object?>
{
    ["pAsSwOrD_HaSh"] = "private-hash",
    ["EMAILADDRESS"] = "private@example.com",
    ["PhoneNumber"] = "+27821234567",
    ["Mobile"] = "0821234567",
    ["ContactNumber"] = "0821234567",
    ["AccessToken"] = "private-token",
    ["Credentials"] = "private-credentials",
    ["Role"] = "Technician",
    ["Nested"] = new[] { new Dictionary<string, object?> { ["Password"] = "private-password", ["Enabled"] = true } },
    ["Note"] = "person@example.com +27821234567"
};
audit.Record("User Administrator", "User", "user-1", "UPDATE", sensitive, sensitive);
var user = audit.GetForEntity("User", "user-1").Single();
foreach (var payload in new[] { user.PreviousValues!, user.NewValues! })
{
    Assert(!payload.Contains("private", StringComparison.OrdinalIgnoreCase), "Sensitive keyed values removed recursively.");
    Assert(!payload.Contains("@") && !payload.Contains("27821234567"), "Contact values under safe keys redacted.");
    using var json = JsonDocument.Parse(payload);
    Assert(json.RootElement.GetProperty("Role").GetString() == "Technician", "Safe values retained.");
    Assert(json.RootElement.GetProperty("Nested")[0].GetProperty("Enabled").GetBoolean(), "Nested safe values retained.");
}
sensitive["Role"] = "Changed after recording";
Assert(!audit.GetForEntity("User", "user-1").Single().NewValues!.Contains("Changed after"), "Caller mutation cannot alter stored values.");
audit.Record("person@example.com", "User", "user-2", "UPDATE");
Assert(!audit.GetForEntity("User", "user-2").Single().Actor.Contains('@'), "Metadata contact redaction.");
Assert(audit.GetForEntity("User", "missing").Count == 0, "Entity queries isolate records.");
var staffId = Guid.NewGuid().ToString();
var staff = new ClaimsPrincipal(new ClaimsIdentity(new[]
{
    new Claim(ClaimTypes.NameIdentifier, staffId),
    new Claim(ClaimTypes.Name, "person@example.com"),
    new Claim(ClaimTypes.Role, "Staff")
}, "Test"));
Assert(AuditService.ActorFor(staff) == staffId, "Actor uses database ID instead of contact details.");
tickets.AddNote(id, "password=private-secret person@example.com", true, staff);
var note = audit.GetForEntity("Ticket", id).Last();
Assert(note.Actor == staffId && note.Action == "ADD_NOTE" && note.NewValues == "{\"IsInternal\":true}",
    "Note mutation audited without sensitive text.");
var count = audit.GetForEntity("Ticket", id).Count;
Assert(!tickets.ChangeStatus(id, "RESOLVED", staff).Success, "Forward lifecycle cannot be skipped.");
Assert(!tickets.ChangeStatus(id, "ASSIGNED", new ClaimsPrincipal()).Success, "Anonymous status mutation rejected.");
Assert(audit.GetForEntity("Ticket", id).Count == count, "Rejected transitions append no audit.");
Assert(tickets.ChangeStatus(id, "ASSIGNED", staff).Success, "Allowed transition works.");
var change = audit.GetForEntity("Ticket", id).Last();
Assert(change.Action == "CHANGE_STATUS" && change.Actor == staffId, "Transition actor/action.");
using (var previous = JsonDocument.Parse(change.PreviousValues!))
using (var next = JsonDocument.Parse(change.NewValues!))
{
    Assert(previous.RootElement.GetProperty("Status").GetString() == "NEW", "Previous status captured.");
    Assert(next.RootElement.GetProperty("Status").GetString() == "ASSIGNED", "New status captured.");
}
var successes = 0;
Parallel.For(0, 20, _ =>
{
    if (tickets.ChangeStatus(id, "IN_PROGRESS", staff).Success)
        Interlocked.Increment(ref successes);
});
Assert(successes == 1 && audit.GetForEntity("Ticket", id).Count == count + 2, "Concurrent transitions audited exactly once.");
Assert(tickets.Find(id)!.StatusHistory.Count == 2, "Existing FR-07 history preserved.");
Assert(tickets.Find(id)!.Priority == "High", "Current calculated priority preserved.");
ticket.Priority = "Low";
Assert(tickets.Find(id)!.Priority == "High", "Dev's isolated snapshot preserved.");
var copy = (AuditEntry[])audit.GetForEntity("Ticket", id);
copy[0] = new AuditEntry { Action = "Tampered" };
Assert(audit.GetForEntity("Ticket", id)[0].Action == "CREATE", "Read snapshots cannot modify stored history.");
audit.Record("Test", "Ticket", "safe", "UPDATE", newValues: new Dictionary<string, object?>
{
    ["AuthorizationHeader"] = "Bearer private", ["Cookies"] = "private", ["Secret"] = "private",
    ["Description"] = "password=private", ["Status"] = "password=private", ["Strings"] = new[] { "private" }
});
Assert(!audit.GetForEntity("Ticket", "safe").Single().NewValues!.Contains("private"),
    "Sensitive headers, free text, invalid workflow labels and arrays removed.");
Console.WriteLine("All FR-08 audit checks passed.");

static void Assert(bool condition, string message)
{
    if (!condition)
    {
        throw new Exception(message);
    }
}
