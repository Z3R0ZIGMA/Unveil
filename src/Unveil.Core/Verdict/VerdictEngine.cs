using Unveil.Core.Battery;
using static Unveil.Core.Formatting;

namespace Unveil.Core.Verdict;

/// <summary>
/// Turns collected data into one plain-English verdict (plan.md section 4).
/// Battery: health bands, cycle-count rating, Windows "needs service" status and a sustained capacity drop.
/// Drain: the live drain test (Phase 4); until that runs, drain is "not tested" and does not affect the verdict.
/// </summary>
public static class VerdictEngine
{
    public static HealthLabel Label(HealthResult health, VerdictSettings? settings = null)
    {
        settings ??= VerdictSettings.Default;
        return health.HealthPercent switch
        {
            null => HealthLabel.Unknown,
            var h when h >= settings.GoodHealthPercent => HealthLabel.Good,
            var h when h >= settings.ReplaceHealthPercent => HealthLabel.Worn,
            _ => HealthLabel.Replace,
        };
    }

    public static Verdict Evaluate(BatterySnapshot snapshot, DrainMeasurement? drain = null, VerdictSettings? settings = null)
    {
        settings ??= VerdictSettings.Default;

        if (!snapshot.HasBattery)
        {
            return NoBattery(snapshot);
        }

        var evidence = new List<Finding>();
        var battery = AssessBattery(snapshot, settings, evidence);
        var drainResult = AssessDrain(drain, settings, evidence);
        AddContext(snapshot, evidence);

        return Combine(snapshot, battery, drainResult, settings, evidence);
    }

    // ---------------------------------------------------------------- battery

    private sealed record BatteryAssessment(
        BatteryCondition Condition,
        BatteryInfo Headline,
        string? ReplaceReason,
        string? UnknownReason);

    private static BatteryAssessment AssessBattery(BatterySnapshot snapshot, VerdictSettings settings, List<Finding> evidence)
    {
        var multiple = snapshot.Batteries.Count > 1;
        var labels = new List<HealthLabel>();
        string? replaceReason = null;

        foreach (var b in snapshot.Batteries)
        {
            var who = multiple ? $"Battery {b.Index}" : "The battery";
            var label = Label(b.Health, settings);
            labels.Add(label);

            if (b.Health.HealthPercent is { } h)
            {
                evidence.Add(new Finding(
                    label switch { HealthLabel.Good => Severity.Good, HealthLabel.Worn => Severity.Warning, _ => Severity.Danger },
                    $"{who} holds {Percent(h)} of its original capacity " +
                    $"({Capacity(b.FullChargeCapacity)} of {Capacity(b.DesignCapacity)})."));
            }
            else
            {
                evidence.Add(new Finding(Severity.Neutral, $"{who}'s health can't be measured: {Lower(b.Health.UnknownReason)}."));
            }

            if (b.CycleCount is not { } cycles)
            {
                evidence.Add(new Finding(Severity.Neutral, $"{who} cycle count is not reported by the manufacturer."));
            }
            else if (cycles > settings.CycleCountRating)
            {
                evidence.Add(new Finding(Severity.Danger,
                    $"{who} has {Number(cycles)} charge cycles, above the {Number(settings.CycleCountRating)}-cycle rating."));
                replaceReason ??= $"{who} has {Number(cycles)} charge cycles, above its {Number(settings.CycleCountRating)}-cycle rating.";
            }
            else
            {
                evidence.Add(new Finding(Severity.Good,
                    $"{who} has {Number(cycles)} charge cycles (rating {Number(settings.CycleCountRating)})."));
            }
        }

        if (snapshot.Combined is { Health.HealthPercent: { } combined } total)
        {
            evidence.Add(new Finding(Severity.Neutral,
                $"Together the {total.Count} batteries hold {Percent(combined)} of their original capacity."));
        }

        if (snapshot.WindowsServiceStatus is { } status)
        {
            evidence.Add(new Finding(Severity.Danger, $"Windows reports the battery status as \"{status}\" (needs service)."));
            replaceReason ??= "Windows reports that the battery needs service.";
        }

        var history = snapshot.Report?.CapacityHistory ?? [];
        if (CapacityTrend.FindSustainedDrop(history, settings) is { } drop)
        {
            evidence.Add(new Finding(Severity.Danger,
                $"Full charge capacity fell {Percent(drop.DropPercent)} within {settings.CapacityDropWindowDays} days " +
                $"({Capacity(drop.PeakCapacity)} on {Date(drop.PeakDate)}, {Capacity(drop.RecentCapacity)} by {Date(drop.RecentDate)})."));
            replaceReason ??= $"Its capacity fell {Percent(drop.DropPercent, "0")} in {settings.CapacityDropWindowDays} days, which points to a failing battery.";
        }
        else if (history.Count > settings.CapacityDropSustainedEntries)
        {
            evidence.Add(new Finding(Severity.Good, "No sudden drop in capacity in the recent history."));
        }

        var condition =
            replaceReason is not null || labels.Contains(HealthLabel.Replace) ? BatteryCondition.Replace
            : labels.Contains(HealthLabel.Worn) ? BatteryCondition.Worn
            : labels.Contains(HealthLabel.Unknown) ? BatteryCondition.Unknown
            : BatteryCondition.Good;

        // The battery the message talks about: the one in the worst health.
        var headline = snapshot.Batteries
            .OrderBy(b => b.Health.HealthPercent ?? double.MaxValue)
            .First();

        var unknownReason = snapshot.Batteries.FirstOrDefault(b => !b.Health.IsKnown)?.Health.UnknownReason;
        return new BatteryAssessment(condition, headline, replaceReason, unknownReason);
    }

    // ---------------------------------------------------------------- drain

    private sealed record DrainAssessment(DrainStatus Status, DrainMeasurement? Measurement, string? Cause);

    private static DrainAssessment AssessDrain(DrainMeasurement? drain, VerdictSettings settings, List<Finding> evidence)
    {
        if (drain is null)
        {
            evidence.Add(new Finding(Severity.Neutral, "The live drain test has not been run, so software drain was not checked."));
            return new DrainAssessment(DrainStatus.NotTested, null, null);
        }

        var status = DrainStatus.Normal;
        string? cause = null;

        if (drain.AverageWatts is { } watts)
        {
            status = watts > settings.HighDrainWatts ? DrainStatus.High
                : watts >= settings.ElevatedDrainWatts ? DrainStatus.Elevated
                : DrainStatus.Normal;

            evidence.Add(new Finding(
                status switch { DrainStatus.Normal => Severity.Good, DrainStatus.Elevated => Severity.Warning, _ => Severity.Danger },
                $"Average power draw on battery was {Watts(watts)} over {Duration(drain.Duration)} " +
                $"(for a {Lower(settings.DrainProfileName)}, normal is below {Watts(settings.ElevatedDrainWatts, "0")})."));

            if (drain.PluggedIn)
            {
                evidence.Add(new Finding(Severity.Neutral, "The charger was connected for part of the test, so those readings were left out."));
            }

            cause = status == DrainStatus.Normal ? null : $"a power draw of {Watts(watts)}";
        }
        else
        {
            evidence.Add(new Finding(Severity.Neutral,
                "The charger stayed connected during the test, so the power draw could not be measured; only app activity was checked."));
        }

        if (drain.TopApps.Count > 0)
        {
            evidence.Add(new Finding(Severity.Neutral,
                "Apps using the most power during the test: " +
                string.Join(", ", drain.TopApps.Take(3).Select(a => $"{a.Name} ({Percent(a.SharePercent, "0")})")) + "."));
        }

        // One app taking a large share while the laptop sat idle. Ignored when the whole machine was nearly idle.
        var dominant = drain.TopApps
            .Where(a => a.SharePercent > settings.DominantAppSharePercent && a.Impact >= settings.DominantAppMinimumImpact)
            .ToList();
        foreach (var app in dominant)
        {
            evidence.Add(new Finding(Severity.Warning,
                $"{app.Name}{(app.Category is null ? "" : $" ({app.Category})")} used {Percent(app.SharePercent, "0")} of the power while the laptop was idle."));
        }

        // Known battery-draining apps that were noticeably active, even if not dominant.
        foreach (var app in drain.TopApps.Where(a => a.Category is not null && !dominant.Contains(a) && a.SharePercent >= 5 && a.Impact >= 0.5))
        {
            evidence.Add(new Finding(Severity.Warning, $"{app.Name} ({app.Category}) used {Percent(app.SharePercent, "0")} of the power."));
        }

        if (dominant.Count > 0)
        {
            status = status == DrainStatus.Normal ? DrainStatus.Elevated : status;
            var apps = string.Join(", ", dominant.Select(a => $"{a.Name} ({Percent(a.SharePercent, "0")} of power)"));
            cause = cause is null ? apps : $"{cause}, mostly from {apps}";
        }

        return new DrainAssessment(status, drain, cause);
    }

    // ---------------------------------------------------------------- context

    /// <summary>Information that does not change the verdict but helps the tech.</summary>
    private static void AddContext(BatterySnapshot snapshot, List<Finding> evidence)
    {
        if (snapshot.Report?.RuntimeEstimates?.AtFullChargeCapacity?.ActiveRuntime is { } now)
        {
            var whenNew = snapshot.Report.RuntimeEstimates.AtDesignCapacity?.ActiveRuntime;
            evidence.Add(new Finding(Severity.Neutral,
                $"Windows estimates {Duration(now)} of active use on a full charge" +
                (whenNew is null ? "." : $" ({Duration(whenNew)} when new).")));
        }

        var usage = UsageAnalyzer.Summarize(snapshot.Report);
        if (usage.InUse is { } inUse)
        {
            evidence.Add(new Finding(Severity.Neutral,
                $"Recent on-battery use averaged {Watts(inUse.AverageWatts)} while in use " +
                $"({Duration(inUse.Duration)} of use in the battery report)."));
        }

        if (usage.Asleep is { } asleep)
        {
            evidence.Add(new Finding(Severity.Neutral,
                $"While asleep on battery it used {Watts(asleep.AverageWatts, "0.00")}" +
                (asleep.PercentPerHour is { } pph ? $", about {Percent(pph)} of a full charge per hour." : ".")));
        }
    }

    // ---------------------------------------------------------------- verdict

    private static Verdict Combine(
        BatterySnapshot snapshot,
        BatteryAssessment battery,
        DrainAssessment drain,
        VerdictSettings settings,
        List<Finding> evidence)
    {
        var actions = new List<string>();
        var drainBad = drain.Status is DrainStatus.Elevated or DrainStatus.High;
        var health = battery.Headline.Health.HealthPercent;
        var who = snapshot.Batteries.Count > 1 ? $"Battery {battery.Headline.Index}" : "It";
        var holds = health is { } h ? $"{who} holds {Percent(h, "0")} of its original capacity." : null;
        var drainText = drain.Cause ?? "apps or settings";

        VerdictKind kind;
        Severity severity;
        string title;
        string message;

        switch (battery.Condition)
        {
            case BatteryCondition.Good when !drainBad:
                kind = VerdictKind.Healthy;
                severity = Severity.Good;
                title = "Battery healthy";
                message = drain.Status == DrainStatus.Normal
                    ? $"{holds} Runtime is in line with usage."
                    : $"{holds} Run the full test to check whether apps or settings are draining it.";
                break;

            case BatteryCondition.Good:
                kind = VerdictKind.SoftwareDrain;
                severity = drain.Status == DrainStatus.High ? Severity.Danger : Severity.Warning;
                title = "Software/settings issue";
                message = $"The battery is fine; it is being drained by {drainText}.";
                break;

            case BatteryCondition.Worn when !drainBad:
                kind = VerdictKind.BatteryWorn;
                severity = Severity.Warning;
                title = "Battery worn";
                message = $"{holds} Plan a replacement if the runtime is no longer enough.";
                break;

            case BatteryCondition.Worn:
                kind = VerdictKind.Both;
                severity = Severity.Warning;
                title = "Battery worn and software drain";
                message = $"The battery is being drained by {drainText}. The battery is also worn: {Lower(holds)}";
                break;

            case BatteryCondition.Replace when !drainBad:
                kind = VerdictKind.ReplaceBattery;
                severity = Severity.Danger;
                title = "Replace battery";
                message = ReplaceMessage(battery, holds, settings);
                break;

            case BatteryCondition.Replace:
                kind = VerdictKind.Both;
                severity = Severity.Danger;
                title = "Replace battery and fix drain";
                message = $"{ReplaceMessage(battery, holds, settings)} It is also being drained by {drainText}.";
                break;

            case BatteryCondition.Unknown when drainBad:
                kind = VerdictKind.SoftwareDrain;
                severity = drain.Status == DrainStatus.High ? Severity.Danger : Severity.Warning;
                title = "Software/settings issue";
                message = $"The battery is being drained by {drainText}. Battery health could not be measured.";
                break;

            default:
                kind = VerdictKind.Inconclusive;
                severity = Severity.Neutral;
                title = "Inconclusive";
                message = $"Battery health can't be measured on this laptop ({Lower(battery.UnknownReason)}). " +
                          (drain.Status == DrainStatus.NotTested
                              ? "Run the full test to check the power draw."
                              : "The power draw is normal.");
                break;
        }

        // ---- recommended actions
        if (battery.Condition == BatteryCondition.Replace)
        {
            actions.Add($"Replace the battery{PartHint(snapshot, battery.Headline)}.");
        }
        else if (battery.Condition == BatteryCondition.Worn)
        {
            actions.Add($"Plan a battery replacement{PartHint(snapshot, battery.Headline)} if the customer needs more runtime.");
        }
        else if (battery.Condition == BatteryCondition.Unknown)
        {
            actions.Add("The battery does not report enough data to measure its health; compare the runtime with what the customer expects.");
        }

        if (drain.Measurement is { } m)
        {
            // Advice for known offenders that were active, whatever the verdict.
            foreach (var app in m.TopApps.Where(a => a.Advice is not null && a.SharePercent >= 5 && a.Impact >= 0.5).Take(3))
            {
                actions.Add($"{app.Name} used {Percent(app.SharePercent, "0")} of the power: {app.Advice}");
            }

            if (drainBad)
            {
                var apps = m.TopApps.Where(a => a.Advice is null && a.SharePercent >= 10).Take(5).ToList();
                if (apps.Count > 0)
                {
                    actions.AddRange(apps.Select(a => $"Check {a.Name}: it used {Percent(a.SharePercent, "0")} of the power during the test."));
                }
                else if (!actions.Any(a => a.Contains("of the power", StringComparison.Ordinal)))
                {
                    actions.Add($"Check background apps and power settings: the draw was {Watts(m.AverageWatts)} (normal is below {Watts(settings.ElevatedDrainWatts, "0")}).");
                }
            }
        }
        else if (drain.Status == DrainStatus.NotTested)
        {
            actions.Add("Run the full test (unplugged, 60 seconds) to check for software drain.");
        }

        if (actions.Count == 0)
        {
            actions.Add("No action needed.");
        }

        return new Verdict
        {
            Kind = kind,
            Severity = severity,
            Title = title,
            Message = message.Trim(),
            Battery = battery.Condition,
            Drain = drain.Status,
            HealthPercent = health,
            Evidence = evidence,
            Actions = actions,
            ExitCode = kind switch
            {
                VerdictKind.Healthy => ExitCodes.Healthy,
                VerdictKind.SoftwareDrain => ExitCodes.SoftwareDrain,
                VerdictKind.BatteryWorn or VerdictKind.ReplaceBattery => ExitCodes.Battery,
                VerdictKind.Both => ExitCodes.Both,
                VerdictKind.Inconclusive => ExitCodes.Inconclusive,
                _ => ExitCodes.NoBattery,
            },
        };
    }

    private static Verdict NoBattery(BatterySnapshot snapshot)
    {
        var role = snapshot.Report?.System.PlatformRole;
        var isMobile = snapshot.System.PcSystemType is 2 or 8 || role is "Mobile" or "Slate";
        return new Verdict
        {
            Kind = VerdictKind.NoBattery,
            Severity = Severity.Neutral,
            Title = "No battery detected",
            Message = isMobile
                ? "This laptop has no battery that Windows can see. It may be missing, disconnected or faulty."
                : "This looks like a desktop or virtual machine, so there is no battery to test.",
            Battery = BatteryCondition.Unknown,
            Drain = DrainStatus.NotTested,
            Evidence = [new Finding(Severity.Neutral, $"Windows reports this machine as: {snapshot.System.PcSystemTypeName}.")],
            Actions = isMobile
                ? ["Check Device Manager > Batteries, reseat the battery if possible, or test with a known-good battery."]
                : ["No action needed."],
            ExitCode = ExitCodes.NoBattery,
        };
    }

    private static string ReplaceMessage(BatteryAssessment battery, string? holds, VerdictSettings settings)
    {
        var healthBased = battery.Headline.Health.HealthPercent is { } h && h < settings.ReplaceHealthPercent;
        return healthBased || battery.ReplaceReason is null
            ? $"{holds} {battery.ReplaceReason}".Trim()
            : battery.ReplaceReason;
    }

    private static string PartHint(BatterySnapshot snapshot, BatteryInfo battery)
    {
        var model = snapshot.System.DisplayModel;
        return (battery.Name, model) switch
        {
            ({ } name, { } m) => $" ({name} for the {m})",
            ({ } name, null) => $" ({name})",
            (null, { } m) => $" for the {m}",
            _ => "",
        };
    }

    private static string Lower(string? text) =>
        string.IsNullOrEmpty(text) ? "unknown reason" : char.ToLowerInvariant(text[0]) + text[1..];
}
