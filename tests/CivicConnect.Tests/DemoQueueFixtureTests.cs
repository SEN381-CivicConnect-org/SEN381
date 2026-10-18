using CivicConnect.Web.Services;

namespace CivicConnect.Tests;

public class DemoQueueFixtureTests
{
    [Fact]
    public void DemoStaffHasDeterministicCategoryPriorityAndStatusVariation()
    {
        var service = new TicketService();
        var queue = service.GetStaffQueue("IT Support", "Daniel Jacobs").ToList();
        Assert.Contains(queue, ticket => ticket.Id == "CC-014" && ticket.Priority == "High" && ticket.Status == "In Progress");
        Assert.Contains(queue, ticket => ticket.Id == "CC-010" && ticket.Requester == "Ayesha Khan" && ticket.Status == "Open");
        Assert.Contains(queue, ticket => ticket.Id == "CC-013" && ticket.Category == "Facilities" && ticket.Priority == "Medium");
        Assert.True(queue.Select(ticket => ticket.Category).Distinct().Count() >= 2);
        Assert.True(queue.Select(ticket => ticket.Priority).Distinct().Count() >= 2);
        Assert.True(queue.Select(ticket => ticket.Status).Distinct().Count() >= 2);
        Assert.DoesNotContain(queue, ticket => ticket.Status is "Resolved" or "Closed");
    }

    [Fact]
    public void CrossCategoryFixtureIsVisibleOnlyThroughExistingAssignmentRule()
    {
        var service = new TicketService();
        Assert.DoesNotContain(service.GetStaffQueue("IT Support", "Another technician"), ticket => ticket.Id == "CC-013");
        Assert.Contains(service.GetStaffQueue("Facilities", "Another technician"), ticket => ticket.Id == "CC-013");
    }
}
