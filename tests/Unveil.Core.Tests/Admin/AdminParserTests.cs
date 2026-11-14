using System.Xml.Linq;
using Unveil.Core.Admin;

namespace Unveil.Core.Tests.Admin;

public class SrumParserTests
{
    /// <summary>Scan time of the real samples (the SRUM data ends at 08:02 UTC).</summary>
    private static readonly DateTimeOffset Now = new(2026, 11, 14, 8, 3, 33, TimeSpan.Zero);

    private static SrumSummary Asus()
    {
        using var reader = new StreamReader(TestFiles.Sample("srum", "asus-expertbook-b1403cva.csv"));
        return SrumParser.Summarize(reader, Now);
    }

    [Fact]
    public void Real_asus_sample_last_24_hours()
    {
        // Expected values computed independently (Python) from the sample.
        var day = Asus().Last24Hours!;

        Assert.True(day.OnBatteryOnly);
        Assert.Equal(6755682, day.TotalEnergy);
        Assert.Equal(262, day.AppCount);
        Assert.Equal(50.8672, day.UnassignedPercent, 3);
        Assert.Equal(SrumParser.TopApps, day.Apps.Count);
        Assert.Equal(["Unveil", "Claude", "Unbound", "System"], day.Apps.Take(4).Select(a => a.Name));
        Assert.Equal(15.8470, day.Apps[0].SharePercent, 3);
        Assert.Equal(1070573, day.Apps[0].Energy);
    }

    [Fact]
    public void Real_asus_sample_whole_period()
    {
        var summary = Asus();
        var week = summary.Last7Days!;

        Assert.Equal(new DateTimeOffset(2026, 11, 9, 5, 50, 24, TimeSpan.Zero), summary.DataFrom);
        Assert.Equal(new DateTimeOffset(2026, 11, 14, 8, 2, 0, TimeSpan.Zero), summary.DataTo);
        Assert.Equal(18508162, week.TotalEnergy);
        Assert.Equal(286, week.AppCount);
        Assert.Equal("AnyDesk", week.Apps[0].Name);
        Assert.Equal(31.0512, week.Apps[0].SharePercent, 3);
        Assert.Equal("chrome", week.Apps[1].Name);
        Assert.Equal(0.5260, week.Apps.Single(a => a.Name == "Claude").BackgroundPercent, 3);
    }

    [Fact]
    public void Packaged_apps_are_checked_against_known_offenders()
    {
        var teams = Asus().Last7Days!.Apps.Concat(Asus().Last24Hours!.Apps).FirstOrDefault(a => a.Name == "MSTeams");

        // Teams is in the sample; if it made the top list it must carry the offender label.
        if (teams is not null)
        {
            Assert.Equal("Microsoft Teams", teams.Offender?.Category);
        }

        Assert.Equal("Microsoft Teams", Unveil.Core.Power.KnownOffenders.Match("MSTeams", 1)?.Category);
    }

    [Fact]
    public void Energy_meters_and_unknown_are_not_apps()
    {
        var names = Asus().Last7Days!.Apps.Select(a => a.Name).ToList();

        Assert.DoesNotContain(names, n => n.StartsWith("EMI_", StringComparison.Ordinal));
        Assert.DoesNotContain("Unknown", names);
    }

    [Theory]
    [InlineData(@"\Device\HarddiskVolume3\Program Files\Google\Chrome\Application\chrome.exe", "chrome", "chrome")]
    [InlineData(@"\Device\HarddiskVolume3\Windows\System32\svchost.exe [DcomLaunch]", "svchost (DcomLaunch)", "svchost")]
    [InlineData(@"\Device\HarddiskVolume3\Windows\System32\svchost.exe [osprivacy] [camsvc]", "svchost (osprivacy, camsvc)", "svchost")]
    [InlineData(@"\Device\HarddiskVolume3\Windows\System32\svchost.exe [LocalSystemNetworkRestricted] [PrintDeviceConfigurationS", "svchost (LocalSystemNetworkRestricted, PrintDeviceConfigurationS)", "svchost")]
    [InlineData(@"\Device\HarddiskVolume3\Program Files (x86)\AnyDesk\AnyDesk", "AnyDesk", "AnyDesk")]
    [InlineData(@"\Device\HarddiskVolume3\Windows\System32\chcp.com", "chcp", "chcp")]
    [InlineData("Microsoft.OutlookForWindows_1.2026.1026.100_x64__8wekyb3d8bbwe", "Microsoft.OutlookForWindows", null)]
    [InlineData("Microsoft.AAD.BrokerPlugin_1000.19580.1000.2_neutral_neutral_cw5n1h2txyewy", "Microsoft.AAD.BrokerPlugin", null)]
    [InlineData("Claude_5.11492.2.0_x64__pzs8sxrjxfjjc", "Claude", null)]
    [InlineData("5319275A.WhatsAppDesktop_2.2644.101.0_x64__cv1g1gvanyjgm", "WhatsAppDesktop", null)]
    [InlineData("B9ECED6F.ASUSPCAssistant_4.0.82.0_x64__qmba6cd70vzyy", "ASUSPCAssistant", null)]
    [InlineData("MemCompression", "MemCompression", null)]
    [InlineData("System Interrupts", "System Interrupts", null)]
    public void App_ids_become_readable_names(string appId, string name, string? exe)
    {
        Assert.Equal((name, exe), SrumParser.DisplayName(appId));
    }

    private const string Header =
        "AppId, UserId, TimeStamp, OnBattery, ScreenOn, BatterySaverActive, LowPowerEpochActive, Foreground, InteractivityState, Container, Committed, TimeInMSec, MeasuredBitmap, EnergyLoss, CPUEnergyConsumption, SocEnergyConsumption, DisplayEnergyConsumption, DiskEnergyConsumption, NetworkEnergyConsumption, MBBEnergyConsumption, NPUEnergyConsumption, OtherEnergyConsumption, EmiEnergyConsumption, CPUEnergyConsumptionWorkOnBehalf, CPUEnergyConsumptionAttributed, TotalEnergyConsumption";

    private static string Row(string app, string time, bool battery, bool foreground, int cpu, int display, int network, int total) =>
        $"{app}, S-1-5-18, {time}, {(battery ? "TRUE" : "FALSE")}, TRUE, FALSE, FALSE, {(foreground ? "TRUE" : "FALSE")}, NotUnique, FALSE, TRUE, 1000, b0, 0, {cpu}, 0, {display}, 0, {network}, 0, 0, 0, 0, 0, 0, {total}";

    [Fact]
    public void Plugged_in_only_data_falls_back_to_all_time()
    {
        var csv = string.Join("\n", Header,
            Row(@"\Device\X\a.exe", "2026-11-14T07:00:00.0000", battery: false, foreground: true, 10, 0, 0, 10),
            Row(@"\Device\X\b.exe", "2026-11-14T07:00:00.0000", battery: false, foreground: false, 30, 0, 0, 30));

        var day = SrumParser.Summarize(new StringReader(csv), Now).Last24Hours!;

        Assert.False(day.OnBatteryOnly);
        Assert.Equal(["b", "a"], day.Apps.Select(a => a.Name));
        Assert.Equal(75.0, day.Apps[0].SharePercent);
        Assert.Equal(100.0, day.Apps[0].BackgroundPercent);
    }

    [Fact]
    public void Battery_rows_win_and_main_component_is_reported()
    {
        var csv = string.Join("\n", Header,
            Row(@"\Device\X\video.exe", "2026-11-14T07:00:00.0000", battery: true, foreground: true, 10, 60, 0, 70),
            Row(@"\Device\X\sync.exe", "2026-11-14T07:00:00.0000", battery: true, foreground: false, 5, 0, 25, 30),
            Row(@"\Device\X\plugged.exe", "2026-11-14T07:00:00.0000", battery: false, foreground: true, 500, 0, 0, 500),
            Row("Unknown", "2026-11-14T07:00:00.0000", battery: true, foreground: false, 0, 0, 0, 100),
            Row("EMI_RAPL_Package0_PKG", "2026-11-14T07:00:00.0000", battery: true, foreground: false, 0, 0, 0, 9999));

        var day = SrumParser.Summarize(new StringReader(csv), Now).Last24Hours!;

        Assert.True(day.OnBatteryOnly);
        Assert.Equal(100, day.TotalEnergy);
        Assert.Equal(50.0, day.UnassignedPercent);
        Assert.Equal("video", day.Apps[0].Name);
        Assert.Equal("Display", day.Apps[0].MainComponent);
        Assert.Equal("Network", day.Apps[1].MainComponent);
    }

    [Fact]
    public void Old_rows_are_outside_the_24_hour_window()
    {
        var csv = string.Join("\n", Header,
            Row(@"\Device\X\old.exe", "2026-11-12T07:00:00.0000", true, true, 10, 0, 0, 10),
            Row(@"\Device\X\new.exe", "2026-11-14T07:00:00.0000", true, true, 10, 0, 0, 10));

        var summary = SrumParser.Summarize(new StringReader(csv), Now);

        Assert.Equal(["new"], summary.Last24Hours!.Apps.Select(a => a.Name));
        Assert.Equal(2, summary.Last7Days!.Apps.Count);
    }

    [Fact]
    public void App_id_containing_a_comma_is_joined_back()
    {
        var csv = string.Join("\n", Header, Row(@"\Device\X\My, Company\tool.exe", "2026-11-14T07:00:00.0000", true, true, 1, 0, 0, 1));

        Assert.Equal("tool", SrumParser.Summarize(new StringReader(csv), Now).Last24Hours!.Apps.Single().Name);
    }

    [Fact]
    public void Unexpected_format_is_logged_and_skipped()
    {
        var log = new CollectionLog();

        var summary = SrumParser.Summarize(new StringReader("Foo, Bar\n1, 2"), Now, log);

        Assert.Null(summary.Last24Hours);
        Assert.Contains(log.Entries, e => e.Message.Contains("unexpected format"));
    }

    [Fact]
    public void Empty_input_gives_an_empty_summary()
    {
        Assert.Null(SrumParser.Summarize(new StringReader(""), Now).Last7Days);
    }
}

public class SleepStudyParserTests
{
    private static SleepStudy Asus() =>
        SleepStudyParser.Parse(XDocument.Load(TestFiles.Sample("sleepstudy", "asus-expertbook-b1403cva.xml")));

    [Fact]
    public void Real_asus_sessions()
    {
        // Expected values computed independently (Python) from the sample.
        var sleep = Asus();

        Assert.True(sleep.ModernStandby);
        Assert.Equal(3, sleep.Sessions.Count);

        var first = sleep.Sessions[0];
        Assert.Equal(new DateTimeOffset(2026, 11, 9, 14, 40, 29, TimeSpan.Zero), first.Start);
        Assert.Equal(0.7158, first.Duration.TotalHours, 3);
        Assert.False(first.OnAc);
        Assert.Equal(0, first.EnergyUsed);
        Assert.Equal(97.76, first.HardwareLowPowerPercent!.Value, 2);
        Assert.Equal(@"Intel RST VMD Controller A77F (\_SB.PC00.VMD0)", first.TopBlockers[0].Name);
        Assert.Equal("Fx Device", first.TopBlockers[0].Type);

        Assert.Equal(201, sleep.Sessions[1].EnergyUsed);
        Assert.Equal(1.4266, sleep.Sessions[1].PercentPerHour!.Value, 3);
        Assert.Equal(95.02, sleep.Sessions[2].HardwareLowPowerPercent!.Value, 2);
    }

    [Fact]
    public void Only_battery_sessions_of_30_minutes_or_more_are_judged()
    {
        var sleep = Asus();

        var judged = Assert.Single(sleep.BatterySessions); // 43 min; the other two are 23 and 12 min
        Assert.Equal(0.0, judged.PercentPerHour);
        Assert.Equal(0.0, sleep.AveragePercentPerHour);
        Assert.False(sleep.IsEnoughToJudge); // 43 minutes is under the 2-hour minimum
        Assert.Null(sleep.JudgedPercentPerHour);
    }

    [Fact]
    public void Average_is_weighted_by_time()
    {
        var full = 40000;
        var sleep = new SleepStudy
        {
            Sessions =
            [
                new SleepSession { Duration = TimeSpan.FromHours(1), EnergyUsed = 400, FullChargeCapacity = full },  // 1 %/h
                new SleepSession { Duration = TimeSpan.FromHours(3), EnergyUsed = 3600, FullChargeCapacity = full }, // 3 %/h
                new SleepSession { Duration = TimeSpan.FromHours(5), EnergyUsed = 0, FullChargeCapacity = full, OnAc = true },
            ],
        };

        Assert.Equal(2.5, sleep.AveragePercentPerHour!.Value, 6);
        Assert.Equal(TimeSpan.FromHours(4), sleep.BatterySleepTime);
    }

    [Fact]
    public void Wrong_root_throws()
    {
        Assert.Throws<Unveil.Core.Battery.BatteryReportParseException>(() => SleepStudyParser.Parse(XDocument.Parse("<Other/>")));
    }
}

public class EnergyDiagnosticsParserTests
{
    private static EnergyDiagnostics Asus() =>
        EnergyDiagnosticsParser.Parse(XDocument.Load(TestFiles.Sample("energy", "asus-expertbook-b1403cva.xml")));

    [Fact]
    public void Real_asus_counts_and_grouping()
    {
        var energy = Asus();

        Assert.Equal(7, energy.ErrorCount);
        Assert.Equal(14, energy.WarningCount);
        Assert.Equal(47, energy.InformationalCount);
        Assert.Equal(TimeSpan.FromSeconds(30), energy.Duration);

        // 7 different errors + the 14 "process with significant utilization" warnings grouped into one.
        Assert.Equal(8, energy.Findings.Count);
        Assert.All(energy.Findings.Take(7), f => Assert.Equal(EnergySeverity.Error, f.Severity));
        var processes = energy.Find(EnergyDiagnostics.Known.ProcessWithHighUtilization)!;
        Assert.Equal(14, processes.Occurrences.Count);
        Assert.Equal("svchost.exe", processes.Occurrences[0][0].Value);
    }

    [Fact]
    public void Known_checks_are_found_by_guid()
    {
        var energy = Asus();

        Assert.NotNull(energy.Find(EnergyDiagnostics.Known.DisplayTimeoutDisabledOnBattery));
        Assert.NotNull(energy.Find(EnergyDiagnostics.Known.SleepTimeoutDisabledOnBattery));
        var usb = energy.Find(EnergyDiagnostics.Known.UsbNotSuspending)!;
        Assert.Equal("USB Input Device", usb.Occurrences[0][0].Value);
    }

    [Theory]
    [InlineData("Error", EnergySeverity.Error)]
    [InlineData(" warning ", EnergySeverity.Warning)]
    [InlineData("Informational", EnergySeverity.Informational)]
    [InlineData("Fehler", EnergySeverity.Warning)] // unknown (e.g. localised) severities are shown, not hidden
    [InlineData(null, EnergySeverity.Warning)]
    public void Severity_parsing(string? value, EnergySeverity expected)
    {
        Assert.Equal(expected, EnergyDiagnosticsParser.ParseSeverity(value));
    }
}
