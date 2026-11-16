using System.Diagnostics;
using System.Management;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using Microsoft.Win32;
using Unveil.Core.Wmi;

namespace Unveil.Core.SystemInfo;

public interface IPowerSettingsSource
{
    /// <summary>Reads everything it can; anything that fails is logged and left null. Never throws.</summary>
    PowerSettings Read(CollectionLog log);
}

/// <summary>
/// Reads power settings as a standard user: the Windows power API (powrprof.dll), WMI, the registry and
/// performance counters. No powercfg console text is parsed.
/// </summary>
public sealed partial class PowerSettingsReader : IPowerSettingsSource
{
    private const string Source = "Settings";

    // Power setting GUIDs (winnt.h).
    private static readonly Guid SubVideo = new("7516b95f-f776-4464-8c53-06167f40cc99");
    private static readonly Guid VideoIdle = new("3c0bc021-c8a8-4e07-a973-6b14cbcb2b7e");
    private static readonly Guid SubSleep = new("238c9fa8-0aad-41ed-83f4-97be242c8f20");
    private static readonly Guid StandbyIdle = new("29f6c1db-86da-48c5-9fdb-f2b67b1f44da");
    private static readonly Guid SubEnergySaver = new("de830923-a562-41af-a086-e3a2c6bad2da");
    private static readonly Guid EnergySaverThreshold = new("e69653ca-cf7f-4f05-aa73-cb833fa90ad4");

    public PowerSettings Read(CollectionLog log)
    {
        var status = Try(log, "battery saver state", ReadPowerStatus);
        var scheme = Try(log, "power plan", ActiveScheme);
        var (mode, forBattery) = Try(log, "power mode", () => ReadPowerMode(status?.OnBattery));
        var adapters = Try(log, "graphics adapters", ReadAdapters) ?? [];
        var gpu = Try(log, "graphics activity", () => GpuActivity.Check(adapters.Select(a => a.Name).ToList()));

        return new PowerSettings
        {
            CollectedAt = DateTimeOffset.Now,
            OnBattery = status?.OnBattery,
            BatterySaverOn = status?.SaverOn,
            PowerPlan = scheme,
            PowerPlanName = scheme is { } s ? Try(log, "power plan name", () => FriendlyName(s)) : null,
            PowerMode = mode,
            PowerModeIsForBattery = forBattery,
            BatterySaverThresholdPercent = scheme is { } s1 ? Try(log, "battery saver threshold", () => (int?)ReadDc(s1, SubEnergySaver, EnergySaverThreshold)) : null,
            ScreenOffOnBattery = scheme is { } s2 ? Try(log, "screen timeout", () => Seconds(ReadDc(s2, SubVideo, VideoIdle))) : null,
            SleepOnBattery = scheme is { } s3 ? Try(log, "sleep timeout", () => Seconds(ReadDc(s3, SubSleep, StandbyIdle))) : null,
            BrightnessPercent = Try(log, "screen brightness", ReadBrightness),
            RefreshRateHz = adapters.Select(a => a.RefreshRate).Where(r => r is > 0).Max(),
            StartupApps = Try(log, "startup apps", StartupApps.Read) ?? [],
            GraphicsAdapters = adapters.Select(a => a.Name).ToList(),
            DiscreteGpu = gpu?.DiscreteName,
            DiscreteGpuActive = gpu?.Active,
            DiscreteGpuBusyPercent = gpu?.BusyPercent,
            Uptime = TimeSpan.FromMilliseconds(Environment.TickCount64),
            UpdateRestartPending = Try(log, "Windows Update state", ReadUpdateRestartPending),
        };
    }

    private static T? Try<T>(CollectionLog log, string what, Func<T?> read)
    {
        try
        {
            return read();
        }
#pragma warning disable CA1031 // Each setting is optional: log and carry on.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            log.Info(Source, $"Could not read the {what}: {ex.Message}");
            return default;
        }
    }

    // ------------------------------------------------------------ power API

    private sealed record PowerStatus(bool? OnBattery, bool SaverOn);

    private static PowerStatus? ReadPowerStatus()
    {
        if (!GetSystemPowerStatus(out var s))
        {
            return null;
        }

        bool? onBattery = s.AcLineStatus switch { 0 => true, 1 => false, _ => null };
        return new PowerStatus(onBattery, s.SystemStatusFlag == 1);
    }

    private static Guid? ActiveScheme()
    {
        if (PowerGetActiveScheme(IntPtr.Zero, out var ptr) != 0)
        {
            return null;
        }

        try
        {
            return Marshal.PtrToStructure<Guid>(ptr);
        }
        finally
        {
            LocalFree(ptr);
        }
    }

    private static string? FriendlyName(Guid scheme)
    {
        uint size = 0;
        PowerReadFriendlyName(IntPtr.Zero, ref scheme, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, ref size);
        if (size == 0)
        {
            return null;
        }

        var buffer = Marshal.AllocHGlobal((int)size);
        try
        {
            return PowerReadFriendlyName(IntPtr.Zero, ref scheme, IntPtr.Zero, IntPtr.Zero, buffer, ref size) == 0
                ? Marshal.PtrToStringUni(buffer)
                : null;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static uint? ReadDc(Guid scheme, Guid subgroup, Guid setting) =>
        PowerReadDCValueIndex(IntPtr.Zero, ref scheme, ref subgroup, ref setting, out var value) == 0 ? value : null;

    private static TimeSpan? Seconds(uint? value) => value is { } v ? TimeSpan.FromSeconds(v) : null;

    /// <summary>
    /// The battery power mode is stored in the registry when it differs from the default; otherwise the effective
    /// overlay is what applies right now (and is the battery mode only when on battery).
    /// </summary>
    private static (PowerMode, bool) ReadPowerMode(bool? onBattery)
    {
        using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Power\User\PowerSchemes");
        if (key?.GetValue("ActiveOverlayDcPowerScheme") is string dc && Guid.TryParse(dc, out var dcGuid))
        {
            return (ModeFromOverlay(dcGuid), true);
        }

        return PowerGetEffectiveOverlayScheme(out var overlay) == 0
            ? (ModeFromOverlay(overlay), onBattery == true)
            : (PowerMode.Unknown, false);
    }

    /// <summary>Overlay scheme GUIDs used by Windows 10/11 for the power mode slider.</summary>
    internal static PowerMode ModeFromOverlay(Guid overlay) => overlay.ToString().ToLowerInvariant() switch
    {
        "00000000-0000-0000-0000-000000000000" or "381b4222-f694-41f0-9685-ff5bb260df2e" => PowerMode.Balanced,
        "961cc777-2547-4f9d-8174-7d86181b8a7a" => PowerMode.BestPowerEfficiency,
        "3af9b8d9-7c97-431d-ad78-34a8bfea439f" => PowerMode.BetterPerformance,
        "ded574b5-45a0-4f42-8737-46345c09c238" => PowerMode.BestPerformance,
        _ => PowerMode.Unknown,
    };

    // ------------------------------------------------------------ WMI, registry

    private static int? ReadBrightness() =>
        WmiQuery.Select(@"root\wmi", "SELECT CurrentBrightness, Active FROM WmiMonitorBrightness", o =>
                (Active: WmiQuery.Bool(o, "Active") != false, Level: WmiQuery.Int(o, "CurrentBrightness")))
            .Where(m => m.Active && m.Level is not null)
            .Select(m => m.Level)
            .FirstOrDefault();

    private sealed record Adapter(string Name, int? RefreshRate);

    private static List<Adapter> ReadAdapters() =>
        WmiQuery.Select(@"root\cimv2", "SELECT Name, CurrentRefreshRate FROM Win32_VideoController", o =>
                new Adapter(WmiQuery.String(o, "Name") ?? "Unknown", WmiQuery.Int(o, "CurrentRefreshRate")))
            .Where(a => !a.Name.Contains("Basic Render", StringComparison.OrdinalIgnoreCase)
                        && !a.Name.Contains("Basic Display", StringComparison.OrdinalIgnoreCase))
            .ToList();

    private static bool? ReadUpdateRestartPending()
    {
        string[] keys =
        [
            @"SOFTWARE\Microsoft\Windows\CurrentVersion\WindowsUpdate\Auto Update\RebootRequired",
            @"SOFTWARE\Microsoft\Windows\CurrentVersion\Component Based Servicing\RebootPending",
        ];
        return keys.Any(k =>
        {
            using var key = Registry.LocalMachine.OpenSubKey(k);
            return key is not null;
        });
    }

    // ------------------------------------------------------------ P/Invoke

    [StructLayout(LayoutKind.Sequential)]
    private struct SystemPowerStatus
    {
        public byte AcLineStatus;
        public byte BatteryFlag;
        public byte BatteryLifePercent;
        public byte SystemStatusFlag;
        public int BatteryLifeTime;
        public int BatteryFullLifeTime;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSystemPowerStatus(out SystemPowerStatus status);

    [DllImport("powrprof.dll")]
    private static extern uint PowerGetActiveScheme(IntPtr rootPowerKey, out IntPtr activePolicyGuid);

    [DllImport("powrprof.dll")]
    private static extern uint PowerReadDCValueIndex(IntPtr rootPowerKey, ref Guid scheme, ref Guid subGroup, ref Guid setting, out uint value);

    [DllImport("powrprof.dll")]
    private static extern uint PowerReadFriendlyName(IntPtr rootPowerKey, ref Guid scheme, IntPtr subGroup, IntPtr setting, IntPtr buffer, ref uint bufferSize);

    [DllImport("powrprof.dll")]
    private static extern uint PowerGetEffectiveOverlayScheme(out Guid overlay);

    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr handle);
}

/// <summary>Apps that start with Windows: the Run keys and Startup folders, with their on/off state from StartupApproved.</summary>
public static class StartupApps
{
    private const string Approved = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\";

    public static IReadOnlyList<StartupApp> Read()
    {
        var apps = new List<StartupApp>();
        AddRunKey(apps, Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Run", "Run (this user)", Approved + "Run");
        AddRunKey(apps, Registry.LocalMachine, @"Software\Microsoft\Windows\CurrentVersion\Run", "Run (all users)", Approved + "Run");
        AddRunKey(apps, Registry.LocalMachine, @"Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Run", "Run (all users, 32-bit)", Approved + "Run32");
        AddFolder(apps, Environment.GetFolderPath(Environment.SpecialFolder.Startup), "Startup folder (this user)", Registry.CurrentUser);
        AddFolder(apps, Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup), "Startup folder (all users)", Registry.LocalMachine);
        return apps;
    }

    private static void AddRunKey(List<StartupApp> apps, RegistryKey hive, string path, string location, string approvedPath)
    {
        using var key = hive.OpenSubKey(path);
        if (key is null)
        {
            return;
        }

        using var approved = hive.OpenSubKey(approvedPath);
        foreach (var name in key.GetValueNames().Where(n => n.Length > 0))
        {
            var description = Description(key.GetValue(name) as string);
            apps.Add(new StartupApp(DisplayName(name, description), location, IsEnabled(approved?.GetValue(name) as byte[])));
        }
    }

    /// <summary>Generic hosts whose description says nothing about the app they start.</summary>
    private static readonly string[] GenericHosts = ["rundll32", "cmd", "powershell", "pwsh", "wscript", "cscript", "explorer", "conhost", "msiexec"];

    /// <summary>The file description of the exe a Run entry starts, e.g. "Google Chrome"; null when unknown.</summary>
    private static string? Description(string? command)
    {
        if (Power.AppNames.ExePath(Environment.ExpandEnvironmentVariables(command ?? "")) is not { } exe
            || GenericHosts.Contains(Path.GetFileNameWithoutExtension(exe), StringComparer.OrdinalIgnoreCase))
        {
            return null;
        }

        try
        {
            return File.Exists(exe) ? Power.AppNames.Clean(System.Diagnostics.FileVersionInfo.GetVersionInfo(exe).FileDescription) : null;
        }
#pragma warning disable CA1031 // Unreadable file: use the entry's name.
        catch (Exception)
#pragma warning restore CA1031
        {
            return null;
        }
    }

    /// <summary>
    /// What to call a Run entry. Its exe's description when there is one ("Google Chrome"); otherwise its name without
    /// the ID some browsers add: "GoogleChromeAutoLaunch_5C2FF762..." becomes "GoogleChrome".
    /// </summary>
    internal static string DisplayName(string valueName, string? description)
    {
        if (!string.IsNullOrWhiteSpace(description))
        {
            return description.Trim();
        }

        var name = Regex.Replace(valueName.Trim(), "_[0-9A-Fa-f]{16,}$", "");
        name = Regex.Replace(name, "AutoLaunch$", "", RegexOptions.IgnoreCase);
        return name.Length > 0 ? name : valueName;
    }

    private static void AddFolder(List<StartupApp> apps, string folder, string location, RegistryKey hive)
    {
        if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
        {
            return;
        }

        using var approved = hive.OpenSubKey(Approved + "StartupFolder");
        foreach (var file in Directory.EnumerateFiles(folder).Where(f => !Path.GetFileName(f).Equals("desktop.ini", StringComparison.OrdinalIgnoreCase)))
        {
            var name = Path.GetFileName(file);
            apps.Add(new StartupApp(Path.GetFileNameWithoutExtension(file), location, IsEnabled(approved?.GetValue(name) as byte[])));
        }
    }

    /// <summary>
    /// StartupApproved values: the first byte is even (02, 06) when enabled and odd (03, 07) when disabled.
    /// No value means it was never switched off.
    /// </summary>
    internal static bool IsEnabled(byte[]? approved) => approved is not { Length: > 0 } || approved[0] % 2 == 0;
}

/// <summary>Finds a discrete GPU and whether it is busy, from the GPU performance counters.</summary>
public static class GpuActivity
{
    /// <summary>An adapter with at least this much dedicated (own) memory in use is a discrete GPU.</summary>
    public const long DiscreteDedicatedBytes = 64L * 1024 * 1024;

    public sealed record Result(string? DiscreteName, bool? Active, double? BusyPercent);

    public static Result Check(IReadOnlyList<string> adapterNames)
    {
        var named = adapterNames.FirstOrDefault(LooksDiscrete);
        Dictionary<string, long> dedicated;
        Dictionary<string, TimeSpan> before;
        try
        {
            dedicated = ReadByLuid("GPU Adapter Memory", "dedicated usage", v => v);
            before = ReadByLuid("GPU Engine", "running time", TimeSpan.FromTicks);
        }
        catch (Exception ex) when (ex is InvalidOperationException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            return new Result(named, null, null);
        }

        var clock = Stopwatch.StartNew();
        Thread.Sleep(1000);
        var after = ReadByLuid("GPU Engine", "running time", TimeSpan.FromTicks);
        var busy = Busy(before, after, clock.Elapsed);
        return Classify(named, dedicated, busy);
    }

    /// <summary>Pure decision, for tests: which adapter is discrete, and was it busy.</summary>
    internal static Result Classify(string? namedDiscrete, IReadOnlyDictionary<string, long> dedicatedByLuid, IReadOnlyDictionary<string, double> busyByLuid)
    {
        var discreteLuids = dedicatedByLuid.Where(kv => kv.Value >= DiscreteDedicatedBytes).Select(kv => kv.Key).ToList();
        if (namedDiscrete is null && discreteLuids.Count == 0)
        {
            return new Result(null, null, null);
        }

        if (discreteLuids.Count == 0)
        {
            return new Result(namedDiscrete, null, null); // known by name, but its counters cannot be told apart
        }

        var percent = discreteLuids.Max(l => busyByLuid.GetValueOrDefault(l));
        return new Result(namedDiscrete ?? "Discrete graphics", percent >= 1, Math.Round(percent, 1));
    }

    /// <summary>NVIDIA, AMD Radeon RX/Pro and Intel Arc A-series are separate graphics cards.</summary>
    internal static bool LooksDiscrete(string name) =>
        name.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase)
        || name.Contains("Radeon RX", StringComparison.OrdinalIgnoreCase)
        || name.Contains("Radeon Pro", StringComparison.OrdinalIgnoreCase)
        || System.Text.RegularExpressions.Regex.IsMatch(name, @"Arc\(?T?M?\)?\s+A\d{3}", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

    private static Dictionary<string, double> Busy(Dictionary<string, TimeSpan> before, Dictionary<string, TimeSpan> after, TimeSpan elapsed) =>
        after.ToDictionary(kv => kv.Key, kv =>
            Math.Min(100, Math.Max(0, (kv.Value - before.GetValueOrDefault(kv.Key)).TotalMilliseconds) / Math.Max(1, elapsed.TotalMilliseconds) * 100));

    private static Dictionary<string, T> ReadByLuid<T>(string category, string counter, Func<long, T> convert)
        where T : struct
    {
        var result = new Dictionary<string, T>();
        var values = new PerformanceCounterCategory(category).ReadCategory()[counter];
        if (values is null)
        {
            return result;
        }

        foreach (InstanceData instance in values.Values)
        {
            if (Luid(instance.InstanceName) is not { } luid)
            {
                continue;
            }

            // Engines of one adapter are added up (GPU Engine has one instance per engine per process).
            result[luid] = result.TryGetValue(luid, out var existing) ? Add(existing, convert(instance.RawValue)) : convert(instance.RawValue);
        }

        return result;
    }

    private static T Add<T>(T a, T b) => (a, b) switch
    {
        (TimeSpan x, TimeSpan y) => (T)(object)(x + y),
        (long x, long y) => (T)(object)Math.Max(x, y),
        _ => b,
    };

    /// <summary>"luid_0x00000000_0x00010174_phys_0" or "pid_1_luid_0x00000000_0x00010174_phys_0_eng_3" -> "0x00000000_0x00010174".</summary>
    internal static string? Luid(string instance)
    {
        var at = instance.IndexOf("luid_", StringComparison.Ordinal);
        if (at < 0)
        {
            return null;
        }

        var parts = instance[(at + 5)..].Split('_');
        return parts.Length >= 2 ? $"{parts[0]}_{parts[1]}" : null;
    }
}
