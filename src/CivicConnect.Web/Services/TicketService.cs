using System.Security.Claims;
using CivicConnect.Web.Models;

namespace CivicConnect.Web.Services;

public sealed class TicketService
{
    private readonly object statusLock = new();

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

    public Ticket? Find(string id) => Tickets.FirstOrDefault(x => x.Id == id);

    public string Create(Ticket ticket)
    {
        var number = Tickets.Select(x => int.Parse(x.Id.Split('-')[1])).DefaultIfEmpty(0).Max() + 1;
        ticket.Id = $"CC-{number:000}";
        ticket.Status = "Open";
        ticket.Updated = "Just now";
        ticket.Created = DateTime.Now.ToString("d MMM yyyy");
        Tickets.Insert(0, ticket);
        return ticket.Id;
    }
    // Minimal forward lifecycle: Open is the existing UI name for NEW.
    private static readonly Dictionary<string, string> NextStatuses = new()
    {
        ["NEW"] = "ASSIGNED",
        ["ASSIGNED"] = "IN_PROGRESS",
        ["IN_PROGRESS"] = "RESOLVED",
        ["RESOLVED"] = "CLOSED"
    };

    public static string? NormalizeStatus(string? status) => status?.Trim().ToUpperInvariant() switch
    {
        "OPEN" or "NEW" => "NEW",
        "ASSIGNED" => "ASSIGNED",
        "IN PROGRESS" or "IN_PROGRESS" => "IN_PROGRESS",
        "RESOLVED" => "RESOLVED",
        "CLOSED" => "CLOSED",
        _ => null
    };

    public static string DisplayStatus(string status) => status switch
    {
        "NEW" => "Open",
        "ASSIGNED" => "Assigned",
        "IN_PROGRESS" => "In Progress",
        "RESOLVED" => "Resolved",
        "CLOSED" => "Closed",
        _ => status
    };

    public static string? GetNextStatus(string? status)
    {
        var current = NormalizeStatus(status);
        return current is not null && NextStatuses.TryGetValue(current, out var next) ? next : null;
    }

    public StatusTransitionResult ChangeStatus(string ticketId, string? newStatus, ClaimsPrincipal actor)
    {
        if (actor.Identity?.IsAuthenticated != true || !actor.IsInRole("Staff") ||
            string.IsNullOrWhiteSpace(actor.Identity.Name))
        {
            return new(false, "Only signed-in staff can change ticket status.");
        }

        // TicketService is shared across circuits: validation and history append must be atomic.
        lock (statusLock)
        {
            var ticket = Find(ticketId);
            if (ticket is null)
            {
                return new(false, "Ticket not found.");
            }

            var previous = NormalizeStatus(ticket.Status);
            var next = NormalizeStatus(newStatus);
            if (previous is null || next is null ||
                !NextStatuses.TryGetValue(previous, out var permitted) || permitted != next)
            {
                return new(false, $"Cannot change status from '{ticket.Status}' to '{newStatus}'. Follow the forward lifecycle: Open → Assigned → In Progress → Resolved → Closed.");
            }

            var changedAt = DateTime.UtcNow;
            ticket.StatusHistory.Add(new TicketStatusHistoryEntry
            {
                PreviousStatus = previous,
                NewStatus = next,
                Actor = actor.Identity.Name,
                ChangedAt = changedAt
            });
            // Preserve the existing display vocabulary used by dashboard and requester filters.
            ticket.Status = ticket.Status == previous ? next : DisplayStatus(next);
            ticket.Updated = "Just now";
            return new(true, "Status updated.");
        }
    }
}
