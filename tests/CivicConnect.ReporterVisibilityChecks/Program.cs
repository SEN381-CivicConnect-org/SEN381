using System.Security.Claims;
using CivicConnect.Web.Models;
using CivicConnect.Web.Services;

var auth = new DemoAuthenticationStateProvider();
Check(auth.SignIn("requester@civicconnect.demo", "password"), "Requester sign-in.");
var reporterA = (await auth.GetAuthenticationStateAsync()).User;
var reporterB = User("other-reporter", "Requester");
var service = new TicketService();
service.Tickets.Clear();

// Display names deliberately match. Ownership must depend on the authenticated ID.
foreach (var (id, owner, day, status) in new[]
{
    ("CC-001", reporterA, 1, "Open"),
    ("CC-002", reporterB, 5, "Resolved"),
    ("CC-003", reporterA, 3, "In Progress"),
    ("CC-004", reporterB, 4, "Open"),
    ("CC-005", reporterA, 2, "Resolved")
})
{
    service.Tickets.Add(new Ticket
    {
        Id = id,
        ReporterId = owner.FindFirstValue(ClaimTypes.NameIdentifier)!,
        Requester = owner.Identity!.Name!,
        SubmittedAt = new DateTimeOffset(2026, 1, day, 0, 0, 0, TimeSpan.Zero),
        Status = status,
        Description = "Private request description"
    });
}

var history = service.GetReporterHistory(reporterA);
Check(history.Count == 3, "IT-VIS-01 OwnHistoryOnly: exactly three own requests.");
Check(history.Select(ticket => ticket.Id).SequenceEqual(new[] { "CC-003", "CC-005", "CC-001" }),
    "IT-VIS-01: exclude other reporters and order newest first.");
Check(history.Select(ticket => ticket.Status).SequenceEqual(new[] { "In Progress", "Resolved", "Open" }),
    "Each reference retains its current status.");
Check(history.All(ticket => ticket.Description == ""), "History omits private detail fields.");
Check(service.GetReporterHistory(reporterB).Select(ticket => ticket.Id).SequenceEqual(new[] { "CC-002", "CC-004" }),
    "Reporter B also receives only their own requests.");

service.Tickets.Single(ticket => ticket.Id == "CC-003").Status = "Resolved";
Check(service.GetReporterHistory(reporterA)[0].Status == "Resolved", "Reload returns current status.");

var submitted = new Ticket { Title = "New request", ReporterId = "other-reporter" };
var reference = service.Create(submitted, reporterA);
history = service.GetReporterHistory(reporterA);
Check(history.Count == 4 && history[0].Id == reference && history[0].Status == "Open",
    "IT-VIS-02 ReadYourWritesAfterSubmit: new request appears first immediately.");
Check(submitted.ReporterId == reporterA.FindFirstValue(ClaimTypes.NameIdentifier),
    "Submission assigns ownership from authentication, overriding supplied ownership.");
Check(!service.GetReporterHistory(reporterB).Any(ticket => ticket.Id == reference),
    "New request remains excluded from another reporter's history.");

Check(service.FindVisible("CC-002", reporterA) is null, "Requester cannot open another reporter's detail.");
Check(service.FindVisible(reference, reporterA) is not null, "Requester can open their own detail.");
Check(service.FindVisible(reference, reporterB) is null, "Other requester cannot open the new detail.");
Check(service.FindVisible("CC-002", User("staff", "Staff")) is not null, "Preserve staff detail access.");
Check(service.FindVisible("CC-002", User("manager", "Management")) is not null, "Preserve management detail access.");

foreach (var denied in new[]
{
    new ClaimsPrincipal(new ClaimsIdentity()),
    new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.Role, "Requester") }, "Test")),
    User("", "Requester"),
    User("staff", "Staff"),
    new ClaimsPrincipal(new ClaimsIdentity(new[]
    {
        new Claim(ClaimTypes.NameIdentifier, "requester@civicconnect.demo"),
        new Claim(ClaimTypes.Role, "Requester")
    }))
})
{
    Check(service.GetReporterHistory(denied).Count == 0, "History requires an authenticated requester ID.");
    if (!denied.IsInRole("Staff"))
        Check(service.FindVisible(reference, denied) is null, "Detail rejects anonymous or unidentified requester.");
    try
    {
        service.Create(new Ticket(), denied);
        throw new Exception("Unauthorized submission was accepted.");
    }
    catch (UnauthorizedAccessException) { }
}

for (var i = 0; i < 25; i++)
    service.Create(new Ticket { Title = $"Request {i}" }, reporterA);
Check(service.GetReporterHistory(reporterA).Count == 20, "History follows the documented limit of 20.");

Console.WriteLine("IT-VIS-01 OwnHistoryOnly passed.");
Console.WriteLine("IT-VIS-02 ReadYourWritesAfterSubmit passed.");
Console.WriteLine("Ordering, current status, detail ownership, and authentication checks passed.");

static ClaimsPrincipal User(string id, string role) => new(new ClaimsIdentity(new[]
{
    new Claim(ClaimTypes.NameIdentifier, id),
    new Claim(ClaimTypes.Name, "Bernard Small"),
    new Claim(ClaimTypes.Role, role)
}, "Test"));

static void Check(bool condition, string message)
{
    if (!condition)
        throw new Exception(message);
}
