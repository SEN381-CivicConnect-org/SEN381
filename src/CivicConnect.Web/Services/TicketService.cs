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
            ReporterId = "requester@civicconnect.demo",
            SubmittedAt = new DateTimeOffset(2026, 9, 14, 0, 0, 0, TimeSpan.Zero)
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
            ReporterId = "requester@civicconnect.demo",
            SubmittedAt = new DateTimeOffset(2026, 9, 13, 0, 0, 0, TimeSpan.Zero)
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
            ReporterId = "requester@civicconnect.demo",
            SubmittedAt = new DateTimeOffset(2026, 9, 12, 0, 0, 0, TimeSpan.Zero)
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
            ReporterId = "ayesha@civicconnect.demo",
            SubmittedAt = new DateTimeOffset(2026, 9, 9, 0, 0, 0, TimeSpan.Zero)
        }
    };

    // Return a snapshot containing only fields displayed by the existing history table.
    public IReadOnlyList<Ticket> GetReporterHistory(ClaimsPrincipal user)
    {
        var reporterId = ReporterId(user);
        if (reporterId is null)
            return [];

        return Tickets
            .Where(ticket => ticket.ReporterId == reporterId)
            .OrderByDescending(ticket => ticket.SubmittedAt)
            .Take(20)
            .Select(ticket => new Ticket
            {
                Id = ticket.Id,
                Title = ticket.Title,
                Category = ticket.Category,
                Priority = ticket.Priority,
                Status = ticket.Status,
                Updated = ticket.Updated
            })
            .ToList();
    }

    public Ticket? FindVisible(string id, ClaimsPrincipal user)
    {
        if (user.Identity?.IsAuthenticated != true)
            return null;

        if (user.IsInRole("Requester"))
        {
            var reporterId = ReporterId(user);
            return reporterId is null ? null :
                Tickets.FirstOrDefault(ticket => ticket.Id == id && ticket.ReporterId == reporterId);
        }

        return user.IsInRole("Staff") || user.IsInRole("Management")
            ? Tickets.FirstOrDefault(ticket => ticket.Id == id)
            : null;
    }

    public string Create(Ticket ticket, ClaimsPrincipal user)
    {
        var reporterId = ReporterId(user)
            ?? throw new UnauthorizedAccessException("An authenticated requester identity is required.");

        var number = Tickets.Select(item => int.Parse(item.Id.Split('-')[1])).DefaultIfEmpty(0).Max() + 1;
        ticket.Id = $"CC-{number:000}";
        ticket.ReporterId = reporterId;
        ticket.Requester = user.Identity!.Name ?? "";
        ticket.SubmittedAt = DateTimeOffset.UtcNow;
        ticket.Status = "Open";
        ticket.Updated = "Just now";
        ticket.Created = DateTime.Now.ToString("d MMM yyyy");
        Tickets.Insert(0, ticket);
        return ticket.Id;
    }

    private static string? ReporterId(ClaimsPrincipal user)
    {
        if (user.Identity?.IsAuthenticated != true || !user.IsInRole("Requester"))
            return null;

        var id = user.FindFirstValue(ClaimTypes.NameIdentifier);
        return string.IsNullOrWhiteSpace(id) ? null : id;
    }
}
