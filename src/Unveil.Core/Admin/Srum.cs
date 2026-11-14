using System.Globalization;
using System.Text.RegularExpressions;
using Unveil.Core.Power;

namespace Unveil.Core.Admin;

/// <summary>One app's energy use in a time window, from SRUM.</summary>
public sealed record SrumAppEnergy
{
    public required string Name { get; init; }

    /// <summary>Energy in SRUM's own units (undocumented). Use <see cref="SharePercent"/> for display.</summary>
    public long Energy { get; init; }

    /// <summary>Share of all apps' energy in the window, 0-100.</summary>
    public double SharePercent { get; init; }

    /// <summary>Share of this app's energy used while it was in the background, 0-100.</summary>
    public double BackgroundPercent { get; init; }

    /// <summary>Where most of its energy went: "CPU", "Display", "Disk", "Network" or "Other".</summary>
    public string MainComponent { get; init; } = "CPU";

    public KnownOffender? Offender { get; init; }
}

/// <summary>Energy by app over one time window.</summary>
public sealed record EnergyWindow
{
    public DateTimeOffset From { get; init; }

    public DateTimeOffset To { get; init; }

    /// <summary>True when only time on battery was counted (false when the laptop was never on battery in the window).</summary>
    public bool OnBatteryOnly { get; init; }

    /// <summary>Energy assigned to apps (excludes energy Windows could not assign).</summary>
    public long TotalEnergy { get; init; }

    /// <summary>Share of all energy in the window that Windows could not assign to any app ("Unknown"), 0-100.</summary>
    public double UnassignedPercent { get; init; }

    public int AppCount { get; init; }

    /// <summary>Top apps by energy, highest first.</summary>
    public IReadOnlyList<SrumAppEnergy> Apps { get; init; } = [];
}

/// <summary>Per-app energy history from <c>powercfg /srumutil</c> (admin only).</summary>
public sealed record SrumSummary
{
    /// <summary>Oldest and newest record in the data (Windows keeps only a few days on some machines).</summary>
    public DateTimeOffset? DataFrom { get; init; }

    public DateTimeOffset? DataTo { get; init; }

    public EnergyWindow? Last24Hours { get; init; }

    public EnergyWindow? Last7Days { get; init; }
}

/// <summary>
/// Parses the CSV from <c>powercfg /srumutil /csv</c>. Columns are found by header name, so added or reordered
/// columns do not break it. Fields are separated by ", "; an app ID that itself contains ", " is re-joined.
/// </summary>
public static partial class SrumParser
{
    public const int TopApps = 15;

    private static readonly string[] Required = ["AppId", "TimeStamp", "OnBattery", "Foreground", "TotalEnergyConsumption"];

    private sealed record Row(string App, string? ExeName, DateTimeOffset Time, bool OnBattery, bool Foreground,
        long Total, long Cpu, long Display, long Disk, long Network)
    {
        public bool IsUnassigned => string.Equals(App, UnassignedId, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>SRUM's bucket for energy it could not assign to an app.</summary>
    private const string UnassignedId = "Unknown";

    /// <summary>Hardware energy meters (e.g. "EMI_RAPL_Package0_PKG"): readings, not apps, and they would double-count.</summary>
    private static bool IsMeter(string appId) => appId.StartsWith("EMI_", StringComparison.OrdinalIgnoreCase);

    public static SrumSummary Summarize(TextReader reader, DateTimeOffset now, CollectionLog? log = null)
    {
        var rows = Read(reader, log);
        if (rows.Count == 0)
        {
            return new SrumSummary();
        }

        return new SrumSummary
        {
            DataFrom = rows.Min(r => r.Time),
            DataTo = rows.Max(r => r.Time),
            Last24Hours = Window(rows, now.AddHours(-24), now),
            Last7Days = Window(rows, now.AddDays(-7), now),
        };
    }

    private static List<Row> Read(TextReader reader, CollectionLog? log)
    {
        var header = reader.ReadLine();
        if (header is null)
        {
            log?.Warning("SRUM", "The energy history file is empty.");
            return [];
        }

        var columns = header.TrimStart('﻿').Split(',').Select(c => c.Trim()).ToList();
        var index = columns.Select((name, i) => (name, i)).ToDictionary(x => x.name, x => x.i, StringComparer.OrdinalIgnoreCase);
        var missing = Required.Where(r => !index.ContainsKey(r)).ToList();
        if (missing.Count > 0)
        {
            log?.Warning("SRUM", $"The energy history has an unexpected format (missing {string.Join(", ", missing)}); skipped.");
            return [];
        }

        int Col(string name) => index.GetValueOrDefault(name, -1);
        var (cTime, cBattery, cForeground, cTotal) = (Col("TimeStamp"), Col("OnBattery"), Col("Foreground"), Col("TotalEnergyConsumption"));
        var (cCpu, cSoc, cDisplay, cDisk, cNet, cMbb) = (Col("CPUEnergyConsumption"), Col("SocEnergyConsumption"), Col("DisplayEnergyConsumption"),
            Col("DiskEnergyConsumption"), Col("NetworkEnergyConsumption"), Col("MBBEnergyConsumption"));

        var rows = new List<Row>();
        var bad = 0;
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            if (line.Length == 0)
            {
                continue;
            }

            var parts = line.Split(", ");
            if (parts.Length < columns.Count)
            {
                bad++;
                continue;
            }

            // An app ID containing ", " splits into extra parts at the front: join them back.
            var extra = parts.Length - columns.Count;
            var app = string.Join(", ", parts[..(extra + 1)]).Trim();
            var fields = parts[extra..];
            if (IsMeter(app))
            {
                continue;
            }

            if (!DateTimeOffset.TryParse(fields[cTime].Trim(), CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var time))
            {
                bad++;
                continue;
            }

            long Num(int c) => c >= 0 && long.TryParse(fields[c].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? Math.Max(0, v) : 0;
            bool Flag(int c) => string.Equals(fields[c].Trim(), "TRUE", StringComparison.OrdinalIgnoreCase);

            var (name, exe) = DisplayName(app);
            rows.Add(new Row(name, exe, time, Flag(cBattery), Flag(cForeground), Num(cTotal),
                Num(cCpu) + Num(cSoc), Num(cDisplay), Num(cDisk), Num(cNet) + Num(cMbb)));
        }

        if (bad > 0)
        {
            log?.Info("SRUM", $"{bad} energy history rows could not be read and were skipped.");
        }

        return rows;
    }

    private static EnergyWindow? Window(List<Row> all, DateTimeOffset from, DateTimeOffset to)
    {
        var inWindow = all.Where(r => r.Time > from && r.Time <= to).ToList();
        if (inWindow.Count == 0)
        {
            return null;
        }

        // Battery complaints are about time on battery; fall back to everything if it never ran on battery.
        var onBattery = inWindow.Where(r => r.OnBattery).ToList();
        var batteryOnly = onBattery.Sum(r => r.Total) > 0;
        var window = batteryOnly ? onBattery : inWindow;
        var unassigned = window.Where(r => r.IsUnassigned).Sum(r => r.Total);
        var rows = window.Where(r => !r.IsUnassigned).ToList();
        var total = rows.Sum(r => r.Total);

        var apps = rows
            .GroupBy(r => r.App, StringComparer.OrdinalIgnoreCase)
            .Select(g =>
            {
                var energy = g.Sum(r => r.Total);
                var parts = new (string Name, long Value)[]
                {
                    ("CPU", g.Sum(r => r.Cpu)), ("Display", g.Sum(r => r.Display)), ("Disk", g.Sum(r => r.Disk)), ("Network", g.Sum(r => r.Network)),
                };
                var main = parts.MaxBy(p => p.Value);
                return new SrumAppEnergy
                {
                    Name = g.First().App,
                    Energy = energy,
                    SharePercent = total > 0 ? energy * 100.0 / total : 0,
                    BackgroundPercent = energy > 0 ? g.Where(r => !r.Foreground).Sum(r => r.Total) * 100.0 / energy : 0,
                    MainComponent = main.Value > 0 ? main.Name : "Other",
                    // Packaged apps (e.g. "MSTeams") have no exe name; match on the app name instead.
                    Offender = KnownOffenders.Match(g.First().ExeName ?? g.First().App, 1),
                };
            })
            .Where(a => a.Energy > 0)
            .OrderByDescending(a => a.Energy)
            .ToList();

        return new EnergyWindow
        {
            From = rows.Min(r => r.Time),
            To = rows.Max(r => r.Time),
            OnBatteryOnly = batteryOnly,
            TotalEnergy = total,
            UnassignedPercent = total + unassigned > 0 ? unassigned * 100.0 / (total + unassigned) : 0,
            AppCount = apps.Count,
            Apps = apps.Take(TopApps).ToList(),
        };
    }

    /// <summary>
    /// Turns a SRUM app ID into a readable name, and returns the executable name (without extension) when there is one:
    /// <list type="bullet">
    /// <item>"\Device\HarddiskVolume3\...\chrome.exe" becomes "chrome"</item>
    /// <item>"...\svchost.exe [DcomLaunch] [x]" becomes "svchost (DcomLaunch, x)" (also when Windows cut off the last "]")</item>
    /// <item>"...\AnyDesk\AnyDesk" or "...\chcp.com" become "AnyDesk" / "chcp"</item>
    /// <item>"Microsoft.OutlookForWindows_1.2026.1026.100_x64__8wekyb3d8bbwe" becomes "Microsoft.OutlookForWindows" (all versions merge)</item>
    /// <item>anything else ("System", "MemCompression") is kept as-is</item>
    /// </list>
    /// </summary>
    internal static (string Name, string? Exe) DisplayName(string appId)
    {
        var id = appId.Trim();
        if (id.StartsWith('\\') || id.Contains(@":\", StringComparison.Ordinal))
        {
            var segment = id[(id.LastIndexOf('\\') + 1)..];
            var exeAt = segment.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
            if (exeAt > 0)
            {
                var exe = segment[..exeAt];
                var services = ServiceTag().Matches(segment[(exeAt + 4)..]).Select(m => m.Groups[1].Value.Trim()).Where(v => v.Length > 0).ToList();
                return (services.Count > 0 ? $"{exe} ({string.Join(", ", services)})" : exe, exe);
            }

            var name = Path.GetFileNameWithoutExtension(segment);
            return (name.Length > 0 ? name : segment, name.Length > 0 ? name : null);
        }

        var package = PackageName().Match(id);
        if (!package.Success)
        {
            return (id, null);
        }

        // "5319275A.WhatsAppDesktop" -> "WhatsAppDesktop": drop the 8-hex-digit publisher code some Store apps carry.
        var packageName = package.Groups["name"].Value;
        return (PublisherCode().Replace(packageName, ""), null);
    }

    [GeneratedRegex(@"^[0-9A-F]{8}\.(?=.)", RegexOptions.IgnoreCase)]
    private static partial Regex PublisherCode();

    [GeneratedRegex(@"^(?<name>[^_\\\s]+)_\d+(\.\d+){1,3}_")]
    private static partial Regex PackageName();

    /// <summary>"[name]" or a final "[name" that Windows cut off.</summary>
    [GeneratedRegex(@"\[([^\[\]]*)\]?")]
    private static partial Regex ServiceTag();
}
