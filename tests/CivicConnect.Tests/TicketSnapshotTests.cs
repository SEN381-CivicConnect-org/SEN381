using CivicConnect.Data.Entities;
using CivicConnect.Web.Services;
using Xunit;
using WebTicket = CivicConnect.Web.Models.Ticket;

namespace CivicConnect.Tests;

/// <summary>
/// Unit tests for FR-05 Priority Snapshot and Immutability requirements (IT-PRI-02 ConfigChangeDoesNotRewriteHistory, STK-CFL-01).
/// </summary>
public class TicketSnapshotTests
{
    [Fact]
    public void TicketCreation_SnapshotsImpactUrgencyAndCalculatedPriority()
    {
        var service = new TicketService(new PriorityMatrix());

        var ticket = new WebTicket
        {
            Title = "Hallway short circuit",
            Category = "Electrical",
            Location = "Ground Floor",
            ImpactLevel = PriorityMatrix.LevelMedium,     // 2 (Medium Impact)
            UrgencyLevel = PriorityMatrix.LevelCritical,  // 4 (Critical Urgency)
            Description = "Sparks coming from the light switch in the ground floor hallway."
        };

        var ticketId = service.Create(ticket);
        var created = service.Find(ticketId);

        Assert.NotNull(created);
        Assert.Equal(PriorityMatrix.LevelMedium, created.ImpactLevel);
        Assert.Equal(PriorityMatrix.LevelCritical, created.UrgencyLevel);
        Assert.Equal(PriorityMatrix.PriorityCritical, created.ComputedPriorityId);
        Assert.Equal(PriorityMatrix.PriorityCritical, created.PriorityId);
        Assert.Equal("Critical", created.Priority);
    }

    [Fact]
    public void IT_PRI_02_ConfigChangeDoesNotRewriteHistory_OnIncidentAndTicket()
    {
        var matrix = new PriorityMatrix();
        var service = new TicketService(matrix);

        // 1. Initial configuration: Category "Electrical" (Urgency = 4 Critical), Location "Ground Floor" (Impact = 2 Medium)
        var category = new Category
        {
            Id = 10,
            Name = "Electrical",
            Description = "Electrical systems",
            UrgencyLevel = PriorityMatrix.LevelCritical, // 4
            DefaultServiceTeamId = 1,
            IsActive = true
        };

        var location = new Location
        {
            Id = 20,
            Name = "Ground Floor",
            ImpactLevel = PriorityMatrix.LevelMedium, // 2
            LocationKindId = 2,
            IsActive = true
        };

        // 2. Create Incident and Ticket under initial configuration (2 + 4 = 6 -> PriorityTarget 1 "Critical")
        short initialPriorityId = matrix.CalculatePriority(location.ImpactLevel, category.UrgencyLevel);
        var existingIncident = new Incident
        {
            Id = 1,
            CategoryId = category.Id,
            Category = category,
            LocationId = location.Id,
            Location = location,
            Title = "Exposed wiring in corridor",
            Description = "Live wire near water cooler.",
            ComputedPriority = initialPriorityId,
            Priority = initialPriorityId,
            PriorityOverridden = false
        };

        var inputTicket = new WebTicket
        {
            Title = existingIncident.Title,
            CategoryId = category.Id,
            Category = category.Name,
            LocationId = location.Id,
            Location = location.Name,
            ImpactLevel = location.ImpactLevel,
            UrgencyLevel = category.UrgencyLevel,
            Description = existingIncident.Description
        };
        var firstTicketId = service.Create(inputTicket);

        // 3. Administrator later reconfigures Category.UrgencyLevel and Location.ImpactLevel to Low (1)
        category.UrgencyLevel = PriorityMatrix.LevelLow; // 1
        location.ImpactLevel = PriorityMatrix.LevelLow;  // 1

        // Also mutate caller's inputTicket reference to ensure TicketService stored an isolated snapshot
        inputTicket.ImpactLevel = PriorityMatrix.LevelLow;
        inputTicket.UrgencyLevel = PriorityMatrix.LevelLow;
        inputTicket.PriorityId = PriorityMatrix.PriorityLow;
        inputTicket.Priority = "Low";

        // 4. Create a new Incident and Ticket after the configuration change (1 + 1 = 2 -> PriorityTarget 4 "Low")
        short subsequentPriorityId = matrix.CalculatePriority(location.ImpactLevel, category.UrgencyLevel);
        var newIncident = new Incident
        {
            Id = 2,
            CategoryId = category.Id,
            Category = category,
            LocationId = location.Id,
            Location = location,
            Title = "Minor bulb replacement",
            Description = "Indicator light dim.",
            ComputedPriority = subsequentPriorityId,
            Priority = subsequentPriorityId,
            PriorityOverridden = false
        };

        var secondTicketId = service.Create(new WebTicket
        {
            Title = newIncident.Title,
            CategoryId = category.Id,
            Category = category.Name,
            LocationId = location.Id,
            Location = location.Name,
            ImpactLevel = location.ImpactLevel,
            UrgencyLevel = category.UrgencyLevel,
            Description = newIncident.Description
        });

        // 5. Verify existing Incident and Ticket retain their original Critical (1) priority (IT-PRI-02)
        Assert.Equal(PriorityMatrix.PriorityCritical, existingIncident.ComputedPriority);
        Assert.Equal(PriorityMatrix.PriorityCritical, existingIncident.Priority);

        var fetchedFirstTicket = service.Find(firstTicketId);
        Assert.NotNull(fetchedFirstTicket);
        Assert.Equal(PriorityMatrix.LevelMedium, fetchedFirstTicket.ImpactLevel);
        Assert.Equal(PriorityMatrix.LevelCritical, fetchedFirstTicket.UrgencyLevel);
        Assert.Equal(PriorityMatrix.PriorityCritical, fetchedFirstTicket.ComputedPriorityId);
        Assert.Equal(PriorityMatrix.PriorityCritical, fetchedFirstTicket.PriorityId);
        Assert.Equal("Critical", fetchedFirstTicket.Priority);

        // 6. Verify subsequent Incident and Ticket reflect the updated Low (4) priority
        Assert.Equal(PriorityMatrix.PriorityLow, newIncident.ComputedPriority);
        Assert.Equal(PriorityMatrix.PriorityLow, newIncident.Priority);

        var fetchedSecondTicket = service.Find(secondTicketId);
        Assert.NotNull(fetchedSecondTicket);
        Assert.Equal(PriorityMatrix.LevelLow, fetchedSecondTicket.ImpactLevel);
        Assert.Equal(PriorityMatrix.LevelLow, fetchedSecondTicket.UrgencyLevel);
        Assert.Equal(PriorityMatrix.PriorityLow, fetchedSecondTicket.ComputedPriorityId);
        Assert.Equal(PriorityMatrix.PriorityLow, fetchedSecondTicket.PriorityId);
        Assert.Equal("Low", fetchedSecondTicket.Priority);
    }
}
