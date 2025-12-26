using System.Globalization;

namespace RacerRemote.Converters;

public sealed class BoolToOnOffColorConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var isOn = value is bool b && b;
        if (!isOn)
        {
            return Colors.Transparent;
        }

        var name = parameter as string;
        return name?.ToLowerInvariant() switch
        {
            "red" => Colors.Red,
            "green" => Colors.LimeGreen,
            "blue" => Colors.DeepSkyBlue,
            "yellow" => Colors.Gold,
            _ => Colors.White,
        };
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
