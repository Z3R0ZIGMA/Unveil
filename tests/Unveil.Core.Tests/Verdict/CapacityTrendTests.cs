using Unveil.Core.Battery;
using Unveil.Core.Verdict;

namespace Unveil.Core.Tests.Verdict;

public class CapacityTrendTests
{
    private static readonly VerdictSettings Settings = VerdictSettings.Default;

    [Fact]
    public void Sustained_drop_over_ten_percent_is_found()
    {
        var history = TestData.DailyHistory(45000, 45000, 44800, 44500, 39000, 38900, 38800);

        var drop = CapacityTrend.FindSustainedDrop(history, Settings);

        Assert.NotNull(drop);
        Assert.Equal(45000, drop.PeakCapacity);
        Assert.Equal(39000, drop.RecentCapacity); // highest of the last 3 = smallest drop
        Assert.Equal(13.3, drop.DropPercent);
    }

    [Fact]
    public void One_off_dip_is_ignored()
    {
        // Calibration-style dip in one entry, then back to normal.
        var history = TestData.DailyHistory(45000, 45000, 44900, 38000, 44800, 44700);

        Assert.Null(CapacityTrend.FindSustainedDrop(history, Settings));
    }

    [Fact]
    public void Drop_in_only_two_of_the_last_three_entries_is_ignored()
    {
        var history = TestData.DailyHistory(45000, 45000, 44900, 44800, 39000, 38900);

        Assert.Null(CapacityTrend.FindSustainedDrop(history, Settings));
    }

    [Fact]
    public void Drop_of_exactly_ten_percent_is_not_enough()
    {
        var history = TestData.DailyHistory(50000, 50000, 45000, 45000, 45000);

        Assert.Null(CapacityTrend.FindSustainedDrop(history, Settings));
    }

    [Fact]
    public void Gradual_ageing_is_not_a_drop()
    {
        var history = TestData.DailyHistory(45000, 44900, 44800, 44700, 44600, 44500, 44400);

        Assert.Null(CapacityTrend.FindSustainedDrop(history, Settings));
    }

    [Fact]
    public void Peak_older_than_the_window_is_ignored()
    {
        var old = new CapacityHistoryEntry
        {
            Start = TestData.Now.AddDays(-90),
            DesignCapacity = 50000,
            FullChargeCapacity = 50000,
        };
        var history = new[] { old }.Concat(TestData.DailyHistory(40000, 39900, 39800, 39700)).ToList();

        Assert.Null(CapacityTrend.FindSustainedDrop(history, Settings));
    }

    [Fact]
    public void Battery_replacement_in_the_window_is_ignored()
    {
        var history = TestData.DailyHistory(45000, 45000, 30000, 30000, 30000).ToList();
        history[2] = history[2] with { BatteryChanged = true };

        Assert.Null(CapacityTrend.FindSustainedDrop(history, Settings));
    }

    [Fact]
    public void Too_little_history_is_not_evaluated()
    {
        Assert.Null(CapacityTrend.FindSustainedDrop(TestData.DailyHistory(45000, 30000, 30000), Settings));
        Assert.Null(CapacityTrend.FindSustainedDrop([], Settings));
    }

    [Fact]
    public void Real_asus_history_has_no_sudden_drop()
    {
        Assert.Null(CapacityTrend.FindSustainedDrop(TestData.AsusReport().CapacityHistory, Settings));
    }
}
