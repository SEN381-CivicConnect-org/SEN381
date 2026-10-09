namespace CivicConnect.Data.Entities;

public enum PriorityLevel
{
    Critical = 1,
    High = 2,
    Medium = 3,
    Low = 4
}

/// <summary>
/// Core domain entity representing a service request with DDD priority override behavior.
/// </summary>
public class ServiceRequest
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public PriorityLevel Priority { get; private set; } = PriorityLevel.Medium;
    public bool IsSupervisorOverridden { get; private set; }
    public string? OverrideReason { get; private set; }
    public string? OverriddenBySupervisorId { get; private set; }
    public DateTimeOffset? OverriddenAt { get; private set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? UpdatedAt { get; set; }

    public ServiceRequest()
    {
    }

    public ServiceRequest(string title, string description, PriorityLevel priority)
    {
        Title = title;
        Description = description;
        Priority = priority;
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
        UpdatedAt = DateTimeOffset.UtcNow;
    }
}
