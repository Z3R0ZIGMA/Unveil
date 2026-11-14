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
        SourceInitialized += (_, _) => DarkTitleBar.Apply(this);
    }

    /// <summary>The scrollable content, for the screenshot mode.</summary>
    public FrameworkElement RootContent => Root;

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
