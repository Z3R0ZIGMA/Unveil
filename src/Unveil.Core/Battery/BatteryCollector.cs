using System.Text.Json.Serialization;
using Unveil.Core.SystemInfo;

namespace Unveil.Core.Battery;

public sealed record BatteryCollectionOptions
{
    /// <summary>Skip WMI (to test the battery-report-only fallback).</summary>
    public bool SkipWmi { get; init; }

    /// <summary>Skip powercfg (to test the WMI-only fallback).</summary>
    public bool SkipBatteryReport { get; init; }
}

/// <summary>Everything collected about the machine's batteries.</summary>
public sealed record BatterySnapshot
{
    public DateTimeOffset CollectedAt { get; init; }

    public bool IsAdministrator { get; init; }

    public required SystemIdentity System { get; init; }

    public IReadOnlyList<BatteryInfo> Batteries { get; init; } = [];

    /// <summary>Only set when there is more than one battery.</summary>
    public BatteryTotals? Combined { get; init; }

    public BatteryReport? Report { get; init; }

    /// <summary><c>Win32_Battery.Status</c> per battery, e.g. "OK", "Pred Fail", "Service".</summary>
    public IReadOnlyList<string> WindowsBatteryStatus { get; init; } = [];

    public IReadOnlyList<LogEntry> Log { get; init; } = [];

    public bool HasBattery => Batteries.Count > 0;

    /// <summary>Statuses meaning Windows thinks the battery needs service or is failing.</summary>
    public static readonly IReadOnlyList<string> ServiceStatuses = ["Pred Fail", "Service", "Degraded", "Error", "NonRecover"];

    /// <summary>The first Windows status that means the battery needs service, if any.</summary>
    public string? WindowsServiceStatus =>
        WindowsBatteryStatus.FirstOrDefault(s => ServiceStatuses.Contains(s, StringComparer.OrdinalIgnoreCase));

    /// <summary>Raw source data, kept for saving scrubbed samples. Not part of the result model.</summary>
    [JsonIgnore]
    public RawBatteryData Raw { get; init; } = new();
}

public sealed record RawBatteryData
{
    public IReadOnlyList<WmiBatteryData> Wmi { get; init; } = [];

    public BatteryReportResult? BatteryReport { get; init; }
}

public sealed class BatteryCollector(
    IWmiBatterySource wmiSource,
    IBatteryReportSource reportSource,
    ISystemIdentitySource systemSource)
{
    public static BatteryCollector CreateDefault() =>
        new(new WmiBatteryReader(), new PowercfgBatteryReportSource(), new WmiSystemIdentityReader());

    public BatterySnapshot Collect(BatteryCollectionOptions? options = null)
    {
        options ??= new BatteryCollectionOptions();
        var log = new CollectionLog();

        var system = Guard(log, "System", () => systemSource.Read(log)) ?? new SystemIdentity();

        IReadOnlyList<WmiBatteryData> wmi = [];
        IReadOnlyList<string> windowsStatus = [];
        if (options.SkipWmi)
        {
            log.Info("WMI", "Skipped (--skip-wmi).");
        }
        else
        {
            wmi = Guard(log, "WMI", () => wmiSource.Read(log)) ?? [];
            windowsStatus = Guard(log, "WMI", () => wmiSource.ReadWindowsStatus(log)) ?? [];
        }

        BatteryReportResult? reportResult = null;
        if (options.SkipBatteryReport)
        {
            log.Info("Battery report", "Skipped (--skip-report).");
        }
        else
        {
            reportResult = Guard(log, "Battery report", () => reportSource.Load(log));
        }

        var report = reportResult?.Report;
        var reportBatteries = report?.Batteries ?? [];

        if (wmi.Count == 0 && reportBatteries.Count > 0)
        {
            log.Info("WMI", "No battery data from WMI; using the battery report only (no live charge or rate data).");
        }
        else if (wmi.Count > 0 && report is null && !options.SkipBatteryReport)
        {
            log.Info("Battery report", "Battery report unavailable; using WMI only (design capacity may be missing).");
        }

        var batteries = BatteryMerger.Merge(wmi, reportBatteries, log);

        // Fill identity gaps from the battery report's system section.
        if (report is not null)
        {
            system = system with
            {
                Manufacturer = system.Manufacturer ?? report.System.Manufacturer,
                Model = system.Model ?? report.System.ProductName,
                BiosVersion = system.BiosVersion ?? report.System.BiosVersion,
            };
        }

        return new BatterySnapshot
        {
            CollectedAt = DateTimeOffset.Now,
            IsAdministrator = WmiBatteryReader.IsAdministrator(),
            System = system,
            Batteries = batteries,
            Combined = BatteryMerger.Combine(batteries),
            Report = report,
            WindowsBatteryStatus = windowsStatus,
            Log = log.Entries,
            Raw = new RawBatteryData { Wmi = wmi, BatteryReport = reportResult },
        };
    }

    /// <summary>Last line of defence: a collector bug must not stop the rest of the run.</summary>
    private static T? Guard<T>(CollectionLog log, string source, Func<T> action)
        where T : class?
    {
        try
        {
            return action();
        }
#pragma warning disable CA1031 // Deliberately catch everything: degrade gracefully, log, keep going.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            log.Error(source, $"Unexpected error, section skipped: {ex.GetType().Name}: {ex.Message}");
            return null;
        }
    }
}
