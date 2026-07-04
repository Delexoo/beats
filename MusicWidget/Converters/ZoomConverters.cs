using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace MusicWidget.Converters;

public sealed class ZoomMultiplyConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not double zoom)
        {
            return 0d;
        }

        var baseVal = ParseBase(parameter);
        return baseVal * zoom;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();

    private static double ParseBase(object? parameter)
    {
        if (parameter is double d)
        {
            return d;
        }

        if (parameter is string s
            && double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
        {
            return parsed;
        }

        return 1d;
    }
}

public sealed class ZoomThicknessConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not double zoom)
        {
            return new Thickness(0);
        }

        var parts = (parameter as string ?? "0,0,0,0").Split(',');
        double L = 0, T = 0, R = 0, B = 0;
        if (parts.Length > 0) double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out L);
        if (parts.Length > 1) double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out T);
        if (parts.Length > 2) double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out R);
        if (parts.Length > 3) double.TryParse(parts[3], NumberStyles.Float, CultureInfo.InvariantCulture, out B);
        return new Thickness(L * zoom, T * zoom, R * zoom, B * zoom);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
