using Unveil.Core.Power;

namespace Unveil.Core.Tests.Power;

public class PowerImpactTests
{
    private static ProcessCounters P(int pid, string name, double cpuSeconds, long io = 0) =>
        new(pid, name, TimeSpan.FromSeconds(cpuSeconds), io);

    private static ActivitySnapshot At(double seconds, IReadOnlyList<ProcessCounters> processes, Dictionary<int, TimeSpan>? gpu = null) =>
        new(TimeSpan.FromSeconds(seconds), processes, gpu ?? []);

    [Fact]
    public void Cpu_is_normalised_to_all_cores()
    {
        // 4 cores; the app used 2 CPU-seconds in 10 s = 20% of one core = 5% of the machine.
        var result = PowerImpact.Analyze(
            [At(0, [P(1, "app", 0)]), At(10, [P(1, "app", 2)])],
            coreCount: 4);

        var app = Assert.Single(result.Apps);
        Assert.Equal(5.0, app.CpuPercent, 6);
        Assert.Equal(TimeSpan.FromSeconds(10), result.Duration);
        Assert.Equal(100.0, app.SharePercent, 6);
    }

    [Fact]
    public void Gpu_and_io_are_averaged_over_the_test()
    {
        var gpu0 = new Dictionary<int, TimeSpan> { [1] = TimeSpan.Zero };
        var gpu1 = new Dictionary<int, TimeSpan> { [1] = TimeSpan.FromSeconds(3) };
        var result = PowerImpact.Analyze(
            [At(0, [P(1, "game", 0, io: 0)], gpu0), At(10, [P(1, "game", 0, io: 10 * 1024 * 1024)], gpu1)],
            coreCount: 1);

        var app = Assert.Single(result.Apps);
        Assert.Equal(30.0, app.GpuPercent, 6);
        Assert.Equal(1024 * 1024, app.IoBytesPerSecond, 6);
        Assert.True(result.GpuMeasured);
    }

    [Fact]
    public void Score_weights_cpu_gpu_and_io()
    {
        Assert.Equal(6.0, PowerImpact.Score(10, 0, 0), 6);
        Assert.Equal(3.0, PowerImpact.Score(0, 10, 0), 6);
        Assert.Equal(10.0, PowerImpact.Score(0, 0, PowerImpact.IoFullScaleBytesPerSecond), 6);
        Assert.Equal(10.0, PowerImpact.Score(0, 0, PowerImpact.IoFullScaleBytesPerSecond * 5), 6); // capped
    }

    [Fact]
    public void Processes_with_the_same_name_are_grouped_into_one_app()
    {
        var result = PowerImpact.Analyze(
        [
            At(0, [P(1, "chrome", 0), P(2, "chrome", 0), P(3, "notepad", 0)]),
            At(10, [P(1, "chrome", 1), P(2, "chrome", 3), P(3, "notepad", 1)]),
        ], coreCount: 1);

        Assert.Equal(2, result.Apps.Count);
        var chrome = result.Apps[0];
        Assert.Equal("chrome", chrome.Name);
        Assert.Equal(2, chrome.ProcessCount);
        Assert.Equal(40.0, chrome.CpuPercent, 6);
        Assert.Equal(2, chrome.Processes[0].Pid); // busiest process first
        Assert.Equal(80.0, chrome.SharePercent, 6);
        Assert.Equal(20.0, result.Apps[1].SharePercent, 6);
    }

    [Fact]
    public void Excluded_pids_and_idle_are_left_out()
    {
        var result = PowerImpact.Analyze(
            [At(0, [P(1, "unveil", 0), P(0, "Idle", 0), P(2, "app", 0)]), At(10, [P(1, "unveil", 5), P(0, "Idle", 9), P(2, "app", 1)])],
            coreCount: 1,
            excludePids: new HashSet<int> { 1 });

        Assert.Equal("app", Assert.Single(result.Apps).Name);
    }

    [Fact]
    public void A_process_that_starts_mid_test_counts_from_its_second_reading()
    {
        var result = PowerImpact.Analyze(
        [
            At(0, []),
            At(5, [P(7, "late", 100)]),   // first seen with 100 s of past CPU time: must not count
            At(10, [P(7, "late", 101)]),
        ], coreCount: 1);

        var app = Assert.Single(result.Apps);
        Assert.Equal(10.0, app.CpuPercent, 6); // 1 s over the 10 s test
    }

    [Fact]
    public void A_reused_pid_with_a_different_name_is_a_new_process()
    {
        var result = PowerImpact.Analyze(
            [At(0, [P(5, "old", 50)]), At(10, [P(5, "new", 1)])],
            coreCount: 1);

        Assert.Empty(result.Apps);
    }

    [Fact]
    public void Counters_going_backwards_are_treated_as_zero()
    {
        var result = PowerImpact.Analyze(
            [At(0, [P(1, "app", 10, io: 500)]), At(10, [P(1, "app", 5, io: 100)])],
            coreCount: 1);

        var app = Assert.Single(result.Apps);
        Assert.Equal(0, app.CpuPercent);
        Assert.Equal(0, app.IoBytesPerSecond);
    }

    [Fact]
    public void Missing_gpu_counters_mean_gpu_not_measured()
    {
        var result = PowerImpact.Analyze(
            [new ActivitySnapshot(TimeSpan.Zero, [P(1, "app", 0)], null), new ActivitySnapshot(TimeSpan.FromSeconds(10), [P(1, "app", 1)], null)],
            coreCount: 1);

        Assert.False(result.GpuMeasured);
        Assert.Equal(0, result.Apps[0].GpuPercent);
    }

    [Fact]
    public void Fewer_than_two_snapshots_gives_no_apps()
    {
        Assert.Empty(PowerImpact.Analyze([At(0, [P(1, "app", 0)])], 4).Apps);
        Assert.Empty(PowerImpact.Analyze([], 4).Apps);
    }

    [Fact]
    public void Known_offenders_are_attached()
    {
        var result = PowerImpact.Analyze(
            [At(0, [P(1, "TiWorker", 0)]), At(10, [P(1, "TiWorker", 1)])],
            coreCount: 1);

        Assert.Equal("Windows Update", result.Apps[0].Offender?.Category);
    }

    [Fact]
    public void Windows_processes_get_a_plain_name_and_a_note()
    {
        var result = PowerImpact.Analyze(
            [At(0, [P(1, "dwm", 0), P(2, "chrome", 0)]), At(10, [P(1, "dwm", 1), P(2, "chrome", 1)])],
            coreCount: 1);

        var dwm = result.Apps.Single(a => a.Name == "dwm");
        Assert.Equal("Desktop Window Manager", dwm.DisplayName);
        Assert.Equal("Part of Windows: draws everything on the screen", dwm.Note);

        var chrome = result.Apps.Single(a => a.Name == "chrome");
        Assert.Equal("chrome", chrome.DisplayName);
        Assert.Null(chrome.Note);
    }

    [Fact]
    public void Apps_with_the_same_display_name_are_merged()
    {
        // Two different exes from the same product with the same file description (e.g. N-able Take Control).
        var result = PowerImpact.Analyze(
        [
            At(0, [P(1, "BASupSrvc", 0), P(2, "BASupSrvcUpdater", 0), P(3, "notepad", 0)]),
            At(10, [P(1, "BASupSrvc", 2), P(2, "BASupSrvcUpdater", 1), P(3, "notepad", 2.5)]),
        ], coreCount: 1);
        result = result with
        {
            Apps = result.Apps.Select(a => a.Name.StartsWith("BASup", StringComparison.Ordinal) ? a with { DisplayName = "N-able Take Control Agent" } : a).ToList(),
        };

        var merged = PowerImpact.MergeByDisplayName(result);

        Assert.Equal(2, merged.Apps.Count);
        var agent = merged.Apps[0];
        Assert.Equal("N-able Take Control Agent", agent.DisplayName);
        Assert.Equal("BASupSrvc", agent.Name);
        Assert.Equal(2, agent.ProcessCount);
        Assert.Equal(30.0, agent.CpuPercent, 6);
        Assert.Equal(100.0 * 18 / 33, agent.SharePercent, 6);
        Assert.Equal(["BASupSrvc", "BASupSrvcUpdater"], agent.Processes.Select(p => p.Name));
        Assert.Equal("notepad", merged.Apps[1].Name);
    }

    [Theory]
    [InlineData("\"C:\\Program Files (x86)\\Take Control Agent\\BASupSrvc.exe\"", @"C:\Program Files (x86)\Take Control Agent\BASupSrvc.exe")]
    [InlineData("\"C:\\Program Files\\Managed Antivirus\\EPSecurityService.exe\" /service", @"C:\Program Files\Managed Antivirus\EPSecurityService.exe")]
    [InlineData(@"C:\WINDOWS\system32\SearchIndexer.exe /Embedding", @"C:\WINDOWS\system32\SearchIndexer.exe")]
    [InlineData(@"C:\Windows\system32\svchost.exe -k netsvcs -p", @"C:\Windows\system32\svchost.exe")]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void Service_command_lines_give_the_exe_path(string? commandLine, string? expected)
    {
        Assert.Equal(expected, AppNames.ExePath(commandLine));
    }

    [Theory]
    [InlineData("chrome#3", "chrome")]
    [InlineData("svchost", "svchost")]
    [InlineData("Memory Compression", "Memory Compression")]
    public void Instance_names_lose_their_number_suffix(string instance, string expected)
    {
        Assert.Equal(expected, PerfCounterActivitySource.BaseName(instance));
    }

    [Theory]
    [InlineData("pid_10512_luid_0x00000000_0x00010174_phys_0_eng_11_engtype_3D", 10512)]
    [InlineData("pid_4_luid_0x0_phys_0_eng_0_engtype_Copy", 4)]
    [InlineData("_Total", null)]
    [InlineData("pid_abc_luid", null)]
    public void Gpu_instance_names_give_the_pid(string instance, int? expected)
    {
        Assert.Equal(expected, PerfCounterActivitySource.GpuPid(instance));
    }
}
