using PowerProbe.Core.Battery;

namespace PowerProbe.Core.Tests.Battery;

public class BatteryHealthTests
{
    [Fact]
    public void Health_is_full_charge_over_design()
    {
        var result = BatteryHealth.Calculate(42067, 36628);

        Assert.True(result.IsKnown);
        Assert.Equal(87.1, result.HealthPercent);
        Assert.Equal(12.9, result.WearPercent);
        Assert.Null(result.UnknownReason);
    }

    [Theory]
    [InlineData(50000, 50000, 100.0, 0.0)]
    [InlineData(50000, 40000, 80.0, 20.0)]
    [InlineData(50000, 29950, 59.9, 40.1)]
    [InlineData(50000, 0, 0.0, 100.0)]
    public void Health_and_wear_are_rounded_to_one_decimal(long design, long full, double health, double wear)
    {
        var result = BatteryHealth.Calculate(design, full);

        Assert.Equal(health, result.HealthPercent);
        Assert.Equal(wear, result.WearPercent);
    }

    [Fact]
    public void Health_above_100_is_kept_and_wear_is_zero()
    {
        var result = BatteryHealth.Calculate(42067, 43091);

        Assert.Equal(102.4, result.HealthPercent);
        Assert.Equal(0.0, result.WearPercent);
    }

    [Fact]
    public void Design_capacity_zero_gives_unknown_not_replace()
    {
        var result = BatteryHealth.Calculate(0, 41000);

        Assert.False(result.IsKnown);
        Assert.Null(result.HealthPercent);
        Assert.Null(result.WearPercent);
        Assert.Contains("Design capacity reported as 0", result.UnknownReason);
    }

    [Fact]
    public void Missing_design_capacity_gives_unknown()
    {
        var result = BatteryHealth.Calculate(null, 41000);

        Assert.False(result.IsKnown);
        Assert.Contains("Design capacity not reported", result.UnknownReason);
    }

    [Fact]
    public void Missing_full_charge_capacity_gives_unknown()
    {
        var result = BatteryHealth.Calculate(50000, null);

        Assert.False(result.IsKnown);
        Assert.Contains("Full charge capacity not reported", result.UnknownReason);
    }

    [Fact]
    public void Negative_full_charge_capacity_gives_unknown()
    {
        Assert.False(BatteryHealth.Calculate(50000, -1).IsKnown);
    }

    [Fact]
    public void Relative_capacity_gives_unknown_even_with_values()
    {
        var result = BatteryHealth.Calculate(100, 45, isRelativeCapacity: true);

        Assert.False(result.IsKnown);
        Assert.Contains("relative", result.UnknownReason);
    }

    [Fact]
    public void Implausibly_high_health_gives_unknown()
    {
        // e.g. design reported in mAh (4,000) and full charge in mWh (45,000)
        var result = BatteryHealth.Calculate(4000, 45000);

        Assert.False(result.IsKnown);
        Assert.Contains("not plausible", result.UnknownReason);
    }

    [Fact]
    public void Health_at_the_plausibility_limit_is_still_known()
    {
        Assert.Equal(130.0, BatteryHealth.Calculate(10000, 13000).HealthPercent);
    }
}
