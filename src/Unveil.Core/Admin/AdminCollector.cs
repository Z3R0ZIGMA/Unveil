using System.Diagnostics;
using System.Security.Principal;
using System.Text.Json.Serialization;
using System.Xml.Linq;
using Unveil.Core.Battery;

namespace Unveil.Core.Admin;

public sealed record AdminOptions
{
    /// <summary>Also run <c>powercfg /energy</c> (takes <see cref="EnergyDuration"/>).</summary>
    public bool IncludeEnergyDiagnostics { get; init; }

    /// <summary>plan.md: 60 s.</summary>
    public TimeSpan EnergyDuration { get; init; } = TimeSpan.FromSeconds(60);

    /// <summary>Keep the raw outputs (for saving scrubbed samples).</summary>
    public bool KeepRaw { get; init; }
}

/// <summary>Results of the administrator-only checks. Each part is null when skipped or failed (see the log).</summary>
public sealed record AdminData
{
    public bool IsAdministrator { get; init; }

    public DateTimeOffset CollectedAt { get; init; }

    public SrumSummary? Srum { get; init; }

    public SleepStudy? Sleep { get; init; }

    public EnergyDiagnostics? Energy { get; init; }

    /// <summary>What was skipped or failed while collecting.</summary>
    public IReadOnlyList<LogEntry> Log { get; init; } = [];

    [JsonIgnore]
    public AdminRaw Raw { get; init; } = new();
}

/// <summary>Raw outputs, unscrubbed. Never save them as-is.</summary>
public sealed record AdminRaw
{
    public string? SrumCsv { get; init; }

    public XDocument? SleepStudy { get; init; }

    public XDocument? Energy { get; init; }
}

/// <summary>
/// Runs the admin-only powercfg reports (plan.md 3.4): SRUM energy history, sleep study and, optionally, the
/// 60-second energy diagnostics. Without admin rights everything is skipped and logged, never an error.
/// </summary>
public sealed class AdminCollector
{
    public static bool IsAdministrator()
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException)
        {
            return false;
        }
    }

    public AdminData Collect(AdminOptions? options, CollectionLog log, DateTimeOffset? now = null)
    {
        options ??= new AdminOptions();
        var at = now ?? DateTimeOffset.Now;
        if (!IsAdministrator())
        {
            log.Info("Admin checks", "Skipped: energy history (SRUM), sleep study and energy diagnostics need administrator rights.");
            return new AdminData { IsAdministrator = false, CollectedAt = at, Log = log.Entries };
        }

        var folder = Path.Combine(Path.GetTempPath(), "Unveil", Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(folder);
            var (srum, srumRaw) = RunSrum(folder, at, log, options.KeepRaw);
            var (sleep, sleepRaw) = RunSleepStudy(folder, log);
            var (energy, energyRaw) = options.IncludeEnergyDiagnostics ? RunEnergy(folder, options.EnergyDuration, log) : (null, null);

            return new AdminData
            {
                IsAdministrator = true,
                CollectedAt = at,
                Srum = srum,
                Sleep = sleep,
                Energy = energy,
                Log = log.Entries,
                Raw = options.KeepRaw ? new AdminRaw { SrumCsv = srumRaw, SleepStudy = sleepRaw, Energy = energyRaw } : new AdminRaw(),
            };
        }
        finally
        {
            TryDelete(folder);
        }
    }

    /// <summary>Runs only the energy diagnostics (the app's separate 60-second button).</summary>
    public (EnergyDiagnostics? Result, XDocument? Raw) RunEnergyDiagnostics(TimeSpan duration, CollectionLog log)
    {
        if (!IsAdministrator())
        {
            log.Info("Energy diagnostics", "Skipped: needs administrator rights.");
            return (null, null);
        }

        var folder = Path.Combine(Path.GetTempPath(), "Unveil", Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(folder);
            return RunEnergy(folder, duration, log);
        }
        finally
        {
            TryDelete(folder);
        }
    }

    private static (SrumSummary?, string?) RunSrum(string folder, DateTimeOffset now, CollectionLog log, bool keepRaw)
    {
        const string source = "SRUM";
        var file = Path.Combine(folder, "srum.csv");
        if (!Powercfg.Run(["/srumutil", "/output", file, "/csv"], file, TimeSpan.FromMinutes(2), log, source))
        {
            return (null, null);
        }

        try
        {
            var text = File.ReadAllText(file);
            using var reader = new StringReader(text);
            return (SrumParser.Summarize(reader, now, log), keepRaw ? text : null);
        }
        catch (IOException ex)
        {
            log.Warning(source, $"Could not read the energy history: {ex.Message}");
            return (null, null);
        }
    }

    private static (SleepStudy?, XDocument?) RunSleepStudy(string folder, CollectionLog log)
    {
        const string source = "Sleep study";
        var file = Path.Combine(folder, "sleepstudy.xml");
        if (!Powercfg.Run(["/sleepstudy", "/xml", "/output", file], file, TimeSpan.FromMinutes(2), log, source))
        {
            return (null, null);
        }

        return ParseXml(file, log, source, SleepStudyParser.Parse);
    }

    private static (EnergyDiagnostics?, XDocument?) RunEnergy(string folder, TimeSpan duration, CollectionLog log)
    {
        const string source = "Energy diagnostics";
        var file = Path.Combine(folder, "energy.xml");
        var seconds = Math.Max(5, (int)duration.TotalSeconds).ToString(System.Globalization.CultureInfo.InvariantCulture);
        if (!Powercfg.Run(["/energy", "/xml", "/duration", seconds, "/output", file], file, duration + TimeSpan.FromMinutes(2), log, source))
        {
            return (null, null);
        }

        return ParseXml(file, log, source, EnergyDiagnosticsParser.Parse);
    }

    private static (T?, XDocument?) ParseXml<T>(string file, CollectionLog log, string source, Func<XDocument, T> parse)
        where T : class
    {
        try
        {
            using var reader = new StreamReader(file, detectEncodingFromByteOrderMarks: true);
            var document = BatteryReportParser.LoadDocument(reader);
            return (parse(document), document);
        }
        catch (Exception ex) when (ex is BatteryReportParseException or IOException)
        {
            log.Warning(source, $"Could not read the report: {ex.Message}");
            return (null, null);
        }
    }

    private static void TryDelete(string folder)
    {
        try
        {
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, recursive: true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Best effort.
        }
    }
}

/// <summary>Runs powercfg.exe with a timeout. Console output is localised, so it is drained and never parsed.</summary>
internal static class Powercfg
{
    /// <returns>True when <paramref name="expectedOutput"/> exists afterwards.</returns>
    public static bool Run(IReadOnlyList<string> args, string expectedOutput, TimeSpan timeout, CollectionLog log, string source)
    {
        try
        {
            var startInfo = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "powercfg.exe"))
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            foreach (var arg in args)
            {
                startInfo.ArgumentList.Add(arg);
            }

            using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("powercfg.exe did not start.");
            _ = process.StandardOutput.ReadToEndAsync();
            _ = process.StandardError.ReadToEndAsync();

            if (!process.WaitForExit(timeout))
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch (InvalidOperationException)
                {
                }

                log.Warning(source, $"powercfg {args[0]} timed out after {timeout.TotalSeconds:0} s; skipped.");
                return false;
            }

            if (!File.Exists(expectedOutput))
            {
                log.Warning(source, $"powercfg {args[0]} did not produce a report (exit code {process.ExitCode}); skipped.");
                return false;
            }

            return true;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
        {
            log.Warning(source, $"Could not run powercfg {args[0]}: {ex.Message}");
            return false;
        }
    }
}
