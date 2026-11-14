using System.Diagnostics;
using System.Windows;
using Microsoft.Win32;
using Unveil.App.Native;
using Unveil.App.ViewModels;

namespace Unveil.App;

public partial class MainWindow : Window, IUiServices
{
    public MainWindow()
    {
        InitializeComponent();
        FitToScreen();
        SourceInitialized += (_, _) => DarkTitleBar.Apply(this);
    }

    /// <summary>
    /// Keeps the whole window, title bar included, on screen. A 14" laptop at 150% scaling has only about
    /// 1280 x 672 usable units, less than the default 1200 x 880.
    /// </summary>
    private void FitToScreen()
    {
        const double margin = 24;
        var area = SystemParameters.WorkArea;

        MinWidth = Math.Min(MinWidth, area.Width);
        MinHeight = Math.Min(MinHeight, area.Height);
        Width = Math.Min(Width, area.Width - margin);
        Height = Math.Min(Height, area.Height - margin);

        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = area.Left + (area.Width - Width) / 2;
        Top = area.Top + (area.Height - Height) / 2;
    }

    /// <summary>The scrollable content, for the screenshot mode.</summary>
    public FrameworkElement RootContent => Root;

    /// <summary>The power hogs tabs, for the screenshot mode.</summary>
    public System.Windows.Controls.TabControl PowerTabControl => PowerTabs;

    public void CopyText(string text) => Clipboard.SetText(text);

    public string? PickSavePath(string suggestedFileName)
    {
        var dialog = new SaveFileDialog
        {
            Title = "Export report",
            FileName = suggestedFileName,
            DefaultExt = ".html",
            Filter = "HTML report (*.html)|*.html",
            AddExtension = true,
        };
        return dialog.ShowDialog(this) == true ? dialog.FileName : null;
    }

    public string? PickFolder(string title)
    {
        var dialog = new OpenFolderDialog { Title = title };
        return dialog.ShowDialog(this) == true ? dialog.FolderName : null;
    }

    public bool RestartAsAdministrator()
    {
        if (Environment.ProcessPath is not { } exe)
        {
            return false;
        }

        try
        {
            // Shows the Windows UAC prompt. Cancelling it throws (error 1223) and Unveil keeps running.
            using var _ = Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true, Verb = "runas" });
            Application.Current.Shutdown();
            return true;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }

    public void OpenFile(string path)
    {
        try
        {
            // Opens the local file in its default app (the browser). No network involved.
            using var _ = Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            // The report is saved; failing to open it is not worth interrupting the user.
        }
    }
}
