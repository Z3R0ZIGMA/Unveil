using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace Unveil.Core.Power;

/// <summary>Turns executable names into friendly names (the file description, e.g. "Google Chrome").</summary>
public static class AppNames
{
    /// <summary>Generic Windows hosts whose file description says nothing useful.</summary>
    private static readonly HashSet<string> KeepExeName = new(StringComparer.OrdinalIgnoreCase)
    {
        "svchost", "dllhost", "rundll32", "conhost", "System", "Registry", "Memory Compression", "smss", "csrss", "wininit",
        "services", "lsass", "winlogon", "fontdrvhost", "dwm", "WmiPrvSE", "audiodg", "taskhostw", "RuntimeBroker",
    };

    /// <summary>Fills in display names for the top <paramref name="count"/> apps. Failures keep the executable name.</summary>
    public static ActivityResult Resolve(ActivityResult result, int count)
    {
        var services = new Lazy<IReadOnlyDictionary<int, string>>(ServicePaths);
        var apps = result.Apps.Select((app, i) => i < count ? app with { DisplayName = Describe(app, services) } : app).ToList();
        return result with { Apps = apps };
    }

    /// <summary>
    /// Exe paths of running services by process ID. Windows refuses to open most SYSTEM services' processes for a
    /// standard user, but the service list (with paths) is readable.
    /// </summary>
    private static IReadOnlyDictionary<int, string> ServicePaths()
    {
        try
        {
            return Wmi.WmiQuery.Select(@"root\cimv2", "SELECT ProcessId, PathName FROM Win32_Service WHERE ProcessId <> 0",
                    o => (Pid: Wmi.WmiQuery.Int(o, "ProcessId"), Path: ExePath(Wmi.WmiQuery.String(o, "PathName"))))
                .Where(s => s.Pid is not null && s.Path is not null)
                .GroupBy(s => s.Pid!.Value)
                .ToDictionary(g => g.Key, g => g.First().Path!);
        }
#pragma warning disable CA1031 // No service list: names fall back to the exe name.
        catch (Exception)
#pragma warning restore CA1031
        {
            return new Dictionary<int, string>();
        }
    }

    /// <summary>The exe from a service command line: <c>"C:\Program Files\X\x.exe" /service</c> or <c>C:\Windows\system32\y.exe -k z</c>.</summary>
    internal static string? ExePath(string? commandLine)
    {
        var s = commandLine?.Trim();
        if (string.IsNullOrEmpty(s))
        {
            return null;
        }

        if (s[0] == '"')
        {
            var end = s.IndexOf('"', 1);
            return end > 1 ? s[1..end] : null;
        }

        var exe = s.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
        return exe > 0 ? s[..(exe + 4)] : s;
    }

    private static string Describe(AppUsage app, Lazy<IReadOnlyDictionary<int, string>> services)
    {
        if (WindowsProcesses.Describe(app.Name) is { } windows)
        {
            return windows.Name;
        }

        if (KeepExeName.Contains(app.Name))
        {
            return app.Name;
        }

        foreach (var process in app.Processes)
        {
            // The exe path works as a standard user for most processes, services running as SYSTEM included
            // (Process.MainModule does not), and the file's description can then be read from disk.
            if (ImagePath(process.Pid) is { } path && FileDescription(path) is { Length: > 0 } description)
            {
                return description;
            }
        }

        foreach (var process in app.Processes)
        {
            if (services.Value.TryGetValue(process.Pid, out var path) && FileDescription(path) is { Length: > 0 } description)
            {
                return description;
            }
        }

        return app.Name;
    }

    private static string? FileDescription(string path)
    {
        try
        {
            return Clean(FileVersionInfo.GetVersionInfo(path).FileDescription);
        }
#pragma warning disable CA1031 // Unreadable file: use the exe name.
        catch (Exception)
#pragma warning restore CA1031
        {
            return null;
        }
    }

    /// <summary>Full path of a process's exe, or null (process gone, protected, or access denied).</summary>
    private static string? ImagePath(int pid)
    {
        var handle = OpenProcess(ProcessQueryLimitedInformation, false, pid);
        if (handle == IntPtr.Zero)
        {
            return null;
        }

        try
        {
            var buffer = new StringBuilder(1024);
            var size = buffer.Capacity;
            return QueryFullProcessImageName(handle, 0, buffer, ref size) ? buffer.ToString(0, size) : null;
        }
        finally
        {
            CloseHandle(handle);
        }
    }

    /// <summary>Some apps' description is just their file name ("SnippingTool.exe"): drop the extension.</summary>
    internal static string? Clean(string? description)
    {
        var d = description?.Trim();
        return d is not null && d.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? d[..^4].Trim() : d;
    }

    private const uint ProcessQueryLimitedInformation = 0x1000;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint access, bool inheritHandle, int processId);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "QueryFullProcessImageNameW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryFullProcessImageName(IntPtr process, uint flags, StringBuilder exeName, ref int size);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr handle);
}
