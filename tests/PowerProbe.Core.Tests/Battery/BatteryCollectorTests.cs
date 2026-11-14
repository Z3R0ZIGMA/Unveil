using System.Xml.Linq;
using PowerProbe.Core.Battery;
using PowerProbe.Core.SystemInfo;

namespace PowerProbe.Core.Tests.Battery;

public class BatteryCollectorTests
{
    private sealed class FakeWmi(Func<IReadOnlyList<WmiBatteryData>> read) : IWmiBatterySource
    {
        public bool Called { get; private set; }

        public IReadOnlyList<WmiBatteryData> Read(CollectionLog log)
        {
            Called = true;
            return read();
        }
    }

    private sealed class FakeReport(string? fixture) : IBatteryReportSource
    {
        public bool Called { get; private set; }

        public BatteryReportResult? Load(CollectionLog log)
        {
            Called = true;
            if (fixture is null)
            {
                log.Warning("Battery report", "powercfg failed (test)");
                return null;
            }

            var doc = XDocument.Load(TestFiles.Fixture(fixture));
            return new BatteryReportResult(BatteryReportParser.Parse(doc), doc);
        }
    }

    private sealed class FakeSystem(SystemIdentity identity) : ISystemIdentitySource
    {
        public SystemIdentity Read(CollectionLog log) => identity;
    }

    private static FakeSystem Desktop => new(new SystemIdentity { Manufacturer = "Contoso", Model = "Tower", PcSystemType = 1 });

    [Fact]
    public void Desktop_with_no_battery_reports_no_battery()
    {
        var collector = new BatteryCollector(new FakeWmi(() => []), new FakeReport("no-battery-desktop.xml"), Desktop);

        var snapshot = collector.Collect();

        Assert.False(snapshot.HasBattery);
        Assert.Empty(snapshot.Batteries);
        Assert.Null(snapshot.Combined);
        Assert.NotNull(snapshot.Report);
    }

    [Fact]
    public void Missing_wmi_falls_back_to_the_battery_report()
    {
        var collector = new BatteryCollector(new FakeWmi(() => []), new FakeReport("two-batteries.xml"), Desktop);

        var snapshot = collector.Collect();

        Assert.Equal(2, snapshot.Batteries.Count);
        Assert.All(snapshot.Batteries, b => Assert.Equal(BatteryDataSources.BatteryReport, b.Sources));
        Assert.NotNull(snapshot.Combined);
        Assert.Contains(snapshot.Log, e => e.Message.Contains("battery report only"));
    }

    [Fact]
    public void Failed_battery_report_falls_back_to_wmi()
    {
        var wmi = new FakeWmi(() => [new WmiBatteryData { InstanceName = "B0", FullChargedCapacity = 40000, RemainingCapacity = 20000 }]);
        var collector = new BatteryCollector(wmi, new FakeReport(null), Desktop);

        var snapshot = collector.Collect();

        var b = Assert.Single(snapshot.Batteries);
        Assert.Equal(BatteryDataSources.Wmi, b.Sources);
        Assert.Equal(50.0, b.ChargePercent);
        Assert.False(b.Health.IsKnown);
        Assert.Contains(snapshot.Log, e => e.Message.Contains("WMI only"));
    }

    [Fact]
    public void Exception_in_a_source_is_logged_and_collection_continues()
    {
        var wmi = new FakeWmi(() => throw new InvalidOperationException("WMI exploded"));
        var collector = new BatteryCollector(wmi, new FakeReport("two-batteries.xml"), Desktop);

        var snapshot = collector.Collect();

        Assert.Equal(2, snapshot.Batteries.Count);
        Assert.Contains(snapshot.Log, e => e.Level == LogLevel.Error && e.Message.Contains("WMI exploded"));
    }

    [Fact]
    public void Skip_options_do_not_call_the_sources()
    {
        var wmi = new FakeWmi(() => []);
        var report = new FakeReport("two-batteries.xml");
        var collector = new BatteryCollector(wmi, report, Desktop);

        var snapshot = collector.Collect(new BatteryCollectionOptions { SkipWmi = true, SkipBatteryReport = true });

        Assert.False(wmi.Called);
        Assert.False(report.Called);
        Assert.False(snapshot.HasBattery);
    }

    [Fact]
    public void System_identity_gaps_are_filled_from_the_report()
    {
        var collector = new BatteryCollector(new FakeWmi(() => []), new FakeReport("two-batteries.xml"), new FakeSystem(new SystemIdentity()));

        var snapshot = collector.Collect();

        Assert.Equal("LENOVO", snapshot.System.Manufacturer);
        Assert.Equal("20L5S00000", snapshot.System.Model);
    }

    [Fact]
    public void Design_capacity_zero_never_produces_a_health_figure()
    {
        var collector = new BatteryCollector(new FakeWmi(() => []), new FakeReport("design-capacity-zero.xml"), Desktop);

        var b = Assert.Single(collector.Collect().Batteries);

        Assert.False(b.Health.IsKnown);
        Assert.Null(b.CycleCount);
        Assert.Equal("XYZ", b.Chemistry);
    }
}
