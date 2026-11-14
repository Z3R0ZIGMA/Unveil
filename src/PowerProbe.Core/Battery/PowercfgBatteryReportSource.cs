using System.Diagnostics;
using System.Xml.Linq;

namespace PowerProbe.Core.Battery;

/// <param name="Report">Parsed report.</param>
/// <param name="Document">The raw XML (unscrubbed — never save it as-is).</param>
public sealed record BatteryReportResult(BatteryReport Report, XDocument Document);

public interface IBatteryReportSource
{
    /// <summary>Returns the report, or null when it could not be produced (reason is logged).</summary>
    BatteryReportResult? Load(CollectionLog log);
}

/// <summary>Runs <c>powercfg /batteryreport /xml</c> into a temp folder and parses it. No admin needed.</summary>
public sealed class PowercfgBatteryReportSource : IBatteryReportSource
{
    private const string Source = "Battery report";

    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(30);

    public BatteryReportResult? Load(CollectionLog log)
    {
        var folder = Path.Combine(Path.GetTempPath(), "PowerProbe", Guid.NewGuid().ToString("N"));
        var output = Path.Combine(folder, "battery.xml");

        try
        {
            Directory.CreateDirectory(folder);
            if (!RunPowercfg(output, log))
            {
                return null;
            }

            using var reader = new StreamReader(output, detectEncodingFromByteOrderMarks: true);
            var document = BatteryReportParser.LoadDocument(reader);
            return new BatteryReportResult(BatteryReportParser.Parse(document), document);
        }
        catch (BatteryReportParseException ex)
        {
            log.Warning(Source, ex.Message);
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            log.Warning(Source, $"Could not create the battery report: {ex.Message}");
            return null;
        }
        finally
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
                // Temp folder clean-up is best effort.
            }
        }
    }

    private bool RunPowercfg(string output, CollectionLog log)
    {
        var startInfo = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "powercfg.exe"))
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        startInfo.ArgumentList.Add("/batteryreport");
        startInfo.ArgumentList.Add("/xml");
        startInfo.ArgumentList.Add("/output");
        startInfo.ArgumentList.Add(output);

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("powercfg.exe did not start.");

        // Drain the (localised) console output so the process cannot block; it is never parsed.
        _ = process.StandardOutput.ReadToEndAsync();
        _ = process.StandardError.ReadToEndAsync();

        if (!process.WaitForExit(Timeout))
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException)
            {
                // Already exited.
            }

            log.Warning(Source, $"powercfg /batteryreport timed out after {Timeout.TotalSeconds:0} s; skipped.");
            return false;
        }

        if (process.ExitCode != 0)
        {
            log.Warning(Source, $"powercfg /batteryreport exited with code {process.ExitCode}.");
        }

        if (!File.Exists(output))
        {
            log.Warning(Source, "powercfg /batteryreport did not produce a file; skipped.");
            return false;
        }

        return true;
    }
}
