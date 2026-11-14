namespace Unveil.Core.Power;

/// <summary>One process's average activity over the test.</summary>
/// <param name="CpuPercent">Share of the whole CPU (all cores), 0-100.</param>
/// <param name="GpuPercent">GPU engine busy time, summed over engines, capped at 100.</param>
/// <param name="IoBytesPerSecond">Disk + network + device I/O.</param>
/// <param name="Impact">Weighted Power Impact score (see <see cref="PowerImpact"/>).</param>
public sealed record ProcessUsage(int Pid, string Name, double CpuPercent, double GpuPercent, double IoBytesPerSecond, double Impact);

/// <summary>All processes with the same executable name (e.g. every chrome.exe), combined.</summary>
public sealed record AppUsage
{
    /// <summary>Executable name without ".exe".</summary>
    public required string Name { get; init; }

    /// <summary>Friendly name (file description) when known, otherwise the executable name.</summary>
    public string DisplayName { get; init; } = "";

    public int ProcessCount { get; init; }

    public double CpuPercent { get; init; }

    public double GpuPercent { get; init; }

    public double IoBytesPerSecond { get; init; }

    public double Impact { get; init; }

    /// <summary>This app's share of the total Power Impact, 0-100.</summary>
    public double SharePercent { get; init; }

    public KnownOffender? Offender { get; init; }

    public IReadOnlyList<ProcessUsage> Processes { get; init; } = [];
}

/// <summary>Result of sampling all processes during the test.</summary>
public sealed record ActivityResult
{
    public TimeSpan Duration { get; init; }

    public int SnapshotCount { get; init; }

    public bool GpuMeasured { get; init; }

    /// <summary>Apps by Power Impact, highest first.</summary>
    public IReadOnlyList<AppUsage> Apps { get; init; } = [];

    /// <summary>Whole-machine CPU use over the test (all processes), 0-100.</summary>
    public double TotalCpuPercent { get; init; }
}

/// <summary>
/// Power Impact: an estimate of how much each app contributes to battery drain.
/// Each part is scaled to 0-100 and weighted: CPU 60%, GPU 30%, I/O 10%
/// (I/O reaches 100 at <see cref="IoFullScaleBytesPerSecond"/>). It ranks apps; it is not a measurement in watts.
/// </summary>
public static class PowerImpact
{
    public const double CpuWeight = 0.6;
    public const double GpuWeight = 0.3;
    public const double IoWeight = 0.1;
    public const double IoFullScaleBytesPerSecond = 20 * 1024 * 1024;

    /// <summary>Kernel bookkeeping entries that are not apps.</summary>
    private static readonly HashSet<string> NotApps = new(StringComparer.OrdinalIgnoreCase) { "Idle", "_Total" };

    public static double Score(double cpuPercent, double gpuPercent, double ioBytesPerSecond) =>
        CpuWeight * cpuPercent
        + GpuWeight * gpuPercent
        + IoWeight * Math.Min(100, ioBytesPerSecond / IoFullScaleBytesPerSecond * 100);

    /// <param name="snapshots">Readings in time order (at least two).</param>
    /// <param name="coreCount">Logical processors, to scale CPU time to 0-100% of the whole CPU.</param>
    /// <param name="excludePids">Processes to leave out (Unveil itself).</param>
    public static ActivityResult Analyze(IReadOnlyList<ActivitySnapshot> snapshots, int coreCount, IReadOnlySet<int>? excludePids = null)
    {
        if (snapshots.Count < 2)
        {
            return new ActivityResult { SnapshotCount = snapshots.Count };
        }

        coreCount = Math.Max(1, coreCount);
        var totals = new Dictionary<(int Pid, string Name), (TimeSpan Cpu, long Io, TimeSpan Gpu)>();
        var duration = TimeSpan.Zero;

        for (var i = 1; i < snapshots.Count; i++)
        {
            var before = snapshots[i - 1];
            var after = snapshots[i];
            var interval = after.Timestamp - before.Timestamp;
            if (interval <= TimeSpan.Zero)
            {
                continue;
            }

            duration += interval;
            var previous = before.Processes
                .GroupBy(p => (p.Pid, p.Name))
                .ToDictionary(g => g.Key, g => g.First());

            foreach (var now in after.Processes)
            {
                var key = (now.Pid, now.Name);
                if (NotApps.Contains(now.Name) || excludePids?.Contains(now.Pid) == true || !previous.TryGetValue(key, out var then))
                {
                    continue; // new processes start counting from their second reading; a reused PID with a new name is a new process
                }

                var cpu = Positive(now.CpuTime - then.CpuTime);
                var io = Math.Max(0, now.IoBytes - then.IoBytes);
                var gpu = before.GpuTime is { } g0 && after.GpuTime is { } g1
                    ? Positive(g1.GetValueOrDefault(now.Pid) - g0.GetValueOrDefault(now.Pid))
                    : TimeSpan.Zero;

                var sum = totals.GetValueOrDefault(key);
                totals[key] = (sum.Cpu + cpu, sum.Io + io, sum.Gpu + gpu);
            }
        }

        if (duration <= TimeSpan.Zero)
        {
            return new ActivityResult { SnapshotCount = snapshots.Count };
        }

        var seconds = duration.TotalSeconds;
        var processes = totals.Select(t =>
        {
            var cpuPercent = t.Value.Cpu.TotalSeconds / seconds / coreCount * 100;
            var gpuPercent = Math.Min(100, t.Value.Gpu.TotalSeconds / seconds * 100);
            var io = t.Value.Io / seconds;
            return new ProcessUsage(t.Key.Pid, t.Key.Name, cpuPercent, gpuPercent, io, Score(cpuPercent, gpuPercent, io));
        }).ToList();

        var totalImpact = processes.Sum(p => p.Impact);
        var apps = processes
            .GroupBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .Select(g =>
            {
                var impact = g.Sum(p => p.Impact);
                return new AppUsage
                {
                    Name = g.First().Name,
                    DisplayName = g.First().Name,
                    ProcessCount = g.Count(),
                    CpuPercent = g.Sum(p => p.CpuPercent),
                    GpuPercent = Math.Min(100, g.Sum(p => p.GpuPercent)),
                    IoBytesPerSecond = g.Sum(p => p.IoBytesPerSecond),
                    Impact = impact,
                    SharePercent = totalImpact > 0 ? impact / totalImpact * 100 : 0,
                    Offender = KnownOffenders.Match(g.First().Name, g.Count()),
                    Processes = g.OrderByDescending(p => p.Impact).ToList(),
                };
            })
            .OrderByDescending(a => a.Impact)
            .ThenBy(a => a.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new ActivityResult
        {
            Duration = duration,
            SnapshotCount = snapshots.Count,
            GpuMeasured = snapshots.All(s => s.GpuTime is not null),
            Apps = apps,
            TotalCpuPercent = Math.Min(100, processes.Sum(p => p.CpuPercent)),
        };
    }

    private static TimeSpan Positive(TimeSpan t) => t > TimeSpan.Zero ? t : TimeSpan.Zero;
}
