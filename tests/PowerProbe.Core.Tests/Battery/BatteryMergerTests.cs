using PowerProbe.Core.Battery;

namespace PowerProbe.Core.Tests.Battery;

public class BatteryMergerTests
{
    private static WmiBatteryData Wmi(string instance = @"ACPI\PNP0C0A\0_0") => new()
    {
        InstanceName = instance,
        FullChargedCapacity = 36628,
        CycleCount = 242,
        RemainingCapacity = 18314,
        Voltage = 12867,
        ChargeRate = 0,
        DischargeRate = 7500,
        PowerOnline = false,
        Charging = false,
        Discharging = true,
        Critical = false,
    };

    private static ReportBattery Report(string id = "ASUS Battery") => new()
    {
        Id = id,
        Manufacturer = "ASUSTeK",
        Chemistry = "LIon",
        DesignCapacity = 42067,
        FullChargeCapacity = 36628,
        CycleCount = 242,
    };

    [Fact]
    public void Wmi_and_report_are_merged_by_position()
    {
        var log = new CollectionLog();
        var b = Assert.Single(BatteryMerger.Merge([Wmi()], [Report()], log));

        Assert.Equal(BatteryDataSources.Wmi | BatteryDataSources.BatteryReport, b.Sources);
        Assert.Equal("ASUS Battery", b.Name);
        Assert.Equal("Lithium-ion", b.Chemistry);
        Assert.Equal(42067, b.DesignCapacity);
        Assert.Equal(36628, b.FullChargeCapacity);
        Assert.Equal(87.1, b.Health.HealthPercent);
        Assert.Equal(242, b.CycleCount);
        Assert.Equal(50.0, b.ChargePercent);
        Assert.Equal(7500, b.DischargeRateMilliwatts);
        Assert.Empty(log.Entries);
    }

    [Fact]
    public void Report_wins_on_disagreement_and_difference_is_logged()
    {
        var log = new CollectionLog();
        var wmi = Wmi();
        wmi.DesignedCapacity = 42000;
        wmi.FullChargedCapacity = 36000;

        var b = Assert.Single(BatteryMerger.Merge([wmi], [Report()], log));

        Assert.Equal(42067, b.DesignCapacity);
        Assert.Equal(36628, b.FullChargeCapacity);
        Assert.Equal(2, log.Entries.Count);
        Assert.All(log.Entries, e => Assert.Equal(LogLevel.Warning, e.Level));
        Assert.Contains(log.Entries, e => e.Message.Contains("design capacity") && e.Message.Contains("42,000"));
    }

    [Fact]
    public void Charge_percent_uses_wmi_full_charge_read_at_the_same_moment()
    {
        var wmi = Wmi();
        wmi.FullChargedCapacity = 40000;
        wmi.RemainingCapacity = 20000;

        var b = Assert.Single(BatteryMerger.Merge([wmi], [Report()], new CollectionLog()));

        Assert.Equal(50.0, b.ChargePercent);
    }

    [Fact]
    public void Charge_percent_is_capped_at_100()
    {
        var wmi = Wmi();
        wmi.RemainingCapacity = 37000;

        Assert.Equal(100.0, Assert.Single(BatteryMerger.Merge([wmi], [], new CollectionLog())).ChargePercent);
    }

    [Fact]
    public void Wmi_only_battery_has_unknown_health_when_static_data_is_missing()
    {
        var b = Assert.Single(BatteryMerger.Merge([Wmi()], [], new CollectionLog()));

        Assert.Equal(BatteryDataSources.Wmi, b.Sources);
        Assert.False(b.Health.IsKnown);
        Assert.Equal(242, b.CycleCount);
        Assert.Equal(50.0, b.ChargePercent);
    }

    [Fact]
    public void Wmi_only_battery_with_static_data_has_health()
    {
        var wmi = Wmi();
        wmi.StaticDataAvailable = true;
        wmi.DesignedCapacity = 42067;
        wmi.DeviceName = "ASUS Battery";
        wmi.Chemistry = "LION";
        wmi.ManufactureDate = new DateOnly(2024, 5, 1);

        var b = Assert.Single(BatteryMerger.Merge([wmi], [], new CollectionLog()));

        Assert.Equal(87.1, b.Health.HealthPercent);
        Assert.Equal("ASUS Battery", b.Name);
        Assert.Equal("Lithium-ion", b.Chemistry);
        Assert.Equal("2024-05-01", b.ManufactureDate);
    }

    [Fact]
    public void Report_only_battery_has_health_but_no_live_data()
    {
        var b = Assert.Single(BatteryMerger.Merge([], [Report()], new CollectionLog()));

        Assert.Equal(BatteryDataSources.BatteryReport, b.Sources);
        Assert.Equal(87.1, b.Health.HealthPercent);
        Assert.Null(b.RemainingCapacity);
        Assert.Null(b.ChargePercent);
        Assert.Null(b.PowerOnline);
    }

    [Fact]
    public void No_data_means_no_batteries()
    {
        Assert.Empty(BatteryMerger.Merge([], [], new CollectionLog()));
    }

    [Fact]
    public void Cycle_count_zero_is_not_reported()
    {
        var wmi = Wmi();
        wmi.CycleCount = 0;
        var report = Report() with { CycleCount = 0 };

        Assert.Null(Assert.Single(BatteryMerger.Merge([wmi], [report], new CollectionLog())).CycleCount);
    }

    [Fact]
    public void Cycle_count_zero_in_wmi_does_not_hide_report_value_or_log_a_difference()
    {
        var log = new CollectionLog();
        var wmi = Wmi();
        wmi.CycleCount = 0;

        Assert.Equal(242, Assert.Single(BatteryMerger.Merge([wmi], [Report()], log)).CycleCount);
        Assert.Empty(log.Entries);
    }

    [Fact]
    public void Relative_capacity_from_either_source_makes_health_unknown()
    {
        var wmi = Wmi();
        wmi.Capabilities = 0x40000000;

        var b = Assert.Single(BatteryMerger.Merge([wmi], [Report()], new CollectionLog()));

        Assert.True(b.IsRelativeCapacity);
        Assert.False(b.Health.IsKnown);
    }

    [Fact]
    public void Two_batteries_match_by_serial_number_even_when_order_differs()
    {
        var w1 = Wmi(@"ACPI\PNP0C0A\0_0");
        w1.SerialNumber = "SER-B";
        w1.FullChargedCapacity = 66000;
        var w2 = Wmi(@"ACPI\PNP0C0A\1_0");
        w2.SerialNumber = "SER-A";
        w2.FullChargedCapacity = 18000;

        var r1 = Report("01AV489") with { SerialNumber = "SER-A", DesignCapacity = 24000, FullChargeCapacity = 18000 };
        var r2 = Report("5B10W13930") with { SerialNumber = "SER-B", DesignCapacity = 72000, FullChargeCapacity = 66000 };

        var log = new CollectionLog();
        var batteries = BatteryMerger.Merge([w1, w2], [r1, r2], log);

        Assert.Equal(2, batteries.Count);
        Assert.Equal("5B10W13930", batteries[0].Name);
        Assert.Equal(@"ACPI\PNP0C0A\0_0", batteries[0].WmiInstanceName);
        Assert.Equal("01AV489", batteries[1].Name);
        Assert.Equal([1, 2], batteries.Select(b => b.Index));
        Assert.Empty(log.Entries);
    }

    [Fact]
    public void Two_batteries_match_by_name_when_serials_are_missing()
    {
        var w1 = Wmi(@"ACPI\PNP0C0A\0_0");
        w1.DeviceName = "5B10W13930";
        var w2 = Wmi(@"ACPI\PNP0C0A\1_0");
        w2.DeviceName = "01AV489";

        var batteries = BatteryMerger.Merge([w1, w2], [Report("01AV489"), Report("5B10W13930")], new CollectionLog());

        Assert.Equal("5B10W13930", batteries[0].Name);
        Assert.Equal("01AV489", batteries[1].Name);
    }

    [Fact]
    public void Unequal_counts_without_keys_are_shown_separately_and_logged()
    {
        var log = new CollectionLog();
        var batteries = BatteryMerger.Merge([Wmi()], [Report("A"), Report("B")], log);

        Assert.Equal(3, batteries.Count);
        Assert.Equal(BatteryDataSources.Wmi, batteries[0].Sources);
        Assert.Equal(BatteryDataSources.BatteryReport, batteries[1].Sources);
        Assert.Contains(log.Entries, e => e.Level == LogLevel.Warning);
    }

    [Fact]
    public void Combined_total_sums_capacities_and_health()
    {
        var r1 = Report("01AV489") with { DesignCapacity = 24000, FullChargeCapacity = 18000 };
        var r2 = Report("5B10W13930") with { DesignCapacity = 72000, FullChargeCapacity = 66000 };
        var batteries = BatteryMerger.Merge([], [r1, r2], new CollectionLog());

        var total = BatteryMerger.Combine(batteries);

        Assert.NotNull(total);
        Assert.Equal(2, total.Count);
        Assert.Equal(96000, total.DesignCapacity);
        Assert.Equal(84000, total.FullChargeCapacity);
        Assert.Equal(87.5, total.Health.HealthPercent);
        Assert.Null(total.RemainingCapacity); // no live data from the report
    }

    [Fact]
    public void Combined_total_includes_live_rates()
    {
        var w1 = Wmi(@"ACPI\PNP0C0A\0_0");
        w1.RemainingCapacity = 10000;
        w1.FullChargedCapacity = 20000;
        w1.DischargeRate = 3000;
        var w2 = Wmi(@"ACPI\PNP0C0A\1_0");
        w2.RemainingCapacity = 30000;
        w2.FullChargedCapacity = 60000;
        w2.DischargeRate = null;

        var r1 = Report("A") with { DesignCapacity = 24000, FullChargeCapacity = 20000 };
        var r2 = Report("B") with { DesignCapacity = 72000, FullChargeCapacity = 60000 };
        var total = BatteryMerger.Combine(BatteryMerger.Merge([w1, w2], [r1, r2], new CollectionLog()));

        Assert.Equal(40000, total!.RemainingCapacity);
        Assert.Equal(50.0, total.ChargePercent);
        Assert.Equal(3000, total.DischargeRateMilliwatts);
    }

    [Fact]
    public void Combined_health_is_unknown_when_one_battery_is_unknown()
    {
        var r1 = Report("A") with { DesignCapacity = 0, FullChargeCapacity = 18000 };
        var r2 = Report("B") with { DesignCapacity = 72000, FullChargeCapacity = 66000 };

        var total = BatteryMerger.Combine(BatteryMerger.Merge([], [r1, r2], new CollectionLog()));

        Assert.False(total!.Health.IsKnown);
        Assert.Contains("battery 1", total.Health.UnknownReason);
    }

    [Fact]
    public void Single_battery_has_no_combined_total()
    {
        Assert.Null(BatteryMerger.Combine(BatteryMerger.Merge([], [Report()], new CollectionLog())));
    }

    [Theory]
    [InlineData("2019-03-12", "2019-03-12")]
    [InlineData("2019/03/12", "2019-03-12")]
    [InlineData("20190312", "2019-03-12")]
    [InlineData("3/1/2023", "3/1/2023")] // ambiguous: kept as the OEM wrote it
    [InlineData("  ", null)]
    [InlineData(null, null)]
    public void Manufacture_dates_are_normalised_only_when_unambiguous(string? raw, string? expected)
    {
        Assert.Equal(expected, BatteryMerger.NormaliseDate(raw));
    }
}
