using System.Collections.ObjectModel;
using System.Windows.Input;
using Unveil.Core.Battery;
using Unveil.Core.Power;
using Unveil.Core.Reports;
using Unveil.Core.Verdict;
using static Unveil.Core.Formatting;

namespace Unveil.App.ViewModels;

public sealed record MachineClassOption(MachineClass Value, string Name)
{
    /// <summary>Shown in the closed ComboBox.</summary>
    public override string ToString() => Name;
}

public enum TestStage
{
    Idle,
    WaitingForUnplug,
    Preparing,
    Measuring,
}

public sealed record ProcessRow(string Name, string Pid, string Cpu, string Gpu, string Io);

/// <summary>One app in the power hogs list. Expands to show its processes.</summary>
public sealed class AppRow : ObservableObject
{
    public AppRow(AppUsage app, int rank, bool gpuMeasured)
    {
        Rank = rank;
        Name = app.DisplayName;
        ProcessCountText = app.ProcessCount > 1 ? $"{app.ProcessCount} processes" : null;
        Share = app.SharePercent;
        ShareText = Percent(app.SharePercent, "0");
        CpuText = Percent(app.CpuPercent);
        GpuText = gpuMeasured ? Percent(app.GpuPercent) : "-";
        IoText = Rate(app.IoBytesPerSecond);
        Category = app.Offender?.Category;
        Advice = app.Offender?.Advice;
        Processes = app.Processes.Select(p => new ProcessRow(
            p.Name, p.Pid.ToString(Inv), Percent(p.CpuPercent), gpuMeasured ? Percent(p.GpuPercent) : "-", Rate(p.IoBytesPerSecond))).ToList();
        ToggleCommand = new RelayCommand(() => IsExpanded = !IsExpanded);
    }

    public int Rank { get; }

    public string Name { get; }

    public string? ProcessCountText { get; }

    public double Share { get; }

    public string ShareText { get; }

    public string CpuText { get; }

    public string GpuText { get; }

    public string IoText { get; }

    public string? Category { get; }

    public string? Advice { get; }

    public IReadOnlyList<ProcessRow> Processes { get; }

    public ICommand ToggleCommand { get; }

    public bool IsExpanded { get => _isExpanded; set => Set(ref _isExpanded, value); }
    private bool _isExpanded;

    internal static string Rate(double bytesPerSecond) => bytesPerSecond switch
    {
        >= 1024 * 1024 => (bytesPerSecond / 1024 / 1024).ToString("0.0", Inv) + " MB/s",
        >= 1024 => (bytesPerSecond / 1024).ToString("0", Inv) + " KB/s",
        _ => "0 KB/s",
    };
}

/// <summary>The guided drain test and the power hogs list (Phase 4).</summary>
public sealed partial class MainViewModel
{
    private const int MaxAppRows = 15;

    private Func<DrainTest> _createTest = DrainTest.CreateDefault;
    private CancellationTokenSource? _testCts;
    private bool _skipUnplug;
    private DrainTestResult? _lastTest;
    private BatterySnapshot? _snapshot;

    public ICommand RunTestCommand { get; private set; } = null!;

    public ICommand SkipUnplugCommand { get; private set; } = null!;

    public ICommand CancelTestCommand { get; private set; } = null!;

    private void InitTest(Func<DrainTest> createTest)
    {
        _createTest = createTest;
        RunTestCommand = new AsyncRelayCommand(() => RunTestAsync(), () => Report?.Snapshot.HasBattery == true && Stage == TestStage.Idle && !IsBusy);
        SkipUnplugCommand = new RelayCommand(() => _skipUnplug = true, () => Stage == TestStage.WaitingForUnplug);
        CancelTestCommand = new RelayCommand(() => _testCts?.Cancel(), () => Stage != TestStage.Idle);
    }

    // ------------------------------------------------------------ laptop type

    public IReadOnlyList<MachineClassOption> MachineClassOptions { get; } =
        Enum.GetValues<MachineClass>().Select(c => new MachineClassOption(c, MachineClasses.Describe(c))).ToList();

    /// <summary>Changing it re-scores the last test with that laptop type's limits.</summary>
    public MachineClass SelectedMachineClass
    {
        get => _selectedMachineClass;
        set
        {
            if (Set(ref _selectedMachineClass, value))
            {
                Recompute();
            }
        }
    }

    private MachineClass _selectedMachineClass = MachineClass.ThinAndLight;

    private VerdictSettings Settings => VerdictSettings.Default.For(SelectedMachineClass);

    // ------------------------------------------------------------ test state

    public TestStage Stage
    {
        get => _stage;
        private set
        {
            if (Set(ref _stage, value))
            {
                OnPropertyChanged(nameof(IsTestActive));
                OnPropertyChanged(nameof(IsWaitingForUnplug));
                OnPropertyChanged(nameof(IsPreparing));
                OnPropertyChanged(nameof(IsMeasuring));
                CommandManager.InvalidateRequerySuggested();
            }
        }
    }

    private TestStage _stage;

    public bool IsTestActive => Stage != TestStage.Idle;

    public bool IsWaitingForUnplug => Stage == TestStage.WaitingForUnplug;

    public bool IsPreparing => Stage == TestStage.Preparing;

    public bool IsMeasuring => Stage == TestStage.Measuring;

    public double TestProgress { get => _testProgress; private set => Set(ref _testProgress, value); }
    private double _testProgress;

    public string TestTimeLeft { get => _testTimeLeft; private set => Set(ref _testTimeLeft, value); }
    private string _testTimeLeft = "";

    public string TestWattsText { get => _testWattsText; private set => Set(ref _testWattsText, value); }
    private string _testWattsText = "-";

    public string TestAverageText { get => _testAverageText; private set => Set(ref _testAverageText, value); }
    private string _testAverageText = "";

    public ObservableCollection<double> TestWattHistory { get; } = [];

    // ------------------------------------------------------------ power hogs

    public ObservableCollection<AppRow> Apps { get; } = [];

    public bool HasActivity { get => _hasActivity; private set => Set(ref _hasActivity, value); }
    private bool _hasActivity;

    public string ActivitySummary { get => _activitySummary; private set => Set(ref _activitySummary, value); }
    private string _activitySummary = "";

    // ------------------------------------------------------------ running the test

    /// <param name="skipUnplug">Start straight away even if plugged in (used by the screenshot mode).</param>
    /// <param name="duration">Test length; 60 s by default (plan.md).</param>
    public async Task RunTestAsync(bool skipUnplug = false, TimeSpan? duration = null)
    {
        if (_snapshot is null || !_snapshot.HasBattery || Stage != TestStage.Idle)
        {
            return;
        }

        _testCts = new CancellationTokenSource();
        var token = _testCts.Token;
        _skipUnplug = skipUnplug;
        var options = new DrainTestOptions { Duration = duration ?? TimeSpan.FromSeconds(60) };

        try
        {
            // 1. Unplug (or skip)
            Stage = TestStage.WaitingForUnplug;
            StatusText = "Full test: waiting for the charger to be unplugged...";
            while (!_skipUnplug)
            {
                var reading = await Task.Run(_live.Read, token);
                if (reading is null || reading.IsOnBattery)
                {
                    break;
                }

                await Task.Delay(1000, token);
            }

            // 2. Warm up the counters (the first read can take seconds)
            Stage = TestStage.Preparing;
            StatusText = "Full test: getting ready...";
            var test = _createTest();
            await Task.Run(test.WarmUp, token);

            // 3. Measure
            _liveTimer.Stop();
            TestWattHistory.Clear();
            TestProgress = 0;
            TestWattsText = "-";
            TestAverageText = "";
            TestTimeLeft = $"{(int)options.Duration.TotalSeconds} s left";
            Stage = TestStage.Measuring;
            StatusText = "Full test: measuring. Leave the laptop idle.";

            var progress = new Progress<DrainTestProgress>(OnTestProgress);
            var result = await test.RunAsync(options, progress, token);

            _lastTest = result;
            Recompute();
            StatusText = result.Measurement.AverageWatts is { } w
                ? $"Full test finished: {Watts(w)} average over {Duration(result.Measurement.Duration)}."
                : "Full test finished (charger connected, so only app activity was measured).";
        }
        catch (OperationCanceledException)
        {
            StatusText = "Full test cancelled.";
        }
#pragma warning disable CA1031 // A failed test must never crash the app.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            StatusText = $"The full test failed: {ex.Message}";
        }
        finally
        {
            Stage = TestStage.Idle;
            _testCts.Dispose();
            _testCts = null;
            if (Report?.Snapshot.HasBattery == true)
            {
                _liveTimer.Start();
            }
        }
    }

    private void OnTestProgress(DrainTestProgress p)
    {
        TestProgress = p.Fraction * 100;
        TestTimeLeft = $"{Math.Max(0, (int)Math.Round((p.Duration - p.Elapsed).TotalSeconds))} s left";
        TestWattsText = p.LatestWatts is { } w ? Watts(w) : "Plugged in";
        TestAverageText = p.AverageWatts is { } a ? $"average so far {Watts(a)}" : "power draw not measured while plugged in";
        if (p.LatestWatts is { } watts)
        {
            TestWattHistory.Add(watts);
        }
    }

    /// <summary>Rebuilds the report from the last snapshot and test with the current laptop type.</summary>
    private void Recompute()
    {
        if (_snapshot is null)
        {
            return;
        }

        Apply(ReportDocument.Create(_snapshot, _lastTest, Settings));
    }

    private void ApplyActivity(ReportDocument report)
    {
        Apps.Clear();
        var activity = report.Activity;
        HasActivity = activity is { Apps.Count: > 0 };
        if (activity is null || report.Drain is not { } drain)
        {
            ActivitySummary = "";
            return;
        }

        var rank = 1;
        foreach (var app in activity.Apps.Take(MaxAppRows))
        {
            Apps.Add(new AppRow(app, rank++, activity.GpuMeasured));
        }

        var draw = drain.AverageWatts is { } w
            ? $"{Watts(w)} average on battery ({Watts(drain.MinWatts)} to {Watts(drain.MaxWatts)}); normal for a {Settings.DrainProfileName.ToLowerInvariant()} is below {Watts(Settings.ElevatedDrainWatts, "0")}."
            : "The charger was connected, so the power draw was not measured.";
        ActivitySummary = $"Measured over {Duration(activity.Duration)}. {draw} Whole-machine CPU use: {Percent(activity.TotalCpuPercent)}.";
    }
}
