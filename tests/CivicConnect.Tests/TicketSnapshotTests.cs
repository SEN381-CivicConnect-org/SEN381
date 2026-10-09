using CivicConnect.Domain.Entities;
using CivicConnect.Domain.Enums;
using CivicConnect.Web.Models;
using CivicConnect.Web.Services;
using Xunit;

namespace CivicConnect.Tests;

/// <summary>
/// Unit tests for FR-05 Ticket Snapshot and Immutability requirements (STK-CFL-01).
/// </summary>
public class TicketSnapshotTests
{
    [Fact]
    public void TicketCreation_SnapshotsImpactUrgencyAndCalculatedPriority()
    {
        var service = new TicketService();

        var ticket = new Ticket
        {
            Title = "Hallway short circuit",
            Category = "Electrical",
            Impact = ImpactLevel.Medium, // Ground Floor / Medium Impact
            Urgency = UrgencyLevel.Critical, // Critical Category Urgency
            Description = "Sparks coming from the light switch in the ground floor hallway."
        };

        var ticketId = service.Create(ticket);
        var created = service.Find(ticketId);

        Assert.NotNull(created);
        Assert.Equal(ImpactLevel.Medium, created.Impact);
        Assert.Equal(UrgencyLevel.Critical, created.Urgency);
        Assert.Equal(PriorityLevel.Critical, created.PriorityLevel);
        Assert.Equal("Critical", created.Priority);
    }

    [Fact]
    public void TicketSnapshot_RemainsImmutableWhenSubsequentCalculationsDiffer()
    {
        var service = new TicketService();

        // 1. Create first ticket (Electrical, Impact Medium -> Critical Priority)
        var electricalTicket = new Ticket
        {
            Title = "Exposed wiring",
            Category = "Electrical",
            Impact = ImpactLevel.Medium,
            Urgency = UrgencyLevel.Critical,
            Description = "Ground floor exposed wiring."
        };
        var id1 = service.Create(electricalTicket);

        // 2. Create second ticket (Facilities, Impact Low -> Low Priority)
        var facilitiesTicket = new Ticket
        {
            Title = "Office desk drawer stuck",
            Category = "Facilities",
            Impact = ImpactLevel.Low,
            Urgency = UrgencyLevel.Low,
            Description = "Drawer handle broken."
        };
        var id2 = service.Create(facilitiesTicket);

        // 3. Verify the first ticket's snapshot properties were not mutated or altered
        var fetchedFirstTicket = service.Find(id1);
        Assert.NotNull(fetchedFirstTicket);
        Assert.Equal(ImpactLevel.Medium, fetchedFirstTicket.Impact);
        Assert.Equal(UrgencyLevel.Critical, fetchedFirstTicket.Urgency);
        Assert.Equal(PriorityLevel.Critical, fetchedFirstTicket.PriorityLevel);
        Assert.Equal("Critical", fetchedFirstTicket.Priority);

        // 4. Verify second ticket snapshot
        var fetchedSecondTicket = service.Find(id2);
        Assert.NotNull(fetchedSecondTicket);
        Assert.Equal(ImpactLevel.Low, fetchedSecondTicket.Impact);
        Assert.Equal(UrgencyLevel.Low, fetchedSecondTicket.Urgency);
        Assert.Equal(PriorityLevel.Low, fetchedSecondTicket.PriorityLevel);
        Assert.Equal("Low", fetchedSecondTicket.Priority);
    }
}
