using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Unveil.App.ViewModels;
using Unveil.Core.Battery;

namespace Unveil.App;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += OnUnhandledException;

        var window = new MainWindow();
        ILiveBatterySource live = e.Args.Contains("--simulate-discharge", StringComparer.OrdinalIgnoreCase)
            ? new SimulatedDischarge(new WmiLiveBatteryReader())
            : new WmiLiveBatteryReader();
        var viewModel = new MainViewModel(() => BatteryCollector.CreateDefault().Collect(), live, window);
        window.DataContext = viewModel;
        MainWindow = window;

        // Developer aid: `Unveil.exe --screenshot <file.png>` renders the dashboard to an image and exits.
        var screenshotPath = ScreenshotPath(e.Args);
        if (screenshotPath is not null)
        {
            window.WindowStartupLocation = WindowStartupLocation.Manual;
            window.Left = -20000;
            window.ShowActivated = false;
            viewModel.ReportLoaded += async (_, _) =>
            {
                // Let live readings and layout settle (longer when simulating, so the sparkline has points).
                var simulating = e.Args.Contains("--simulate-discharge", StringComparer.OrdinalIgnoreCase);
                await Task.Delay(TimeSpan.FromSeconds(simulating ? 16 : 2.5));
                SaveScreenshot(window.RootContent, screenshotPath);
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
            dc.DrawRectangle(new VisualBrush(element), null, new Rect(0, 0, width, height));
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
