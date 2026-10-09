namespace CivicConnect.Data.Entities;

/// Subscribes one reporter to updates on one incident; a reporter is subscribed at most once per incident.
public sealed class IncidentSubscription
{
    public long IncidentId { get; set; }
    public Incident Incident { get; set; } = null!;

    public Guid ReporterId { get; set; }
    public AppUser Reporter { get; set; } = null!;

    public DateTimeOffset SubscribedAt { get; set; }
}
