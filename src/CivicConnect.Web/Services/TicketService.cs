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
            RequesterId = "requester@civicconnect.demo",
            Assignee = "Daniel Jacobs",
            Description = "The office Wi-Fi keeps disconnecting from my laptop. I have restarted the device and forgotten/rejoined the network, but the connection still drops every few minutes."
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
            RequesterId = "requester@civicconnect.demo",
            Description = "The air conditioner in Meeting Room B is not cooling the room."
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
            RequesterId = "requester@civicconnect.demo",
            Assignee = "Nadia Peters",
            Description = "Please provide the latest approved employee leave policy document."
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
            RequesterId = "ayesha@civicconnect.demo",
            Description = "Please create system access for a new staff member starting Monday."
        }
    };

    public IEnumerable<Ticket> GetReporterHistory(ClaimsPrincipal user)
    {
        var email = ReporterEmail(user);
        return email is null
            ? Array.Empty<Ticket>()
            : Tickets.Where(t => Owns(t, email));
    }

    public Ticket? FindVisible(string id, ClaimsPrincipal user)
    {
        if (user.Identity?.IsAuthenticated != true)
        {
            return null;
        }

        var ticket = Tickets.FirstOrDefault(t => t.Id == id);
        if (user.IsInRole("Staff") || user.IsInRole("Management"))
        {
            return ticket;
        }

        var email = ReporterEmail(user);
        return ticket is not null && email is not null && Owns(ticket, email)
            ? ticket
            : null;
    }

    public string Create(Ticket ticket, ClaimsPrincipal user)
    {
        var email = ReporterEmail(user)
            ?? throw new UnauthorizedAccessException(
                "A signed-in Requester with an email claim is required.");
        ticket.RequesterId = email;
        ticket.Requester = user.FindFirstValue(ClaimTypes.Name) ?? "";
        // Copy form fields so later changes to the submitted model cannot change ownership.
        var created = new Ticket
        {
            Title = ticket.Title,
            Category = ticket.Category,
            Priority = ticket.Priority,
            Description = ticket.Description,
            RequesterId = ticket.RequesterId,
            Requester = ticket.Requester
        };
        var n = Tickets
            .Select(x => int.Parse(x.Id.Split('-')[1]))
            .DefaultIfEmpty(0)
            .Max() + 1;
        created.Id = $"CC-{n:000}";
        created.Status = "Open";
        created.Updated = "Just now";
        created.Created = DateTime.Now.ToString("d MMM yyyy");
        ticket.Id = created.Id;
        ticket.Status = created.Status;
        ticket.Updated = created.Updated;
        ticket.Created = created.Created;
        Tickets.Insert(0, created);
        return created.Id;
    }

    private static string? ReporterEmail(ClaimsPrincipal user)
    {
        if (user.Identity?.IsAuthenticated != true || !user.IsInRole("Requester"))
        {
            return null;
        }

        var email = user.FindFirstValue(ClaimTypes.Email);
        return string.IsNullOrWhiteSpace(email) ? null : email.Trim();
    }

    private static bool Owns(Ticket ticket, string email) =>
        string.Equals(ticket.RequesterId, email, StringComparison.OrdinalIgnoreCase);
}
