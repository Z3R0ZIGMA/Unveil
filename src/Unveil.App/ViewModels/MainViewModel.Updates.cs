using System.IO;
using System.Net.Http;
using System.Windows.Input;
using Unveil.Core.Reports;
using Unveil.Core.Updates;

namespace Unveil.App.ViewModels;

/// <summary>
/// "Check for updates" (toolbar), as in Unravel: looks for a newer release on GitHub only when clicked, asks before
/// downloading, checks the download against SHA256SUMS.txt, swaps it in for the running Unveil.exe and offers to
/// restart. This is the only network use in the app, and it sends nothing about the laptop. Never called by --rmm.
/// </summary>
public sealed partial class MainViewModel
{
    public ICommand CheckForUpdatesCommand { get; private set; } = null!;

    public bool IsUpdating { get => _isUpdating; private set => Set(ref _isUpdating, value); }
    private bool _isUpdating;

    private static Version CurrentVersion => typeof(ReportDocument).Assembly.GetName().Version ?? new Version(0, 0, 0);

    /// <summary>The single-file release exe stands alone; a development build (dotnet run) has Unveil.dll next to it.</summary>
    private static bool IsReleaseBuild =>
        Environment.ProcessPath is { } exe && !File.Exists(Path.Combine(Path.GetDirectoryName(exe)!, "Unveil.dll"));

    private void InitUpdates()
    {
        // Not during a test or the energy diagnostics: a restart would throw away the measurement.
        CheckForUpdatesCommand = new AsyncRelayCommand(CheckForUpdatesAsync, () => !IsUpdating && Stage == TestStage.Idle && !EnergyRunning);
    }

    private async Task CheckForUpdatesAsync()
    {
        IsUpdating = true;
        CommandManager.InvalidateRequerySuggested();
        var current = CurrentVersion;
        StatusText = "Checking GitHub for a newer version...";
        try
        {
            using var http = AppUpdate.CreateClient(current);
            var latest = await AppUpdate.GetLatestAsync(http, CancellationToken.None);
            if (latest is null)
            {
                StatusText = "Couldn't find a release to update to.";
                return;
            }

            if (!AppUpdate.IsNewer(latest.Version, current))
            {
                StatusText = $"Unveil is up to date ({current.ToString(3)}).";
                return;
            }

            var newVersion = latest.Version.ToString(3);
            if (!IsReleaseBuild || Environment.ProcessPath is not { } exe)
            {
                StatusText = $"Unveil {newVersion} is available. This is a development build: get it from the releases page.";
                _ui.OpenUrl(latest.PageUrl);
                return;
            }

            if (!_ui.Confirm($"Unveil {newVersion} is available (you have {current.ToString(3)}).\n\n" +
                             "Download it and update now? It is checked against the release's checksums before it is used.",
                             "Update available"))
            {
                StatusText = $"Unveil {newVersion} is available: click Check for updates when you're ready.";
                return;
            }

            // Next to the running exe when that folder can be written to, so it can replace it; else in Downloads.
            var folder = Path.GetDirectoryName(exe)!;
            var staged = exe + ".new";
            var progress = new Progress<double>(p => StatusText = $"Downloading Unveil {newVersion}... {p:0}%");
            try
            {
                await AppUpdate.DownloadAsync(http, latest, staged, progress, CancellationToken.None);
                AppUpdate.Install(staged, exe);
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
            {
                // Read-only folder (a locked USB stick, Program Files): save it where the user can get it.
                TryDelete(staged);
                var downloads = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
                var saved = Path.Combine(downloads, $"Unveil-{newVersion}{(latest.AssetName.Contains("arm64", StringComparison.Ordinal) ? "-arm64" : "")}.exe");
                await AppUpdate.DownloadAsync(http, latest, saved, progress, CancellationToken.None);
                StatusText = $"Couldn't replace Unveil.exe in {folder}: Unveil {newVersion} was saved to {saved}.";
                _ui.ShowInFolder(saved);
                return;
            }

            if (_ui.Confirm($"Unveil was updated to {newVersion}. Restart it now?", "Update installed"))
            {
                _ui.RestartInto(exe);
                return;
            }

            StatusText = $"Updated to {newVersion}: it starts the next time you open Unveil.";
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException or InvalidDataException
                                       or System.Text.Json.JsonException or UnauthorizedAccessException)
        {
            StatusText = $"Couldn't update: {ex.Message}";
        }
        finally
        {
            IsUpdating = false;
            CommandManager.InvalidateRequerySuggested();
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Left for AppUpdate.CleanUpOldVersion on the next start.
        }
    }
}
