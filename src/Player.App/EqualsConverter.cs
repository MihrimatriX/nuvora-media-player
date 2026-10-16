using System.Globalization;
using Avalonia.Data;
using Avalonia.Data.Converters;

namespace Player.App;

/// <summary>Binds a RadioButton to one value of a string property: checked when equal, sets it when checked.</summary>
public sealed class EqualsConverter : IValueConverter
{
    public static readonly EqualsConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        Equals(value?.ToString(), parameter?.ToString());

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? parameter : BindingOperations.DoNothing;
}
