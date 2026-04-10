using System;
using System.Globalization;
using System.IO;
using Avalonia.Data.Converters;
using Avalonia.Media.Imaging;

namespace OptiscalerClient.Converters;

public class NullToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value != null && !(value is string s && string.IsNullOrEmpty(s));
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

public class BoolToVisibilityConverter : IValueConverter
{
    public bool Invert { get; set; }

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        bool isVisible = value is bool b && b;
        if (Invert) isVisible = !isVisible;

        return isVisible;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

public class BitmapValueConverter : IValueConverter
{
    public static readonly BitmapValueConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string path && !string.IsNullOrWhiteSpace(path))
        {
            try
            {
                if (File.Exists(path))
                {
                    return new Bitmap(path);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[BitmapConverter] Failed to load image from '{path}': {ex.Message}");
                return null;
            }
        }

        return null;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}

public class MultiplyConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is double d && parameter is string s && double.TryParse(s, System.Globalization.NumberStyles.Any, CultureInfo.InvariantCulture, out var factor))
            return d * factor;
        return value ?? 0.0;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

public class WidthToColumnSpanConverter : IValueConverter
{
    public double Threshold { get; set; } = 760.0;

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value == null) return 1;

        double width;
        try
        {
            width = System.Convert.ToDouble(value, CultureInfo.InvariantCulture);
        }
        catch
        {
            return 1;
        }

        return width < Threshold ? 2 : 1;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

public class WidthToCardWidthConverter : IValueConverter
{
    public double Threshold { get; set; } = 760.0;
    // total horizontal padding between cards (approx): window padding + margins
    public double HorizontalGutter { get; set; } = 48.0;

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value == null) return double.NaN;

        double width;
        try
        {
            width = System.Convert.ToDouble(value, CultureInfo.InvariantCulture);
        }
        catch
        {
            return double.NaN;
        }

        if (width < Threshold)
        {
            // single column: full available width minus margins
            return width - HorizontalGutter;
        }

        // two columns: half available width minus gutter/2
        return Math.Max(320.0, (width - HorizontalGutter) / 2.0);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

