using Unveil.Core.Admin;
using Unveil.Core.Reports;
using Unveil.Core.Rmm;
using Unveil.Core.Verdict;
using static Unveil.Core.Tests.TestData;

namespace Unveil.Core.Tests.Rmm;

public class RmmTests
{
    [Fact]
    public void Options_parse()
    {
        var o = RmmOptions.Parse(["--rmm", "--test-seconds", "30", "--energy", "--out", @"C:\R", "--result", @"C:\R\result.txt", "--keep", "10"]);

        Assert.Null(o.Error);
        Assert.Equal(30, o.TestSeconds);
        Assert.True(o.EnergyDiagnostics);
        Assert.Equal(@"C:\R", o.ReportFolder);
        Assert.Equal(@"C:\R\result.txt", o.ResultFile);
        Assert.Equal(10, o.Keep);
    }

    [Fact]
    public void Defaults_put_reports_in_programdata()
    {
        var o = RmmOptions.Parse(["--rmm"]);

        Assert.Null(o.Error);
        Assert.Null(o.TestSeconds); // unveil.json decides
        Assert.False(o.EnergyDiagnostics);
        Assert.EndsWith(@"Unveil\Reports", o.ReportFolder, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(30, o.Keep);
    }

    [Theory]
    [InlineData("--test-seconds", "abc")]
    [InlineData("--test-seconds", "900")]
    [InlineData("--keep", "0")]
    [InlineData("--bogus", "")]
    public void Bad_options_are_reported_not_guessed(string option, string value)
    {
        Assert.NotNull(RmmOptions.Parse(["--rmm", option, value]).Error);
    }

    [Theory]
    [InlineData(VerdictKind.Healthy, 0)]
    [InlineData(VerdictKind.Inconclusive, 0)]
    [InlineData(VerdictKind.NoBattery, 0)]
    [InlineData(VerdictKind.SoftwareDrain, 1001)]
    [InlineData(VerdictKind.BatteryWorn, 1002)]
    [InlineData(VerdictKind.ReplaceBattery, 1003)]
    [InlineData(VerdictKind.Both, 1004)]
    public void Exit_codes_pass_or_fail_above_1000(VerdictKind kind, int expected)
    {
        Assert.Equal(expected, RmmExitCodes.For(kind));
    }

    [Fact]
    public void Summary_line_has_the_essentials()
    {
        var doc = ReportDocument.Create(Snapshot(WithHealth(66, cycles: 442)), new Unveil.Core.Power.DrainTestResult
        {
            Measurement = new DrainMeasurement { AverageWatts = 29.5, Duration = TimeSpan.FromMinutes(1), TopApps = [new AppImpact("Adobe Collaboration Synchronizer", 31, 5)] },
            Activity = new Unveil.Core.Power.ActivityResult(),
        }, generatedAt: Now);
        doc = doc with { Admin = new AdminData { IsAdministrator = true, Sleep = new SleepStudy { Sessions = [] } } };

        var line = RmmSummary.Line(doc, "unveil-laptop-14-20261114-080000.html");

        Assert.StartsWith("Battery worn and software drain: 66% health, 442 cycles.", line);
        Assert.Contains("Test: 29.5 W on battery; top: Adobe Collaboration Synchronizer 31%.", line);
        Assert.EndsWith("Report: unveil-laptop-14-20261114-080000.html", line);
        Assert.True(line.Length <= RmmSummary.MaxLength);
    }

    [Fact]
    public void Summary_line_never_exceeds_255_characters()
    {
        var longName = new string('x', 300);
        var doc = ReportDocument.Create(Snapshot(WithHealth(90)), new Unveil.Core.Power.DrainTestResult
        {
            Measurement = new DrainMeasurement { TopApps = [new AppImpact(longName, 50, 5)] },
            Activity = new Unveil.Core.Power.ActivityResult(),
        }, generatedAt: Now);

        var line = RmmSummary.Line(doc, "unveil-laptop-14-20261114-080000.html");

        Assert.True(line.Length <= RmmSummary.MaxLength);
        Assert.StartsWith("Software/settings issue: 90% health", line); // one app had half the power
        Assert.Contains("Report: unveil-laptop-14-20261114-080000.html", line); // the long app part is dropped, not the report
    }

    [Fact]
    public void Plugged_in_test_says_apps_only()
    {
        var doc = ReportDocument.Create(Snapshot(WithHealth(90)), new Unveil.Core.Power.DrainTestResult
        {
            Measurement = new DrainMeasurement { PluggedIn = true },
            Activity = new Unveil.Core.Power.ActivityResult(),
        }, generatedAt: Now);

        Assert.Contains("Test: plugged in, apps only.", RmmSummary.Line(doc, null));
    }

    [Theory]
    [InlineData(0.2, 0.0, false)]  // same day, no change: left out
    [InlineData(0.2, -1.0, true)]  // same day, a real change: kept
    [InlineData(7, 0.0, true)]     // a week later, stable: kept ("0.0 pts" is news)
    public void Comparison_is_left_out_when_it_says_nothing(double days, double change, bool shown)
    {
        var doc = ReportDocument.Create(Snapshot(WithHealth(88)), generatedAt: Now) with
        {
            Comparison = new ReportComparison
            {
                Match = ComparisonMatch.SameBattery,
                PreviousGeneratedAt = Now.AddDays(-days),
                Days = days,
                Batteries = [new BatteryChange { Index = 1, HealthChange = change }],
                Summary = "",
            },
        };

        Assert.Equal(shown, RmmSummary.Line(doc, null).Contains("pts.", StringComparison.Ordinal));
    }

    [Fact]
    public void Retention_keeps_the_newest_runs()
    {
        var folder = Directory.CreateTempSubdirectory("unveil-keep-").FullName;
        try
        {
            for (var i = 0; i < 5; i++)
            {
                foreach (var ext in new[] { ".html", ".json" })
                {
                    var path = Path.Combine(folder, $"unveil-laptop-2026111{i}-080000{ext}");
                    File.WriteAllText(path, "x");
                    File.SetLastWriteTimeUtc(path, new DateTime(2026, 11, 10 + i, 8, 0, 0, DateTimeKind.Utc));
                }
            }

            File.WriteAllText(Path.Combine(folder, "notes.txt"), "keep me");

            var deleted = ReportRetention.Prune(folder, keep: 2);

            Assert.Equal(6, deleted);
            Assert.Equal(["notes.txt", "unveil-laptop-20261113-080000.html", "unveil-laptop-20261113-080000.json",
                          "unveil-laptop-20261114-080000.html", "unveil-laptop-20261114-080000.json"],
                Directory.GetFiles(folder).Select(Path.GetFileName).Order(StringComparer.Ordinal));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void Bad_options_give_the_error_exit_code_and_a_line()
    {
        var file = Path.Combine(Path.GetTempPath(), $"unveil-result-{Guid.NewGuid():N}.txt");
        try
        {
            var outcome = RmmRunner.Run(RmmOptions.Parse(["--rmm", "--bogus"]) with { ResultFile = file });

            Assert.Equal(RmmExitCodes.Error, outcome.ExitCode);
            Assert.Equal("Unveil: Unknown option: --bogus", File.ReadAllText(file).Trim());
        }
        finally
        {
            File.Delete(file);
        }
    }
}
