using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using DataStock.Windows.Models;
using WpfApplication = System.Windows.Application;
using WpfBinding = System.Windows.Data.Binding;
using WpfBrush = System.Windows.Media.Brush;
using WpfBrushes = System.Windows.Media.Brushes;

namespace DataStock.Windows.ViewModels;

public sealed class NullToVisibilityConverter : IValueConverter
{
    public bool Invert { get; set; }

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var isVisible = value is not null;
        if (Invert)
        {
            isVisible = !isVisible;
        }

        return isVisible ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}

public sealed class CountToVisibilityConverter : IValueConverter
{
    public bool Invert { get; set; }

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var count = value switch
        {
            int directCount => directCount,
            System.Collections.ICollection collection => collection.Count,
            _ => 0
        };

        var isVisible = count > 0;
        if (Invert)
        {
            isVisible = !isVisible;
        }

        return isVisible ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}

public sealed class EnumVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var target = parameter?.ToString();
        var isVisible = value?.ToString()?.Equals(target, StringComparison.OrdinalIgnoreCase) == true;
        return isVisible ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}

public sealed class RunModeCheckedConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value?.ToString()?.Equals(parameter?.ToString(), StringComparison.OrdinalIgnoreCase) == true;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is true && parameter is not null
            ? Enum.Parse(typeof(SyncRunMode), parameter.ToString()!, ignoreCase: true)
            : WpfBinding.DoNothing;
    }
}

public sealed class LogLevelBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value switch
        {
            SyncLogLevel.Success => WpfApplication.Current.Resources["SuccessBrush"] as WpfBrush ?? WpfBrushes.Green,
            SyncLogLevel.Warning => WpfApplication.Current.Resources["WarningBrush"] as WpfBrush ?? WpfBrushes.DarkOrange,
            SyncLogLevel.Error => WpfApplication.Current.Resources["ErrorBrush"] as WpfBrush ?? WpfBrushes.Red,
            _ => WpfApplication.Current.Resources["AccentBrush"] as WpfBrush ?? WpfBrushes.DodgerBlue
        };
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}

public sealed class ExclusionGlyphConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object? parameter, CultureInfo culture)
    {
        var relativePath = values.Length > 0 ? values[0]?.ToString() : null;
        var sourcePath = values.Length > 1 ? values[1]?.ToString() : null;
        if (string.IsNullOrWhiteSpace(relativePath) || string.IsNullOrWhiteSpace(sourcePath))
        {
            return "\uE8A5";
        }

        try
        {
            var fullPath = Path.GetFullPath(Path.Combine(sourcePath, relativePath.Replace('/', Path.DirectorySeparatorChar)));
            return Directory.Exists(fullPath) ? "\uE8B7" : "\uE8A5";
        }
        catch
        {
            return "\uE8A5";
        }
    }

    public object[] ConvertBack(object? value, Type[] targetTypes, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}

public sealed class RunModeTextConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is SyncRunMode mode ? L10n.RunModeLabel(mode) : "";
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
