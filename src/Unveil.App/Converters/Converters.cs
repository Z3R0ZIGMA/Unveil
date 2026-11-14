using System.Globalization;
using System.Windows;
using System.Windows.Data;
using Unveil.Core.Verdict;

namespace Unveil.App.Converters;

/// <summary>Severity to the theme's traffic-light brush (Success / Warning / Danger / Muted).</summary>
public sealed class SeverityToBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        Application.Current.FindResource(value switch
        {
            Severity.Good => "Success",
            Severity.Warning => "Warning",
            Severity.Danger => "Danger",
            _ => "Muted",
        });

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Severity to an icon glyph, so status is never shown by colour alone.</summary>
public sealed class SeverityToGlyphConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value switch
    {
        Severity.Good => "",     // check mark
        Severity.Warning => "",  // warning
        Severity.Danger => "",   // cross
        _ => "",                 // info
    };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Severity to the word shown above the verdict.</summary>
public sealed class SeverityToLabelConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var label = value switch
        {
            Severity.Good => "Good",
            Severity.Warning => "Attention",
            Severity.Danger => "Action needed",
            _ => "Info",
        };
        return string.Equals(parameter as string, "upper", StringComparison.OrdinalIgnoreCase)
            ? label.ToUpperInvariant()
            : label;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>True to Visible. Pass "invert" as the parameter to flip it.</summary>
public sealed class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var visible = value is true;
        if (string.Equals(parameter as string, "invert", StringComparison.OrdinalIgnoreCase))
        {
            visible = !visible;
        }

        return visible ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Collapses an element when its value is null or an empty string.</summary>
public sealed class NullToCollapsedConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is null || value is string { Length: 0 } ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
