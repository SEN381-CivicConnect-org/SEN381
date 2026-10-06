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
            SubmittedAt = new DateTimeOffset(2026, 9, 9, 0, 0, 0, TimeSpan.Zero)
        }
    };

    public IReadOnlyList<Ticket> GetStaffQueue(TicketQuery query)
    {
        var search = query.Search?.Trim();
        var category = query.Category?.Trim();
        var status = query.Status?.Trim();

        IEnumerable<Ticket> result = Tickets.Where(IsActive);
        if (!string.IsNullOrEmpty(search))
        {
            result = result.Where(ticket =>
                Contains(ticket.Id, search) ||
                Contains(ticket.Title, search) ||
                Contains(ticket.Description, search) ||
                Contains(ticket.Requester, search) ||
                Contains(ticket.Category, search));
        }

        if (!string.IsNullOrEmpty(category))
            result = result.Where(ticket => string.Equals(ticket.Category, category, StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrEmpty(status))
            result = result.Where(ticket => string.Equals(ticket.Status, status, StringComparison.OrdinalIgnoreCase));

        var ordered = query.Sort switch
        {
            TicketSort.PriorityHighFirst => result.OrderByDescending(ticket => PriorityRank(ticket.Priority))
                .ThenByDescending(ticket => ticket.SubmittedAt),
            TicketSort.PriorityLowFirst => result.OrderBy(ticket => PriorityRank(ticket.Priority))
                .ThenByDescending(ticket => ticket.SubmittedAt),
            TicketSort.OldestFirst => result.OrderBy(ticket => ticket.SubmittedAt),
            _ => result.OrderByDescending(ticket => ticket.SubmittedAt)
        };

        return ordered.ThenBy(ticket => ticket.Id, StringComparer.Ordinal).ToList();
    }

    public IReadOnlyList<string> GetStaffQueueCategories() => Tickets.Where(IsActive)
        .Select(ticket => ticket.Category).Where(value => !string.IsNullOrWhiteSpace(value))
        .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(value => value, StringComparer.OrdinalIgnoreCase).ToList();

    public IReadOnlyList<string> GetStaffQueueStatuses() => Tickets.Where(IsActive)
        .Select(ticket => ticket.Status).Where(value => !string.IsNullOrWhiteSpace(value))
        .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(value => value, StringComparer.OrdinalIgnoreCase).ToList();

    private static bool IsActive(Ticket ticket) =>
        !string.Equals(ticket.Status, "Resolved", StringComparison.OrdinalIgnoreCase) &&
        !string.Equals(ticket.Status, "Closed", StringComparison.OrdinalIgnoreCase);

    private static bool Contains(string value, string search) =>
        value.Contains(search, StringComparison.OrdinalIgnoreCase);

    private static int PriorityRank(string priority) => priority switch
    {
        "High" => 3,
        "Medium" => 2,
        "Low" => 1,
        _ => 0
    };

    public Ticket? Find(string id) => Tickets.FirstOrDefault(ticket => ticket.Id == id);

    public string Create(Ticket ticket)
    {
        var number = Tickets.Select(item => int.Parse(item.Id.Split('-')[1])).DefaultIfEmpty(0).Max() + 1;
        ticket.Id = $"CC-{number:000}";
        ticket.Status = "Open";
        ticket.Updated = "Just now";
        ticket.Created = DateTime.Now.ToString("d MMM yyyy");
        ticket.SubmittedAt = DateTimeOffset.UtcNow;
        Tickets.Insert(0, ticket);
        return ticket.Id;
    }
}
