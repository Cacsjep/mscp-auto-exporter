using System;
using System.Globalization;
using Avalonia.Data;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace AutoExporter.Tray.ViewModels
{
    /// <summary>Red when true (error), muted green otherwise. Used for the service connection line.</summary>
    public sealed class ErrorBrushConverter : IValueConverter
    {
        public static readonly ErrorBrushConverter Instance = new();
        private static readonly IBrush Error = new SolidColorBrush(Color.Parse("#E06363"));
        private static readonly IBrush Ok = new SolidColorBrush(Color.Parse("#7FB069"));

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => value is bool b && b ? Error : Ok;

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => BindingOperations.DoNothing;
    }

    /// <summary>Maps an enum value to bool for radio buttons (ConverterParameter = enum name).</summary>
    public sealed class EnumToBoolConverter : IValueConverter
    {
        public static readonly EnumToBoolConverter Instance = new();

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => value?.ToString() == parameter?.ToString();

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool b && b && parameter != null)
            {
                try { return Enum.Parse(targetType, parameter.ToString()!); }
                catch { return BindingOperations.DoNothing; }
            }
            return BindingOperations.DoNothing;
        }
    }

    /// <summary>Connect button label: "Connecting..." while busy, otherwise "Connect".</summary>
    public sealed class BusyConverter : IValueConverter
    {
        public static readonly BusyConverter Instance = new();

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => value is bool b && b ? "Connecting..." : "Connect";

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => BindingOperations.DoNothing;
    }
}
