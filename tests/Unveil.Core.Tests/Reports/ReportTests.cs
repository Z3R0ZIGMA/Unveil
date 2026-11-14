using System.Text.Json;
using Unveil.Core.Battery;
using Unveil.Core.Reports;
using Unveil.Core.Verdict;
using static Unveil.Core.Tests.TestData;

namespace Unveil.Core.Tests.Reports;

public class ReportTests
{
    private static ReportDocument AsusDocument()
    {
        var report = AsusReport();
        var battery = BatteryMerger.Merge([], report.Batteries, new CollectionLog()).Single();
        return ReportDocument.Create(Snapshot([battery], report), generatedAt: Now);
    }

    // ---------------------------------------------------------------- JSON

    [Fact]
    public void Json_round_trips_the_key_data()
    {
        var doc = AsusDocument();

        var back = JsonReportWriter.Read(JsonReportWriter.Serialize(doc));

        Assert.Equal(ReportDocument.CurrentSchemaVersion, back.SchemaVersion);
        Assert.Equal(VerdictKind.Healthy, back.Verdict.Kind);
        Assert.Equal(87.1, back.Snapshot.Batteries[0].Health.HealthPercent);
        Assert.Equal(42067, back.Snapshot.Batteries[0].DesignCapacity);
        Assert.Equal("FAKE-SN-1", back.Snapshot.System.SerialNumber);
        Assert.Equal(96, back.Snapshot.Report!.CapacityHistory.Count);
        Assert.Equal(doc.Usage.InUse!.EnergyUsed, back.Usage.InUse!.EnergyUsed);
    }

    [Fact]
    public void Json_uses_camel_case_and_readable_enums_and_leaves_out_raw_xml()
    {
        var json = JsonReportWriter.Serialize(AsusDocument());
        using var parsed = JsonDocument.Parse(json);
        var root = parsed.RootElement;

        Assert.Equal(1, root.GetProperty("schemaVersion").GetInt32());
        Assert.Equal("healthy", root.GetProperty("verdict").GetProperty("kind").GetString());
        Assert.Equal("good", root.GetProperty("verdict").GetProperty("severity").GetString());
        Assert.False(root.GetProperty("snapshot").TryGetProperty("raw", out _));
        Assert.DoesNotContain("<BatteryReport", json);
    }

    [Fact]
    public void File_stem_is_safe_and_descriptive()
    {
        Assert.Equal("unveil-laptop-14-20261114-080000", ReportDocument.Create(Snapshot(WithHealth(90)), generatedAt: Now).FileStem);
    }

    // ---------------------------------------------------------------- text summary

    [Fact]
    public void Summary_is_five_to_eight_lines_with_the_verdict()
    {
        var text = TextSummary.Create(AsusDocument());
        var lines = text.Split('\n');

        Assert.InRange(lines.Length, 5, 8);
        Assert.StartsWith("Unveil battery check", lines[0]);
        Assert.Contains("S/N FAKE-SN-1", text);
        Assert.Contains("87.1% health (36,628 / 42,067 mWh), 242 cycles", text);
        Assert.Contains("Verdict: BATTERY HEALTHY", text);
        Assert.Contains("Runtime: about 3 h 17 m on a full charge (3 h 47 m when new)", text);
    }

    [Fact]
    public void Summary_lists_each_battery_when_there_are_two()
    {
        var doc = ReportDocument.Create(Snapshot(WithHealth(92, index: 1), WithHealth(55, index: 2)), generatedAt: Now);
        var text = TextSummary.Create(doc);

        Assert.Contains("Batteries: 2", text);
        Assert.Contains("#1 TEST-BATTERY, 92.0% health", text);
        Assert.Contains("#2 TEST-BATTERY, 55.0% health", text);
        Assert.InRange(text.Split('\n').Length, 5, 8);
    }

    // ---------------------------------------------------------------- HTML

    [Fact]
    public void Html_contains_the_verdict_figures_and_chart()
    {
        var html = HtmlReportWriter.Render(AsusDocument());

        Assert.StartsWith("<!doctype html>", html);
        Assert.Contains("<h1>Battery healthy</h1>", html);
        Assert.Contains(">87%<", html);
        Assert.Contains("class=\"chart\"", html);
        Assert.Contains("Capacity history table (96 periods)", html);
        Assert.Contains("Serial number FAKE-SN-1", html);
    }

    [Fact]
    public void Html_is_self_contained_with_no_scripts_or_network()
    {
        var html = HtmlReportWriter.Render(AsusDocument());

        Assert.DoesNotContain("<script", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<link", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("http://", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("https://", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("@import", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Html_uses_the_theme_colours_and_has_a_print_stylesheet()
    {
        var html = HtmlReportWriter.Render(AsusDocument());

        foreach (var colour in new[] { "#1e1f22", "#2b2d31", "#3a3d43", "#e6e6e6", "#8b8f97", "#3b82f6", "#ef4444", "#22c55e", "#f59e0b" })
        {
            Assert.Contains(colour, html);
        }

        Assert.Contains("@media print", html);
    }

    [Fact]
    public void Html_never_shows_status_by_colour_alone()
    {
        var html = HtmlReportWriter.Render(ReportDocument.Create(Snapshot(WithHealth(52)), generatedAt: Now));

        Assert.Contains("<div class=\"verdict-label\">Action needed</div>", html);
        Assert.Contains("<h1>Replace battery</h1>", html);
        Assert.Contains("Replace</div>", html); // gauge word
    }

    [Fact]
    public void Html_escapes_text_from_the_machine()
    {
        var evil = MakeBattery(name: "<script>alert(1)</script>");
        var html = HtmlReportWriter.Render(ReportDocument.Create(Snapshot(evil), generatedAt: Now));

        Assert.DoesNotContain("<script>alert(1)</script>", html);
        Assert.Contains("&lt;script&gt;alert(1)&lt;/script&gt;", html);
    }

    [Fact]
    public void Html_for_no_battery_renders_without_figures()
    {
        var html = HtmlReportWriter.Render(ReportDocument.Create(Snapshot([], pcSystemType: 1), generatedAt: Now));

        Assert.Contains("<h1>No battery detected</h1>", html);
        Assert.DoesNotContain("class=\"gauge", html);
    }

    [Fact]
    public void Html_for_unknown_health_shows_question_mark_and_reason()
    {
        var html = HtmlReportWriter.Render(ReportDocument.Create(Snapshot(MakeBattery(design: 0)), generatedAt: Now));

        Assert.Contains(">?<", html);
        Assert.Contains("Design capacity reported as 0", html);
        Assert.Contains("<h1>Inconclusive</h1>", html);
    }
}

public class ReportWithDrainTestTests
{
    private static Unveil.Core.Power.DrainTestResult Test()
    {
        var apps = new[]
        {
            new Unveil.Core.Power.AppUsage
            {
                Name = "TiWorker", DisplayName = "Windows Modules Installer Worker", ProcessCount = 1, CpuPercent = 12, SharePercent = 70, Impact = 7.2,
                Offender = Unveil.Core.Power.KnownOffenders.Match("TiWorker", 1),
            },
            new Unveil.Core.Power.AppUsage { Name = "chrome", DisplayName = "Google Chrome", ProcessCount = 12, CpuPercent = 3, SharePercent = 30, Impact = 1.8 },
        };
        return new Unveil.Core.Power.DrainTestResult
        {
            Activity = new Unveil.Core.Power.ActivityResult { Duration = TimeSpan.FromSeconds(60), SnapshotCount = 31, GpuMeasured = true, Apps = apps },
            Measurement = new DrainMeasurement
            {
                AverageWatts = 11.2, MinWatts = 9.8, MaxWatts = 13.1, Duration = TimeSpan.FromSeconds(60), SampleCount = 30,
                TopApps = [new AppImpact("Windows Modules Installer Worker", 70, 7.2, "Windows Update", "Let it finish."), new AppImpact("Google Chrome", 30, 1.8)],
            },
        };
    }

    [Fact]
    public void Report_includes_the_test_and_scores_with_the_chosen_laptop_type()
    {
        var doc = ReportDocument.Create(Snapshot(WithHealth(90)), Test(), VerdictSettings.Default.For(MachineClass.Standard), Now);

        Assert.Equal("Standard laptop", doc.DrainProfile);
        Assert.Equal(VerdictKind.SoftwareDrain, doc.Verdict.Kind);
        Assert.NotNull(doc.Activity);
    }

    [Fact]
    public void Html_lists_the_apps_with_offender_labels()
    {
        var html = HtmlReportWriter.Render(ReportDocument.Create(Snapshot(WithHealth(90)), Test(), null, Now));

        Assert.Contains("Apps using the most power", html);
        Assert.Contains("Windows Modules Installer Worker", html);
        Assert.Contains("Google Chrome <span class=\"muted\">(12 processes)</span>", html);
        Assert.Contains(">Windows Update</div>", html);
        Assert.Contains("11.2 W average over 1 m (30 readings, 9.8 W to 13.1 W)", html);
    }

    [Fact]
    public void Summary_mentions_the_drain_test_and_stays_within_eight_lines()
    {
        var text = TextSummary.Create(ReportDocument.Create(Snapshot(WithHealth(90)), Test(), null, Now));

        Assert.Contains("Drain test: 11.2 W average over 1 m; top apps: Windows Modules Installer Worker 70%, Google Chrome 30%", text);
        Assert.InRange(text.Split('\n').Length, 5, 8);
    }

    [Fact]
    public void Json_round_trips_the_drain_test()
    {
        var doc = ReportDocument.Create(Snapshot(WithHealth(90)), Test(), null, Now);
        var back = JsonReportWriter.Read(JsonReportWriter.Serialize(doc));

        Assert.Equal(11.2, back.Drain!.AverageWatts);
        Assert.Equal(2, back.Activity!.Apps.Count);
        Assert.Equal("Windows Update", back.Activity.Apps[0].Offender?.Category);
    }
}
