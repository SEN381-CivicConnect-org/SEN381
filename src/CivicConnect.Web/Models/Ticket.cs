using CivicConnect.Domain.Enums;

namespace CivicConnect.Web.Models;

public sealed class Ticket
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public string Category { get; set; } = "";
    public int? CategoryId { get; set; }

    // FR-05 Domain-driven fields
    public ImpactLevel Impact { get; set; } = ImpactLevel.Medium;
    public UrgencyLevel Urgency { get; set; } = UrgencyLevel.Medium;
    public PriorityLevel PriorityLevel { get; set; } = PriorityLevel.Medium;

    public string Priority { get; set; } = "Medium";
    public string Status { get; set; } = "Open";
    public string Updated { get; set; } = "Just now";
    public string Requester { get; set; } = "Bernard Small";
    public string Assignee { get; set; } = "Unassigned";
    public string Description { get; set; } = "";
    public string Created { get; set; } = "";

    // FR-06 Integration
    public bool IsSupervisorOverridden { get; set; }
    public string? SupervisorOverrideReason { get; set; }
}
