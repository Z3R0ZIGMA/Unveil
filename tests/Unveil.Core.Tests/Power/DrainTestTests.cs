using Unveil.Core.Battery;
using Unveil.Core.Power;

namespace Unveil.Core.Tests.Power;

public class DrainTestTests
{
    /// <summary>Returns the queued readings in order, then repeats the last one.</summary>
    private sealed class FakeBattery(params LiveReading?[] readings) : ILiveBatterySource
    {
        private int _i;

        public LiveReading? Read() => readings[Math.Min(_i++, readings.Length - 1)];
    }

    /// <summary>One busy app that uses 1 CPU-second per 2-second step.</summary>
    private sealed class FakeActivity : ILiveBatterySource, IActivitySource
    {
        private int _step;

        public int Calls { get; private set; }

        public LiveReading? Read() => null;

        public ActivitySnapshot? Take(CollectionLog log)
        {
            Calls++;
            var t = TimeSpan.FromSeconds(2 * _step);
            var snapshot = new ActivitySnapshot(t, [new ProcessCounters(42, "busyapp", TimeSpan.FromSeconds(_step), 0)], null);
            _step++;
            return snapshot;
        }
    }

    private static LiveReading OnBattery(double watts) => new()
    {
        Time = TestData.Now,
        BatteryCount = 1,
        PowerOnline = false,
        Discharging = true,
        DischargeRateMilliwatts = (int)(watts * 1000),
    };

    private static readonly LiveReading PluggedIn = new() { Time = TestData.Now, BatteryCount = 1, PowerOnline = true };

    private static readonly Func<TimeSpan, CancellationToken, Task> NoWait = (_, _) => Task.CompletedTask;

    private static DrainTestOptions TenSeconds => new() { Duration = TimeSpan.FromSeconds(10), Interval = TimeSpan.FromSeconds(2) };

    [Fact]
    public async Task Averages_the_on_battery_readings_and_scores_apps()
    {
        var activity = new FakeActivity();
        var test = new DrainTest(new FakeBattery(OnBattery(8), OnBattery(10), OnBattery(12), OnBattery(10), OnBattery(10)), activity, NoWait);

        var result = await test.RunAsync(TenSeconds);

        Assert.Equal(10.0, result.Measurement.AverageWatts);
        Assert.Equal(8.0, result.Measurement.MinWatts);
        Assert.Equal(12.0, result.Measurement.MaxWatts);
        Assert.Equal(5, result.Measurement.SampleCount);
        Assert.False(result.Measurement.PluggedIn);
        Assert.Equal(6, activity.Calls); // one at the start + one per step
        Assert.Equal(TimeSpan.FromSeconds(10), result.Activity.Duration);
        Assert.Equal("busyapp", Assert.Single(result.Measurement.TopApps).Name);
    }

    [Fact]
    public async Task Plugged_in_the_whole_time_gives_no_watts_but_still_scores_apps()
    {
        var test = new DrainTest(new FakeBattery(PluggedIn), new FakeActivity(), NoWait);

        var result = await test.RunAsync(TenSeconds);

        Assert.Null(result.Measurement.AverageWatts);
        Assert.True(result.Measurement.PluggedIn);
        Assert.NotEmpty(result.Measurement.TopApps);
        Assert.Contains(result.Log, e => e.Message.Contains("charger stayed connected"));
    }

    [Fact]
    public async Task Readings_while_plugged_in_are_left_out_of_the_average()
    {
        var test = new DrainTest(new FakeBattery(OnBattery(9), PluggedIn, OnBattery(11), PluggedIn, OnBattery(10)), new FakeActivity(), NoWait);

        var result = await test.RunAsync(TenSeconds);

        Assert.Equal(10.0, result.Measurement.AverageWatts);
        Assert.Equal(3, result.Measurement.SampleCount);
        Assert.True(result.Measurement.PluggedIn);
    }

    [Fact]
    public async Task Progress_is_reported_each_step()
    {
        var reports = new List<DrainTestProgress>();
        var test = new DrainTest(new FakeBattery(OnBattery(10)), new FakeActivity(), NoWait);

        await test.RunAsync(TenSeconds, new SyncProgress(reports.Add));

        Assert.Equal(5, reports.Count);
        Assert.Equal(TimeSpan.FromSeconds(10), reports[^1].Elapsed);
        Assert.Equal(1.0, reports[^1].Fraction);
        Assert.Equal(10.0, reports[^1].AverageWatts);
    }

    [Fact]
    public async Task Can_be_cancelled()
    {
        using var cts = new CancellationTokenSource();
        var test = new DrainTest(new FakeBattery(OnBattery(10)), new FakeActivity(), (_, _) =>
        {
            cts.Cancel();
            return Task.CompletedTask;
        });

        await Assert.ThrowsAsync<OperationCanceledException>(() => test.RunAsync(TenSeconds, null, cts.Token));
    }

    [Fact]
    public async Task A_failing_battery_reading_is_logged_and_the_test_continues()
    {
        var test = new DrainTest(new ThrowingBattery(), new FakeActivity(), NoWait);

        var result = await test.RunAsync(TenSeconds);

        Assert.Null(result.Measurement.AverageWatts);
        Assert.Contains(result.Log, e => e.Message.Contains("Battery reading failed"));
    }

    private sealed class ThrowingBattery : ILiveBatterySource
    {
        public LiveReading? Read() => throw new InvalidOperationException("boom");
    }

    private sealed class SyncProgress(Action<DrainTestProgress> report) : IProgress<DrainTestProgress>
    {
        public void Report(DrainTestProgress value) => report(value);
    }
}
