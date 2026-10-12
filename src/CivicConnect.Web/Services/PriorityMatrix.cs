namespace CivicConnect.Web.Services;

/// <summary>
/// Application-layer priority calculation contract (DD-02, FR-05).
/// Maps Location.ImpactLevel (1=Low..4=Critical) and Category.UrgencyLevel (1=Low..4=Critical)
/// to PriorityTarget.Id (1=Critical, 2=High, 3=Medium, 4=Low).
/// </summary>
public interface IPriorityMatrix
{
    short CalculatePriority(short impactLevel, short urgencyLevel);
    string FormatPriorityName(short priorityTargetId);
    string FormatLevelName(short level);
}

/// <summary>
/// Deterministic ITIL priority matrix implementation (DD-02, FR-05).
/// </summary>
public sealed class PriorityMatrix : IPriorityMatrix
{
    // Input scale constants: Category.UrgencyLevel & Location.ImpactLevel (1..4 ascending severity)
    public const short LevelLow = 1;
    public const short LevelMedium = 2;
    public const short LevelHigh = 3;
    public const short LevelCritical = 4;

    // Output scale constants: PriorityTarget.Id (1..4, where 1 is highest priority)
    public const short PriorityCritical = 1;
    public const short PriorityHigh = 2;
    public const short PriorityMedium = 3;
    public const short PriorityLow = 4;

    /// <summary>
    /// Calculates the PriorityTarget.Id (1=Critical..4=Low) from Location.ImpactLevel (1..4)
    /// and Category.UrgencyLevel (1..4).
    /// </summary>
    public static short Calculate(short impactLevel, short urgencyLevel)
    {
        if (impactLevel is < LevelLow or > LevelCritical)
        {
            throw new ArgumentOutOfRangeException(
                nameof(impactLevel),
                impactLevel,
                "Impact level must be between 1 (Low) and 4 (Critical).");
        }

        if (urgencyLevel is < LevelLow or > LevelCritical)
        {
            throw new ArgumentOutOfRangeException(
                nameof(urgencyLevel),
                urgencyLevel,
                "Urgency level must be between 1 (Low) and 4 (Critical).");
        }

        int score = impactLevel + urgencyLevel;

        return score switch
        {
            >= 6 => PriorityCritical, // 1 ("critical", 60 min SLA)
            5 => PriorityHigh,        // 2 ("high", 240 min SLA)
            4 => PriorityMedium,      // 3 ("medium", 1440 min SLA)
            _ => PriorityLow          // 4 ("low", 4320 min SLA)
        };
    }

    /// <summary>
    /// Formats a PriorityTarget.Id (1..4) into its display name ("Critical", "High", "Medium", "Low").
    /// </summary>
    public static string GetPriorityName(short priorityTargetId) => priorityTargetId switch
    {
        PriorityCritical => "Critical",
        PriorityHigh => "High",
        PriorityMedium => "Medium",
        PriorityLow => "Low",
        _ => throw new ArgumentOutOfRangeException(
            nameof(priorityTargetId),
            priorityTargetId,
            "Priority target ID must be between 1 (Critical) and 4 (Low).")
    };

    /// <summary>
    /// Formats an ImpactLevel or UrgencyLevel (1..4) into its display name ("Low", "Medium", "High", "Critical").
    /// </summary>
    public static string GetLevelName(short level) => level switch
    {
        LevelLow => "Low",
        LevelMedium => "Medium",
        LevelHigh => "High",
        LevelCritical => "Critical",
        _ => throw new ArgumentOutOfRangeException(
            nameof(level),
            level,
            "Severity level must be between 1 (Low) and 4 (Critical).")
    };

    /// <summary>
    /// Returns the CSS badge class corresponding to a PriorityTarget.Id (1..4).
    /// </summary>
    public static string GetPriorityBadgeClass(short priorityTargetId) => priorityTargetId switch
    {
        PriorityCritical => "badge-critical",
        PriorityHigh => "badge-high",
        PriorityMedium => "badge-medium",
        _ => "badge-low"
    };

    public short CalculatePriority(short impactLevel, short urgencyLevel) =>
        Calculate(impactLevel, urgencyLevel);

    public string FormatPriorityName(short priorityTargetId) =>
        GetPriorityName(priorityTargetId);

    public string FormatLevelName(short level) =>
        GetLevelName(level);
}
