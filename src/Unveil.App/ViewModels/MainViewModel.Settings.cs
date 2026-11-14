using System.Collections.ObjectModel;
using Unveil.Core.Reports;
using Unveil.Core.SystemInfo;
using Unveil.Core.Verdict;

namespace Unveil.App.ViewModels;

/// <param name="Note">Shown under the value: the advice for a warning, otherwise the extra detail (e.g. which startup apps).</param>
public sealed record SettingRow(Severity Severity, string Name, string Value, string? Note, bool IsWarning);

/// <summary>Power settings checks (Phase 6).</summary>
public sealed partial class MainViewModel
{
    private PowerSettings? _powerSettings;

    public ObservableCollection<SettingRow> SettingRows { get; } = [];

    public bool HasSettings { get => _hasSettings; private set => Set(ref _hasSettings, value); }
    private bool _hasSettings;

    public string SettingsSummary { get => _settingsSummary; private set => Set(ref _settingsSummary, value); }
    private string _settingsSummary = "";

    private void ApplySettings(ReportDocument report)
    {
        SettingRows.Clear();
        foreach (var c in report.SettingChecks)
        {
            var warning = c.Severity == Severity.Warning;
            SettingRows.Add(new SettingRow(c.Severity, c.Name, c.Value, warning ? c.Advice : c.Finding, warning));
        }

        HasSettings = SettingRows.Count > 0;
        var warnings = report.SettingChecks.Count(c => c.Severity == Severity.Warning);
        SettingsSummary = warnings switch
        {
            0 => "All checked settings look fine for battery life.",
            1 => "1 setting is costing battery life.",
            _ => $"{warnings} settings are costing battery life.",
        };
    }
}
