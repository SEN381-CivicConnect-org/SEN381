using CivicConnect.Web.Services;

namespace CivicConnect.Web.Models;

public sealed class Ticket
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public string Category { get; set; } = "";
    public int? CategoryId { get; set; }
    public string Location { get; set; } = "Ground Floor";
    public int? LocationId { get; set; }

    // FR-05 scale fields matching CivicConnect.Data (1=Low..4=Critical for Impact/Urgency; 1=Critical..4=Low for PriorityTarget.Id)
    public short ImpactLevel { get; set; } = PriorityMatrix.LevelMedium;
    public short UrgencyLevel { get; set; } = PriorityMatrix.LevelMedium;
    public short ComputedPriorityId { get; set; } = PriorityMatrix.PriorityMedium;
    public short PriorityId { get; set; } = PriorityMatrix.PriorityMedium;

    public string ImpactName => PriorityMatrix.GetLevelName(ImpactLevel);
    public string UrgencyName => PriorityMatrix.GetLevelName(UrgencyLevel);

    public string Priority { get; set; } = "Medium";
    public string Status { get; set; } = "Open";
    public string Updated { get; set; } = "Just now";
    public string Requester { get; set; } = "";
    public string? RequesterId { get; set; }
    public string Assignee { get; set; } = "Unassigned";
    public string Description { get; set; } = "";
    public string Created { get; set; } = "";

    // FR-06 Supervisor priority override fields matching Incident schema
    public bool PriorityOverridden { get; set; }
    public string? OverrideReason { get; set; }
    public Guid? OverrideBy { get; set; }
    public DateTimeOffset? OverrideAt { get; set; }
    public List<TicketStatusHistoryEntry> StatusHistory { get; } = new();
}
