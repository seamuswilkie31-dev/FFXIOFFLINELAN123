using System.Globalization;
using System.Windows;
using System.Windows.Data;
using LocalXI.Core.Server;

namespace LocalXI.App.Converters;

public sealed class BoolToVisibilityConverter : IValueConverter
{
    public bool Invert { get; set; }

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        (value is true) ^ Invert ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        (value is Visibility.Visible) ^ Invert;
}

public sealed class NullToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object parameter, CultureInfo culture) =>
        value is null || value is string { Length: 0 } ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}

public sealed class CheckMarkConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value is true ? "✓" : "✗";

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}

public sealed class CheckBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        Application.Current.FindResource(value is true ? "GoodBrush" : "BadBrush");

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}

public sealed class ProcessStateBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value switch
    {
        ServerProcessState.Running => Application.Current.FindResource("GoodBrush"),
        ServerProcessState.Starting => Application.Current.FindResource("WarnBrush"),
        ServerProcessState.Crashed => Application.Current.FindResource("BadBrush"),
        _ => Application.Current.FindResource("SubtleTextBrush"),
    };

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>
/// Binds a radio button to one value of an enum or string property:
/// IsChecked="{Binding Mode, Converter=..., ConverterParameter=Host}".
/// </summary>
public sealed class EnumEqualsConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object parameter, CultureInfo culture) =>
        value?.ToString() == parameter?.ToString();

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not true)
        {
            return Binding.DoNothing;
        }

        var text = parameter.ToString()!;
        return targetType.IsEnum ? Enum.Parse(targetType, text) : text;
    }
}

public sealed class TaskGlyphConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value switch
    {
        ViewModels.SetupTaskState.Running => "…",
        ViewModels.SetupTaskState.Done => "✓",
        ViewModels.SetupTaskState.Failed => "✗",
        _ => "○",
    };

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}

public sealed class TaskBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => Application.Current.FindResource(value switch
    {
        ViewModels.SetupTaskState.Running => "GoldBrush",
        ViewModels.SetupTaskState.Done => "GoodBrush",
        ViewModels.SetupTaskState.Failed => "BadBrush",
        _ => "SubtleTextBrush",
    });

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>Shows enum values as words: BorderlessWindowed becomes "Borderless windowed".</summary>
public sealed partial class EnumDisplayConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object parameter, CultureInfo culture)
    {
        var text = value?.ToString() ?? "";
        return text.Length == 0 ? text : text[0] + WordStart().Replace(text[1..], m => " " + m.Value.ToLowerInvariant());
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();

    [System.Text.RegularExpressions.GeneratedRegex("[A-Z]")]
    private static partial System.Text.RegularExpressions.Regex WordStart();
}
