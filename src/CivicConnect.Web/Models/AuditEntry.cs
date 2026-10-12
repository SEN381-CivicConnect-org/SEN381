namespace CivicConnect.Web.Models;

public sealed record AuditEntry
{
    public DateTime Timestamp { get; init; }
    public string Actor { get; init; } = "";
    public string EntityType { get; init; } = "";
    public string EntityId { get; init; } = "";
    public string Action { get; init; } = "";
    public string? PreviousValues { get; init; }
    public string? NewValues { get; init; }
}
