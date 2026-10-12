using CivicConnect.Data.Configurations;
using CivicConnect.Data.Entities;
using CivicConnect.Web.Services;
using Xunit;

namespace CivicConnect.Tests;

/// <summary>
/// Unit tests for FR-05 Priority Matrix calculation (UT-PRI-01, DD-02) against the official
/// CivicConnect.Data schema scales (Category.UrgencyLevel 1..4, Location.ImpactLevel 1..4, PriorityTarget.Id 1..4).
/// </summary>
public class PriorityMatrixTests
{
    [Fact]
    public void UT_PRI_01_ElectricalUrgencyCritical_GroundFloorImpactMedium_CalculatesPriorityCritical()
    {
        // SEN381 FR-05 Acceptance Criteria:
        // Category "Electrical" (UrgencyLevel = 4 Critical) + Location "Ground Floor" (ImpactLevel = 2 Medium)
        // produces PriorityTarget.Id = 1 ("critical", 60 min SLA)
        var category = new Category
        {
            Id = 1,
            Name = "Electrical",
            Description = "Electrical faults and hazards",
            UrgencyLevel = PriorityMatrix.LevelCritical // 4
        };

        var location = new Location
        {
            Id = 1,
            Name = "Ground Floor",
            ImpactLevel = PriorityMatrix.LevelMedium // 2
        };

        IPriorityMatrix matrix = new PriorityMatrix();
        short priorityId = matrix.CalculatePriority(location.ImpactLevel, category.UrgencyLevel);

        Assert.Equal(PriorityMatrix.PriorityCritical, priorityId); // 1
        Assert.Equal("Critical", matrix.FormatPriorityName(priorityId));
    }

    [Theory]
    // Impact 4 (Critical) + Urgency 1..4
    [InlineData((short)4, (short)4, (short)1, "Critical")] // 4 + 4 = 8 -> 1 (Critical)
    [InlineData((short)4, (short)3, (short)1, "Critical")] // 4 + 3 = 7 -> 1 (Critical)
    [InlineData((short)4, (short)2, (short)1, "Critical")] // 4 + 2 = 6 -> 1 (Critical)
    [InlineData((short)4, (short)1, (short)2, "High")]     // 4 + 1 = 5 -> 2 (High)
    // Impact 3 (High) + Urgency 1..4
    [InlineData((short)3, (short)4, (short)1, "Critical")] // 3 + 4 = 7 -> 1 (Critical)
    [InlineData((short)3, (short)3, (short)1, "Critical")] // 3 + 3 = 6 -> 1 (Critical)
    [InlineData((short)3, (short)2, (short)2, "High")]     // 3 + 2 = 5 -> 2 (High)
    [InlineData((short)3, (short)1, (short)3, "Medium")]   // 3 + 1 = 4 -> 3 (Medium)
    // Impact 2 (Medium) + Urgency 1..4
    [InlineData((short)2, (short)4, (short)1, "Critical")] // 2 + 4 = 6 -> 1 (Critical, FR-05 AC)
    [InlineData((short)2, (short)3, (short)2, "High")]     // 2 + 3 = 5 -> 2 (High)
    [InlineData((short)2, (short)2, (short)3, "Medium")]   // 2 + 2 = 4 -> 3 (Medium)
    [InlineData((short)2, (short)1, (short)4, "Low")]      // 2 + 1 = 3 -> 4 (Low)
    // Impact 1 (Low) + Urgency 1..4
    [InlineData((short)1, (short)4, (short)2, "High")]     // 1 + 4 = 5 -> 2 (High)
    [InlineData((short)1, (short)3, (short)3, "Medium")]   // 1 + 3 = 4 -> 3 (Medium)
    [InlineData((short)1, (short)2, (short)4, "Low")]      // 1 + 2 = 3 -> 4 (Low)
    [InlineData((short)1, (short)1, (short)4, "Low")]      // 1 + 1 = 2 -> 4 (Low)
    public void UT_PRI_01_PriorityMatrix_CalculatesExpectedPriorityTargetId(
        short impactLevel,
        short urgencyLevel,
        short expectedPriorityId,
        string expectedPriorityName)
    {
        short actualPriorityId = PriorityMatrix.Calculate(impactLevel, urgencyLevel);
        string actualPriorityName = PriorityMatrix.GetPriorityName(actualPriorityId);

        Assert.Equal(expectedPriorityId, actualPriorityId);
        Assert.Equal(expectedPriorityName, actualPriorityName);

        // Verify output aligns with seeded PriorityTargetConfiguration.BaselinePriorityTargets
        var seededTarget = PriorityTargetConfiguration.BaselinePriorityTargets
            .Single(pt => pt.Id == actualPriorityId);
        Assert.Equal(expectedPriorityName.ToLowerInvariant(), seededTarget.Name);
    }

    [Theory]
    [InlineData((short)0, (short)2)]
    [InlineData((short)-1, (short)2)]
    [InlineData((short)5, (short)2)]
    [InlineData((short)2, (short)0)]
    [InlineData((short)2, (short)-3)]
    [InlineData((short)2, (short)5)]
    public void UT_PRI_01_PriorityMatrix_OutOfRangeInputs_ThrowsArgumentOutOfRangeException(
        short impactLevel,
        short urgencyLevel)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            PriorityMatrix.Calculate(impactLevel, urgencyLevel));
    }

    [Fact]
    public void Incident_SupervisorOverride_UpdatesEffectivePriorityWhilePreservingComputedPriority()
    {
        // FR-05 + FR-06 schema invariant: Incident stores both ComputedPriority and effective Priority
        short computed = PriorityMatrix.Calculate(
            impactLevel: PriorityMatrix.LevelHigh,      // 3
            urgencyLevel: PriorityMatrix.LevelCritical); // 4 -> 1 (Critical)

        var incident = new Incident
        {
            CategoryId = 1,
            LocationId = 1,
            Title = "Substation breaker trip",
            Description = "Main breaker tripped on floor 2.",
            ComputedPriority = computed,
            Priority = computed,
            PriorityOverridden = false
        };

        Assert.Equal(PriorityMatrix.PriorityCritical, incident.ComputedPriority);
        Assert.Equal(PriorityMatrix.PriorityCritical, incident.Priority);

        // Supervisor overrides effective priority to Low (4) with recorded reason, actor, and timestamp
        incident.Priority = PriorityMatrix.PriorityLow;
        incident.PriorityOverridden = true;
        incident.OverrideReason = "Temporary generator online; hazard mitigated.";
        incident.OverrideBy = Guid.NewGuid();
        incident.OverrideAt = DateTimeOffset.UtcNow;

        Assert.True(incident.PriorityOverridden);
        Assert.Equal(PriorityMatrix.PriorityCritical, incident.ComputedPriority);
        Assert.Equal(PriorityMatrix.PriorityLow, incident.Priority);
    }
}
