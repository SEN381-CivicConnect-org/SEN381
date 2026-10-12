using System.Security.Claims;
using CivicConnect.Web.Models;

namespace CivicConnect.Web.Services;

public sealed class TicketService
{
    public List<Ticket> Tickets { get; } = new()
    {
        new()
        {
            Id = "CC-014",
            Title = "Unable to connect to office Wi-Fi",
            Category = "IT Support",
            Priority = "High",
            Status = "In Progress",
            Updated = "Today, 14:20",
            Requester = "Bernard Small",
            Assignee = "Daniel Jacobs",
            Description = "The office Wi-Fi keeps disconnecting from my laptop. I have restarted the device and forgotten/rejoined the network, but the connection still drops every few minutes.",
        },
        new()
        {
            Id = "CC-013",
            Title = "Air conditioner not working",
            Category = "Facilities",
            Priority = "Medium",
            Status = "Open",
            Updated = "Yesterday",
            Requester = "Bernard Small",
            Description = "The air conditioner in Meeting Room B is not cooling the room.",
        },
        new()
        {
            Id = "CC-012",
            Title = "Request copy of leave policy",
            Category = "HR Request",
            Priority = "Low",
            Status = "Resolved",
            Updated = "12 Sep 2026",
            Requester = "Bernard Small",
            Assignee = "Nadia Peters",
            Description = "Please provide the latest approved employee leave policy document.",
        },
        new()
        {
            Id = "CC-010",
            Title = "New starter system access",
            Category = "IT Support",
            Priority = "High",
            Status = "Open",
            Updated = "9 Sep 2026",
            Requester = "Ayesha Khan",
            Description = "Please create system access for a new staff member starting Monday.",
        },
    };

    public Ticket? Find(string id) => Tickets.FirstOrDefault(ticket => ticket.Id == id);

    public string Create(Ticket ticket)
    {
        var number = Tickets.Select(item => int.Parse(item.Id.Split('-')[1])).DefaultIfEmpty(0).Max() + 1;
        ticket.Id = $"CC-{number:000}";
        ticket.Status = "Open";
        ticket.Updated = "Just now";
        ticket.Created = DateTime.Now.ToString("d MMM yyyy");
        Tickets.Insert(0, ticket);
        return ticket.Id;
    }

    private readonly object _notesLock = new();
    private readonly List<TicketNote> _notes = new();

    private static bool CanAddNotes(ClaimsPrincipal user) =>
        user.Identity?.IsAuthenticated == true &&
        (user.IsInRole("Staff") || user.IsInRole("Management"));

    public TicketNote AddNote(string ticketId, string text, bool isInternal, ClaimsPrincipal user)
    {
        if (!CanAddNotes(user))
            throw new UnauthorizedAccessException("Only staff and management can add notes.");
        if (string.IsNullOrWhiteSpace(text))
            throw new ArgumentException("Enter a note before saving.", nameof(text));
        if (Find(ticketId) is null)
            throw new ArgumentException("Ticket not found.", nameof(ticketId));

        var note = new TicketNote(ticketId, text.Trim(), DateTimeOffset.UtcNow,
            user.Identity!.Name ?? user.FindFirstValue(ClaimTypes.Email) ?? "Staff",
            user.FindFirstValue(ClaimTypes.Email), isInternal);
        lock (_notesLock)
        {
            _notes.Add(note);
        }
        return note;
    }

    public IReadOnlyList<TicketNote> GetNotes(string ticketId, ClaimsPrincipal user)
    {
        if (user.Identity?.IsAuthenticated != true)
            return Array.Empty<TicketNote>();

        // Filter before returning data to the UI; requesters never receive internal notes.
        var includeInternal = CanAddNotes(user) && !user.IsInRole("Requester");
        lock (_notesLock)
        {
            return _notes.Where(note => note.TicketId == ticketId &&
                (includeInternal || !note.IsInternal))
                .OrderByDescending(note => note.Timestamp).ToArray();
        }
    }
}
