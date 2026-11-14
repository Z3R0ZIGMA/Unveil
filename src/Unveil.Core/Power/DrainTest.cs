using System.Diagnostics;
using Unveil.Core.Battery;
using Unveil.Core.Verdict;

namespace Unveil.Core.Power;

public sealed record DrainTestOptions
{
    /// <summary>plan.md: sample for 60 s.</summary>
    public TimeSpan Duration { get; init; } = TimeSpan.FromSeconds(60);

    /// <summary>plan.md: every 2 s.</summary>
    public TimeSpan Interval { get; init; } = TimeSpan.FromSeconds(2);

    /// <summary>How many apps to keep in the measurement summary.</summary>
    public int TopAppCount { get; init; } = 15;
}

/// <param name="Elapsed">Time into the test.</param>
/// <param name="Duration">Planned test length.</param>
/// <param name="LatestWatts">Last discharge reading; null when plugged in.</param>
/// <param name="AverageWatts">Average of the on-battery readings so far.</param>
/// <param name="OnBattery">Whether the last reading was on battery.</param>
public sealed record DrainTestProgress(TimeSpan Elapsed, TimeSpan Duration, double? LatestWatts, double? AverageWatts, bool OnBattery)
{
    public double Fraction => Duration > TimeSpan.Zero ? Math.Clamp(Elapsed / Duration, 0, 1) : 1;
}

public sealed record DrainTestResult
{
    public required DrainMeasurement Measurement { get; init; }

    public required ActivityResult Activity { get; init; }

    public IReadOnlyList<LogEntry> Log { get; init; } = [];
}

/// <summary>
/// The live drain test (plan.md 3.3 and 3.4): reads the battery discharge rate and every process's activity
/// every 2 s for 60 s, then averages the draw and scores the apps.
/// </summary>
public sealed class DrainTest(
    ILiveBatterySource battery,
    IActivitySource activity,
    Func<TimeSpan, CancellationToken, Task>? delay = null)
{
    private readonly Func<TimeSpan, CancellationToken, Task> _delay = delay ?? Task.Delay;

    public static DrainTest CreateDefault() => new(new WmiLiveBatteryReader(), new PerfCounterActivitySource());

    /// <summary>The first performance-counter read can take seconds; do it before the timed part.</summary>
    public void WarmUp() => activity.Take(new CollectionLog());

    public async Task<DrainTestResult> RunAsync(
        DrainTestOptions? options = null,
        IProgress<DrainTestProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        options ??= new DrainTestOptions();
        var log = new CollectionLog();
        var snapshots = new List<ActivitySnapshot>();
        var watts = new List<double>();
        var pluggedIn = false;
        var steps = Math.Max(1, (int)Math.Round(options.Duration / options.Interval));
        var clock = Stopwatch.StartNew();

        if (activity.Take(log) is { } first)
        {
            snapshots.Add(first);
        }

        for (var i = 1; i <= steps; i++)
        {
            await _delay(options.Interval, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();

            LiveReading? reading = null;
            try
            {
                reading = battery.Read();
            }
#pragma warning disable CA1031 // A failed reading must not end the test.
            catch (Exception ex)
#pragma warning restore CA1031
            {
                log.Warning("Drain test", $"Battery reading failed: {ex.Message}");
            }

            if (reading?.DischargeWatts is { } w)
            {
                watts.Add(w);
            }
            else if (reading is not null && !reading.IsOnBattery)
            {
                pluggedIn = true;
            }

            if (activity.Take(log) is { } snapshot)
            {
                snapshots.Add(snapshot);
            }

            progress?.Report(new DrainTestProgress(
                options.Interval * i,
                options.Duration,
                reading?.DischargeWatts,
                watts.Count > 0 ? watts.Average() : null,
                reading?.IsOnBattery ?? false));
        }

        var result = PowerImpact.Analyze(snapshots, Environment.ProcessorCount, new HashSet<int> { Environment.ProcessId });
        result = AppNames.Resolve(result, options.TopAppCount);

        if (watts.Count == 0)
        {
            log.Info("Drain test", "The charger stayed connected, so the power draw was not measured; only app activity was recorded.");
        }

        var measurement = new DrainMeasurement
        {
            AverageWatts = watts.Count > 0 ? Math.Round(watts.Average(), 2) : null,
            MinWatts = watts.Count > 0 ? watts.Min() : null,
            MaxWatts = watts.Count > 0 ? watts.Max() : null,
            Duration = result.Duration > TimeSpan.Zero ? result.Duration : clock.Elapsed,
            SampleCount = watts.Count,
            PluggedIn = pluggedIn,
            WholeMachineCpuPercent = result.SnapshotCount >= 2 ? Math.Round(result.TotalCpuPercent, 1) : null,
            TopApps = result.Apps
                .Take(options.TopAppCount)
                .Select(a => new AppImpact(a.DisplayName, Math.Round(a.SharePercent, 1), Math.Round(a.Impact, 2), a.Offender?.Category, a.Offender?.Advice))
                .ToList(),
        };

        return new DrainTestResult { Measurement = measurement, Activity = result, Log = log.Entries };
    }
}
