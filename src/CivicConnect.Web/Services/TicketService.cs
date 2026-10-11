using CivicConnect.Web.Models;

namespace CivicConnect.Web.Services;

public sealed class TicketService
{
    private readonly IPriorityMatrix _priorityMatrix;

    public TicketService(IPriorityMatrix? priorityMatrix = null)
    {
        _priorityMatrix = priorityMatrix ?? new PriorityMatrix();
    }

    public List<Ticket> Tickets { get; } = new()
    {
        new()
        {
            Id = "CC-014",
            Title = "Unable to connect to office Wi-Fi",
            Category = "IT Support",
            Location = "Ground Floor",
            ImpactLevel = PriorityMatrix.LevelMedium,     // 2 (Medium)
            UrgencyLevel = PriorityMatrix.LevelHigh,      // 3 (High)
            ComputedPriorityId = PriorityMatrix.PriorityHigh, // 2 + 3 = 5 -> 2 (High)
            PriorityId = PriorityMatrix.PriorityHigh,
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
            Location = "Ground Floor",
            ImpactLevel = PriorityMatrix.LevelMedium,       // 2 (Medium)
            UrgencyLevel = PriorityMatrix.LevelMedium,      // 2 (Medium)
            ComputedPriorityId = PriorityMatrix.PriorityMedium, // 2 + 2 = 4 -> 3 (Medium)
            PriorityId = PriorityMatrix.PriorityMedium,
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
            Location = "Single Workstation / Office Room",
            ImpactLevel = PriorityMatrix.LevelLow,        // 1 (Low)
            UrgencyLevel = PriorityMatrix.LevelLow,       // 1 (Low)
            ComputedPriorityId = PriorityMatrix.PriorityLow, // 1 + 1 = 2 -> 4 (Low)
            PriorityId = PriorityMatrix.PriorityLow,
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
            Location = "Ground Floor",
            ImpactLevel = PriorityMatrix.LevelMedium,     // 2 (Medium)
            UrgencyLevel = PriorityMatrix.LevelHigh,      // 3 (High)
            ComputedPriorityId = PriorityMatrix.PriorityHigh, // 2 + 3 = 5 -> 2 (High)
            PriorityId = PriorityMatrix.PriorityHigh,
            Priority = "High",
            Status = "Open",
            Updated = "9 Sep 2026",
            Requester = "Ayesha Khan",
            Description = "Please create system access for a new staff member starting Monday."
        }
    };

    public Ticket? Find(string id) => Tickets.FirstOrDefault(x => x.Id == id);

    /// <summary>
    /// Creates a ticket and snapshots its calculated priority via PriorityMatrix (FR-05, DD-02).
    /// </summary>
    public string Create(Ticket t)
    {
        var n = Tickets
            .Select(x => int.TryParse(x.Id.Split('-').LastOrDefault(), out var parsed) ? parsed : 0)
            .DefaultIfEmpty(0)
            .Max() + 1;

        var computedPriorityId = _priorityMatrix.CalculatePriority(t.ImpactLevel, t.UrgencyLevel);
        var effectivePriorityId = t.PriorityOverridden ? t.PriorityId : computedPriorityId;
        var effectivePriorityName = _priorityMatrix.FormatPriorityName(effectivePriorityId);

        t.Id = $"CC-{n:000}";
        t.Status = "Open";
        t.Updated = "Just now";
        t.Created = DateTime.Now.ToString("d MMM yyyy");
        t.ComputedPriorityId = computedPriorityId;
        t.PriorityId = effectivePriorityId;
        t.Priority = effectivePriorityName;

        // Store an immutable snapshot of the ticket's priority state at creation time (IT-PRI-02)
        var snapshot = new Ticket
        {
            Id = t.Id,
            Title = t.Title,
            Category = t.Category,
            CategoryId = t.CategoryId,
            Location = t.Location,
            LocationId = t.LocationId,
            ImpactLevel = t.ImpactLevel,
            UrgencyLevel = t.UrgencyLevel,
            ComputedPriorityId = t.ComputedPriorityId,
            PriorityId = t.PriorityId,
            Priority = t.Priority,
            Status = t.Status,
            Updated = t.Updated,
            Requester = t.Requester,
            Assignee = t.Assignee,
            Description = t.Description,
            Created = t.Created,
            PriorityOverridden = t.PriorityOverridden,
            OverrideReason = t.OverrideReason,
            OverrideBy = t.OverrideBy,
            OverrideAt = t.OverrideAt
        };

        Tickets.Insert(0, snapshot);
        return snapshot.Id;
    }
}
