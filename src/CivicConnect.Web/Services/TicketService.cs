using System.Security.Claims;
using CivicConnect.Web.Models;

namespace CivicConnect.Web.Services;

public sealed class TicketService
{
    private readonly object statusLock = new();
    private readonly IPriorityMatrix _priorityMatrix;
    private readonly AuditService _audit;
    private readonly object creationLock = new();

    public TicketService(IPriorityMatrix? priorityMatrix = null, AuditService? audit = null)
    {
        _priorityMatrix = priorityMatrix ?? new PriorityMatrix();
        _audit = audit ?? new AuditService();
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
            RequesterId = "requester@civicconnect.demo",
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
            RequesterId = "requester@civicconnect.demo",
            // Cross-category demo assignment exercises the existing team OR assignee visibility rule.
            Assignee = "Daniel Jacobs",
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
            RequesterId = "requester@civicconnect.demo",
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
            RequesterId = "ayesha@civicconnect.demo",
            Description = "Please create system access for a new staff member starting Monday."
        }
    };

    public Ticket? Find(string id) => Tickets.FirstOrDefault(x => x.Id == id);

    // Supports both NameIdentifier (dev) and Email (staging) claims for ID-backed tickets,
    // while falling back to Identity.Name for legacy demo tickets or when RequesterId is absent.
    private static bool IsOwner(Ticket ticket, ClaimsPrincipal user)
    {
        var nameId = user.FindFirstValue(ClaimTypes.NameIdentifier);
        var email = user.FindFirstValue(ClaimTypes.Email)?.Trim();

        if (!string.IsNullOrWhiteSpace(ticket.RequesterId))
        {
            if (!string.IsNullOrWhiteSpace(nameId) && string.Equals(ticket.RequesterId, nameId, StringComparison.Ordinal))
                return true;

            if (!string.IsNullOrWhiteSpace(email) && string.Equals(ticket.RequesterId, email, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        var isSeededDemoTicket = ticket.Id is "CC-010" or "CC-012" or "CC-013" or "CC-014";
        if (isSeededDemoTicket || string.IsNullOrWhiteSpace(ticket.RequesterId))
        {
            return !string.IsNullOrWhiteSpace(user.Identity?.Name) &&
                   string.Equals(ticket.Requester, user.Identity.Name, StringComparison.Ordinal);
        }

        return false;
    }

    public IEnumerable<Ticket> GetVisibleTickets(ClaimsPrincipal user)
    {
        if (user.Identity?.IsAuthenticated != true) return Array.Empty<Ticket>();
        if (user.IsInRole("Staff") || user.IsInRole("Management")) return Tickets;
        return user.IsInRole("Requester") ? Tickets.Where(ticket => IsOwner(ticket, user)) : Array.Empty<Ticket>();
    }

    // Backwards-compatible method for staging components/tests
    public IEnumerable<Ticket> GetReporterHistory(ClaimsPrincipal user) =>
        user.Identity?.IsAuthenticated == true && user.IsInRole("Requester")
            ? Tickets.Where(ticket => IsOwner(ticket, user))
            : Array.Empty<Ticket>();

    public Ticket? FindVisible(string id, ClaimsPrincipal user) =>
        GetVisibleTickets(user).FirstOrDefault(ticket => ticket.Id == id);

    public string CreateForRequester(Ticket ticket, ClaimsPrincipal user)
    {
        ArgumentNullException.ThrowIfNull(ticket);
        var requesterId = user.FindFirstValue(ClaimTypes.NameIdentifier) ?? user.FindFirstValue(ClaimTypes.Email)?.Trim();
        var requesterName = user.Identity?.Name ?? user.FindFirstValue(ClaimTypes.Name);

        if (user.Identity?.IsAuthenticated != true || !user.IsInRole("Requester") ||
            string.IsNullOrWhiteSpace(requesterName) ||
            string.IsNullOrWhiteSpace(requesterId))
            throw new UnauthorizedAccessException("A signed-in requester identity is required.");

        // Ignore ownership supplied by form state; derive both fields from claims.
        ticket.Requester = requesterName;
        ticket.RequesterId = requesterId;
        return Create(ticket, AuditService.ActorFor(user));
    }

    // Legacy snapshots have display-name assignees; require an authenticated, nonblank staff identity.
    public IEnumerable<Ticket> GetAssignedTickets(ClaimsPrincipal user) =>
        user.Identity?.IsAuthenticated == true && user.IsInRole("Staff") &&
        !string.IsNullOrWhiteSpace(user.Identity.Name)
            ? Tickets.Where(ticket => string.Equals(ticket.Assignee, user.Identity.Name, StringComparison.Ordinal)
                && ticket.Status != "Resolved")
            : Array.Empty<Ticket>();

    // Backwards-compatible overload for staging components/tests
    public string Create(Ticket ticket, ClaimsPrincipal user) => CreateForRequester(ticket, user);

    public IEnumerable<Ticket> GetStaffQueue(string? teamName = null, string? staffName = null) =>
        Tickets.Where(ticket =>
            ticket.Status != "Resolved" &&
            ticket.Status != "Closed" &&
            (string.IsNullOrWhiteSpace(teamName) && string.IsNullOrWhiteSpace(staffName)
                || (!string.IsNullOrWhiteSpace(teamName) && string.Equals(ticket.Category, teamName, StringComparison.OrdinalIgnoreCase))
                || (!string.IsNullOrWhiteSpace(staffName) && string.Equals(ticket.Assignee, staffName, StringComparison.OrdinalIgnoreCase))));

    /// <summary>
    /// Creates a ticket and snapshots its calculated priority via PriorityMatrix (FR-05, DD-02).
    /// </summary>
    // Existing non-UI callers retain their API; the requester UI supplies its signed-in actor.
    public string Create(Ticket t) => Create(t, "System");

    public string Create(Ticket t, string actor)
    {
        ArgumentNullException.ThrowIfNull(t);
        ArgumentException.ThrowIfNullOrWhiteSpace(actor);
        lock (creationLock)
        {
            if (!string.IsNullOrEmpty(t.Id) && Find(t.Id) is not null)
                throw new InvalidOperationException("This ticket has already been created.");
            return CreateCore(t, actor);
        }
    }

    private string CreateCore(Ticket t, string actor)
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
            RequesterId = t.RequesterId,
            Assignee = t.Assignee,
            Description = t.Description,
            Created = t.Created,
            PriorityOverridden = t.PriorityOverridden,
            OverrideReason = t.OverrideReason,
            OverrideBy = t.OverrideBy,
            OverrideAt = t.OverrideAt
        };

        // Capture workflow state, excluding free text, contact details and override reasons.
        _audit.Record(actor, "Ticket", snapshot.Id, "CREATE", newValues: new Dictionary<string, object?>
        {
            ["CategoryId"] = snapshot.CategoryId,
            ["LocationId"] = snapshot.LocationId,
            ["ImpactLevel"] = snapshot.ImpactLevel,
            ["UrgencyLevel"] = snapshot.UrgencyLevel,
            ["ComputedPriorityId"] = snapshot.ComputedPriorityId,
            ["PriorityId"] = snapshot.PriorityId,
            ["Priority"] = snapshot.Priority,
            ["PriorityOverridden"] = snapshot.PriorityOverridden,
            ["Status"] = snapshot.Status
        });
        Tickets.Insert(0, snapshot);
        return snapshot.Id;
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
            _audit.Record(AuditService.ActorFor(user), "Ticket", ticketId, "ADD_NOTE",
                newValues: new Dictionary<string, object?> { ["IsInternal"] = isInternal });
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

            // Auditing runs only after the existing authorization and lifecycle checks succeed.
            _audit.Record(AuditService.ActorFor(actor), "Ticket", ticketId, "CHANGE_STATUS",
                previousValues: new Dictionary<string, object?> { ["Status"] = previous },
                newValues: new Dictionary<string, object?> { ["Status"] = next });
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