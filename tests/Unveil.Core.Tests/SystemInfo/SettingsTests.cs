using Unveil.Core.Reports;
using Unveil.Core.SystemInfo;
using Unveil.Core.Verdict;
using static Unveil.Core.Tests.TestData;

namespace Unveil.Core.Tests.SystemInfo;

public class SettingsAdvisorTests
{
    /// <summary>Settings as read on the ASUS during Phase 6 (brightness 100%, screen and sleep never on battery).</summary>
    internal static PowerSettings Asus => new()
    {
        OnBattery = true,
        PowerMode = PowerMode.Balanced,
        PowerModeIsForBattery = true,
        BatterySaverOn = false,
        BatterySaverThresholdPercent = 30,
        BrightnessPercent = 100,
        RefreshRateHz = 60,
        ScreenOffOnBattery = TimeSpan.Zero,
        SleepOnBattery = TimeSpan.Zero,
        StartupApps =
        [
            new("OneDrive", "Run (this user)", true), new("panel.exe", "Run (this user)", true), new("Microsoft.Lists", "Run (this user)", true),
            new("SecurityHealth", "Run (all users)", true), new("panel.exe", "Run (all users)", true), new("AnyDesk", "Startup folder (all users)", true),
            new("Teams", "Run (this user)", false), new("Spotify", "Run (this user)", false),
        ],
        GraphicsAdapters = ["Intel(R) UHD Graphics"],
        Uptime = TimeSpan.FromHours(5),
        UpdateRestartPending = false,
    };

    private static SettingCheck Check(PowerSettings s, string key) => SettingsAdvisor.Evaluate(s).Single(c => c.Key == key);

    [Fact]
    public void Real_asus_settings_flag_brightness_screen_off_and_sleep()
    {
        var checks = SettingsAdvisor.Evaluate(Asus);

        Assert.Equal(["brightness", "screen-off", "sleep"], checks.Where(c => c.Severity == Severity.Warning).Select(c => c.Key));
        Assert.Equal("100%", Check(Asus, "brightness").Value);
        Assert.Equal("Never", Check(Asus, "screen-off").Value);
        Assert.Equal("Balanced (on battery)", Check(Asus, "power-mode").Value);
        Assert.Equal("Turns on at 30%", Check(Asus, "battery-saver").Value);
        Assert.Equal("5 on, 2 off", Check(Asus, "startup-apps").Value); // panel.exe in two places counts once
        Assert.Equal("5 hours ago", Check(Asus, "uptime").Value);
    }

    [Fact]
    public void Best_performance_on_battery_is_a_warning_but_not_when_plugged_in()
    {
        Assert.Equal(Severity.Warning, Check(Asus with { PowerMode = PowerMode.BestPerformance }, "power-mode").Severity);
        Assert.Equal(Severity.Good, Check(Asus with { PowerMode = PowerMode.BestPerformance, PowerModeIsForBattery = false }, "power-mode").Severity);
    }

    [Theory]
    [InlineData(79, Severity.Good)]
    [InlineData(80, Severity.Warning)]
    public void Brightness_threshold(int brightness, Severity expected)
    {
        Assert.Equal(expected, Check(Asus with { BrightnessPercent = brightness }, "brightness").Severity);
    }

    [Theory]
    [InlineData(60, Severity.Good)]
    [InlineData(120, Severity.Warning)]
    [InlineData(144, Severity.Warning)]
    public void Refresh_rate_threshold(int hz, Severity expected)
    {
        Assert.Equal(expected, Check(Asus with { RefreshRateHz = hz }, "refresh-rate").Severity);
    }

    [Fact]
    public void Timeouts_are_judged()
    {
        Assert.Equal(Severity.Good, Check(Asus with { ScreenOffOnBattery = TimeSpan.FromMinutes(3) }, "screen-off").Severity);
        Assert.Equal("After 3 min", Check(Asus with { ScreenOffOnBattery = TimeSpan.FromMinutes(3) }, "screen-off").Value);
        Assert.Equal(Severity.Warning, Check(Asus with { ScreenOffOnBattery = TimeSpan.FromMinutes(30) }, "screen-off").Severity);
        Assert.Equal(Severity.Good, Check(Asus with { SleepOnBattery = TimeSpan.FromMinutes(15) }, "sleep").Severity);
        Assert.Equal(Severity.Warning, Check(Asus with { SleepOnBattery = TimeSpan.FromHours(2) }, "sleep").Severity);
    }

    [Fact]
    public void Battery_saver_that_never_turns_on_is_a_warning()
    {
        var check = Check(Asus with { BatterySaverThresholdPercent = 0 }, "battery-saver");

        Assert.Equal(Severity.Warning, check.Severity);
        Assert.Contains("20-30%", check.Advice);
    }

    [Fact]
    public void Many_startup_apps_is_a_warning_that_lists_them()
    {
        var apps = Enumerable.Range(1, 9).Select(i => new StartupApp($"App{i}", "Run (this user)", true)).ToList();
        var check = Check(Asus with { StartupApps = apps }, "startup-apps");

        Assert.Equal(Severity.Warning, check.Severity);
        Assert.Contains("App1, App2", check.Finding);
        Assert.Contains("9 start with Windows", check.Advice);
    }

    [Fact]
    public void Long_uptime_and_pending_updates_are_warnings()
    {
        Assert.Equal("12 days ago", Check(Asus with { Uptime = TimeSpan.FromDays(12.5) }, "uptime").Value);
        Assert.Equal(Severity.Warning, Check(Asus with { Uptime = TimeSpan.FromDays(12.5) }, "uptime").Severity);
        Assert.Equal(Severity.Warning, Check(Asus with { UpdateRestartPending = true }, "updates").Severity);
    }

    [Fact]
    public void Busy_discrete_gpu_is_a_warning()
    {
        var busy = Asus with { DiscreteGpu = "NVIDIA GeForce RTX 4060 Laptop GPU", DiscreteGpuActive = true, DiscreteGpuBusyPercent = 23 };
        var idle = busy with { DiscreteGpuActive = false, DiscreteGpuBusyPercent = 0 };

        Assert.Equal(Severity.Warning, Check(busy, "discrete-gpu").Severity);
        Assert.Contains("Power saving", Check(busy, "discrete-gpu").Advice);
        Assert.Equal(Severity.Good, Check(idle, "discrete-gpu").Severity);
        Assert.Equal("Intel(R) UHD Graphics (built-in only)", Check(Asus, "discrete-gpu").Value);
    }

    [Fact]
    public void Unreadable_settings_are_neutral_not_warnings()
    {
        var checks = SettingsAdvisor.Evaluate(new PowerSettings());

        Assert.DoesNotContain(checks, c => c.Severity == Severity.Warning);
        Assert.Equal("Not reported", checks.Single(c => c.Key == "brightness").Value);
    }
}

public class SettingsReaderLogicTests
{
    [Theory]
    [InlineData(new byte[] { 0x02, 0, 0 }, true)]
    [InlineData(new byte[] { 0x06, 0, 0 }, true)]
    [InlineData(new byte[] { 0x03, 0, 0 }, false)]
    [InlineData(new byte[] { 0x07, 0, 0 }, false)]
    [InlineData(null, true)] // never switched off
    public void Startup_approved_flag(byte[]? value, bool enabled)
    {
        Assert.Equal(enabled, StartupApps.IsEnabled(value));
    }

    [Theory]
    [InlineData("00000000-0000-0000-0000-000000000000", PowerMode.Balanced)]
    [InlineData("961cc777-2547-4f9d-8174-7d86181b8a7a", PowerMode.BestPowerEfficiency)]
    [InlineData("3af9b8d9-7c97-431d-ad78-34a8bfea439f", PowerMode.BetterPerformance)]
    [InlineData("ded574b5-45a0-4f42-8737-46345c09c238", PowerMode.BestPerformance)]
    [InlineData("11111111-2222-3333-4444-555555555555", PowerMode.Unknown)]
    public void Power_mode_from_overlay(string guid, PowerMode expected)
    {
        Assert.Equal(expected, PowerSettingsReader.ModeFromOverlay(Guid.Parse(guid)));
    }

    [Theory]
    [InlineData("NVIDIA GeForce RTX 4060 Laptop GPU", true)]
    [InlineData("AMD Radeon RX 7600S", true)]
    [InlineData("Intel(R) Arc(TM) A370M Graphics", true)]
    [InlineData("Intel(R) UHD Graphics", false)]
    [InlineData("Intel(R) Arc(TM) Graphics", false)] // integrated Arc on Core Ultra
    [InlineData("AMD Radeon(TM) Graphics", false)]
    public void Discrete_gpu_names(string name, bool discrete)
    {
        Assert.Equal(discrete, GpuActivity.LooksDiscrete(name));
    }

    [Theory]
    [InlineData("luid_0x00000000_0x00010174_phys_0", "0x00000000_0x00010174")]
    [InlineData("pid_10512_luid_0x00000000_0x00010174_phys_0_eng_11_engtype_3D", "0x00000000_0x00010174")]
    [InlineData("_Total", null)]
    public void Luid_from_instance_names(string instance, string? expected)
    {
        Assert.Equal(expected, GpuActivity.Luid(instance));
    }

    [Fact]
    public void Discrete_gpu_is_found_by_dedicated_memory_and_judged_by_busy_time()
    {
        var dedicated = new Dictionary<string, long> { ["igpu"] = 0, ["dgpu"] = 512L * 1024 * 1024 };

        var busy = GpuActivity.Classify("NVIDIA GeForce RTX 4060 Laptop GPU", dedicated, new Dictionary<string, double> { ["igpu"] = 30, ["dgpu"] = 12 });
        var idle = GpuActivity.Classify("NVIDIA GeForce RTX 4060 Laptop GPU", dedicated, new Dictionary<string, double> { ["igpu"] = 30, ["dgpu"] = 0.2 });

        Assert.True(busy.Active);
        Assert.Equal(12, busy.BusyPercent);
        Assert.False(idle.Active);
    }

    [Fact]
    public void Integrated_only_laptop_has_no_discrete_gpu()
    {
        var result = GpuActivity.Classify(null, new Dictionary<string, long> { ["igpu"] = 0 }, new Dictionary<string, double> { ["igpu"] = 40 });

        Assert.Null(result.DiscreteName);
        Assert.Null(result.Active);
    }
}

public class SettingsVerdictTests
{
    private static DrainMeasurement Drain(double watts, double cpu, params AppImpact[] apps) => new()
    {
        AverageWatts = watts,
        Duration = TimeSpan.FromSeconds(60),
        SampleCount = 30,
        WholeMachineCpuPercent = cpu,
        TopApps = apps,
    };

    [Fact]
    public void Real_asus_case_high_draw_with_idle_apps_points_at_settings()
    {
        // The test from Phase 5: 11.6 W at 7.8% CPU, brightness 100%, screen and sleep never on battery.
        var doc = ReportDocument.Create(Snapshot(WithHealth(87)), new Unveil.Core.Power.DrainTestResult
        {
            Measurement = Drain(11.6, 7.8, new AppImpact("dwm", 21, 1.2)),
            Activity = new Unveil.Core.Power.ActivityResult(),
        }, generatedAt: Now, powerSettings: SettingsAdvisorTests.Asus);
        var v = doc.Verdict;

        Assert.Equal(VerdictKind.SoftwareDrain, v.Kind);
        Assert.Contains("while the apps were mostly idle (likely the screen or settings)", v.Message);
        Assert.Contains(v.Evidence, f => f.Text.StartsWith("The apps were mostly idle during the test (7.8% CPU)", StringComparison.Ordinal));
        Assert.Equal("Lower the screen brightness to about 50% on battery, or turn on automatic brightness.", v.Actions[0]);
        Assert.Contains(v.Actions, a => a.StartsWith("Set the screen to turn off", StringComparison.Ordinal));
        Assert.DoesNotContain(v.Actions, a => a.StartsWith("Check background apps", StringComparison.Ordinal));
        Assert.DoesNotContain(v.Actions, a => a.StartsWith("Check dwm", StringComparison.Ordinal));
    }

    [Fact]
    public void Busy_apps_are_not_called_idle()
    {
        var v = VerdictEngine.Evaluate(Snapshot(WithHealth(90)), Drain(12, 35));

        Assert.DoesNotContain("mostly idle", v.Message);
        Assert.Contains(v.Actions, a => a.StartsWith("Check background apps", StringComparison.Ordinal));
    }

    [Fact]
    public void Idle_apps_without_settings_problems_still_get_advice()
    {
        var v = VerdictEngine.Evaluate(Snapshot(WithHealth(90)), Drain(12, 4));

        Assert.Contains(v.Actions, a => a.StartsWith("The apps were mostly idle: check the screen brightness", StringComparison.Ordinal));
    }

    [Fact]
    public void Settings_warnings_do_not_change_a_healthy_verdict_on_their_own()
    {
        var doc = ReportDocument.Create(Snapshot(WithHealth(90)), generatedAt: Now, powerSettings: SettingsAdvisorTests.Asus);

        Assert.Equal(VerdictKind.Healthy, doc.Verdict.Kind);
        Assert.Contains(doc.Verdict.Evidence, f => f.Severity == Severity.Warning && f.Text.Contains("brightness is 100%"));
        Assert.Contains(doc.Verdict.Evidence, f => f.Severity == Severity.Good && f.Text.StartsWith("These settings look fine: power mode Balanced (on battery)", StringComparison.Ordinal));
        Assert.Equal(10, doc.SettingChecks.Count);
    }

    [Fact]
    public void Energy_report_timeouts_are_not_repeated_when_settings_were_read()
    {
        var energy = Unveil.Core.Admin.EnergyDiagnosticsParser.Parse(System.Xml.Linq.XDocument.Load(TestFiles.Sample("energy", "asus-expertbook-b1403cva.xml")));
        var admin = new Unveil.Core.Admin.AdminData { IsAdministrator = true, Energy = energy };
        var doc = ReportDocument.Create(Snapshot(WithHealth(90)), generatedAt: Now, admin: admin, powerSettings: SettingsAdvisorTests.Asus);

        Assert.Single(doc.Verdict.Actions, a => a.StartsWith("Set the screen to turn off", StringComparison.Ordinal));
        Assert.Single(doc.Verdict.Actions, a => a.StartsWith("Set the laptop to sleep", StringComparison.Ordinal));
        Assert.Contains(doc.Verdict.Actions, a => a.Contains("USB Input Device")); // still reported: settings can't see it
    }
}

public class SettingsWordingTests
{
    [Theory]
    [InlineData(1.0, "1 hour ago")]
    [InlineData(30.0, "1 day ago")]
    [InlineData(60.0, "2 days ago")]
    public void Uptime_wording(double hours, string expected)
    {
        var check = SettingsAdvisor.Evaluate(new PowerSettings { Uptime = TimeSpan.FromHours(hours) }).Single(c => c.Key == "uptime");

        Assert.Equal(expected, check.Value);
    }
}
