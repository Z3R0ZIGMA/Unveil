using Unveil.Core.Battery;

namespace Unveil.Core.Tests.Battery;

public class UsageAnalyzerTests
{
    private static UsageEntry Entry(string type, double minutes, int discharge, bool ac = false, int full = 40000) => new()
    {
        Timestamp = TestData.Now,
        EntryType = type,
        Duration = TimeSpan.FromMinutes(minutes),
        Discharge = discharge,
        OnAc = ac,
        FullChargeCapacity = full,
    };

    [Fact]
    public void Real_asus_report_splits_in_use_and_asleep()
    {
        // Expected values computed independently from the sample XML.
        var usage = UsageAnalyzer.Summarize(TestData.AsusReport());

        Assert.NotNull(usage.InUse);
        Assert.Equal(163920, usage.InUse.EnergyUsed);
        Assert.Equal(13.83, usage.InUse.AverageWatts, 2);
        Assert.Equal(11.85, usage.InUse.Duration.TotalHours, 2);

        Assert.NotNull(usage.Asleep);
        Assert.Equal(4053, usage.Asleep.EnergyUsed);
        Assert.Equal(0.19, usage.Asleep.AverageWatts, 2);
        Assert.Equal(0.51, usage.Asleep.PercentPerHour!.Value, 2);
    }

    [Fact]
    public void Ac_periods_and_charging_periods_are_ignored()
    {
        var report = new BatteryReport
        {
            RecentUsage =
            [
                Entry("Active", 60, 10000),
                Entry("Active", 60, 50000, ac: true),
                Entry("Active", 60, -3000),
            ],
        };

        var usage = UsageAnalyzer.Summarize(report);

        Assert.Equal(TimeSpan.FromHours(1), usage.InUse!.Duration);
        Assert.Equal(10.0, usage.InUse.AverageWatts);
        Assert.Equal(25.0, usage.InUse.PercentPerHour);
    }

    [Fact]
    public void Suspend_and_connected_standby_both_count_as_asleep()
    {
        var report = new BatteryReport
        {
            RecentUsage = [Entry("Suspend", 120, 400), Entry("ConnectedStandby", 120, 400), Entry("ReportGenerated", 60, 5000)],
        };

        var usage = UsageAnalyzer.Summarize(report);

        Assert.Null(usage.InUse);
        Assert.Equal(TimeSpan.FromHours(4), usage.Asleep!.Duration);
        Assert.Equal(0.2, usage.Asleep.AverageWatts);
    }

    [Fact]
    public void Less_than_ten_minutes_is_not_averaged()
    {
        var report = new BatteryReport { RecentUsage = [Entry("Active", 9, 1000)] };

        Assert.Null(UsageAnalyzer.Summarize(report).InUse);
    }

    [Fact]
    public void No_report_gives_empty_summary()
    {
        var usage = UsageAnalyzer.Summarize(null);

        Assert.Null(usage.InUse);
        Assert.Null(usage.Asleep);
    }
}
