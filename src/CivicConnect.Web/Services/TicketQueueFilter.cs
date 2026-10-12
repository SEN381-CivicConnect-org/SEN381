using System.Globalization;
using CivicConnect.Web.Models;

namespace CivicConnect.Web.Services;

public static class TicketQueueFilter
{
    // The caller supplies only tickets its workspace is allowed to display.
    public static IEnumerable<Ticket> Apply(
        IEnumerable<Ticket> permittedTickets,
        string search,
        string category,
        string status,
        string sort,
        DateTime now)
    {
        var keyword = search.Trim();
        var results = permittedTickets
            .Where(ticket =>
                (keyword.Length == 0 || MatchesKeyword(ticket, keyword)) &&
                (category.Length == 0 || ticket.Category == category) &&
                (status.Length == 0 || ticket.Status == status));

        if (sort == "priority-high")
        {
            return results.OrderByDescending(ticket => PriorityRank(ticket.Priority));
        }

        if (sort == "priority-low")
        {
            return results.OrderBy(ticket => PriorityRank(ticket.Priority));
        }

        var datedTickets = results
            .Select(ticket => new
            {
                Ticket = ticket,
                Date = ParseDate(ticket.Updated, now) ?? ParseDate(ticket.Created, now)
            });

        // Unknown dates stay last in either direction; equal dates retain source order.
        var knownFirst = datedTickets.OrderBy(item => item.Date is null);
        return (sort == "date-oldest"
                ? knownFirst.ThenBy(item => item.Date)
                : knownFirst.ThenByDescending(item => item.Date))
            .Select(item => item.Ticket);
    }

    private static bool MatchesKeyword(Ticket ticket, string keyword) =>
        new[]
        {
            ticket.Id,
            ticket.Title,
            ticket.Description,
            ticket.Category,
            ticket.Requester,
            ticket.Assignee
        }.Any(value => value.Contains(keyword, StringComparison.OrdinalIgnoreCase));

    private static int PriorityRank(string priority) => priority switch
    {
        "High" => 3,
        "Medium" => 2,
        "Low" => 1,
        _ => 0
    };

    private static DateTime? ParseDate(string value, DateTime now)
    {
        value = value.Trim();

        if (value.Equals("Just now", StringComparison.OrdinalIgnoreCase))
        {
            return now;
        }

        if (value.Equals("Yesterday", StringComparison.OrdinalIgnoreCase))
        {
            return now.Date.AddDays(-1);
        }

        if (value.Equals("Today", StringComparison.OrdinalIgnoreCase))
        {
            return now.Date;
        }

        if (value.StartsWith("Today, ", StringComparison.OrdinalIgnoreCase) &&
            TimeOnly.TryParseExact(
                value[7..],
                "HH:mm",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var time))
        {
            return now.Date.Add(time.ToTimeSpan());
        }

        return DateTime.TryParseExact(
            value,
            new[] { "d MMM yyyy", "yyyy-MM-dd", "yyyy-MM-dd HH:mm:ss" },
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out var date)
            ? date
            : null;
    }
}
