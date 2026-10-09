using CivicConnect.Domain.Enums;

namespace CivicConnect.Domain.Entities;

/// <summary>
/// Core domain entity for a community service request / incident (FR-05, FR-06).
/// Encapsulates rich domain logic for priority calculation (eliminating anemic model anti-patterns).
/// </summary>
public class ServiceRequest : CivicConnect.Data.Entities.IHasTimestamps
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;

    public int? CategoryId { get; set; }
    public CivicConnect.Data.Entities.Category? Category { get; set; }

    public Guid? RequesterId { get; set; }
    public CivicConnect.Data.Entities.AppUser? Requester { get; set; }

    public Guid? AssignedToId { get; set; }
    public CivicConnect.Data.Entities.AppUser? AssignedTo { get; set; }

    public string Status { get; set; } = "Open";

    // Core attributes are private set to protect encapsulation and integrity (DDD)
    public ImpactLevel Impact { get; private set; } = ImpactLevel.Medium;
    public UrgencyLevel Urgency { get; private set; } = UrgencyLevel.Medium;
    public PriorityLevel Priority { get; private set; } = PriorityLevel.Medium;

    // FR-06 Integration: Supervisor priority override tracking
    public bool IsSupervisorOverridden { get; private set; }
    public string? SupervisorOverrideReason { get; private set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>
    /// Controlled mutation method enforcing ITIL priority matrix recalculation (FR-05).
    /// </summary>
    /// <param name="impact">Physical/location impact level reported by user.</param>
    /// <param name="urgency">Category default urgency fetched from PostgreSQL.</param>
    public void SetImpactAndUrgency(ImpactLevel impact, UrgencyLevel urgency)
    {
        Impact = impact;
        Urgency = urgency;
        RecalculatePriority();
    }

    /// <summary>
    /// Controlled supervisor priority override method (FR-06).
    /// Once overridden, automated recalculation is permanently blocked.
    /// </summary>
    /// <param name="newPriority">The manually assigned priority level.</param>
    /// <param name="reason">Mandatory audited override justification.</param>
    public void OverridePriority(PriorityLevel newPriority, string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new ArgumentException("Supervisor priority override requires a recorded reason.", nameof(reason));
        }

        Priority = newPriority;
        IsSupervisorOverridden = true;
        SupervisorOverrideReason = reason;
    }

    /// <summary>
    /// Computes incident priority directly inside the domain entity.
    /// Formula: score = Urgency + Impact
    /// >= 6 -> Critical (e.g. Urgency Critical (4) + Impact Ground Floor Medium (2) = 6 -> Critical)
    ///    5 -> High
    ///    4 -> Medium
    /// <= 3 -> Low
    /// </summary>
    private void RecalculatePriority()
    {
        // Respect FR-06: Do not recalculate if a supervisor has overridden it
        if (IsSupervisorOverridden) return;

        int score = (int)Urgency + (int)Impact;

        Priority = score switch
        {
            >= 6 => PriorityLevel.Critical,
            5 => PriorityLevel.High,
            4 => PriorityLevel.Medium,
            _ => PriorityLevel.Low
        };
    }
}
