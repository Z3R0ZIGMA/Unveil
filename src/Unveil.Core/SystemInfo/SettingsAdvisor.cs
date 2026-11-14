using Unveil.Core.Verdict;
using static Unveil.Core.Formatting;

namespace Unveil.Core.SystemInfo;

/// <summary>One checked setting, ready to show.</summary>
/// <param name="Key">Stable ID, e.g. "brightness".</param>
/// <param name="Name">Plain-English label, e.g. "Screen brightness".</param>
/// <param name="Value">Current value as text, e.g. "100%".</param>
/// <param name="Severity">Good, Warning or Neutral (could not be read / for information).</param>
/// <param name="Finding">Why it matters, when it is a warning.</param>
/// <param name="Advice">What to do, when it is a warning.</param>
public sealed record SettingCheck(string Key, string Name, string Value, Severity Severity, string? Finding = null, string? Advice = null);

/// <summary>Thresholds for the settings checks. Made configurable in Phase 7.</summary>
public sealed record SettingsThresholds
{
    public int HighBrightnessPercent { get; init; } = 80;

    public int HighRefreshRateHz { get; init; } = 120;

    public TimeSpan LongScreenOffOnBattery { get; init; } = TimeSpan.FromMinutes(10);

    public TimeSpan LongSleepOnBattery { get; init; } = TimeSpan.FromMinutes(30);

    public int ManyStartupApps { get; init; } = 8;

    public TimeSpan LongUptime { get; init; } = TimeSpan.FromDays(7);

    public static SettingsThresholds Default { get; } = new();
}

/// <summary>Turns raw <see cref="PowerSettings"/> into checks and advice (plan.md 3.5). Pure: no I/O.</summary>
public static class SettingsAdvisor
{
    public static IReadOnlyList<SettingCheck> Evaluate(PowerSettings s, SettingsThresholds? t = null)
    {
        t ??= SettingsThresholds.Default;
        var checks = new List<SettingCheck>();

        // Power mode
        var modeName = s.PowerMode switch
        {
            PowerMode.BestPowerEfficiency => "Best power efficiency",
            PowerMode.Balanced => "Balanced",
            PowerMode.BetterPerformance => "Better performance",
            PowerMode.BestPerformance => "Best performance",
            _ => null,
        };
        var modeValue = modeName is null ? "Not reported" : modeName + (s.PowerModeIsForBattery ? " (on battery)" : " (plugged in)");
        checks.Add(s.PowerMode == PowerMode.BestPerformance && s.PowerModeIsForBattery
            ? new SettingCheck("power-mode", "Power mode", modeValue, Severity.Warning,
                "The power mode on battery is Best performance, which keeps the processor running fast.",
                "Set the power mode to Balanced (or Best power efficiency) on battery.")
            : new SettingCheck("power-mode", "Power mode", modeValue, modeName is null ? Severity.Neutral : Severity.Good));

        // Brightness
        checks.Add(s.BrightnessPercent is not { } brightness
            ? new SettingCheck("brightness", "Screen brightness", "Not reported", Severity.Neutral)
            : brightness >= t.HighBrightnessPercent
                ? new SettingCheck("brightness", "Screen brightness", $"{brightness}%", Severity.Warning,
                    $"Screen brightness is {brightness}%. The screen is often the biggest power user on a laptop.",
                    "Lower the screen brightness to about 50% on battery, or turn on automatic brightness.")
                : new SettingCheck("brightness", "Screen brightness", $"{brightness}%", Severity.Good));

        // Refresh rate
        checks.Add(s.RefreshRateHz is not { } hz
            ? new SettingCheck("refresh-rate", "Refresh rate", "Not reported", Severity.Neutral)
            : hz >= t.HighRefreshRateHz
                ? new SettingCheck("refresh-rate", "Refresh rate", $"{hz} Hz", Severity.Warning,
                    $"The screen runs at {hz} Hz, which uses noticeably more power than 60 Hz.",
                    "Use 60 Hz on battery, or turn on Dynamic refresh rate (Settings > Display > Advanced display).")
                : new SettingCheck("refresh-rate", "Refresh rate", $"{hz} Hz", Severity.Good));

        // Battery saver
        checks.Add(s.BatterySaverThresholdPercent switch
        {
            null => new SettingCheck("battery-saver", "Battery saver", OnOff(s.BatterySaverOn), Severity.Neutral),
            0 => new SettingCheck("battery-saver", "Battery saver", "Never turns on by itself", Severity.Warning,
                "Battery saver is set to never turn on by itself.",
                "Set battery saver to turn on at 20-30% (Settings > System > Power & battery)."),
            var pct => new SettingCheck("battery-saver", "Battery saver",
                $"Turns on at {pct}%{(s.BatterySaverOn == true ? " (on now)" : "")}", Severity.Good),
        });

        // Screen off and sleep on battery
        checks.Add(Timeout("screen-off", "Screen turns off (battery)", s.ScreenOffOnBattery, t.LongScreenOffOnBattery,
            "On battery the screen is set to never turn off by itself.",
            "Set the screen to turn off after a few minutes on battery (it is set to never turn off).",
            "Set the screen to turn off after 5 minutes or less on battery."));
        checks.Add(Timeout("sleep", "Sleeps after (battery)", s.SleepOnBattery, t.LongSleepOnBattery,
            "On battery the laptop is set to never go to sleep by itself.",
            "Set the laptop to sleep after some minutes of inactivity on battery (it is set to never sleep).",
            "Set the laptop to sleep after 15 minutes or less on battery."));

        // Startup apps
        var enabled = s.StartupApps.Where(a => a.Enabled).Select(a => a.Name).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var disabled = s.StartupApps.Count(a => !a.Enabled);
        var startupValue = $"{enabled.Count} on" + (disabled > 0 ? $", {disabled} off" : "");
        checks.Add(enabled.Count >= t.ManyStartupApps
            ? new SettingCheck("startup-apps", "Startup apps", startupValue, Severity.Warning,
                $"{enabled.Count} apps start with Windows: {string.Join(", ", enabled.Take(8))}{(enabled.Count > 8 ? ", ..." : "")}.",
                $"Turn off the startup apps the customer does not need (Task Manager > Startup apps); {enabled.Count} start with Windows.")
            : new SettingCheck("startup-apps", "Startup apps", startupValue, Severity.Good,
                enabled.Count > 0 ? $"Starting with Windows: {string.Join(", ", enabled)}." : null));

        // Discrete GPU
        if (s.DiscreteGpu is { } gpu)
        {
            checks.Add(s.DiscreteGpuActive == true
                ? new SettingCheck("discrete-gpu", "Graphics card", $"{gpu}: busy ({Percent(s.DiscreteGpuBusyPercent, "0")})", Severity.Warning,
                    $"The separate graphics card ({gpu}) was busy, and it uses far more power than the built-in graphics.",
                    $"Set apps that don't need it to \"Power saving\" in Settings > Display > Graphics, so the {gpu} can switch off.")
                : new SettingCheck("discrete-gpu", "Graphics card", $"{gpu}: {(s.DiscreteGpuActive == false ? "idle" : "activity not known")}",
                    s.DiscreteGpuActive == false ? Severity.Good : Severity.Neutral));
        }
        else
        {
            checks.Add(new SettingCheck("discrete-gpu", "Graphics card",
                s.GraphicsAdapters.Count > 0 ? $"{string.Join(", ", s.GraphicsAdapters)} (built-in only)" : "Not reported", Severity.Good));
        }

        // Uptime
        if (s.Uptime is { } up)
        {
            var days = (int)up.TotalDays;
            checks.Add(up >= t.LongUptime
                ? new SettingCheck("uptime", "Last restart", $"{days} days ago", Severity.Warning,
                    $"Windows has not restarted in {days} days; background tasks and leaks build up over time.",
                    "Restart the laptop (Start > Power > Restart; Shut down does not count with Fast Startup).")
                : new SettingCheck("uptime", "Last restart", Ago(up), Severity.Good));
        }

        // Windows Update
        checks.Add(s.UpdateRestartPending switch
        {
            true => new SettingCheck("updates", "Windows Update", "Waiting for a restart", Severity.Warning,
                "Windows Update is waiting for a restart to finish installing.",
                "Restart to finish installing updates; update work runs in the background until then."),
            false => new SettingCheck("updates", "Windows Update", "No restart pending", Severity.Good),
            null => new SettingCheck("updates", "Windows Update", "Not reported", Severity.Neutral),
        });

        return checks;
    }

    private static SettingCheck Timeout(string key, string name, TimeSpan? value, TimeSpan longest, string neverFinding, string neverAdvice, string longAdvice)
    {
        if (value is not { } v)
        {
            return new SettingCheck(key, name, "Not reported", Severity.Neutral);
        }

        if (v == TimeSpan.Zero)
        {
            return new SettingCheck(key, name, "Never", Severity.Warning, neverFinding, neverAdvice);
        }

        var text = v.TotalMinutes >= 1 ? $"After {(int)v.TotalMinutes} min" : $"After {(int)v.TotalSeconds} s";
        return v > longest
            ? new SettingCheck(key, name, text, Severity.Warning, $"{name}: {text.ToLowerInvariant()}, which is long on battery.", longAdvice)
            : new SettingCheck(key, name, text, Severity.Good);
    }

    private static string Ago(TimeSpan t) => t.TotalHours < 24
        ? $"{(int)t.TotalHours} hour{((int)t.TotalHours == 1 ? "" : "s")} ago"
        : $"{(int)t.TotalDays} day{((int)t.TotalDays == 1 ? "" : "s")} ago";

    private static string OnOff(bool? on) => on switch { true => "On", false => "Off", null => "Not reported" };
}
