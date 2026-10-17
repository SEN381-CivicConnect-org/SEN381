using System.Globalization;

namespace CivicConnect.Data.Operations;

/// <summary>
/// Represents a structured health sample recorded by the automated sampler (NFR-09, OPS-AVL-01).
/// </summary>
public sealed record HealthSample(
    DateTimeOffset Timestamp,
    int HttpStatus,
    string DatabaseStatus,
    string RabbitMqStatus,
    double LatencyMs,
    bool Success,
    bool IsWorkingHours);

/// <summary>
/// Represents a window of consecutive failed health check samples.
/// </summary>
public sealed record OutageWindow(
    DateTimeOffset StartTime,
    DateTimeOffset EndTime,
    int ConsecutiveFailedSamples,
    double DurationMinutes,
    double DurationHours,
    bool IsRtoBreach);

/// <summary>
/// Summary report produced by the availability computation engine (NFR-09, OPS-AVL-01).
/// </summary>
public sealed record AvailabilityResult(
    int TotalSamples,
    int TotalWorkingHourSamples,
    int SuccessfulWorkingHourSamples,
    int FailedWorkingHourSamples,
    double AvailabilityPercentage,
    int MaxConsecutiveFailedSamples,
    double MaxConsecutiveOutageMinutes,
    double MaxConsecutiveOutageHours,
    double RtoHoursThreshold,
    double AvailabilityTargetPercentage,
    bool IsRtoCompliant,
    bool IsAvailabilityTargetCompliant,
    bool IsCompliant,
    IReadOnlyList<OutageWindow> OutageWindows,
    IReadOnlyList<string> BreachReasons);

/// <summary>
/// Availability Computation Engine implementing NFR-09: System Availability and OPS-AVL-01 HealthCheckSampling.
/// Evaluates health samples against 99% monthly availability during client working hours
/// and verifies that every window of consecutive failed samples is shorter than the 4-hour RTO threshold (NFR-03).
/// </summary>
public static class AvailabilityCalculator
{
    public const double DefaultRtoHours = 4.0;
    public const double DefaultAvailabilityTargetPercent = 99.0;
    public const int DefaultSampleIntervalMinutes = 5;
    public const int DefaultWorkingHoursStart = 8;
    public const int DefaultWorkingHoursEnd = 17;

    /// <summary>
    /// Checks whether the given timestamp falls within client working hours (Monday-Friday 08:00-17:00).
    /// </summary>
    public static bool IsWorkingHour(
        DateTimeOffset timestamp,
        int startHour = DefaultWorkingHoursStart,
        int endHour = DefaultWorkingHoursEnd,
        TimeZoneInfo? timeZone = null)
    {
        var localTime = timeZone is not null
            ? TimeZoneInfo.ConvertTime(timestamp, timeZone)
            : timestamp;

        var dayOfWeek = localTime.DayOfWeek;
        if (dayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
        {
            return false;
        }

        var hourOfDay = localTime.Hour;
        return hourOfDay >= startHour && hourOfDay < endHour;
    }

    /// <summary>
    /// Calculates system availability percentage and consecutive outage duration over a collection of health check samples.
    /// </summary>
    public static AvailabilityResult Calculate(
        IEnumerable<HealthSample> samples,
        double rtoHoursThreshold = DefaultRtoHours,
        double availabilityTargetPercent = DefaultAvailabilityTargetPercent,
        int sampleIntervalMinutes = DefaultSampleIntervalMinutes,
        bool workingHoursOnly = true)
    {
        var allSamples = samples.OrderBy(s => s.Timestamp).ToList();
        var evaluatedSamples = workingHoursOnly
            ? allSamples.Where(s => s.IsWorkingHours).ToList()
            : allSamples;

        var totalWorkingHourSamples = evaluatedSamples.Count;
        if (totalWorkingHourSamples == 0)
        {
            return new AvailabilityResult(
                TotalSamples: allSamples.Count,
                TotalWorkingHourSamples: 0,
                SuccessfulWorkingHourSamples: 0,
                FailedWorkingHourSamples: 0,
                AvailabilityPercentage: 100.0,
                MaxConsecutiveFailedSamples: 0,
                MaxConsecutiveOutageMinutes: 0.0,
                MaxConsecutiveOutageHours: 0.0,
                RtoHoursThreshold: rtoHoursThreshold,
                AvailabilityTargetPercentage: availabilityTargetPercent,
                IsRtoCompliant: true,
                IsAvailabilityTargetCompliant: true,
                IsCompliant: true,
                OutageWindows: Array.Empty<OutageWindow>(),
                BreachReasons: Array.Empty<string>());
        }

        var successfulSamples = evaluatedSamples.Count(s => s.Success);
        var failedSamples = totalWorkingHourSamples - successfulSamples;

        var availabilityPercentage = Math.Round(
            (double)successfulSamples / totalWorkingHourSamples * 100.0,
            2);

        // Detect consecutive failed windows
        var outageWindows = new List<OutageWindow>();
        var maxConsecutiveFailed = 0;
        var currentRun = 0;
        DateTimeOffset? runStartTime = null;
        DateTimeOffset? lastFailureTime = null;

        for (var i = 0; i < evaluatedSamples.Count; i++)
        {
            var sample = evaluatedSamples[i];

            if (!sample.Success)
            {
                if (currentRun == 0)
                {
                    runStartTime = sample.Timestamp;
                }

                currentRun++;
                lastFailureTime = sample.Timestamp;

                if (currentRun > maxConsecutiveFailed)
                {
                    maxConsecutiveFailed = currentRun;
                }
            }
            else
            {
                if (currentRun > 0 && runStartTime.HasValue && lastFailureTime.HasValue)
                {
                    var outageDurationMinutes = currentRun * sampleIntervalMinutes;
                    var outageDurationHours = Math.Round(outageDurationMinutes / 60.0, 2);
                    var isBreach = outageDurationHours > rtoHoursThreshold;

                    outageWindows.Add(new OutageWindow(
                        runStartTime.Value,
                        lastFailureTime.Value,
                        currentRun,
                        outageDurationMinutes,
                        outageDurationHours,
                        isBreach));

                    currentRun = 0;
                    runStartTime = null;
                    lastFailureTime = null;
                }
            }
        }

        // Close out trailing outage run if samples ended on failure
        if (currentRun > 0 && runStartTime.HasValue && lastFailureTime.HasValue)
        {
            var outageDurationMinutes = currentRun * sampleIntervalMinutes;
            var outageDurationHours = Math.Round(outageDurationMinutes / 60.0, 2);
            var isBreach = outageDurationHours > rtoHoursThreshold;

            outageWindows.Add(new OutageWindow(
                runStartTime.Value,
                lastFailureTime.Value,
                currentRun,
                outageDurationMinutes,
                outageDurationHours,
                isBreach));
        }

        var maxConsecutiveOutageMinutes = maxConsecutiveFailed * sampleIntervalMinutes;
        var maxConsecutiveOutageHours = Math.Round(maxConsecutiveOutageMinutes / 60.0, 2);

        var isRtoCompliant = maxConsecutiveOutageHours <= rtoHoursThreshold;
        var isAvailabilityTargetCompliant = availabilityPercentage >= availabilityTargetPercent;
        var isCompliant = isRtoCompliant && isAvailabilityTargetCompliant;

        var breachReasons = new List<string>();
        if (!isAvailabilityTargetCompliant)
        {
            breachReasons.Add(
                $"Availability {availabilityPercentage.ToString("F2", CultureInfo.InvariantCulture)}% fell below target {availabilityTargetPercent.ToString("F1", CultureInfo.InvariantCulture)}%.");
        }

        if (!isRtoCompliant)
        {
            breachReasons.Add(
                $"Maximum consecutive outage of {maxConsecutiveOutageHours.ToString("F2", CultureInfo.InvariantCulture)}h ({maxConsecutiveOutageMinutes}m) exceeded RTO threshold of {rtoHoursThreshold.ToString("F1", CultureInfo.InvariantCulture)}h (NFR-03).");
        }

        return new AvailabilityResult(
            TotalSamples: allSamples.Count,
            TotalWorkingHourSamples: totalWorkingHourSamples,
            SuccessfulWorkingHourSamples: successfulSamples,
            FailedWorkingHourSamples: failedSamples,
            AvailabilityPercentage: availabilityPercentage,
            MaxConsecutiveFailedSamples: maxConsecutiveFailed,
            MaxConsecutiveOutageMinutes: maxConsecutiveOutageMinutes,
            MaxConsecutiveOutageHours: maxConsecutiveOutageHours,
            RtoHoursThreshold: rtoHoursThreshold,
            AvailabilityTargetPercentage: availabilityTargetPercent,
            IsRtoCompliant: isRtoCompliant,
            IsAvailabilityTargetCompliant: isAvailabilityTargetCompliant,
            IsCompliant: isCompliant,
            OutageWindows: outageWindows,
            BreachReasons: breachReasons);
    }

    /// <summary>
    /// Generates a synthetic calendar month of 5-minute health samples representing Scenario A (Nominal):
    /// 99.4% uptime with isolated 5-minute transient drops (asserts PASS: >= 99% and max outage < 4h).
    /// </summary>
    public static List<HealthSample> GenerateNominalMonth(
        int workingDays = 20,
        int startHour = DefaultWorkingHoursStart,
        int endHour = DefaultWorkingHoursEnd,
        int intervalMinutes = DefaultSampleIntervalMinutes)
    {
        var samples = new List<HealthSample>();
        var baseDate = new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero);
        var totalSamplesPerDay = (endHour - startHour) * (60 / intervalMinutes); // 108 samples/day

        var dayCount = 0;
        var currentDay = baseDate;

        while (dayCount < workingDays)
        {
            if (currentDay.DayOfWeek is not DayOfWeek.Saturday and not DayOfWeek.Sunday)
            {
                for (var s = 0; s < totalSamplesPerDay; s++)
                {
                    var timestamp = currentDay.AddHours(startHour).AddMinutes(s * intervalMinutes);

                    // Inject isolated transient failure once every ~160 samples (approx 13 failures across 2160 samples ~ 99.4% uptime)
                    var isFailed = (samples.Count % 165 == 42);

                    samples.Add(new HealthSample(
                        Timestamp: timestamp,
                        HttpStatus: isFailed ? 503 : 200,
                        DatabaseStatus: isFailed ? "Unhealthy" : "Healthy",
                        RabbitMqStatus: "Healthy",
                        LatencyMs: isFailed ? 45.0 : 12.5,
                        Success: !isFailed,
                        IsWorkingHours: true));
                }

                dayCount++;
            }

            currentDay = currentDay.AddDays(1);
        }

        return samples;
    }

    /// <summary>
    /// Generates a synthetic calendar month of 5-minute health samples representing Scenario B (RTO Breach):
    /// A single consecutive outage window of 4 hours 15 minutes (51 samples) exceeding the 4-hour RTO threshold.
    /// </summary>
    public static List<HealthSample> GenerateRtoBreachMonth(
        int workingDays = 20,
        int startHour = DefaultWorkingHoursStart,
        int endHour = DefaultWorkingHoursEnd,
        int intervalMinutes = DefaultSampleIntervalMinutes,
        int breachOutageMinutes = 255) // 4h 15m = 51 consecutive 5-min samples
    {
        var samples = new List<HealthSample>();
        var baseDate = new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero);
        var totalSamplesPerDay = (endHour - startHour) * (60 / intervalMinutes);

        var breachSampleCount = breachOutageMinutes / intervalMinutes; // 51 samples
        var breachStartDayIndex = 5; // Occurs on Day 6
        var breachStartSampleIndex = 20;

        var dayCount = 0;
        var currentDay = baseDate;

        while (dayCount < workingDays)
        {
            if (currentDay.DayOfWeek is not DayOfWeek.Saturday and not DayOfWeek.Sunday)
            {
                for (var s = 0; s < totalSamplesPerDay; s++)
                {
                    var timestamp = currentDay.AddHours(startHour).AddMinutes(s * intervalMinutes);

                    var isBreachWindow = dayCount == breachStartDayIndex &&
                                         s >= breachStartSampleIndex &&
                                         s < breachStartSampleIndex + breachSampleCount;

                    samples.Add(new HealthSample(
                        Timestamp: timestamp,
                        HttpStatus: isBreachWindow ? 503 : 200,
                        DatabaseStatus: isBreachWindow ? "Unhealthy" : "Healthy",
                        RabbitMqStatus: isBreachWindow ? "Unhealthy" : "Healthy",
                        LatencyMs: isBreachWindow ? 2000.0 : 11.8,
                        Success: !isBreachWindow,
                        IsWorkingHours: true));
                }

                dayCount++;
            }

            currentDay = currentDay.AddDays(1);
        }

        return samples;
    }

    /// <summary>
    /// Generates a synthetic calendar month of 5-minute health samples representing Scenario C (Target Breach):
    /// Cumulative random downtime totaling 1.5% (approx 33 failures out of 2160 = 98.47% uptime < 99.0%),
    /// but with no single outage exceeding 4 hours.
    /// </summary>
    public static List<HealthSample> GenerateAvailabilityBreachMonth(
        int workingDays = 20,
        int startHour = DefaultWorkingHoursStart,
        int endHour = DefaultWorkingHoursEnd,
        int intervalMinutes = DefaultSampleIntervalMinutes)
    {
        var samples = new List<HealthSample>();
        var baseDate = new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero);
        var totalSamplesPerDay = (endHour - startHour) * (60 / intervalMinutes);

        var dayCount = 0;
        var currentDay = baseDate;

        while (dayCount < workingDays)
        {
            if (currentDay.DayOfWeek is not DayOfWeek.Saturday and not DayOfWeek.Sunday)
            {
                for (var s = 0; s < totalSamplesPerDay; s++)
                {
                    var timestamp = currentDay.AddHours(startHour).AddMinutes(s * intervalMinutes);

                    // Inject 4 short outages of 8 samples (40 minutes each) on days 2, 7, 12, 17 -> 32 failures = ~98.5% uptime
                    var isFailed = (dayCount is 2 or 7 or 12 or 17) && (s >= 30 && s < 38);

                    samples.Add(new HealthSample(
                        Timestamp: timestamp,
                        HttpStatus: isFailed ? 503 : 200,
                        DatabaseStatus: isFailed ? "Unhealthy" : "Healthy",
                        RabbitMqStatus: "Healthy",
                        LatencyMs: isFailed ? 50.0 : 12.0,
                        Success: !isFailed,
                        IsWorkingHours: true));
                }

                dayCount++;
            }

            currentDay = currentDay.AddDays(1);
        }

        return samples;
    }
}
