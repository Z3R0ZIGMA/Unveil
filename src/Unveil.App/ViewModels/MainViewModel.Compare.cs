using System.IO;
using System.Windows.Input;
using Unveil.Core;
using Unveil.Core.Reports;
using static Unveil.Core.Formatting;

namespace Unveil.App.ViewModels;

/// <summary>Comparison with an earlier report for this laptop (Phase 8).</summary>
public sealed partial class MainViewModel
{
    private readonly DateTimeOffset _sessionStart = DateTimeOffset.Now;
    private PreviousReport? _previous;
    private bool _searchedForPrevious;
    private string? _comparisonSearchNote;

    /// <summary>Where to look for earlier reports. Settable for tests; by default next to Unveil.exe and the usual user folders.</summary>
    public Func<IEnumerable<(string Folder, int Depth)>> ReportFolders { get; set; } = DefaultReportFolders;

    public ICommand CompareWithCommand { get; private set; } = null!;

    public ICommand ClearComparisonCommand { get; private set; } = null!;

    public bool HasComparison { get => _hasComparison; private set => Set(ref _hasComparison, value); }
    private bool _hasComparison;

    public string ComparisonTitle { get => _comparisonTitle; private set => Set(ref _comparisonTitle, value); }
    private string _comparisonTitle = "Since the last check";

    public string ComparisonSource { get => _comparisonSource; private set => Set(ref _comparisonSource, value); }
    private string _comparisonSource = "";

    public string ComparisonSummary { get => _comparisonSummary; private set => Set(ref _comparisonSummary, value); }
    private string _comparisonSummary = "";

    public IReadOnlyList<ComparisonRow> ComparisonRows { get => _comparisonRows; private set => Set(ref _comparisonRows, value); }
    private IReadOnlyList<ComparisonRow> _comparisonRows = [];

    public string ComparisonNotes { get => _comparisonNotes; private set => Set(ref _comparisonNotes, value); }
    private string _comparisonNotes = "";

    public string ComparisonEmptyText { get => _comparisonEmptyText; private set => Set(ref _comparisonEmptyText, value); }
    private string _comparisonEmptyText = "";

    private void InitCompare()
    {
        CompareWithCommand = new RelayCommand(CompareWithFile, () => Report is { Snapshot.HasBattery: true });
        ClearComparisonCommand = new RelayCommand(ClearComparison, () => _previous is not null);
    }

    private static IEnumerable<(string Folder, int Depth)> DefaultReportFolders()
    {
        // Next to Unveil.exe (e.g. a Reports folder on the USB stick), then where reports are usually saved.
        if (Path.GetDirectoryName(Environment.ProcessPath) is { } exeFolder)
        {
            yield return (exeFolder, 2);
        }

        yield return (Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), 0);
        yield return (Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), 1);
        yield return (Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads"), 0);

        // Reports from N-sight runs on this laptop.
        yield return (Unveil.Core.Rmm.RmmOptions.DefaultReportFolder, 0);
    }

    /// <summary>Once per run: looks for the latest earlier report for this laptop. Never throws.</summary>
    private async Task FindPreviousOnceAsync(ReportDocument report)
    {
        if (_searchedForPrevious || !report.Snapshot.HasBattery)
        {
            return;
        }

        _searchedForPrevious = true;
        try
        {
            var log = new CollectionLog();
            var found = await Task.Run(() => PreviousReportFinder.FindLatest(ReportFolders(), report, _sessionStart.AddMinutes(-1), log));
            _previous = found?.Report;
            _comparisonSearchNote = found is null
                ? "Comparison: no earlier report for this laptop was found next to Unveil.exe, on the Desktop, in Documents, in Downloads or in the N-sight reports folder."
                : log.Entries.LastOrDefault()?.Message;
        }
#pragma warning disable CA1031 // A failed search must never stop the check.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            _comparisonSearchNote = $"Comparison: could not look for earlier reports ({ex.Message}).";
        }
    }

    /// <summary>Adds the comparison to a freshly built report.</summary>
    private ReportDocument WithComparison(ReportDocument report) =>
        _previous is { } previous && report.Snapshot.HasBattery
            ? report with { Comparison = ReportComparer.Compare(report, previous) }
            : report with { Comparison = null };

    private void ApplyComparison(ReportDocument report)
    {
        var c = report.Comparison is { Match: not ComparisonMatch.NoMatch } match ? match : null;
        HasComparison = c is not null;
        if (c is null)
        {
            ComparisonTitle = "Since the last check";
            ComparisonSource = ComparisonSummary = ComparisonNotes = "";
            ComparisonRows = [];
            ComparisonEmptyText = "No earlier report for this laptop. Export a report on each visit (it saves a .json next to the .html) " +
                                  "and Unveil shows the change next time. Keep them next to Unveil.exe, on the Desktop, in Documents or in Downloads, " +
                                  "or choose one with \"Compare with a report...\".";
            return;
        }

        ComparisonTitle = c.Match == ComparisonMatch.BatteryReplaced
            ? $"Battery replaced since {Date(c.PreviousGeneratedAt)}"
            : $"Since the check on {Date(c.PreviousGeneratedAt)}";
        ComparisonSource = string.Join(" - ", new[]
        {
            DateAndTime(c.PreviousGeneratedAt),
            c.PreviousFileName,
            c.PreviousGenerator,
        }.Where(x => !string.IsNullOrWhiteSpace(x)));
        ComparisonSummary = c.Summary;
        ComparisonRows = c.Rows();
        ComparisonNotes = string.Join(" ", c.Notes);
    }

    private void CompareWithFile()
    {
        if (Report is null || _ui.PickOpenPath("Compare with an earlier Unveil report", "Unveil report (*.json)|*.json") is not { } path)
        {
            return;
        }

        PreviousReport previous;
        try
        {
            previous = PreviousReportReader.ReadFile(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            StatusText = $"Could not read {Path.GetFileName(path)}: {ex.Message}";
            return;
        }

        if (previous.GeneratedAt >= Report.GeneratedAt)
        {
            StatusText = $"{Path.GetFileName(path)} is from {DateAndTime(previous.GeneratedAt)}, not before this check. Choose an earlier report.";
            return;
        }

        var comparison = ReportComparer.Compare(Report, previous);
        if (comparison.Match == ComparisonMatch.NoMatch)
        {
            StatusText = comparison.Summary + " Nothing to compare.";
            return;
        }

        _previous = previous;
        _comparisonSearchNote = $"Comparison: compared with {previous.FileName}, chosen by hand.";
        Apply(Report);
        StatusText = $"Compared with the report from {Date(previous.GeneratedAt)}.";
    }

    private void ClearComparison()
    {
        if (Report is null)
        {
            return;
        }

        _previous = null;
        _comparisonSearchNote = "Comparison: cleared.";
        Apply(Report);
        StatusText = "Comparison cleared.";
    }
}
