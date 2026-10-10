using System.Security.Claims;
using CivicConnect.Web.Models;
using CivicConnect.Web.Services;

var tickets = new TicketService();
var staff = User("Staff");
var manager = User("Management");
var requester = User("Requester");
var before = DateTimeOffset.UtcNow;
var internalNote = tickets.AddNote("CC-014", "  Investigating connection  ", true, staff);
var publicNote = tickets.AddNote("CC-014", "A technician will contact you.", false, manager);
Assert(internalNote.Text == "Investigating connection", "Valid notes are trimmed.");
Assert(internalNote.TicketId == "CC-014", "Note belongs to the correct ticket.");
Assert(internalNote.Timestamp >= before && internalNote.Timestamp <= DateTimeOffset.UtcNow,
    "Timestamp is recorded in UTC at submission.");
Assert(internalNote.Author == "Test Staff" && internalNote.AuthorEmail == "Staff@example.test",
    "Author comes from the authenticated identity.");
Assert(tickets.GetNotes("CC-014", staff).SequenceEqual(new[] { publicNote, internalNote }),
    "Notes persist across reads and use newest-first ordering.");
Assert(tickets.GetNotes("CC-014", manager).Count == 2, "Management can read internal notes.");
Assert(tickets.GetNotes("CC-014", requester).SequenceEqual(new[] { publicNote }),
    "Requester receives public notes only.");
Assert(tickets.GetNotes("CC-013", staff).Count == 0, "Notes do not leak into another ticket.");
Assert(tickets.GetNotes("CC-014", new ClaimsPrincipal()).Count == 0, "Anonymous readers receive no notes.");
foreach (var text in new[] { "", " ", "\n\t" })
    Reject<ArgumentException>(() => tickets.AddNote("CC-014", text, true, staff));
Reject<UnauthorizedAccessException>(() => tickets.AddNote("CC-014", "Note", false, requester));
Reject<UnauthorizedAccessException>(() => tickets.AddNote("CC-014", "Note", true, new ClaimsPrincipal()));
Reject<ArgumentException>(() => tickets.AddNote("missing", "Note", true, staff));
Assert(tickets.GetNotes("CC-014", staff).Count == 2, "Rejected submissions append nothing.");
var id = tickets.Create(new Ticket { Title = "New request" });
tickets.AddNote(id, "New request received.", false, staff);
Assert(tickets.Find(id)?.Status == "Open" && tickets.GetNotes(id, requester).Count == 1,
    "Existing create/find workflow supports notes on new tickets.");
Assert(tickets.Find("CC-014")?.Status == "In Progress", "Adding notes preserves ticket status.");
Console.WriteLine("All FR-16 note checks passed.");

static ClaimsPrincipal User(string role) => new(new ClaimsIdentity(new[]
{
    new Claim(ClaimTypes.Name, $"Test {role}"),
    new Claim(ClaimTypes.Email, $"{role}@example.test"),
    new Claim(ClaimTypes.Role, role)
}, "Test"));

static void Assert(bool condition, string message)
{
    if (!condition) throw new Exception(message);
}

static void Reject<T>(Action action) where T : Exception
{
    try { action(); }
    catch (T) { return; }
    throw new Exception($"Expected {typeof(T).Name}.");
}
