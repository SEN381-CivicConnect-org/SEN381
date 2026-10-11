namespace CivicConnect.Web.Models;

public sealed class TicketStatusHistoryEntry
{
    public string PreviousStatus { get; init; } = "";
    public string NewStatus { get; init; } = "";
    public string Actor { get; init; } = "";
    public DateTime ChangedAt { get; init; }
}

public sealed record StatusTransitionResult(bool Success, string Message);
