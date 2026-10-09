using CivicConnect.Domain.Entities;
using CivicConnect.Domain.Enums;
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
            Impact = ImpactLevel.Medium,
            Urgency = UrgencyLevel.High,
            PriorityLevel = PriorityLevel.High,
            Priority = "High",
            Status = "In Progress",
            Updated = "Today, 14:20",
            Requester = "Bernard Small",
            Assignee = "Daniel Jacobs",
            Description = "The office Wi-Fi keeps disconnecting from my laptop. I have restarted the device and forgotten/rejoined the network, but the connection still drops every few minutes."
        },
        new()
        {
            Id = "CC-013",
            Title = "Air conditioner not working",
            Category = "Facilities",
            Impact = ImpactLevel.Medium,
            Urgency = UrgencyLevel.Medium,
            PriorityLevel = PriorityLevel.Medium,
            Priority = "Medium",
            Status = "Open",
            Updated = "Yesterday",
            Requester = "Bernard Small",
            Description = "The air conditioner in Meeting Room B is not cooling the room."
        },
        new()
        {
            Id = "CC-012",
            Title = "Request copy of leave policy",
            Category = "HR Request",
            Impact = ImpactLevel.Low,
            Urgency = UrgencyLevel.Low,
            PriorityLevel = PriorityLevel.Low,
            Priority = "Low",
            Status = "Resolved",
            Updated = "12 Sep 2026",
            Requester = "Bernard Small",
            Assignee = "Nadia Peters",
            Description = "Please provide the latest approved employee leave policy document."
        },
        new()
        {
            Id = "CC-010",
            Title = "New starter system access",
            Category = "IT Support",
            Impact = ImpactLevel.High,
            Urgency = UrgencyLevel.High,
            PriorityLevel = PriorityLevel.Critical,
            Priority = "Critical",
            Status = "Open",
            Updated = "9 Sep 2026",
            Requester = "Ayesha Khan",
            Description = "Please create system access for a new staff member starting Monday."
        }
    };

    public Ticket? Find(string id) => Tickets.FirstOrDefault(x => x.Id == id);

    /// <summary>
    /// Creates a ticket using the rich ServiceRequest domain entity to enforce business logic (FR-05).
    /// </summary>
    public string Create(Ticket t)
    {
        var n = Tickets.Select(x => int.Parse(x.Id.Split('-')[1])).DefaultIfEmpty(0).Max() + 1;
        t.Id = $"CC-{n:000}";
        t.Status = "Open";
        t.Updated = "Just now";
        t.Created = DateTime.Now.ToString("d MMM yyyy");

        // Rich Domain Model encapsulation: logic resides inside ServiceRequest
        var domainRequest = new ServiceRequest();
        domainRequest.SetImpactAndUrgency(t.Impact, t.Urgency);

        t.PriorityLevel = domainRequest.Priority;
        t.Priority = domainRequest.Priority.ToString();

        Tickets.Insert(0, t);
        return t.Id;
    }
}
