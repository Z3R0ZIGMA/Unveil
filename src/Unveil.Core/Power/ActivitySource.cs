using System.Diagnostics;

namespace Unveil.Core.Power;

/// <summary>Cumulative counters for one process at one moment.</summary>
/// <param name="Pid">Process ID.</param>
/// <param name="Name">Executable name without ".exe", e.g. "chrome".</param>
/// <param name="CpuTime">Total CPU time used since the process started.</param>
/// <param name="IoBytes">Total bytes read/written (disk, network and devices) since the process started.</param>
public sealed record ProcessCounters(int Pid, string Name, TimeSpan CpuTime, long IoBytes);

/// <summary>All processes' cumulative counters at one moment.</summary>
/// <param name="Timestamp">Monotonic time of the reading.</param>
/// <param name="Processes">One entry per running process.</param>
/// <param name="GpuTime">Total GPU engine running time per PID (null when GPU counters are unavailable).</param>
public sealed record ActivitySnapshot(
    TimeSpan Timestamp,
    IReadOnlyList<ProcessCounters> Processes,
    IReadOnlyDictionary<int, TimeSpan>? GpuTime);

public interface IActivitySource
{
    /// <summary>Takes a reading. Returns null when it fails (the reason is logged). Never throws.</summary>
    ActivitySnapshot? Take(CollectionLog log);
}

/// <summary>
/// Reads the "Process" and "GPU Engine" performance counter categories in one go each (about 30 ms and 70 ms
/// after the first read). Works for a standard user. The first read can take several seconds, so call it
/// once to warm up before a timed test.
/// </summary>
public sealed class PerfCounterActivitySource : IActivitySource
{
    private const string Source = "Process sampler";
    private readonly PerformanceCounterCategory _process = new("Process");
    private readonly PerformanceCounterCategory _gpu = new("GPU Engine");
    private bool _gpuFailed;

    public ActivitySnapshot? Take(CollectionLog log)
    {
        List<ProcessCounters> processes;
        try
        {
            processes = ReadProcesses();
        }
        catch (Exception ex) when (ex is InvalidOperationException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            log.Warning(Source, $"Could not read process counters: {ex.Message}");
            return null;
        }

        var timestamp = Stopwatch.GetElapsedTime(0);
        return new ActivitySnapshot(timestamp, processes, _gpuFailed ? null : ReadGpu(log));
    }

    private List<ProcessCounters> ReadProcesses()
    {
        var data = _process.ReadCategory();
        var cpu = data["% Processor Time"];
        var pid = data["ID Process"];
        var io = data["IO Data Bytes/sec"];
        var list = new List<ProcessCounters>(pid.Count);

        foreach (InstanceData p in pid.Values)
        {
            var name = p.InstanceName;
            if (name is "_Total" or "Idle")
            {
                continue;
            }

            var id = (int)p.RawValue;
            var cpuRaw = cpu.Contains(name) ? cpu[name].RawValue : 0;   // 100 ns units
            var ioRaw = io.Contains(name) ? io[name].RawValue : 0;      // bytes
            list.Add(new ProcessCounters(id, BaseName(name), TimeSpan.FromTicks(cpuRaw), ioRaw));
        }

        return list;
    }

    private Dictionary<int, TimeSpan>? ReadGpu(CollectionLog log)
    {
        try
        {
            if (!PerformanceCounterCategory.Exists("GPU Engine"))
            {
                _gpuFailed = true;
                log.Info(Source, "GPU counters are not available on this machine; GPU use is not measured.");
                return null;
            }

            var running = _gpu.ReadCategory()["running time"];
            var byPid = new Dictionary<int, TimeSpan>();
            foreach (InstanceData engine in running.Values)
            {
                if (GpuPid(engine.InstanceName) is { } id)
                {
                    byPid[id] = byPid.GetValueOrDefault(id) + TimeSpan.FromTicks(engine.RawValue); // 100 ns units
                }
            }

            return byPid;
        }
        catch (Exception ex) when (ex is InvalidOperationException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            _gpuFailed = true;
            log.Info(Source, $"GPU counters could not be read ({ex.Message}); GPU use is not measured.");
            return null;
        }
    }

    /// <summary>"chrome#3" becomes "chrome".</summary>
    internal static string BaseName(string instance)
    {
        var hash = instance.IndexOf('#', StringComparison.Ordinal);
        return hash > 0 ? instance[..hash] : instance;
    }

    /// <summary>Reads the PID from a GPU Engine instance name such as "pid_10512_luid_0x..._engtype_3D".</summary>
    internal static int? GpuPid(string instance)
    {
        if (!instance.StartsWith("pid_", StringComparison.Ordinal))
        {
            return null;
        }

        var end = instance.IndexOf('_', 4);
        return end > 4 && int.TryParse(instance.AsSpan(4, end - 4), out var pid) ? pid : null;
    }
}
