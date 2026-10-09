using CivicConnect.Data.Entities;
using CivicConnect.Domain.Entities;
using CivicConnect.Domain.Enums;
using Xunit;

namespace CivicConnect.Tests;

/// <summary>
/// Unit tests for FR-05 Rich Domain Model (Domain-Driven Design).
/// Tests encapsulation, invariant enforcement, and ITIL matrix calculations inside ServiceRequest.
/// </summary>
public class ServiceRequestDomainTests
{
    [Fact]
    public void SetImpactAndUrgency_ElectricalUrgencyCritical_GroundFloorImpactMedium_CalculatesPriorityCritical()
    {
        // SEN381 Acceptance Criteria:
        // Category "Electrical" (Critical Urgency) + Location "Ground Floor" (Medium Impact) produces Priority: Critical
        var request = new ServiceRequest();

        request.SetImpactAndUrgency(ImpactLevel.Medium, UrgencyLevel.Critical);

        Assert.Equal(ImpactLevel.Medium, request.Impact);
        Assert.Equal(UrgencyLevel.Critical, request.Urgency);
        Assert.Equal(PriorityLevel.Critical, request.Priority);
    }

    [Theory]
    // Critical Category Urgency (4)
    [InlineData(UrgencyLevel.Critical, ImpactLevel.High, PriorityLevel.Critical)]   // 4 + 3 = 7 -> Critical
    [InlineData(UrgencyLevel.Critical, ImpactLevel.Medium, PriorityLevel.Critical)] // 4 + 2 = 6 -> Critical (FR-05 acceptance criteria)
    [InlineData(UrgencyLevel.Critical, ImpactLevel.Low, PriorityLevel.High)]        // 4 + 1 = 5 -> High
    // High Category Urgency (3)
    [InlineData(UrgencyLevel.High, ImpactLevel.High, PriorityLevel.Critical)]       // 3 + 3 = 6 -> Critical
    [InlineData(UrgencyLevel.High, ImpactLevel.Medium, PriorityLevel.High)]          // 3 + 2 = 5 -> High
    [InlineData(UrgencyLevel.High, ImpactLevel.Low, PriorityLevel.Medium)]          // 3 + 1 = 4 -> Medium
    // Medium Category Urgency (2)
    [InlineData(UrgencyLevel.Medium, ImpactLevel.High, PriorityLevel.High)]         // 2 + 3 = 5 -> High
    [InlineData(UrgencyLevel.Medium, ImpactLevel.Medium, PriorityLevel.Medium)]     // 2 + 2 = 4 -> Medium
    [InlineData(UrgencyLevel.Medium, ImpactLevel.Low, PriorityLevel.Low)]           // 2 + 1 = 3 -> Low
    // Low Category Urgency (1)
    [InlineData(UrgencyLevel.Low, ImpactLevel.High, PriorityLevel.Medium)]          // 1 + 3 = 4 -> Medium
    [InlineData(UrgencyLevel.Low, ImpactLevel.Medium, PriorityLevel.Low)]           // 1 + 2 = 3 -> Low
    [InlineData(UrgencyLevel.Low, ImpactLevel.Low, PriorityLevel.Low)]              // 1 + 1 = 2 -> Low
    public void SetImpactAndUrgency_ITILMatrix_CalculatesCorrectPriority(
        UrgencyLevel urgency,
        ImpactLevel impact,
        PriorityLevel expectedPriority)
    {
        var request = new ServiceRequest();

        request.SetImpactAndUrgency(impact, urgency);

        Assert.Equal(expectedPriority, request.Priority);
    }

    [Fact]
    public void RecalculatePriority_DoesNotAlterPriority_WhenSupervisorHasOverridden()
    {
        // FR-06 Invariant: Supervisor priority override freezes automated recalculation
        var request = new ServiceRequest();
        request.SetImpactAndUrgency(ImpactLevel.High, UrgencyLevel.Critical);
        Assert.Equal(PriorityLevel.Critical, request.Priority);

        // Supervisor manually downgrades to Low with an audited reason
        request.OverridePriority(PriorityLevel.Low, "Temporary bypass installed, danger neutralized.");

        Assert.True(request.IsSupervisorOverridden);
        Assert.Equal("Temporary bypass installed, danger neutralized.", request.SupervisorOverrideReason);
        Assert.Equal(PriorityLevel.Low, request.Priority);

        // Later update to impact/urgency should NOT recalculate or revert priority
        request.SetImpactAndUrgency(ImpactLevel.High, UrgencyLevel.Critical);

        Assert.Equal(PriorityLevel.Low, request.Priority);
    }

    [Fact]
    public void OverridePriority_ThrowsArgumentException_WhenReasonIsMissing()
    {
        var request = new ServiceRequest();

        Assert.Throws<ArgumentException>(() =>
            request.OverridePriority(PriorityLevel.High, ""));

        Assert.Throws<ArgumentException>(() =>
            request.OverridePriority(PriorityLevel.High, "   "));
    }

    [Fact]
    public void Category_DefaultUrgency_CanBeAssignedAndRetrieved()
    {
        var category = new Category
        {
            Name = "Electrical",
            Description = "Electrical faults and hazards",
            DefaultUrgency = UrgencyLevel.Critical
        };

        Assert.Equal(UrgencyLevel.Critical, category.DefaultUrgency);
    }
}
