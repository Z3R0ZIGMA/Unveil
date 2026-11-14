using System.Collections.ObjectModel;
using System.Windows.Input;
using Unveil.Core;
using Unveil.Core.Admin;
using Unveil.Core.Reports;
using Unveil.Core.Verdict;
using static Unveil.Core.Formatting;

namespace Unveil.App.ViewModels;

public sealed record SrumRow(int Rank, string Name, double Share, string ShareText, string Mostly, string BackgroundText, string? Category, string? Advice);

public sealed record SleepRow(string Start, string Length, string Power, string Used, string PerHour, string DeepSleep, string Blocker);

public sealed record EnergyFindingRow(Severity Severity, string Title, string Description, string? Detail);

/// <summary>Administrator-only checks (Phase 5): energy history, sleep study and energy diagnostics.</summary>
public sealed partial class MainViewModel
{
    private AdminData? _admin;
    private Func<AdminCollector> _createAdmin = () => new AdminCollector();

    public ICommand RestartAsAdminCommand { get; private set; } = null!;

    public ICommand RunEnergyCommand { get; private set; } = null!;

    /// <summary>Exports scrubbed raw data (no serials, names or network IDs) to bring back as test samples.</summary>
    public ICommand SaveSamplesCommand { get; private set; } = null!;

    private void InitAdmin()
    {
        IsAdministrator = AdminCollector.IsAdministrator();
        RestartAsAdminCommand = new RelayCommand(RestartAsAdmin, () => !IsAdministrator && Stage == TestStage.Idle && !EnergyRunning);
        RunEnergyCommand = new AsyncRelayCommand(RunEnergyAsync, () => IsAdministrator && HasBattery && !AdminLoading && !EnergyRunning && Stage == TestStage.Idle);
        SaveSamplesCommand = new RelayCommand(SaveSamples, () => _snapshot is not null && !AdminLoading);
    }

    public bool IsAdministrator { get => _isAdministrator; private set => Set(ref _isAdministrator, value); }
    private bool _isAdministrator;

    public bool AdminLoading { get => _adminLoading; private set => Set(ref _adminLoading, value); }
    private bool _adminLoading;

    public bool EnergyRunning { get => _energyRunning; private set => Set(ref _energyRunning, value); }
    private bool _energyRunning;

    public string EnergyRunText { get => _energyRunText; private set => Set(ref _energyRunText, value); }
    private string _energyRunText = "";

    // ------------------------------------------------------------ energy history (SRUM)

    public ObservableCollection<SrumRow> SrumDay { get; } = [];

    public ObservableCollection<SrumRow> SrumWeek { get; } = [];

    public bool HasSrum { get => _hasSrum; private set => Set(ref _hasSrum, value); }
    private bool _hasSrum;

    public string SrumDaySummary { get => _srumDaySummary; private set => Set(ref _srumDaySummary, value); }
    private string _srumDaySummary = "";

    public string SrumWeekHeader { get => _srumWeekHeader; private set => Set(ref _srumWeekHeader, value); }
    private string _srumWeekHeader = "Last 7 days";

    public string SrumWeekSummary { get => _srumWeekSummary; private set => Set(ref _srumWeekSummary, value); }
    private string _srumWeekSummary = "";

    // ------------------------------------------------------------ sleep

    public ObservableCollection<SleepRow> SleepRows { get; } = [];

    public bool HasSleep { get => _hasSleep; private set => Set(ref _hasSleep, value); }
    private bool _hasSleep;

    public string SleepSummary { get => _sleepSummary; private set => Set(ref _sleepSummary, value); }
    private string _sleepSummary = "";

    // ------------------------------------------------------------ energy diagnostics

    public ObservableCollection<EnergyFindingRow> EnergyFindings { get; } = [];

    public bool HasEnergy { get => _hasEnergy; private set => Set(ref _hasEnergy, value); }
    private bool _hasEnergy;

    public string EnergySummary { get => _energySummary; private set => Set(ref _energySummary, value); }
    private string _energySummary = "";

    /// <summary>Message for the admin tabs and card when the data is not there.</summary>
    public string AdminHint => IsAdministrator
        ? (AdminLoading ? "Reading the energy history and sleep study..." : "No data was available; see Data notes for details.")
        : "Needs administrator rights. Click \"Restart as administrator\" to include this.";

    // ------------------------------------------------------------ actions

    /// <summary>Runs after each check when running as administrator (not the 60-second energy diagnostics).</summary>
    private async Task LoadAdminAsync()
    {
        if (!IsAdministrator || _snapshot is not { HasBattery: true })
        {
            _admin = IsAdministrator ? null : new AdminData { IsAdministrator = false, CollectedAt = DateTimeOffset.Now };
            return;
        }

        AdminLoading = true;
        OnPropertyChanged(nameof(AdminHint));
        try
        {
            var previousEnergy = _admin?.Energy;
            // Keep the raw outputs so "Save samples" can export scrubbed copies.
            var admin = await Task.Run(() => _createAdmin().Collect(new AdminOptions { KeepRaw = true }, new CollectionLog()));
            _admin = admin with { Energy = previousEnergy, Raw = admin.Raw with { Energy = _admin?.Raw.Energy } };
            Recompute();
        }
#pragma warning disable CA1031 // Admin extras must never break the main check.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            StatusText = $"Could not read the energy history or sleep study: {ex.Message}";
        }
        finally
        {
            AdminLoading = false;
            OnPropertyChanged(nameof(AdminHint));
            CommandManager.InvalidateRequerySuggested();
        }
    }

    public async Task RunEnergyAsync()
    {
        const int seconds = 60;
        EnergyRunning = true;
        var started = DateTimeOffset.Now;
        var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        timer.Tick += (_, _) => EnergyRunText = $"Running energy diagnostics: {Math.Max(0, seconds - (int)(DateTimeOffset.Now - started).TotalSeconds)} s left. Leave the laptop idle.";
        EnergyRunText = $"Running energy diagnostics: {seconds} s left. Leave the laptop idle.";
        timer.Start();
        StatusText = "Running Windows energy diagnostics (60 seconds)...";
        try
        {
            var log = new CollectionLog();
            var (energy, energyRaw) = await Task.Run(() => _createAdmin().RunEnergyDiagnostics(TimeSpan.FromSeconds(seconds), log));
            if (energy is null)
            {
                StatusText = "Energy diagnostics did not produce a report: " + (log.Entries.LastOrDefault()?.Message ?? "unknown reason");
                return;
            }

            var current = _admin ?? new AdminData { IsAdministrator = true, CollectedAt = DateTimeOffset.Now };
            _admin = current with { Energy = energy, Raw = current.Raw with { Energy = energyRaw } };
            Recompute();
            StatusText = $"Energy diagnostics finished: {energy.ErrorCount} errors, {energy.WarningCount} warnings.";
        }
#pragma warning disable CA1031
        catch (Exception ex)
#pragma warning restore CA1031
        {
            StatusText = $"Energy diagnostics failed: {ex.Message}";
        }
        finally
        {
            timer.Stop();
            EnergyRunning = false;
            EnergyRunText = "";
        }
    }

    private void SaveSamples()
    {
        if (_snapshot is null || _ui.PickFolder("Choose a folder for the scrubbed samples") is not { } folder)
        {
            return;
        }

        try
        {
            var files = Unveil.Core.Admin.SampleExport.Save(folder, _snapshot, _admin);
            StatusText = $"Saved {files.Count} scrubbed sample files to {folder}.";
        }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException)
        {
            StatusText = $"Could not save the samples: {ex.Message}";
        }
    }

    private void RestartAsAdmin()
    {
        if (!_ui.RestartAsAdministrator())
        {
            StatusText = "Restart as administrator was cancelled.";
        }
    }

    // ------------------------------------------------------------ building the view

    private void ApplyAdmin(ReportDocument report)
    {
        var admin = report.Admin;

        SrumDay.Clear();
        SrumWeek.Clear();
        HasSrum = admin?.Srum?.Last24Hours is not null || admin?.Srum?.Last7Days is not null;
        if (admin?.Srum is { } srum)
        {
            Fill(SrumDay, srum.Last24Hours);
            Fill(SrumWeek, srum.Last7Days);
            SrumDaySummary = Describe(srum.Last24Hours, "the last 24 hours");
            SrumWeekHeader = srum.DataFrom is { } from && (DateTimeOffset.Now - from).TotalDays < 6.5
                ? $"Since {from.ToLocalTime():d MMM}"
                : "Last 7 days";
            SrumWeekSummary = Describe(srum.Last7Days, srum.DataFrom is { } f ? $"everything Windows kept (since {Date(f)})" : "the last 7 days");
        }

        SleepRows.Clear();
        HasSleep = admin?.Sleep is not null;
        if (admin?.Sleep is { } sleep)
        {
            foreach (var s in sleep.Sessions.Reverse())
            {
                SleepRows.Add(new SleepRow(
                    DateAndTime(s.Start),
                    Duration(s.Duration),
                    s.OnAc ? "Plugged in" : "Battery",
                    s.EnergyUsed is { } used ? Number(used) + " mWh" : "-",
                    s.PercentPerHour is { } pph ? Percent(pph) : "-",
                    Percent(s.HardwareLowPowerPercent, "0"),
                    s.TopBlockers.FirstOrDefault()?.Name ?? "-"));
            }

            var settings = Settings;
            SleepSummary = sleep.JudgedPercentPerHour is { } rate
                ? $"Asleep on battery it used {Percent(rate)} of a charge per hour over {Duration(sleep.BatterySleepTime)} " +
                  $"(sessions of 30 minutes or more). Above {Percent(settings.HighSleepDrainPercentPerHour)} per hour counts as high. " +
                  "Deep sleep is the share of time in the lowest hardware power state: higher is better."
                : sleep.Sessions.Count == 0
                    ? "No sleep sessions in the sleep study."
                    : $"Only {Duration(sleep.BatterySleepTime)} of sleep on battery (sessions of 30 minutes or more): too little to judge, " +
                      $"at least {Duration(SleepStudy.MinimumJudgedSleep)} is needed. Sessions are listed for reference; the Recent use card has the battery report's sleep figure.";
        }

        EnergyFindings.Clear();
        HasEnergy = admin?.Energy is not null;
        if (admin?.Energy is { } energy)
        {
            foreach (var f in energy.Findings)
            {
                var detail = f.Occurrences.Count switch
                {
                    0 => null,
                    1 => string.Join(", ", f.Occurrences[0].Take(2).Select(kv => $"{kv.Key}: {kv.Value}")),
                    _ => $"{f.Occurrences.Count} times: " + string.Join(", ", f.Occurrences.Take(6).Select(o => o.FirstOrDefault().Value).Where(v => !string.IsNullOrEmpty(v))),
                };
                EnergyFindings.Add(new EnergyFindingRow(f.Severity == EnergySeverity.Error ? Severity.Danger : Severity.Warning, f.Name, f.Description, detail));
            }

            EnergySummary = $"{energy.ErrorCount} errors, {energy.WarningCount} warnings and {energy.InformationalCount} informational items over {Duration(energy.Duration)}. Repeated items are grouped.";
        }

        OnPropertyChanged(nameof(AdminHint));
    }

    private static void Fill(ObservableCollection<SrumRow> rows, EnergyWindow? window)
    {
        if (window is null)
        {
            return;
        }

        var rank = 1;
        foreach (var app in window.Apps)
        {
            rows.Add(new SrumRow(rank++, app.Name, app.SharePercent, Percent(app.SharePercent, "0"), app.MainComponent,
                Percent(app.BackgroundPercent, "0"), app.Offender?.Category, app.Offender?.Advice));
        }
    }

    private static string Describe(EnergyWindow? window, string period) => window is null
        ? $"No energy history for {period}."
        : $"Energy use by app over {period}{(window.OnBatteryOnly ? ", on battery only" : " (the laptop was never on battery, so all time is shown)")}. " +
          $"{Percent(window.UnassignedPercent, "0")} of the energy could not be assigned to an app and is left out.";
}
