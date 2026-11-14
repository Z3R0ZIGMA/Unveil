using Unveil.Core.Battery;
using Unveil.Core.Verdict;
using static Unveil.Core.Tests.TestData;

namespace Unveil.Core.Tests.Verdict;

public class VerdictEngineTests
{
    private static DrainMeasurement Drain(double watts, params AppImpact[] apps) => new()
    {
        AverageWatts = watts,
        Duration = TimeSpan.FromSeconds(60),
        SampleCount = 30,
        TopApps = apps,
    };

    // ---------------------------------------------------------------- health bands

    [Theory]
    [InlineData(102.4, HealthLabel.Good)]
    [InlineData(80.0, HealthLabel.Good)]
    [InlineData(79.9, HealthLabel.Worn)]
    [InlineData(60.0, HealthLabel.Worn)]
    [InlineData(59.9, HealthLabel.Replace)]
    [InlineData(0.0, HealthLabel.Replace)]
    public void Health_bands_follow_the_plan(double health, HealthLabel expected)
    {
        Assert.Equal(expected, VerdictEngine.Label(new HealthResult(health, Math.Max(0, 100 - health), null)));
    }

    [Fact]
    public void Unknown_health_is_unknown_label()
    {
        Assert.Equal(HealthLabel.Unknown, VerdictEngine.Label(HealthResult.Unknown("x")));
    }

    // ---------------------------------------------------------------- the four plan verdicts

    [Fact]
    public void Healthy_battery_without_drain_test_is_healthy_and_asks_for_the_test()
    {
        var v = VerdictEngine.Evaluate(Snapshot(WithHealth(87.1)));

        Assert.Equal(VerdictKind.Healthy, v.Kind);
        Assert.Equal(Severity.Good, v.Severity);
        Assert.Equal(ExitCodes.Healthy, v.ExitCode);
        Assert.Equal(DrainStatus.NotTested, v.Drain);
        Assert.Contains("87%", v.Message);
        Assert.Contains(v.Actions, a => a.Contains("full test"));
    }

    [Fact]
    public void Healthy_battery_with_normal_drain_is_in_line_with_usage()
    {
        var v = VerdictEngine.Evaluate(Snapshot(WithHealth(90)), Drain(5.5));

        Assert.Equal(VerdictKind.Healthy, v.Kind);
        Assert.Equal(DrainStatus.Normal, v.Drain);
        Assert.Contains("in line with usage", v.Message);
        Assert.Equal(["No action needed."], v.Actions);
    }

    [Fact]
    public void Healthy_battery_with_elevated_drain_is_a_software_issue()
    {
        var v = VerdictEngine.Evaluate(Snapshot(WithHealth(90)), Drain(11));

        Assert.Equal(VerdictKind.SoftwareDrain, v.Kind);
        Assert.Equal(Severity.Warning, v.Severity);
        Assert.Equal(ExitCodes.SoftwareDrain, v.ExitCode);
        Assert.Equal(DrainStatus.Elevated, v.Drain);
        Assert.Contains("11.0 W", v.Message);
    }

    [Fact]
    public void High_drain_is_danger()
    {
        var v = VerdictEngine.Evaluate(Snapshot(WithHealth(90)), Drain(22));

        Assert.Equal(DrainStatus.High, v.Drain);
        Assert.Equal(Severity.Danger, v.Severity);
    }

    [Fact]
    public void One_app_above_a_quarter_of_power_makes_drain_elevated_even_at_low_watts()
    {
        var v = VerdictEngine.Evaluate(Snapshot(WithHealth(90)), Drain(6, new AppImpact("Teams", 38), new AppImpact("Chrome", 12)));

        Assert.Equal(VerdictKind.SoftwareDrain, v.Kind);
        Assert.Contains("Teams (38% of power)", v.Message);
        Assert.Contains(v.Actions, a => a.StartsWith("Check Teams", StringComparison.Ordinal));
        Assert.Contains(v.Actions, a => a.StartsWith("Check Chrome", StringComparison.Ordinal));
    }

    [Fact]
    public void Worn_battery_gets_the_softer_amber_verdict()
    {
        var v = VerdictEngine.Evaluate(Snapshot(WithHealth(72)));

        Assert.Equal(VerdictKind.BatteryWorn, v.Kind);
        Assert.Equal(Severity.Warning, v.Severity);
        Assert.Equal("Battery worn", v.Title);
        Assert.Equal(ExitCodes.Battery, v.ExitCode);
        Assert.Contains("72%", v.Message);
        Assert.Contains(v.Actions, a => a.StartsWith("Plan a battery replacement", StringComparison.Ordinal));
    }

    [Fact]
    public void Worn_battery_with_drain_is_both_but_amber()
    {
        var v = VerdictEngine.Evaluate(Snapshot(WithHealth(72)), Drain(12));

        Assert.Equal(VerdictKind.Both, v.Kind);
        Assert.Equal(Severity.Warning, v.Severity);
        Assert.Equal(ExitCodes.Both, v.ExitCode);
    }

    [Fact]
    public void Battery_below_sixty_percent_must_be_replaced()
    {
        var v = VerdictEngine.Evaluate(Snapshot(WithHealth(52)));

        Assert.Equal(VerdictKind.ReplaceBattery, v.Kind);
        Assert.Equal(Severity.Danger, v.Severity);
        Assert.Equal("Replace battery", v.Title);
        Assert.Equal(ExitCodes.Battery, v.ExitCode);
        Assert.Contains("52%", v.Message);
        Assert.Contains(v.Actions, a => a.Contains("TEST-BATTERY for the Laptop 14"));
    }

    [Fact]
    public void Replace_with_drain_is_both_and_red()
    {
        var v = VerdictEngine.Evaluate(Snapshot(WithHealth(52)), Drain(16));

        Assert.Equal(VerdictKind.Both, v.Kind);
        Assert.Equal(Severity.Danger, v.Severity);
        Assert.Equal(ExitCodes.Both, v.ExitCode);
        Assert.Equal("Replace battery and fix drain", v.Title);
    }

    // ---------------------------------------------------------------- extra replace triggers

    [Fact]
    public void Cycle_count_above_rating_triggers_replace_even_with_good_health()
    {
        var v = VerdictEngine.Evaluate(Snapshot(WithHealth(85, cycles: 612)));

        Assert.Equal(VerdictKind.ReplaceBattery, v.Kind);
        Assert.Contains("612 charge cycles", v.Message);
        Assert.Contains(v.Evidence, f => f.Severity == Severity.Danger && f.Text.Contains("612"));
    }

    [Fact]
    public void Cycle_count_at_rating_is_fine()
    {
        Assert.Equal(VerdictKind.Healthy, VerdictEngine.Evaluate(Snapshot(WithHealth(85, cycles: 500))).Kind);
    }

    [Fact]
    public void Custom_cycle_rating_is_used()
    {
        var settings = new VerdictSettings { CycleCountRating = 1000 };

        Assert.Equal(VerdictKind.Healthy, VerdictEngine.Evaluate(Snapshot(WithHealth(85, cycles: 612)), null, settings).Kind);
    }

    [Fact]
    public void Windows_needs_service_status_triggers_replace()
    {
        var v = VerdictEngine.Evaluate(Snapshot([WithHealth(90)], windowsStatus: ["Pred Fail"]));

        Assert.Equal(VerdictKind.ReplaceBattery, v.Kind);
        Assert.Contains("needs service", v.Message);
    }

    [Fact]
    public void Sustained_capacity_drop_triggers_replace()
    {
        var report = ReportWithHistory(DailyHistory(45000, 45000, 44800, 38000, 37900, 37800));
        var v = VerdictEngine.Evaluate(Snapshot([WithHealth(85)], report));

        Assert.Equal(VerdictKind.ReplaceBattery, v.Kind);
        Assert.Contains("fell", v.Message);
    }

    [Fact]
    public void One_off_capacity_dip_does_not_trigger_replace()
    {
        var report = ReportWithHistory(DailyHistory(45000, 45000, 38000, 44900, 44800, 44700));
        var v = VerdictEngine.Evaluate(Snapshot([WithHealth(85)], report));

        Assert.Equal(VerdictKind.Healthy, v.Kind);
        Assert.Contains(v.Evidence, f => f.Text.Contains("No sudden drop"));
    }

    // ---------------------------------------------------------------- unknown / edge cases

    [Fact]
    public void Design_capacity_zero_is_inconclusive_never_replace()
    {
        var v = VerdictEngine.Evaluate(Snapshot(MakeBattery(design: 0, full: 41000, cycles: null)));

        Assert.Equal(VerdictKind.Inconclusive, v.Kind);
        Assert.Equal(ExitCodes.Inconclusive, v.ExitCode);
        Assert.Equal(Severity.Neutral, v.Severity);
        Assert.Contains("design capacity reported as 0", v.Message);
        Assert.Contains(v.Evidence, f => f.Text.Contains("not reported by the manufacturer"));
    }

    [Fact]
    public void Unknown_health_with_elevated_drain_is_a_software_issue()
    {
        var v = VerdictEngine.Evaluate(Snapshot(MakeBattery(design: 0)), Drain(13));

        Assert.Equal(VerdictKind.SoftwareDrain, v.Kind);
        Assert.Contains("could not be measured", v.Message);
    }

    [Fact]
    public void Unknown_health_with_cycles_over_rating_still_triggers_replace()
    {
        var v = VerdictEngine.Evaluate(Snapshot(MakeBattery(design: 0, cycles: 900)));

        Assert.Equal(VerdictKind.ReplaceBattery, v.Kind);
    }

    [Fact]
    public void Relative_capacity_is_inconclusive()
    {
        var v = VerdictEngine.Evaluate(Snapshot(MakeBattery(design: 100, full: 45, relative: true)));

        Assert.Equal(VerdictKind.Inconclusive, v.Kind);
    }

    [Fact]
    public void Health_over_100_percent_is_healthy()
    {
        Assert.Equal(VerdictKind.Healthy, VerdictEngine.Evaluate(Snapshot(WithHealth(102.4))).Kind);
    }

    [Fact]
    public void Desktop_without_battery_is_no_battery()
    {
        var v = VerdictEngine.Evaluate(Snapshot([], pcSystemType: 1));

        Assert.Equal(VerdictKind.NoBattery, v.Kind);
        Assert.Equal(ExitCodes.NoBattery, v.ExitCode);
        Assert.Contains("desktop or virtual machine", v.Message);
        Assert.Equal(["No action needed."], v.Actions);
    }

    [Fact]
    public void Laptop_without_battery_suggests_checking_it()
    {
        var v = VerdictEngine.Evaluate(Snapshot([], pcSystemType: 2));

        Assert.Equal(VerdictKind.NoBattery, v.Kind);
        Assert.Contains("missing, disconnected or faulty", v.Message);
    }

    [Fact]
    public void Two_batteries_use_the_worst_one()
    {
        var v = VerdictEngine.Evaluate(Snapshot(WithHealth(92, index: 1), WithHealth(55, index: 2)));

        Assert.Equal(VerdictKind.ReplaceBattery, v.Kind);
        Assert.Contains("Battery 2 holds 55%", v.Message);
        Assert.Contains(v.Evidence, f => f.Text.StartsWith("Battery 1 holds 92.0%", StringComparison.Ordinal));
        Assert.Contains(v.Evidence, f => f.Text.StartsWith("Together the 2 batteries", StringComparison.Ordinal));
    }

    [Fact]
    public void Every_verdict_has_evidence_and_at_least_one_action()
    {
        BatterySnapshot[] snapshots =
        [
            Snapshot(WithHealth(90)), Snapshot(WithHealth(70)), Snapshot(WithHealth(40)),
            Snapshot(MakeBattery(design: 0)), Snapshot([], pcSystemType: 1),
        ];

        foreach (var s in snapshots)
        {
            var v = VerdictEngine.Evaluate(s);
            Assert.NotEmpty(v.Evidence);
            Assert.NotEmpty(v.Actions);
            Assert.False(string.IsNullOrWhiteSpace(v.Message));
        }
    }

    [Fact]
    public void Real_asus_laptop_is_healthy_with_usage_context()
    {
        var report = AsusReport();
        var battery = BatteryMerger.Merge([], report.Batteries, new CollectionLog()).Single();
        var v = VerdictEngine.Evaluate(Snapshot([battery], report));

        Assert.Equal(VerdictKind.Healthy, v.Kind);
        Assert.Equal(87.1, v.HealthPercent);
        Assert.Contains(v.Evidence, f => f.Text.Contains("13.8 W while in use"));
        Assert.Contains(v.Evidence, f => f.Text.Contains("0.19 W") && f.Text.Contains("0.5% of a full charge per hour"));
        Assert.Contains(v.Evidence, f => f.Text.Contains("3 h 17 m"));
    }
}

public class VerdictEngineDrainTests
{
    private static DrainMeasurement Drain(double? watts, bool pluggedIn = false, params AppImpact[] apps) => new()
    {
        AverageWatts = watts,
        Duration = TimeSpan.FromSeconds(60),
        SampleCount = watts is null ? 0 : 30,
        PluggedIn = pluggedIn,
        TopApps = apps,
    };

    [Fact]
    public void Plugged_in_test_has_no_watts_and_stays_healthy_when_apps_are_quiet()
    {
        var v = VerdictEngine.Evaluate(TestData.Snapshot(TestData.WithHealth(90)), Drain(null, pluggedIn: true, new AppImpact("Notepad", 40, Impact: 0.3)));

        Assert.Equal(VerdictKind.Healthy, v.Kind);
        Assert.Equal(DrainStatus.Normal, v.Drain);
        Assert.Contains(v.Evidence, f => f.Text.Contains("charger stayed connected"));
    }

    [Fact]
    public void A_big_share_of_a_nearly_idle_machine_is_not_a_problem()
    {
        var v = VerdictEngine.Evaluate(TestData.Snapshot(TestData.WithHealth(90)), Drain(5, false, new AppImpact("svchost", 60, Impact: 0.4)));

        Assert.Equal(VerdictKind.Healthy, v.Kind);
    }

    [Fact]
    public void A_busy_dominant_app_makes_drain_elevated_even_when_plugged_in()
    {
        var v = VerdictEngine.Evaluate(TestData.Snapshot(TestData.WithHealth(90)), Drain(null, true, new AppImpact("Teams", 45, Impact: 6)));

        Assert.Equal(VerdictKind.SoftwareDrain, v.Kind);
        Assert.Contains("Teams (45% of power)", v.Message);
    }

    [Fact]
    public void Known_offender_advice_becomes_an_action()
    {
        var update = new AppImpact("Windows Update", 12, Impact: 2, Category: "Windows Update", Advice: "Let it finish while plugged in.");
        var v = VerdictEngine.Evaluate(TestData.Snapshot(TestData.WithHealth(90)), Drain(6, false, update));

        Assert.Contains(v.Actions, a => a.Contains("Let it finish while plugged in."));
        Assert.Contains(v.Evidence, f => f.Severity == Severity.Warning && f.Text.Contains("(Windows Update)"));
    }

    [Fact]
    public void Laptop_type_changes_the_drain_limits()
    {
        var snapshot = TestData.Snapshot(TestData.WithHealth(90));
        var drain = Drain(14);

        var thin = VerdictEngine.Evaluate(snapshot, drain, VerdictSettings.Default.For(MachineClass.ThinAndLight));
        var gaming = VerdictEngine.Evaluate(snapshot, drain, VerdictSettings.Default.For(MachineClass.Performance));

        Assert.Equal(DrainStatus.Elevated, thin.Drain);
        Assert.Equal(DrainStatus.Normal, gaming.Drain);
        Assert.Contains(gaming.Evidence, f => f.Text.Contains("gaming or workstation laptop, normal is below 20 W"));
    }

    [Theory]
    [InlineData(MachineClass.ThinAndLight, 8, 15)]
    [InlineData(MachineClass.Standard, 12, 20)]
    [InlineData(MachineClass.Performance, 20, 35)]
    public void Machine_class_thresholds(MachineClass c, double elevated, double high)
    {
        var s = VerdictSettings.Default.For(c);

        Assert.Equal(elevated, s.ElevatedDrainWatts);
        Assert.Equal(high, s.HighDrainWatts);
    }

    [Fact]
    public void Top_apps_are_listed_in_the_evidence()
    {
        var v = VerdictEngine.Evaluate(TestData.Snapshot(TestData.WithHealth(90)),
            Drain(6, false, new AppImpact("A", 20, 1), new AppImpact("B", 10, 0.5), new AppImpact("C", 5, 0.2), new AppImpact("D", 1, 0.1)));

        Assert.Contains(v.Evidence, f => f.Text == "Apps using the most power during the test: A (20%), B (10%), C (5%).");
    }
}
