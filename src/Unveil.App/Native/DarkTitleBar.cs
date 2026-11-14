using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace Unveil.App.Native;

/// <summary>Makes the window's title bar dark to match the theme (plan.md section 5.1).</summary>
internal static class DarkTitleBar
{
    private const int DwmwaUseImmersiveDarkMode = 20;
    private const int DwmwaUseImmersiveDarkModeOld = 19; // Windows 10 before 20H1
    private const int DwmwaBorderColor = 34;             // Windows 11 only
    private const int DwmwaCaptionColor = 35;            // Windows 11 only
    private const int DwmwaTextColor = 36;               // Windows 11 only

    public static void Apply(Window window)
    {
        var hwnd = new WindowInteropHelper(window).EnsureHandle();
        var on = 1;
        if (DwmSetWindowAttribute(hwnd, DwmwaUseImmersiveDarkMode, ref on, sizeof(int)) != 0)
        {
            DwmSetWindowAttribute(hwnd, DwmwaUseImmersiveDarkModeOld, ref on, sizeof(int));
        }

        // On Windows 11, match the caption exactly to the theme colours. These calls fail harmlessly on Windows 10.
        SetColor(hwnd, DwmwaCaptionColor, "WindowColor");
        SetColor(hwnd, DwmwaBorderColor, "BorderColor");
        SetColor(hwnd, DwmwaTextColor, "TextColor");
    }

    private static void SetColor(IntPtr hwnd, int attribute, string resourceKey)
    {
        if (Application.Current.TryFindResource(resourceKey) is Color c)
        {
            var colorRef = c.R | (c.G << 8) | (c.B << 16); // COLORREF is 0x00BBGGRR
            DwmSetWindowAttribute(hwnd, attribute, ref colorRef, sizeof(int));
        }
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
}
