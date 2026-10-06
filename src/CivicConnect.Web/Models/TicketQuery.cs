namespace CivicConnect.Web.Models;

public enum TicketSort
{
    NewestFirst,
    OldestFirst,
    PriorityHighFirst,
    PriorityLowFirst
}

public sealed record TicketQuery
{
    public string? Search { get; init; }
    public string? Category { get; init; }
    public string? Status { get; init; }
    public TicketSort Sort { get; init; } = TicketSort.NewestFirst;
}
