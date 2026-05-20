using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using Cleaner.Core.Models;
using Cleaner.Core.Utils;

namespace Cleaner.App.Converters;

public sealed class BytesToSizeConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is long l ? ByteFormatter.Format(l) : value is int i ? ByteFormatter.Format(i) : "0 B";

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

public sealed class SafetyLevelToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        // Funktioniert für SafetyLevel UND IssueSafety (gleiche Enum-Namen)
        var name = value?.ToString() ?? "";
        return name switch
        {
            "Safe"        => new SolidColorBrush(Color.FromRgb(0x3F, 0xB9, 0x50)),
            "Recommended" => new SolidColorBrush(Color.FromRgb(0x58, 0xA6, 0xFF)),
            "Caution"     => new SolidColorBrush(Color.FromRgb(0xD2, 0x99, 0x22)),
            "Warning"     => new SolidColorBrush(Color.FromRgb(0xF8, 0x51, 0x49)),
            _ => Brushes.Gray,
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

public sealed class SafetyLevelToTextConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not SafetyLevel level) return string.Empty;
        return level switch
        {
            SafetyLevel.Safe        => "Sicher",
            SafetyLevel.Recommended => "Empfohlen",
            SafetyLevel.Caution     => "Vorsicht",
            SafetyLevel.Warning     => "Warnung",
            _ => string.Empty,
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

public sealed class BooleanToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is true ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => value is Visibility v && v == Visibility.Visible;
}

public sealed class InverseBooleanToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is true ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

public sealed class IntGreaterZeroToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is int i && i > 0 ? Visibility.Visible : Visibility.Collapsed;
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

public sealed class IntZeroToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is int i && i == 0 ? Visibility.Visible : Visibility.Collapsed;
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

public sealed class NullToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is null ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

public sealed class PercentToWidthConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length < 2) return 0d;
        if (values[0] is not double percent) return 0d;
        if (values[1] is not double totalWidth) return 0d;
        return Math.Clamp(percent, 0, 1) * totalWidth;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
