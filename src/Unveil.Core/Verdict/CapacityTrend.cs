using Unveil.Core.Battery;

namespace Unveil.Core.Verdict;

/// <summary>A sustained fall in full charge capacity found in the capacity history.</summary>
/// <param name="PeakCapacity">Highest full charge capacity in the window before the drop.</param>
/// <param name="PeakDate">When that peak was recorded.</param>
/// <param name="RecentCapacity">Highest of the most recent entries (the smallest drop, to be conservative).</param>
/// <param name="RecentDate">Start of the most recent entry.</param>
/// <param name="DropPercent">Drop from peak to recent, as % of peak.</param>
public sealed record CapacityDrop(int PeakCapacity, DateTimeOffset PeakDate, int RecentCapacity, DateTimeOffset RecentDate, double DropPercent);

public static class CapacityTrend
{
    /// <summary>
    /// Returns a drop when the last <see cref="VerdictSettings.CapacityDropSustainedEntries"/> history entries are
    /// ALL more than <see cref="VerdictSettings.CapacityDropPercent"/> below the highest full charge capacity
    /// recorded in the <see cref="VerdictSettings.CapacityDropWindowDays"/> days before them.
    /// One-off dips (e.g. after calibration) are ignored. Returns null when there is no such drop or too little history.
    /// </summary>
    public static CapacityDrop? FindSustainedDrop(IReadOnlyList<CapacityHistoryEntry> history, VerdictSettings settings)
    {
        var usable = history
            .Where(e => e.Start is not null && e.FullChargeCapacity is > 0)
            .OrderBy(e => e.Start)
            .ToList();

        var recentCount = Math.Max(1, settings.CapacityDropSustainedEntries);
        if (usable.Count <= recentCount)
        {
            return null;
        }

        var recent = usable.TakeLast(recentCount).ToList();
        var recentStart = recent[0].Start!.Value;
        var windowStart = recentStart.AddDays(-settings.CapacityDropWindowDays);
        var window = usable.Where(e => e.Start >= windowStart && e.Start < recentStart).ToList();

        // A battery swap inside the window makes the comparison meaningless.
        if (window.Count == 0 || window.Concat(recent).Any(e => e.BatteryChanged))
        {
            return null;
        }

        var peak = window.MaxBy(e => e.FullChargeCapacity!.Value)!;
        var peakCapacity = peak.FullChargeCapacity!.Value;
        var bestRecent = recent.Max(e => e.FullChargeCapacity!.Value);
        var dropPercent = (peakCapacity - bestRecent) * 100.0 / peakCapacity;

        return dropPercent > settings.CapacityDropPercent
            ? new CapacityDrop(peakCapacity, peak.Start!.Value, bestRecent, recent[^1].Start!.Value, Math.Round(dropPercent, 1))
            : null;
    }
}
