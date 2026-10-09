namespace CivicConnect.Data.Entities;

public enum PriorityLevel
{
    Critical = 1,
    High = 2,
    Medium = 3,
    Low = 4
}

public enum RequestStatus
{
    New = 1,
    Assigned = 2,
    InProgress = 3,
    OnHold = 4,
    Resolved = 5,
    Closed = 6
}

/// <summary>
/// Core domain entity representing a service request with DDD priority override and SLA overdue behavior.
/// </summary>
public class ServiceRequest
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public PriorityLevel Priority { get; private set; } = PriorityLevel.Medium;
    public RequestStatus Status { get; set; } = RequestStatus.New;
    public DateTimeOffset? DueAt { get; set; }
    public bool IsSupervisorOverridden { get; private set; }
    public string? OverrideReason { get; private set; }
    public string? OverriddenBySupervisorId { get; private set; }
    public DateTimeOffset? OverriddenAt { get; private set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? UpdatedAt { get; set; }

    /// <summary>
    /// Read-only computed property indicating whether the service request is overdue against its SLA.
    /// </summary>
    public bool IsOverdue => DueAt.HasValue
        && DateTimeOffset.UtcNow > DueAt.Value
        && Status != RequestStatus.Resolved
        && Status != RequestStatus.Closed;

    public ServiceRequest()
    {
    }

    public ServiceRequest(string title, string description, PriorityLevel priority)
    {
        Title = title;
        Description = description;
        Priority = priority;
        CalculateDueDate();
    }

    /// <summary>
    /// Calculates and sets the due date based on standard SLA times relative to the current priority.
    /// Critical = 4 hours, High = 24 hours, Medium = 3 days, Low = 7 days.
    /// </summary>
    public void CalculateDueDate()
    {
        var now = DateTimeOffset.UtcNow;
        DueAt = Priority switch
        {
            PriorityLevel.Critical => now.AddHours(4),
            PriorityLevel.High => now.AddHours(24),
            PriorityLevel.Medium => now.AddDays(3),
            PriorityLevel.Low => now.AddDays(7),
            _ => now.AddDays(3)
        };
        UpdatedAt = now;
    }

    /// <summary>
    /// Overrides the priority of the service request with supervisor justification and audit tracking.
    /// </summary>
    /// <param name="newPriority">The new priority level assigned by the supervisor.</param>
    /// <param name="reason">The mandatory justification for overriding priority.</param>
    /// <param name="supervisorId">The authenticated identifier of the supervisor performing the override.</param>
    public void OverridePriority(PriorityLevel newPriority, string reason, string supervisorId)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new ArgumentException("A valid reason must be provided to override priority.", nameof(reason));
        }

        if (string.IsNullOrWhiteSpace(supervisorId))
        {
            throw new ArgumentException("Supervisor identifier must be provided.", nameof(supervisorId));
        }

        Priority = newPriority;
        IsSupervisorOverridden = true;
        OverrideReason = reason.Trim();
        OverriddenBySupervisorId = supervisorId.Trim();
        OverriddenAt = DateTimeOffset.UtcNow;
        CalculateDueDate();
        UpdatedAt = DateTimeOffset.UtcNow;
    }
}
