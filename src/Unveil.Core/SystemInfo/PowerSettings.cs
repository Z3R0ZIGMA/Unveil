namespace Unveil.Core.SystemInfo;

/// <summary>Windows 11 power mode (Settings > Power > Power mode), or the Windows 10 battery slider.</summary>
public enum PowerMode
{
    Unknown,
    BestPowerEfficiency,
    Balanced,
    BetterPerformance,
    BestPerformance,
}

/// <param name="Name">Registry value or shortcut name, e.g. "OneDrive".</param>
/// <param name="Location">"Run (this user)", "Run (all users)", "Startup folder" etc.</param>
/// <param name="Enabled">False when switched off in Task Manager / Settings > Apps > Startup.</param>
public sealed record StartupApp(string Name, string Location, bool Enabled);

/// <summary>The power-related settings Unveil checks (plan.md 3.5). Null means "could not be read".</summary>
public sealed record PowerSettings
{
    public bool? OnBattery { get; init; }

    public string? PowerPlanName { get; init; }

    public Guid? PowerPlan { get; init; }

    public PowerMode PowerMode { get; init; }

    /// <summary>True when <see cref="PowerMode"/> is the battery setting (read on battery, or stored for battery).</summary>
    public bool PowerModeIsForBattery { get; init; }

    public bool? BatterySaverOn { get; init; }

    /// <summary>Battery saver turns on at this charge %; 0 means never.</summary>
    public int? BatterySaverThresholdPercent { get; init; }

    public int? BrightnessPercent { get; init; }

    /// <summary>Highest refresh rate in use on any display.</summary>
    public int? RefreshRateHz { get; init; }

    /// <summary>Turn off the screen after this long on battery; <see cref="TimeSpan.Zero"/> means never.</summary>
    public TimeSpan? ScreenOffOnBattery { get; init; }

    /// <summary>Sleep after this long on battery; <see cref="TimeSpan.Zero"/> means never.</summary>
    public TimeSpan? SleepOnBattery { get; init; }

    public IReadOnlyList<StartupApp> StartupApps { get; init; } = [];

    public int EnabledStartupApps => StartupApps.Count(a => a.Enabled);

    public IReadOnlyList<string> GraphicsAdapters { get; init; } = [];

    /// <summary>Name of the discrete (separate) graphics card, if the laptop has one.</summary>
    public string? DiscreteGpu { get; init; }

    /// <summary>Whether the discrete GPU was busy when checked; null when unknown or there is none.</summary>
    public bool? DiscreteGpuActive { get; init; }

    public double? DiscreteGpuBusyPercent { get; init; }

    /// <summary>Time since Windows last really restarted (Fast Startup "shut down" does not reset it).</summary>
    public TimeSpan? Uptime { get; init; }

    /// <summary>Windows Update is waiting for a restart to finish installing.</summary>
    public bool? UpdateRestartPending { get; init; }

    public DateTimeOffset CollectedAt { get; init; }
}
