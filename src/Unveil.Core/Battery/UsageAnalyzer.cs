namespace Unveil.Core.Battery;

/// <summary>Energy used on battery in one state (in use or asleep).</summary>
/// <param name="Duration">Total time spent in this state on battery.</param>
/// <param name="EnergyUsed">mWh used.</param>
/// <param name="AverageWatts">Average draw.</param>
/// <param name="PercentPerHour">Average % of full charge used per hour.</param>
public sealed record StateUsage(TimeSpan Duration, long EnergyUsed, double AverageWatts, double? PercentPerHour);

/// <summary>
/// Historical on-battery drain from the battery report's recent usage, split into in-use and asleep.
/// This is context only: it reflects how the customer used the laptop, not a controlled test.
/// </summary>
public sealed record UsageSummary
{
    public StateUsage? InUse { get; init; }

    public StateUsage? Asleep { get; init; }

    /// <summary>
    /// True on a Modern Standby laptop whose "asleep" time includes hibernation ("Suspend" periods), which uses
    /// almost nothing: the average is then much lower than the sleep study's standby-only figure.
    /// </summary>
    public bool AsleepIncludesHibernation { get; init; }

    public DateTimeOffset? From { get; init; }

    public DateTimeOffset? To { get; init; }
}

public static class UsageAnalyzer
{
    /// <summary>Less on-battery time than this in a state is too little to average.</summary>
    public static readonly TimeSpan MinimumDuration = TimeSpan.FromMinutes(10);

    public static UsageSummary Summarize(BatteryReport? report)
    {
        var entries = report?.RecentUsage ?? [];
        var onBattery = entries
            .Where(e => !e.OnAc && e.Duration > TimeSpan.Zero && e.Discharge is >= 0)
            .ToList();

        return new UsageSummary
        {
            InUse = Sum(onBattery.Where(e => e.IsActive).ToList()),
            Asleep = Sum(onBattery.Where(e => e.IsSleep).ToList()),
            AsleepIncludesHibernation = report?.System.ModernStandby == true
                && onBattery.Any(e => string.Equals(e.EntryType, "Suspend", StringComparison.OrdinalIgnoreCase)),
            From = entries.Select(e => e.Timestamp).Where(t => t.HasValue).Min(),
            To = entries.Select(e => e.Timestamp).Where(t => t.HasValue).Max(),
        };
    }

    private static StateUsage? Sum(List<UsageEntry> entries)
    {
        var duration = entries.Aggregate(TimeSpan.Zero, (total, e) => total + e.Duration);
        if (duration < MinimumDuration)
        {
            return null;
        }

        long energy = entries.Sum(e => (long)e.Discharge!.Value);
        var hours = duration.TotalHours;

        // % per hour relative to each period's own full charge capacity, weighted by time.
        double? percentPerHour = null;
        var withFull = entries.Where(e => e.FullChargeCapacity is > 0).ToList();
        if (withFull.Count > 0)
        {
            var percentUsed = withFull.Sum(e => e.Discharge!.Value * 100.0 / e.FullChargeCapacity!.Value);
            var withFullHours = withFull.Sum(e => e.Duration.TotalHours);
            percentPerHour = withFullHours > 0 ? Math.Round(percentUsed / withFullHours, 2) : null;
        }

        return new StateUsage(duration, energy, Math.Round(energy / hours / 1000.0, 2), percentPerHour);
    }
}
