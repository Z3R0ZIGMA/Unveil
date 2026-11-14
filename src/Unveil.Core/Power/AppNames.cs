using System.Diagnostics;

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
        var apps = result.Apps.Select((app, i) => i < count ? app with { DisplayName = Describe(app) } : app).ToList();
        return result with { Apps = apps };
    }

    private static string Describe(AppUsage app)
    {
        if (KeepExeName.Contains(app.Name))
        {
            return app.Name;
        }

        foreach (var process in app.Processes)
        {
            try
            {
                using var p = Process.GetProcessById(process.Pid);
                var description = p.MainModule?.FileVersionInfo.FileDescription?.Trim();
                if (!string.IsNullOrEmpty(description))
                {
                    return description;
                }
            }
#pragma warning disable CA1031 // Access denied, process exited, 32/64-bit mismatch: all mean "use the exe name".
            catch (Exception)
#pragma warning restore CA1031
            {
            }
        }

        return app.Name;
    }
}
