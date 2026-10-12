namespace CivicConnect.Web.Models;

public sealed record TicketNote(
    string TicketId,
    string Text,
    DateTimeOffset Timestamp,
    string Author,
    string? AuthorEmail,
    bool IsInternal);
