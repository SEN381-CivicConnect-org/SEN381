namespace CivicConnect.Data.Entities;

/// One permitted move in the incident status transition matrix.
public sealed class IncidentStatusTransition
{
    public IncidentStatus FromStatus { get; set; }
    public IncidentStatus ToStatus { get; set; }
}
