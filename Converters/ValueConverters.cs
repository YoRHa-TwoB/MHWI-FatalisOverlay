using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace FatalisOverlay.Converters;

public class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is true ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>
/// Returns Collapsed when true, Visible when false (inverse of BoolToVisibility)
/// </summary>
public class InverseBoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is true ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>
/// Converts a percentage (0-100) and a container width to a fill width in pixels
/// </summary>
public class GaugeWidthConverter : IMultiValueConverter
{
    public static readonly GaugeWidthConverter Instance = new();

    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length < 2) return 0.0;
        double percent = values[0] switch
        {
            double d => d,
            float f => f,
            int i => i,
            _ => 0
        };
        double containerWidth = values[1] switch
        {
            double d => d,
            _ => 0
        };
        return Math.Max(0, percent / 100.0 * containerWidth);
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>
/// Converts percentage + container width to a Thickness with Left margin for phase markers
/// </summary>
public class PercentToLeftMarginConverter : IMultiValueConverter
{
    public static readonly PercentToLeftMarginConverter Instance = new();

    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        double percent = values[0] switch { double d => d, float f => f, int i => i, _ => 0 };
        double width = values[1] switch { double d => d, _ => 0 };
        double left = Math.Min(percent / 100.0 * width, width - 1);
        return new Thickness(left, 0, 0, 0);
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}
