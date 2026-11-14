using Unveil.Core.Battery;

namespace Unveil.Core.Tests.Battery;

public class LiveBatteryReaderTests
{
    private static WmiBatteryData Status(uint remaining, uint full, int discharge = 0, int charge = 0, bool online = false) => new()
    {
        InstanceName = $"B{remaining}",
        RemainingCapacity = remaining,
        FullChargedCapacity = full,
        DischargeRate = discharge,
        ChargeRate = charge,
        PowerOnline = online,
        Discharging = !online && discharge > 0,
        Charging = online && charge > 0,
    };

    [Fact]
    public void No_batteries_gives_no_reading()
    {
        Assert.Null(WmiLiveBatteryReader.Combine([], TestData.Now));
    }

    [Fact]
    public void One_battery_on_battery_power()
    {
        var r = WmiLiveBatteryReader.Combine([Status(18000, 36000, discharge: 7400)], TestData.Now)!;

        Assert.Equal(50.0, r.ChargePercent);
        Assert.True(r.IsOnBattery);
        Assert.Equal(7.4, r.DischargeWatts);
    }

    [Fact]
    public void Plugged_in_has_no_discharge_watts()
    {
        var r = WmiLiveBatteryReader.Combine([Status(36000, 36000, discharge: 0, charge: 0, online: true)], TestData.Now)!;

        Assert.False(r.IsOnBattery);
        Assert.Null(r.DischargeWatts);
        Assert.Equal(100.0, r.ChargePercent);
    }

    [Fact]
    public void Two_batteries_are_added_up()
    {
        var r = WmiLiveBatteryReader.Combine([Status(10000, 20000, discharge: 3000), Status(30000, 60000, discharge: 2000)], TestData.Now)!;

        Assert.Equal(2, r.BatteryCount);
        Assert.Equal(40000, r.RemainingCapacity);
        Assert.Equal(80000, r.FullChargeCapacity);
        Assert.Equal(50.0, r.ChargePercent);
        Assert.Equal(5000, r.DischargeRateMilliwatts);
    }

    [Fact]
    public void Unknown_full_charge_gives_unknown_percent()
    {
        var battery = Status(10000, 20000);
        battery.FullChargedCapacity = null;

        Assert.Null(WmiLiveBatteryReader.Combine([battery], TestData.Now)!.ChargePercent);
    }

    [Theory]
    [InlineData(36628L, 8.0, 4.5785)]
    [InlineData(42067L, 8.0, 5.2584)]
    public void Runtime_is_capacity_over_watts(long capacity, double watts, double hours)
    {
        Assert.Equal(hours, WmiLiveBatteryReader.Runtime(capacity, watts)!.Value.TotalHours, 3);
    }

    [Fact]
    public void Runtime_is_unknown_without_a_real_draw()
    {
        Assert.Null(WmiLiveBatteryReader.Runtime(36628, 0));
        Assert.Null(WmiLiveBatteryReader.Runtime(null, 8));
    }
}
