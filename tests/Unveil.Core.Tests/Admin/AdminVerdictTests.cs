using System.Xml.Linq;
using Unveil.Core.Admin;
using Unveil.Core.Reports;
using Unveil.Core.Verdict;
using static Unveil.Core.Tests.TestData;

namespace Unveil.Core.Tests.Admin;

public class AdminVerdictTests
{
    private static SleepSession Session(double hours, double percentPerHour, double deepSleep = 95, string blocker = "Wi-Fi adapter") => new()
    {
        Start = Now.AddDays(-1),
        Duration = TimeSpan.FromHours(hours),
        FullChargeCapacity = 40000,
        EnergyUsed = (int)(40000 * percentPerHour / 100 * hours),
        HardwareLowPowerPercent = deepSleep,
        TopBlockers = [new SleepBlocker(blocker, "Fx Device", TimeSpan.FromMinutes(10), 5)],
    };

    private static AdminData Admin(SleepStudy? sleep = null, EnergyDiagnostics? energy = null, SrumSummary? srum = null) =>
        new() { IsAdministrator = true, CollectedAt = Now, Sleep = sleep, Energy = energy, Srum = srum };

    [Fact]
    public void Not_admin_is_reported_as_skipped_not_an_error()
    {
        var v = VerdictEngine.Evaluate(Snapshot(WithHealth(90)), admin: new AdminData { IsAdministrator = false });

        Assert.Equal(VerdictKind.Healthy, v.Kind);
        Assert.Contains(v.Evidence, f => f.Severity == Severity.Neutral && f.Text.Contains("need administrator rights"));
    }

    [Fact]
    public void Healthy_sleep_is_good_evidence()
    {
        var v = VerdictEngine.Evaluate(Snapshot(WithHealth(90)), admin: Admin(new SleepStudy { Sessions = [Session(8, 0.4)] }));

        Assert.Equal(VerdictKind.Healthy, v.Kind);
        Assert.Contains(v.Evidence, f => f.Severity == Severity.Good && f.Text.Contains("0.4% of a charge per hour"));
    }

    [Fact]
    public void High_sleep_drain_is_a_software_issue_even_without_the_live_test()
    {
        var v = VerdictEngine.Evaluate(Snapshot(WithHealth(90)), admin: Admin(new SleepStudy { Sessions = [Session(8, 3.0, blocker: "Bluetooth radio")] }));

        Assert.Equal(VerdictKind.SoftwareDrain, v.Kind);
        Assert.Equal(DrainStatus.Elevated, v.Drain);
        Assert.Contains("losing 3.0% of a charge per hour while asleep", v.Message);
        Assert.Contains(v.Actions, a => a.Contains("Bluetooth radio"));
        Assert.Contains(v.Actions, a => a.Contains("Run the full test")); // live test still not run
    }

    [Fact]
    public void Too_little_sleep_is_not_judged()
    {
        var v = VerdictEngine.Evaluate(Snapshot(WithHealth(90)), admin: Admin(new SleepStudy { Sessions = [Session(1, 5.0)] }));

        Assert.Equal(VerdictKind.Healthy, v.Kind);
        Assert.Contains(v.Evidence, f => f.Text.Contains("Sleep study: only 1 h 0 m of sleep on battery, too little to judge"));
    }

    [Fact]
    public void High_sleep_drain_with_a_worn_battery_is_both()
    {
        var v = VerdictEngine.Evaluate(Snapshot(WithHealth(70)), admin: Admin(new SleepStudy { Sessions = [Session(6, 2.5)] }));

        Assert.Equal(VerdictKind.Both, v.Kind);
    }

    [Fact]
    public void Shallow_deep_sleep_names_the_blocker()
    {
        var v = VerdictEngine.Evaluate(Snapshot(WithHealth(90)), admin: Admin(new SleepStudy { Sessions = [Session(8, 0.5, deepSleep: 40, blocker: "Intel Wi-Fi 6E")] }));

        Assert.Contains(v.Evidence, f => f.Severity == Severity.Warning && f.Text.Contains("deep sleep only 40%") && f.Text.Contains("Intel Wi-Fi 6E"));
        Assert.Contains(v.Actions, a => a.Contains("Intel Wi-Fi 6E"));
    }

    [Fact]
    public void Real_energy_report_turns_known_problems_into_actions()
    {
        var energy = EnergyDiagnosticsParser.Parse(XDocument.Load(TestFiles.Sample("energy", "asus-expertbook-b1403cva.xml")));
        var v = VerdictEngine.Evaluate(Snapshot(WithHealth(90)), admin: Admin(energy: energy));

        Assert.Contains(v.Evidence, f => f.Text == "Windows energy diagnostics found 7 errors and 14 warnings.");
        Assert.Contains(v.Actions, a => a.StartsWith("Set the screen to turn off", StringComparison.Ordinal));
        Assert.Contains(v.Actions, a => a.StartsWith("Set the laptop to sleep", StringComparison.Ordinal));
        Assert.Contains(v.Actions, a => a.Contains("USB Input Device"));
        Assert.Equal(VerdictKind.Healthy, v.Kind); // settings advice does not by itself change the verdict
    }

    [Fact]
    public void Srum_top_apps_and_offenders_are_reported()
    {
        var srum = new SrumSummary
        {
            Last24Hours = new EnergyWindow
            {
                OnBatteryOnly = true,
                Apps =
                [
                    new SrumAppEnergy { Name = "TiWorker", SharePercent = 40, Offender = Unveil.Core.Power.KnownOffenders.Match("TiWorker", 1) },
                    new SrumAppEnergy { Name = "chrome", SharePercent = 20 },
                ],
            },
        };

        var v = VerdictEngine.Evaluate(Snapshot(WithHealth(90)), admin: Admin(srum: srum));

        Assert.Contains(v.Evidence, f => f.Text == "Over the last 24 hours on battery, most app energy went to: TiWorker (40%), chrome (20%).");
        Assert.Contains(v.Actions, a => a.StartsWith("TiWorker used 40% of app energy", StringComparison.Ordinal));
    }

    [Fact]
    public void Reports_include_admin_sections()
    {
        var sleep = SleepStudyParser.Parse(XDocument.Load(TestFiles.Sample("sleepstudy", "asus-expertbook-b1403cva.xml")));
        var energy = EnergyDiagnosticsParser.Parse(XDocument.Load(TestFiles.Sample("energy", "asus-expertbook-b1403cva.xml")));
        SrumSummary srum;
        using (var reader = new StreamReader(TestFiles.Sample("srum", "asus-expertbook-b1403cva.csv")))
        {
            srum = SrumParser.Summarize(reader, new DateTimeOffset(2026, 11, 14, 8, 3, 33, TimeSpan.Zero));
        }

        var doc = ReportDocument.Create(Snapshot(WithHealth(90)), null, null, Now, Admin(sleep, energy, srum));
        var html = HtmlReportWriter.Render(doc);
        var text = TextSummary.Create(doc);
        var back = JsonReportWriter.Read(JsonReportWriter.Serialize(doc));

        Assert.Contains("Energy use by app: last 24 hours", html);
        Assert.Contains("Sleep (Modern Standby)", html);
        Assert.Contains("Windows energy diagnostics", html);
        Assert.Contains("USB Device not Entering Selective Suspend", html);
        Assert.DoesNotContain("<script", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Sleep:", text); // 42 minutes of sleep on battery is too little to judge
        Assert.Contains("too little to judge", html);
        Assert.InRange(text.Split('\n').Length, 5, 8);
        Assert.Equal(3, back.Admin!.Sleep!.Sessions.Count);
        Assert.Equal(7, back.Admin.Energy!.ErrorCount);
        Assert.Equal("Unveil", back.Admin.Srum!.Last24Hours!.Apps[0].Name);
    }
}

public class SampleScrubberTests
{
    [Fact]
    public void Text_removes_names_sids_and_user_folders()
    {
        var text = @"C:\Users\jsmith\AppData\x.exe, S-1-5-21-111-222-333-1001, host JSMITH-LAPTOP, \Users\Public\y";

        var scrubbed = SampleScrubber.Text(text, ["jsmith", "JSMITH-LAPTOP"]);

        Assert.DoesNotContain("jsmith", scrubbed, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("1001", scrubbed);
        Assert.Contains(@"\Users\REDACTED\AppData", scrubbed);
        Assert.Contains(@"\Users\Public\y", scrubbed);
    }

    [Fact]
    public void Xml_redacts_identity_elements_and_user_ids()
    {
        var doc = XDocument.Parse("""
            <SleepStudy xmlns="http://schemas.microsoft.com/sleepstudy/2012">
              <ReportInformation><ReportGuid>ecb7fa03-26f6-44df-b10d-cf8cb5c640b2</ReportGuid></ReportInformation>
              <SystemInformation><ComputerName>JSMITH-LAPTOP</ComputerName></SystemInformation>
              <Record AppId="\Device\HarddiskVolume3\Users\jsmith\app.exe" UserId="jsmith" />
            </SleepStudy>
            """);

        var text = SampleScrubber.Xml(doc, ["jsmith", "JSMITH-LAPTOP"]).ToString();

        Assert.DoesNotContain("jsmith", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ecb7fa03", text);
        Assert.Contains("UserId=\"REDACTED\"", text);
    }

    [Fact]
    public void Wifi_network_name_and_mac_addresses_are_removed()
    {
        var doc = XDocument.Parse("""
            <EnergyReport xmlns="http://schemas.microsoft.com/energy/2007">
              <LogEntry guid="x"><Name>Wireless access point does not support WMM Power Save</Name>
                <Details>
                  <Detail guid="a"><Name>SSID</Name><Value>Smith Family WiFi</Value></Detail>
                  <Detail guid="b"><Name>MAC Address</Name><Value>e8:78:29:14:b1:18</Value></Detail>
                </Details>
              </LogEntry>
            </EnergyReport>
            """);

        var text = SampleScrubber.Xml(doc, []).ToString();

        Assert.DoesNotContain("Smith Family WiFi", text);
        Assert.DoesNotContain("e8:78:29", text);
        Assert.Equal("adapter REDACTED", SampleScrubber.Text("adapter E8-78-29-14-B1-18", []));
    }

    [Fact]
    public void Srum_trim_keeps_recent_rows_and_a_sample_of_older_ones()
    {
        var lines = new List<string> { "AppId, TimeStamp" };
        lines.AddRange(Enumerable.Range(0, 100).Select(i => $"old{i}, 2026-11-01T00:00:00.0000"));
        lines.AddRange(Enumerable.Range(0, 10).Select(i => $"new{i}, 2026-11-14T07:00:00.0000"));

        var trimmed = SampleScrubber.TrimSrum(string.Join("\n", lines), Now, TimeSpan.FromHours(26), olderEvery: 40);
        var kept = trimmed.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);

        Assert.Equal("AppId, TimeStamp", kept[0]);
        Assert.Equal(10, kept.Count(l => l.StartsWith("new", StringComparison.Ordinal)));
        Assert.InRange(kept.Count(l => l.StartsWith("old", StringComparison.Ordinal)), 2, 3);
    }
}
