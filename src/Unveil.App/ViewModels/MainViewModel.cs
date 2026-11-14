using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Windows.Input;
using System.Windows.Threading;
using Unveil.Core.Battery;
using Unveil.Core.Reports;
using Unveil.Core.Verdict;
using static Unveil.Core.Formatting;

namespace Unveil.App.ViewModels;

/// <summary>What the view model needs from the window (clipboard, save dialog, opening files).</summary>
public interface IUiServices
{
    void CopyText(string text);

    /// <summary>Shows a save dialog. Returns the chosen path, or null if cancelled.</summary>
    string? PickSavePath(string suggestedFileName);

    void OpenFile(string path);
}

public sealed record FindingItem(Severity Severity, string Text);

public sealed record DetailRow(string Label, string Value);

public sealed record DetailSection(string Title, IReadOnlyList<DetailRow> Rows);

public sealed record HistoryRow(string Date, string FullCharge, string Design, string Health, string Cycles);

public sealed partial class MainViewModel : ObservableObject
{
    private const int LiveHistoryLength = 60;      // 2 minutes at one reading per 2 s
    private const int LiveAverageSamples = 15;     // smooth runtime estimates over 30 s

    private readonly Func<BatterySnapshot> _collect;
    private readonly ILiveBatterySource _live;
    private readonly IUiServices _ui;
    private readonly DispatcherTimer _liveTimer;
    private readonly List<double> _recentWatts = [];
    private bool _liveReading;

    public MainViewModel(Func<BatterySnapshot> collect, ILiveBatterySource live, IUiServices ui, Func<Unveil.Core.Power.DrainTest>? createTest = null)
    {
        _collect = collect;
        _live = live;
        _ui = ui;
        InitTest(createTest ?? Unveil.Core.Power.DrainTest.CreateDefault);

        RefreshCommand = new AsyncRelayCommand(RefreshAsync, () => !IsBusy && Stage == TestStage.Idle);
        CopySummaryCommand = new RelayCommand(CopySummary, () => Report is not null);
        ExportCommand = new RelayCommand(Export, () => Report is not null);

        _liveTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _liveTimer.Tick += async (_, _) => await ReadLiveAsync();
    }

    /// <summary>Raised after each completed check (used by the screenshot mode).</summary>
    public event EventHandler? ReportLoaded;

    public ICommand RefreshCommand { get; }

    public ICommand CopySummaryCommand { get; }

    public ICommand ExportCommand { get; }

    // ------------------------------------------------------------ state

    public ReportDocument? Report { get => _report; private set { if (Set(ref _report, value)) { OnPropertyChanged(nameof(HasReport)); } } }
    private ReportDocument? _report;

    public bool HasReport => Report is not null;

    public bool IsBusy { get => _isBusy; private set { if (Set(ref _isBusy, value)) { OnPropertyChanged(nameof(ShowLoading)); } } }
    private bool _isBusy;

    public bool ShowLoading => IsBusy && Report is null;

    public bool HasBattery { get => _hasBattery; private set => Set(ref _hasBattery, value); }
    private bool _hasBattery;

    public string StatusText { get => _statusText; private set => Set(ref _statusText, value); }
    private string _statusText = "Checking the battery...";

    // ------------------------------------------------------------ header

    public string DeviceTitle { get => _deviceTitle; private set => Set(ref _deviceTitle, value); }
    private string _deviceTitle = "";

    public string? DeviceSubtitle { get => _deviceSubtitle; private set => Set(ref _deviceSubtitle, value); }
    private string? _deviceSubtitle;

    public string? BatterySubtitle { get => _batterySubtitle; private set => Set(ref _batterySubtitle, value); }
    private string? _batterySubtitle;

    // ------------------------------------------------------------ verdict

    public Severity VerdictSeverity { get => _verdictSeverity; private set => Set(ref _verdictSeverity, value); }
    private Severity _verdictSeverity = Severity.Neutral;

    public string VerdictTitle { get => _verdictTitle; private set => Set(ref _verdictTitle, value); }
    private string _verdictTitle = "";

    public string VerdictMessage { get => _verdictMessage; private set => Set(ref _verdictMessage, value); }
    private string _verdictMessage = "";

    public IReadOnlyList<string> Actions { get => _actions; private set => Set(ref _actions, value); }
    private IReadOnlyList<string> _actions = [];

    public IReadOnlyList<FindingItem> Evidence { get => _evidence; private set => Set(ref _evidence, value); }
    private IReadOnlyList<FindingItem> _evidence = [];

    // ------------------------------------------------------------ health card

    public string HealthCardTitle { get => _healthCardTitle; private set => Set(ref _healthCardTitle, value); }
    private string _healthCardTitle = "Battery health";

    /// <summary>Health % for the gauge; NaN when unknown.</summary>
    public double HealthValue { get => _healthValue; private set => Set(ref _healthValue, value); }
    private double _healthValue = double.NaN;

    public string HealthText { get => _healthText; private set => Set(ref _healthText, value); }
    private string _healthText = "";

    public Severity HealthSeverity { get => _healthSeverity; private set => Set(ref _healthSeverity, value); }
    private Severity _healthSeverity = Severity.Neutral;

    public string HealthLabelText { get => _healthLabelText; private set => Set(ref _healthLabelText, value); }
    private string _healthLabelText = "";

    public string CapacityText { get => _capacityText; private set => Set(ref _capacityText, value); }
    private string _capacityText = "";

    public string CyclesText { get => _cyclesText; private set => Set(ref _cyclesText, value); }
    private string _cyclesText = "";

    public string ChemistryText { get => _chemistryText; private set => Set(ref _chemistryText, value); }
    private string _chemistryText = "";

    public string AgeText { get => _ageText; private set => Set(ref _ageText, value); }
    private string _ageText = "";

    // ------------------------------------------------------------ live card

    public string ChargeText { get => _chargeText; private set => Set(ref _chargeText, value); }
    private string _chargeText = "-";

    public double ChargeValue { get => _chargeValue; private set => Set(ref _chargeValue, value); }
    private double _chargeValue;

    public string PowerText { get => _powerText; private set => Set(ref _powerText, value); }
    private string _powerText = "Reading...";

    public bool IsOnBattery { get => _isOnBattery; private set => Set(ref _isOnBattery, value); }
    private bool _isOnBattery;

    public string WattsText { get => _wattsText; private set => Set(ref _wattsText, value); }
    private string _wattsText = "-";

    public ObservableCollection<double> WattHistory { get; } = [];

    public string TimeLeftText { get => _timeLeftText; private set => Set(ref _timeLeftText, value); }
    private string _timeLeftText = "-";

    public string RuntimeNowText { get => _runtimeNowText; private set => Set(ref _runtimeNowText, value); }
    private string _runtimeNowText = "-";

    public string RuntimeNewText { get => _runtimeNewText; private set => Set(ref _runtimeNewText, value); }
    private string _runtimeNewText = "-";

    // ------------------------------------------------------------ usage card

    public string InUseWatts { get => _inUseWatts; private set => Set(ref _inUseWatts, value); }
    private string _inUseWatts = "-";

    public string InUseDetail { get => _inUseDetail; private set => Set(ref _inUseDetail, value); }
    private string _inUseDetail = "";

    public string AsleepWatts { get => _asleepWatts; private set => Set(ref _asleepWatts, value); }
    private string _asleepWatts = "-";

    public string AsleepDetail { get => _asleepDetail; private set => Set(ref _asleepDetail, value); }
    private string _asleepDetail = "";

    public string EstimateText { get => _estimateText; private set => Set(ref _estimateText, value); }
    private string _estimateText = "";

    // ------------------------------------------------------------ chart, details

    public IReadOnlyList<CapacityHistoryEntry> History { get => _history; private set { if (Set(ref _history, value)) { OnPropertyChanged(nameof(HasHistory)); } } }
    private IReadOnlyList<CapacityHistoryEntry> _history = [];

    public bool HasHistory => History.Count > 1;

    public IReadOnlyList<HistoryRow> HistoryRows { get => _historyRows; private set => Set(ref _historyRows, value); }
    private IReadOnlyList<HistoryRow> _historyRows = [];

    public IReadOnlyList<DetailSection> Details { get => _details; private set => Set(ref _details, value); }
    private IReadOnlyList<DetailSection> _details = [];

    public IReadOnlyList<string> Notes { get => _notes; private set => Set(ref _notes, value); }
    private IReadOnlyList<string> _notes = [];

    // ------------------------------------------------------------ actions

    public async Task RefreshAsync()
    {
        IsBusy = true;
        StatusText = "Checking the battery...";
        try
        {
            var snapshot = await Task.Run(_collect);
            _snapshot = snapshot;
            _lastTest = null; // a fresh check makes the old test results stale
            var report = ReportDocument.Create(snapshot, null, Settings);
            Apply(report);
            StatusText = $"Checked {report.GeneratedAt.ToString("HH:mm", Inv)} as {(snapshot.IsAdministrator ? "administrator" : "standard user")}";
            await ReadLiveAsync();
            _liveTimer.Start();
        }
#pragma warning disable CA1031 // The UI must never crash on a collection failure; show it instead.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            StatusText = $"Could not check the battery: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
            CommandManager.InvalidateRequerySuggested();
            ReportLoaded?.Invoke(this, EventArgs.Empty);
        }
    }

    private void CopySummary()
    {
        if (Report is null)
        {
            return;
        }

        try
        {
            _ui.CopyText(TextSummary.Create(Report));
            StatusText = "Summary copied to the clipboard.";
        }
        catch (System.Runtime.InteropServices.COMException ex)
        {
            StatusText = $"Could not copy: {ex.Message}";
        }
    }

    private void Export()
    {
        if (Report is null || _ui.PickSavePath(Report.FileStem + ".html") is not { } htmlPath)
        {
            return;
        }

        try
        {
            HtmlReportWriter.Write(Report, htmlPath);
            var jsonPath = Path.ChangeExtension(htmlPath, ".json");
            JsonReportWriter.Write(Report, jsonPath);
            StatusText = $"Report saved: {Path.GetFileName(htmlPath)} (and .json)";
            _ui.OpenFile(htmlPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            StatusText = $"Could not save the report: {ex.Message}";
        }
    }

    // ------------------------------------------------------------ building the view

    private void Apply(ReportDocument report)
    {
        Report = report;
        var s = report.Snapshot;
        var v = report.Verdict;
        HasBattery = s.HasBattery;

        // Header
        var device = string.Join(" ", new[] { s.System.Manufacturer, s.System.DisplayModel }.Where(x => !string.IsNullOrWhiteSpace(x)));
        DeviceTitle = device.Length > 0 ? device : "Unknown laptop";
        DeviceSubtitle = s.System.SerialNumber is { } sn ? $"Serial number {sn}" : null;
        BatterySubtitle = s.Batteries.Count switch
        {
            0 => "No battery detected",
            1 => BatteryLine(s.Batteries[0]),
            _ => $"{s.Batteries.Count} batteries: " + string.Join(", ", s.Batteries.Select(b => b.Name ?? $"Battery {b.Index}")),
        };

        // Verdict
        VerdictSeverity = v.Severity;
        VerdictTitle = v.Title;
        VerdictMessage = v.Message;
        Actions = v.Actions;
        Evidence = v.Evidence.Select(f => new FindingItem(f.Severity, f.Text)).ToList();

        // Health card
        if (s.HasBattery)
        {
            var multiple = s.Combined is not null;
            var health = multiple ? s.Combined!.Health : s.Batteries[0].Health;
            var label = VerdictEngine.Label(health);
            HealthCardTitle = multiple ? "Combined battery health" : "Battery health";
            HealthValue = health.HealthPercent ?? double.NaN;
            HealthText = health.HealthPercent is { } h ? h.ToString("0", Inv) + "%" : "?";
            HealthSeverity = label switch
            {
                HealthLabel.Good => Severity.Good,
                HealthLabel.Worn => Severity.Warning,
                HealthLabel.Replace => Severity.Danger,
                _ => Severity.Neutral,
            };
            HealthLabelText = label.ToString();
            var full = multiple ? s.Combined!.FullChargeCapacity : s.Batteries[0].FullChargeCapacity;
            var design = multiple ? s.Combined!.DesignCapacity : s.Batteries[0].DesignCapacity;
            CapacityText = health.HealthPercent is null
                ? health.UnknownReason ?? "Health unknown"
                : $"Holds {Number(full)} of its original {Number(design)} mWh";
            CyclesText = string.Join(" / ", s.Batteries.Select(b => BatteryText.CycleCount(b.CycleCount)).Distinct());
            ChemistryText = string.Join(" / ", s.Batteries.Select(BatteryText.Chemistry).Distinct());
            AgeText = string.Join(" / ", s.Batteries.Select(b => Age(b.ManufactureDate, report.GeneratedAt)).Distinct());
        }

        // Usage card
        var usage = report.Usage;
        InUseWatts = usage.InUse is { } use ? Watts(use.AverageWatts) : "-";
        InUseDetail = usage.InUse is { } u ? $"average while in use ({Duration(u.Duration)} on battery)" : "not enough time on battery recently";
        AsleepWatts = usage.Asleep is { } sleep ? Watts(sleep.AverageWatts, "0.00") : "-";
        AsleepDetail = usage.Asleep is { PercentPerHour: { } pph } ? $"while asleep, about {Percent(pph)} of a full charge per hour" : "not enough sleep on battery recently";
        var est = s.Report?.RuntimeEstimates;
        EstimateText = est?.AtFullChargeCapacity?.ActiveRuntime is { } now
            ? $"Windows estimates {Duration(now)} of use on a full charge ({Duration(est.AtDesignCapacity?.ActiveRuntime)} when new)."
            : "Windows has no runtime estimate yet.";

        // Chart and tables
        History = s.Report?.CapacityHistory ?? [];
        HistoryRows = History.Reverse().Select(e => new HistoryRow(
            Date(e.Start) + (e.BatteryChanged ? " (battery replaced)" : ""),
            Number(e.FullChargeCapacity),
            Number(e.DesignCapacity),
            Percent(e.Health.HealthPercent),
            e.CycleCount is > 0 ? Number(e.CycleCount) : "-")).ToList();

        ApplyActivity(report);
        Details = BuildDetails(s);
        Notes = s.Log.Count == 0
            ? ["All sources were read without problems."]
            : s.Log.Select(e => $"{e.Level} - {e.Source}: {e.Message}").ToList();
    }

    private static string BatteryLine(BatteryInfo b)
    {
        var name = b.Name ?? "Battery";
        return b.SerialNumber is { } serial ? $"{name} - serial {serial}" : name;
    }

    private static IReadOnlyList<DetailSection> BuildDetails(BatterySnapshot s)
    {
        static DetailRow Row(string label, string? value) => new(label, value ?? "Not reported");

        var sections = new List<DetailSection>
        {
            new("Laptop",
            [
                Row("Manufacturer", s.System.Manufacturer),
                Row("Model", s.System.DisplayModel),
                Row("Serial number", s.System.SerialNumber),
                Row("BIOS", s.System.BiosVersion),
                Row("Type", s.System.PcSystemTypeName),
                Row("Windows build", s.Report?.System.OsBuild),
                Row("Checked as", s.IsAdministrator ? "Administrator" : "Standard user"),
            ]),
        };

        foreach (var b in s.Batteries)
        {
            var unit = b.IsRelativeCapacity ? "units (relative)" : "mWh";
            sections.Add(new(s.Batteries.Count > 1 ? $"Battery {b.Index} of {s.Batteries.Count}" : "Battery",
            [
                Row("Name / part no.", b.Name),
                Row("Manufacturer", b.Manufacturer),
                Row("Serial number", b.SerialNumber),
                Row("Chemistry", BatteryText.Chemistry(b)),
                Row("Manufacture date", b.ManufactureDate),
                Row("Design capacity", b.DesignCapacity is null ? null : $"{Number(b.DesignCapacity)} {unit}"),
                Row("Full charge capacity", b.FullChargeCapacity is null ? null : $"{Number(b.FullChargeCapacity)} {unit}"),
                Row("Health", BatteryText.Health(b.Health)),
                Row("Cycle count", BatteryText.CycleCount(b.CycleCount)),
                Row("Voltage", b.VoltageMillivolts is { } mv ? $"{(mv / 1000.0).ToString("0.00", Inv)} V" : null),
                Row("Data from", BatteryText.Sources(b.Sources)),
            ]));
        }

        return sections;
    }

    private static string Age(string? manufactureDate, DateTimeOffset now)
    {
        if (manufactureDate is null)
        {
            return "Not reported";
        }

        if (!DateTime.TryParseExact(manufactureDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var made))
        {
            return manufactureDate;
        }

        var years = (now.Date - made).TotalDays / 365.25;
        return years < 1
            ? $"Made {manufactureDate} (under a year old)"
            : string.Create(Inv, $"Made {manufactureDate} (about {years:0} years old)");
    }

    // ------------------------------------------------------------ live

    private async Task ReadLiveAsync()
    {
        if (_liveReading || Report is null || !Report.Snapshot.HasBattery || Stage == TestStage.Measuring)
        {
            return;
        }

        _liveReading = true;
        try
        {
            var reading = await Task.Run(_live.Read);
            ApplyLive(reading);
        }
        finally
        {
            _liveReading = false;
        }
    }

    private void ApplyLive(LiveReading? r)
    {
        if (r is null)
        {
            PowerText = "Live data not available";
            return;
        }

        ChargeValue = r.ChargePercent ?? 0;
        ChargeText = r.ChargePercent is { } pct ? pct.ToString("0", Inv) + "%" : "-";
        IsOnBattery = r.IsOnBattery;
        PowerText = r.IsOnBattery ? "On battery"
            : r.Charging == true ? (r.ChargeRateMilliwatts is > 0 ? $"Plugged in, charging at {Watts(r.ChargeRateMilliwatts / 1000.0)}" : "Plugged in, charging")
            : "Plugged in, not charging";

        if (r.DischargeWatts is { } w)
        {
            _recentWatts.Add(w);
            if (_recentWatts.Count > LiveAverageSamples)
            {
                _recentWatts.RemoveAt(0);
            }

            WattHistory.Add(w);
            while (WattHistory.Count > LiveHistoryLength)
            {
                WattHistory.RemoveAt(0);
            }

            var average = _recentWatts.Average();
            var design = Report?.Snapshot.Combined?.DesignCapacity ?? Report?.Snapshot.Batteries.FirstOrDefault()?.DesignCapacity;
            WattsText = Watts(w);
            TimeLeftText = Duration(WmiLiveBatteryReader.Runtime(r.RemainingCapacity, average));
            RuntimeNowText = Duration(WmiLiveBatteryReader.Runtime(r.FullChargeCapacity, average));
            RuntimeNewText = Duration(WmiLiveBatteryReader.Runtime(design, average));
        }
        else
        {
            _recentWatts.Clear();
            WattHistory.Clear();
            WattsText = "-";
            TimeLeftText = RuntimeNowText = RuntimeNewText = "-";
        }
    }
}
