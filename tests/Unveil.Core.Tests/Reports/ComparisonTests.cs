using Unveil.Core.Battery;
using Unveil.Core.Reports;
using static Unveil.Core.Tests.TestData;

namespace Unveil.Core.Tests.Reports;

public class ComparisonTests
{
    private static BatteryInfo Battery(double health, string? serial = "FAKE-BAT-1", int? cycles = 200, int index = 1) =>
        WithHealth(health, cycles, index) with { SerialNumber = serial };

    private static ReportDocument Doc(DateTimeOffset at, string? laptopSerial = "FAKE-SN-1", params BatteryInfo[] batteries)
    {
        var snapshot = Snapshot(batteries);
        snapshot = snapshot with { System = snapshot.System with { SerialNumber = laptopSerial } };
        return ReportDocument.Create(snapshot, generatedAt: at);
    }

    /// <summary>Exports a report to JSON and reads it back the way the app does.</summary>
    private static PreviousReport Exported(ReportDocument doc, string file = "unveil-old.json") =>
        PreviousReportReader.Read(JsonReportWriter.Serialize(doc), file);

    [Fact]
    public void Same_battery_shows_the_change_since_the_earlier_report()
    {
        var before = Exported(Doc(Now.AddDays(-60), batteries: [Battery(90, cycles: 200)]));
        var now = Doc(Now, batteries: [Battery(85, cycles: 260)]);

        var c = ReportComparer.Compare(now, before);

        Assert.Equal(ComparisonMatch.SameBattery, c.Match);
        Assert.Equal(60, c.Days);
        var b = Assert.Single(c.Batteries);
        Assert.True(b.MatchedBySerial);
        Assert.Equal(90.0, b.HealthBefore);
        Assert.Equal(85.0, b.HealthNow);
        Assert.Equal(-5.0, b.HealthChange);
        Assert.Equal(-2.5, b.HealthChangePerMonth);
        Assert.Equal(-5000, b.FullChargeChange);
        Assert.Equal(60, b.CyclesChange);
        Assert.Equal("Since 2026-09-15 (2 months ago): health 90% -> 85% (down 5.0 points, about 2.5 points a month).", c.Summary);
        Assert.Equal("Battery healthy", c.VerdictBefore);
        Assert.Equal("unveil-old.json", c.PreviousFileName);
    }

    [Fact]
    public void Rows_show_then_now_and_change()
    {
        var c = ReportComparer.Compare(Doc(Now, batteries: [Battery(85, cycles: 260)]), Exported(Doc(Now.AddDays(-30), batteries: [Battery(90, cycles: 200)])));

        var rows = c.Rows();

        Assert.Contains(new ComparisonRow("Health", "90.0%", "85.0%", "-5.0 points"), rows);
        Assert.Contains(new ComparisonRow("Full charge", "90,000 mWh", "85,000 mWh", "-5,000 mWh"), rows);
        Assert.Contains(new ComparisonRow("Charge cycles", "200", "260", "+60"), rows);
        Assert.Contains(rows, r => r.Label == "Verdict");
    }

    [Fact]
    public void No_monthly_rate_for_reports_less_than_two_weeks_apart()
    {
        var c = ReportComparer.Compare(Doc(Now, batteries: [Battery(85)]), Exported(Doc(Now.AddDays(-5), batteries: [Battery(86)])));

        Assert.Equal(-1.0, c.Batteries[0].HealthChange);
        Assert.Null(c.Batteries[0].HealthChangePerMonth);
        Assert.EndsWith("(5 days ago): health 86% -> 85% (down 1.0 point).", c.Summary);
    }

    [Fact]
    public void Small_changes_read_as_about_the_same()
    {
        var c = ReportComparer.Compare(Doc(Now, batteries: [Battery(85.2)]), Exported(Doc(Now.AddDays(-40), batteries: [Battery(85)])));

        Assert.Contains("(about the same)", c.Summary);
    }

    [Fact]
    public void New_battery_in_the_same_laptop_is_reported_as_replaced()
    {
        var before = Exported(Doc(Now.AddDays(-90), batteries: [Battery(52, serial: "OLD-BAT")]));
        var now = Doc(Now, batteries: [Battery(99, serial: "NEW-BAT", cycles: 3)]);

        var c = ReportComparer.Compare(now, before);

        Assert.Equal(ComparisonMatch.BatteryReplaced, c.Match);
        var b = Assert.Single(c.Batteries);
        Assert.Equal(52.0, b.HealthBefore);
        Assert.Equal(99.0, b.HealthNow);
        Assert.Null(b.HealthChange);
        Assert.Null(b.CyclesChange);
        Assert.Equal("The battery has been replaced since 2026-08-16 (3 months ago): the old one was at 52% health, this one is at 99%.", c.Summary);
    }

    [Fact]
    public void Different_laptop_does_not_match()
    {
        var before = Exported(Doc(Now.AddDays(-10), laptopSerial: "OTHER-SN", Battery(70, serial: "OTHER-BAT")));

        var c = ReportComparer.Compare(Doc(Now, batteries: [Battery(85)]), before);

        Assert.Equal(ComparisonMatch.NoMatch, c.Match);
        Assert.Empty(c.Batteries);
        Assert.Equal("The report from 2026-11-04 is for a different laptop (Laptop 14).", c.Summary);
    }

    [Fact]
    public void Battery_without_a_serial_is_matched_by_the_laptop_serial()
    {
        var before = Exported(Doc(Now.AddDays(-30), batteries: [Battery(90, serial: null)]));

        var c = ReportComparer.Compare(Doc(Now, batteries: [Battery(88, serial: null)]), before);

        Assert.Equal(ComparisonMatch.SameBattery, c.Match);
        Assert.False(c.Batteries[0].MatchedBySerial);
        Assert.Contains(c.Notes, n => n.Contains("matched by the laptop serial", StringComparison.Ordinal));
    }

    [Fact]
    public void Placeholder_serials_never_match()
    {
        Assert.False(ReportComparer.IsRealSerial("0000000"));
        Assert.False(ReportComparer.IsRealSerial("  "));
        Assert.False(ReportComparer.IsRealSerial("To be filled by O.E.M."));
        Assert.True(ReportComparer.IsRealSerial("FAKE-BAT-1"));

        // Same placeholder battery serial, different laptops: no match.
        var before = Exported(Doc(Now.AddDays(-30), laptopSerial: "OTHER-SN", Battery(90, serial: "0000000")));
        Assert.Equal(ComparisonMatch.NoMatch, ReportComparer.Compare(Doc(Now, batteries: [Battery(88, serial: "0000000")]), before).Match);
    }

    [Fact]
    public void Unknown_health_still_compares_capacity_when_units_match()
    {
        var before = Exported(Doc(Now.AddDays(-30), batteries: [MakeBattery(design: null, full: 40000) with { SerialNumber = "FAKE-BAT-1" }]));
        var now = Doc(Now, batteries: [MakeBattery(design: null, full: 38000) with { SerialNumber = "FAKE-BAT-1" }]);

        var c = ReportComparer.Compare(now, before);

        Assert.Null(c.Batteries[0].HealthChange);
        Assert.Equal(-2000, c.Batteries[0].FullChargeChange);
        Assert.Contains("full charge -2,000 mWh (health could not be measured)", c.Summary);
    }

    [Fact]
    public void Capacity_change_is_skipped_when_units_differ()
    {
        var before = Exported(Doc(Now.AddDays(-30), batteries: [MakeBattery(full: 90, relative: true) with { SerialNumber = "FAKE-BAT-1" }]));

        var c = ReportComparer.Compare(Doc(Now, batteries: [Battery(88)]), before);

        Assert.Null(c.Batteries[0].FullChargeChange);
        Assert.Null(c.Batteries[0].HealthChange);
    }

    [Fact]
    public void Two_batteries_are_matched_by_serial_not_position()
    {
        var before = Exported(Doc(Now.AddDays(-30), batteries: [Battery(90, serial: "BAT-A", index: 1), Battery(80, serial: "BAT-B", index: 2)]));
        var now = Doc(Now, batteries: [Battery(78, serial: "BAT-B", index: 1), Battery(89, serial: "BAT-A", index: 2)]);

        var c = ReportComparer.Compare(now, before);

        Assert.Equal(2, c.Batteries.Count);
        Assert.Equal(80.0, c.Batteries.Single(b => b.Index == 1).HealthBefore);
        Assert.Equal(90.0, c.Batteries.Single(b => b.Index == 2).HealthBefore);
        Assert.StartsWith("Since 2026-10-15 (30 days ago): Battery 1: health 80% -> 78%", c.Summary);
    }

    [Fact]
    public void Reader_accepts_minimal_reports_and_rejects_other_json()
    {
        var minimal = PreviousReportReader.Read("""{ "generatedAt": "2026-10-01T10:00:00+00:00", "snapshot": { "batteries": [ { "fullChargeCapacity": 40000 } ] }, "futureField": [1, 2] }""");
        Assert.Equal(new DateTimeOffset(2026, 10, 1, 10, 0, 0, TimeSpan.Zero), minimal.GeneratedAt);
        Assert.Equal(1, minimal.Batteries[0].Index);
        Assert.Equal(40000, minimal.Batteries[0].FullChargeCapacity);
        Assert.Null(minimal.LaptopSerial);

        Assert.Throws<InvalidDataException>(() => PreviousReportReader.Read("""{ "name": "something else" }"""));
        Assert.Throws<InvalidDataException>(() => PreviousReportReader.Read("not json"));
    }

    [Fact]
    public void Text_summary_and_html_include_the_comparison()
    {
        var now = Doc(Now, batteries: [Battery(85)]);
        var doc = now with { Comparison = ReportComparer.Compare(now, Exported(Doc(Now.AddDays(-60), batteries: [Battery(90)]))) };

        var text = TextSummary.Create(doc);
        var html = HtmlReportWriter.Render(doc);

        Assert.Contains("Compared: Since 2026-09-15", text);
        Assert.InRange(text.Split('\n').Length, 5, 8);
        Assert.Contains("Compared with the report from 2026-09-15", html);
        Assert.Contains("-5.0 points", html);
    }

    [Fact]
    public void Html_leaves_out_a_comparison_that_did_not_match()
    {
        var now = Doc(Now, batteries: [Battery(85)]);
        var doc = now with { Comparison = ReportComparer.Compare(now, Exported(Doc(Now.AddDays(-60), laptopSerial: "OTHER-SN", Battery(90, serial: "X-1")))) };

        Assert.DoesNotContain("Compared with the report", HtmlReportWriter.Render(doc));
        Assert.DoesNotContain("Compared:", TextSummary.Create(doc));
    }

    [Fact]
    public void Finder_picks_the_latest_earlier_report_for_this_laptop()
    {
        var folder = Directory.CreateTempSubdirectory("unveil-compare-").FullName;
        try
        {
            var sub = Directory.CreateDirectory(Path.Combine(folder, "Reports", "Client A")).FullName;
            JsonReportWriter.Write(Doc(Now.AddDays(-90), batteries: [Battery(92)]), Path.Combine(folder, "unveil-a-old.json"));
            JsonReportWriter.Write(Doc(Now.AddDays(-30), batteries: [Battery(89)]), Path.Combine(sub, "unveil-a-newer.json"));
            JsonReportWriter.Write(Doc(Now.AddDays(-1), laptopSerial: "OTHER-SN", Battery(50, serial: "OTHER-BAT")), Path.Combine(folder, "unveil-b.json"));
            JsonReportWriter.Write(Doc(Now.AddMinutes(-5), batteries: [Battery(85)]), Path.Combine(folder, "unveil-this-session.json"));
            File.WriteAllText(Path.Combine(folder, "unveil-broken.json"), "{ not json");
            File.WriteAllText(Path.Combine(folder, "unveil.json"), """{ "health": { "goodPercent": 80 } }""");

            var log = new CollectionLog();
            var found = PreviousReportFinder.FindLatest([(folder, 2)], Doc(Now, batteries: [Battery(85)]), before: Now.AddMinutes(-10), log);

            Assert.NotNull(found);
            Assert.Equal("unveil-a-newer.json", found.Value.Report.FileName);
            Assert.Equal(-4.0, found.Value.Comparison.Batteries[0].HealthChange);
            Assert.Contains(log.Entries, e => e.Message.Contains("unveil-a-newer.json", StringComparison.Ordinal));

            // Without searching subfolders, only the older report is found.
            var shallow = PreviousReportFinder.FindLatest([(folder, 0)], Doc(Now, batteries: [Battery(85)]), before: Now.AddMinutes(-10));
            Assert.Equal("unveil-a-old.json", shallow?.Report.FileName);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void Finder_copes_with_missing_folders()
    {
        Assert.Null(PreviousReportFinder.FindLatest([(@"Z:\does\not\exist", 2), ("", 0)], Doc(Now, batteries: [Battery(85)]), Now));
    }
}
