using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Unveil.App.ViewModels;
using Unveil.Core.Battery;
using Unveil.Core.Power;

namespace Unveil.App;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += OnUnhandledException;

        // unveil.json next to the exe: thresholds, laptop type, test length, extra known offenders.
        var config = Unveil.Core.Config.ConfigLoader.Load(Unveil.Core.Config.ConfigLoader.ExeFolder);
        config.Config.ApplyKnownOffenders();

        var window = new MainWindow();
        ILiveBatterySource live = e.Args.Contains("--simulate-discharge", StringComparer.OrdinalIgnoreCase)
            ? new SimulatedDischarge(new WmiLiveBatteryReader())
            : new WmiLiveBatteryReader();
        var viewModel = new MainViewModel(
            () => BatteryCollector.CreateDefault().Collect(),
            live,
            window,
            () => new DrainTest(live, new PerfCounterActivitySource()),
            config);
        window.DataContext = viewModel;
        MainWindow = window;

        // Developer aid: `Unveil.exe --screenshot <file.png>` renders the dashboard to an image and exits.
        var screenshotPath = ScreenshotPath(e.Args);
        if (screenshotPath is not null)
        {
            window.WindowStartupLocation = WindowStartupLocation.Manual;
            window.Left = -20000;
            window.ShowActivated = false;
            var testSeconds = TestSeconds(e.Args);
            viewModel.ReportLoaded += async (_, _) =>
            {
                // Developer aid: `--test-seconds N` runs a short full test (without waiting for unplug) before the screenshot.
                if (testSeconds is { } seconds)
                {
                    await viewModel.RunTestAsync(skipUnplug: true, TimeSpan.FromSeconds(seconds));
                }

                // Developer aid: `--run-energy` runs the 60-second energy diagnostics first (admin only).
                if (e.Args.Contains("--run-energy", StringComparer.OrdinalIgnoreCase) && viewModel.IsAdministrator)
                {
                    await viewModel.RunEnergyAsync();
                }

                // Wait for the admin data (energy history, sleep study) that loads in the background.
                for (var i = 0; i < 60 && viewModel.AdminLoading; i++)
                {
                    await Task.Delay(500);
                }

                // Let live readings and layout settle (longer when simulating, so the sparkline has points).
                var simulating = e.Args.Contains("--simulate-discharge", StringComparer.OrdinalIgnoreCase);
                await Task.Delay(TimeSpan.FromSeconds(simulating ? 16 : 2.5));
                // Developer aid: `--show-tab all` saves one image per power hogs tab (file-tab0.png ... file-tab3.png).
                if (e.Args.Contains("--show-tab", StringComparer.OrdinalIgnoreCase))
                {
                    for (var tab = 0; tab < window.PowerTabControl.Items.Count; tab++)
                    {
                        window.PowerTabControl.SelectedIndex = tab;
                        await Task.Delay(300);
                        SaveScreenshot(window.RootContent, Path.ChangeExtension(screenshotPath, null) + $"-tab{tab}.png");
                    }
                }
                else
                {
                    SaveScreenshot(window.RootContent, screenshotPath);
                }

                Shutdown();
            };
        }

        window.Show();
        _ = viewModel.RefreshAsync();
    }

    /// <summary>
    /// Developer aid (<c>--simulate-discharge</c>): pretends the laptop is unplugged and drawing 7-10 W,
    /// so the on-battery layout can be checked without unplugging. Uses the real charge figures.
    /// </summary>
    private sealed class SimulatedDischarge(ILiveBatterySource real) : ILiveBatterySource
    {
        private int _tick;

        public LiveReading? Read()
        {
            var reading = real.Read();
            _tick++;
            var watts = 8.2 + 1.4 * Math.Sin(_tick / 2.0) + (_tick % 3) * 0.2;
            return reading is null ? null : reading with
            {
                PowerOnline = false,
                Charging = false,
                Discharging = true,
                DischargeRateMilliwatts = (int)(watts * 1000),
            };
        }
    }

    private static int? TestSeconds(string[] args)
    {
        var i = Array.FindIndex(args, a => string.Equals(a, "--test-seconds", StringComparison.OrdinalIgnoreCase));
        return i >= 0 && i + 1 < args.Length && int.TryParse(args[i + 1], out var s) ? s : null;
    }

    private static string? ScreenshotPath(string[] args)
    {
        var i = Array.FindIndex(args, a => string.Equals(a, "--screenshot", StringComparison.OrdinalIgnoreCase));
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }

    private static void SaveScreenshot(FrameworkElement element, string path)
    {
        element.UpdateLayout();
        var width = (int)Math.Ceiling(element.ActualWidth);
        var height = (int)Math.Ceiling(element.ActualHeight);

        // Draw through a VisualBrush so the element's own margin/offset does not shift the image.
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(new VisualBrush(element) { Stretch = Stretch.None, AlignmentX = AlignmentX.Left, AlignmentY = AlignmentY.Top }, null, new Rect(0, 0, width, height));
        }

        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        using var file = File.Create(path);
        encoder.Save(file);
    }

    private void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        // Degrade gracefully: tell the user, keep the app running.
        MessageBox.Show(MainWindow, $"Something went wrong: {e.Exception.Message}", "Unveil",
            MessageBoxButton.OK, MessageBoxImage.Warning);
        e.Handled = true;
    }
}
