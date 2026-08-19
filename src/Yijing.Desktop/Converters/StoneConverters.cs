using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using Yijing.Domain.Board;

namespace Yijing.Desktop.Converters;

public sealed class StoneColorToBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value switch
    {
        StoneColor.Black => Brushes.Black,
        StoneColor.White => Brushes.White,
        _ => Brushes.Transparent
    };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        Binding.DoNothing;
}

public sealed class EnumEqualsConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is not null && Normalize(parameter, value.GetType(), culture) is { } normalized && value.Equals(normalized);

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? Normalize(parameter, targetType, culture) ?? Binding.DoNothing : Binding.DoNothing;

    private static object? Normalize(object? value, Type targetType, CultureInfo culture)
    {
        if (value is null || targetType.IsInstanceOfType(value)) return value;
        if (targetType.IsEnum && value is string text) return Enum.Parse(targetType, text, true);
        return System.Convert.ChangeType(value, targetType, culture);
    }
}

public sealed class BooleanToOpacityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? 1d : 0.58d;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        DependencyProperty.UnsetValue;
}
