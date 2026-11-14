using Unveil.Core.Battery;

namespace Unveil.Core.Tests.Battery;

public class BatteryReportParserTests
{
    [Fact]
    public void Real_asus_report_parses_battery_details()
    {
        var report = BatteryReportParser.Load(TestFiles.Sample("battery-report", "asus-expertbook-b1403cva.xml"));

        var battery = Assert.Single(report.Batteries);
        Assert.Equal("ASUS Battery", battery.Id);
        Assert.Equal("ASUSTeK", battery.Manufacturer);
        Assert.Null(battery.SerialNumber); // whitespace-only in the report
        Assert.Null(battery.ManufactureDate);
        Assert.Equal("LIon", battery.Chemistry);
        Assert.False(battery.IsRelativeCapacity);
        Assert.Equal(42067, battery.DesignCapacity);
        Assert.Equal(36628, battery.FullChargeCapacity);
        Assert.Equal(242, battery.CycleCount);
    }

    [Fact]
    public void Real_asus_report_parses_system_info_without_computer_name()
    {
        var report = BatteryReportParser.Load(TestFiles.Sample("battery-report", "asus-expertbook-b1403cva.xml"));

        Assert.Equal("1.1", report.ReportVersion);
        Assert.NotNull(report.ScanTime);
        Assert.Equal("ASUSTeK COMPUTER INC.", report.System.Manufacturer);
        Assert.Equal("ASUS EXPERTBOOK B1403CVA", report.System.ProductName);
        Assert.Equal("Mobile", report.System.PlatformRole);
        Assert.True(report.System.ModernStandby);
    }

    [Fact]
    public void Real_asus_report_parses_runtime_estimates()
    {
        var report = BatteryReportParser.Load(TestFiles.Sample("battery-report", "asus-expertbook-b1403cva.xml"));

        var est = Assert.IsType<RuntimeEstimates>(report.RuntimeEstimates);
        Assert.Equal(42067, est.AtDesignCapacity!.Capacity);
        Assert.Equal(new TimeSpan(3, 47, 13), est.AtDesignCapacity.ActiveRuntime);
        Assert.Equal(new TimeSpan(3, 4, 14, 51), est.AtDesignCapacity.ModernStandbyRuntime);
        Assert.Equal(36628, est.AtFullChargeCapacity!.Capacity);
        Assert.Equal(new TimeSpan(3, 17, 51), est.AtFullChargeCapacity.ActiveRuntime);
        Assert.Equal(new TimeSpan(2, 18, 23, 21), est.AtFullChargeCapacity.ModernStandbyRuntime);
    }

    [Fact]
    public void Real_asus_report_parses_capacity_history_oldest_first()
    {
        var report = BatteryReportParser.Load(TestFiles.Sample("battery-report", "asus-expertbook-b1403cva.xml"));

        Assert.Equal(96, report.CapacityHistory.Count);
        var first = report.CapacityHistory[0];
        Assert.Equal(new DateTimeOffset(2025, 3, 24, 5, 54, 27, TimeSpan.Zero), first.Start);
        Assert.Equal(42067, first.DesignCapacity);
        Assert.Equal(43091, first.FullChargeCapacity);
        Assert.Equal(4, first.CycleCount);
        Assert.Equal(102.4, first.Health.HealthPercent);
        Assert.All(report.CapacityHistory, e => Assert.False(e.BatteryChanged));

        var starts = report.CapacityHistory.Select(e => e.Start).ToList();
        Assert.Equal(starts.OrderBy(s => s), starts);
    }

    [Fact]
    public void Real_asus_report_parses_recent_usage_and_drains()
    {
        var report = BatteryReportParser.Load(TestFiles.Sample("battery-report", "asus-expertbook-b1403cva.xml"));

        Assert.Equal(106, report.RecentUsage.Count);
        Assert.Equal(90, report.EnergyDrains.Count);
        Assert.Contains(report.RecentUsage, u => u.EntryType == "ConnectedStandby");
        Assert.Contains(report.EnergyDrains, d => d.AverageWatts > 0);
    }

    [Fact]
    public void Two_batteries_are_both_parsed_in_order()
    {
        var report = BatteryReportParser.Load(TestFiles.Fixture("two-batteries.xml"));

        Assert.Equal(2, report.Batteries.Count);
        Assert.Equal("01AV489", report.Batteries[0].Id);
        Assert.Equal(24000, report.Batteries[0].DesignCapacity);
        Assert.Equal("2019-03-12", report.Batteries[0].ManufactureDate);
        Assert.Equal("5B10W13930", report.Batteries[1].Id);
        Assert.Equal(0, report.Batteries[1].CycleCount);
        Assert.Equal("LENOVO", report.System.Manufacturer);
        Assert.False(report.System.ModernStandby);
    }

    [Fact]
    public void History_is_sorted_and_battery_changed_flag_is_read()
    {
        var report = BatteryReportParser.Load(TestFiles.Fixture("two-batteries.xml"));

        Assert.Equal(2, report.CapacityHistory.Count);
        Assert.True(report.CapacityHistory[0].Start < report.CapacityHistory[1].Start);
        Assert.True(report.CapacityHistory[0].BatteryChanged);
        Assert.False(report.CapacityHistory[1].BatteryChanged);
    }

    [Fact]
    public void Invalid_standby_runtimes_become_null()
    {
        var report = BatteryReportParser.Load(TestFiles.Fixture("two-batteries.xml"));

        Assert.Equal(new TimeSpan(9, 30, 0), report.RuntimeEstimates!.AtDesignCapacity!.ActiveRuntime);
        Assert.Null(report.RuntimeEstimates.AtDesignCapacity.ModernStandbyRuntime); // "-"
        Assert.Null(report.RuntimeEstimates.AtFullChargeCapacity!.ModernStandbyRuntime); // empty element
    }

    [Fact]
    public void Design_capacity_zero_and_empty_fields_are_read_as_reported()
    {
        var battery = Assert.Single(BatteryReportParser.Load(TestFiles.Fixture("design-capacity-zero.xml")).Batteries);

        Assert.Equal(0, battery.DesignCapacity);
        Assert.Equal(41000, battery.FullChargeCapacity);
        Assert.Equal(0, battery.CycleCount);
        Assert.Null(battery.Manufacturer);
        Assert.Null(battery.SerialNumber);
        Assert.Equal("3/1/2023", battery.ManufactureDate);
    }

    [Fact]
    public void Relative_capacity_flag_is_read()
    {
        var battery = Assert.Single(BatteryReportParser.Load(TestFiles.Fixture("relative-capacity.xml")).Batteries);

        Assert.True(battery.IsRelativeCapacity);
        Assert.Null(battery.CycleCount);
    }

    [Fact]
    public void Desktop_report_has_no_batteries()
    {
        var report = BatteryReportParser.Load(TestFiles.Fixture("no-battery-desktop.xml"));

        Assert.Empty(report.Batteries);
        Assert.Empty(report.CapacityHistory);
        Assert.Equal("Desktop", report.System.PlatformRole);
    }

    [Fact]
    public void Missing_and_malformed_values_become_null_without_throwing()
    {
        var report = BatteryReportParser.Load(TestFiles.Fixture("missing-fields.xml"));

        var battery = Assert.Single(report.Batteries);
        Assert.Null(battery.Id);
        Assert.Null(battery.DesignCapacity);
        Assert.Null(battery.FullChargeCapacity);
        Assert.Null(report.ScanTime);
        Assert.Null(report.RuntimeEstimates!.AtFullChargeCapacity!.ActiveRuntime);

        var usage = Assert.Single(report.RecentUsage);
        Assert.Null(usage.Timestamp);
        Assert.Equal(TimeSpan.Zero, usage.Duration);
        Assert.True(usage.OnAc);

        var history = Assert.Single(report.CapacityHistory);
        Assert.Null(history.Start);
        Assert.False(history.Health.IsKnown);

        var drain = Assert.Single(report.EnergyDrains);
        Assert.Null(drain.Duration); // end before start
        Assert.Null(drain.AverageWatts);
    }

    [Fact]
    public void Truncated_xml_throws_parse_exception()
    {
        Assert.Throws<BatteryReportParseException>(() => BatteryReportParser.Load(TestFiles.Fixture("truncated.xml")));
    }

    [Fact]
    public void Wrong_root_element_throws_parse_exception()
    {
        var ex = Assert.Throws<BatteryReportParseException>(() => BatteryReportParser.Load(TestFiles.Fixture("wrong-root.xml")));
        Assert.Contains("EnergyReport", ex.Message);
    }

    [Fact]
    public void Dtd_is_rejected()
    {
        const string xml = """
            <?xml version="1.0"?>
            <!DOCTYPE BatteryReport [<!ENTITY x "boom">]>
            <BatteryReport><Batteries/></BatteryReport>
            """;

        Assert.Throws<BatteryReportParseException>(() => BatteryReportParser.Parse(xml));
    }

    [Fact]
    public void Parsing_does_not_depend_on_the_xml_namespace()
    {
        const string xml = """
            <BatteryReport xmlns="http://schemas.microsoft.com/battery/2099">
              <Batteries><Battery><Id>X</Id><DesignCapacity>50000</DesignCapacity></Battery></Batteries>
            </BatteryReport>
            """;

        var battery = Assert.Single(BatteryReportParser.Parse(xml).Batteries);
        Assert.Equal(50000, battery.DesignCapacity);
    }

    [Theory]
    [InlineData("PT3H47M13S", 0, 3, 47, 13)]
    [InlineData("P3DT4H14M51S", 3, 4, 14, 51)]
    [InlineData("PT0S", 0, 0, 0, 0)]
    public void Iso_durations_are_parsed(string value, int d, int h, int m, int s)
    {
        Assert.Equal(new TimeSpan(d, h, m, s), BatteryReportParser.Duration(value));
    }

    [Theory]
    [InlineData("-")]
    [InlineData("garbage")]
    [InlineData(null)]
    public void Invalid_durations_are_null(string? value)
    {
        Assert.Null(BatteryReportParser.Duration(value));
    }

    [Fact]
    public void Energy_drain_average_watts_is_calculated()
    {
        var drain = new EnergyDrain
        {
            Start = new DateTimeOffset(2026, 1, 1, 10, 0, 0, TimeSpan.Zero),
            End = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero),
            StartChargeCapacity = 40000,
            EndChargeCapacity = 24000,
        };

        Assert.Equal(16000, drain.Used);
        Assert.Equal(8.0, drain.AverageWatts);
    }

    [Fact]
    public void Energy_drain_that_charged_has_no_average_watts()
    {
        var drain = new EnergyDrain
        {
            Start = new DateTimeOffset(2026, 1, 1, 10, 0, 0, TimeSpan.Zero),
            End = new DateTimeOffset(2026, 1, 1, 11, 0, 0, TimeSpan.Zero),
            StartChargeCapacity = 20000,
            EndChargeCapacity = 25000,
        };

        Assert.Null(drain.AverageWatts);
    }
}
