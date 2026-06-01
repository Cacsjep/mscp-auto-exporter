do we really have god loging in all parts, plugin, agent, tray so when users create GH issues we have all information we need.
you can remove the loglevel from tray, we do always log as much is possible. but with a good max rotation so we never fill
the disk with our logs like 50MB max

i dont want that we have warnings:
Restore complete (0,8s)
  AutoExporter.Contracts succeeded (0,2s) → src\AutoExporter.Contracts\bin\Debug\netstandard2.0\AutoExporter.Contracts.dll
  AutoExporter.Tray succeeded with 15 warning(s) (8,5s) → src\AutoExporter.Tray\bin\Debug\net9.0-windows\win-x64\publish\
    G:\auto-exporter\src\AutoExporter.Tray\Services\Log.cs(19,61): warning CS8625: Ein NULL-Literal kann nicht in einen Non-Nullable-Verweistyp konvertiert werden.
    G:\auto-exporter\src\AutoExporter.Tray\ViewModels\Converters.cs(16,23): warning CS8767: Die NULL-Zulässigkeit von Verweistypen im Typ des Parameters "value" von "object ErrorBrushConverter.Convert(obj
ect value, Type targetType, object parameter, CultureInfo culture)" entspricht (möglicherweise aufgrund von Attributen für die NULL-Zulässigkeit) nicht dem implizit implementierten Member "object? IValueC
onverter.Convert(object? value, Type targetType, object? parameter, CultureInfo culture)".
    G:\auto-exporter\src\AutoExporter.Tray\ViewModels\Converters.cs(28,23): warning CS8767: Die NULL-Zulässigkeit von Verweistypen im Typ des Parameters "value" von "object EnumToBoolConverter.Convert(obj
ect value, Type targetType, object parameter, CultureInfo culture)" entspricht (möglicherweise aufgrund von Attributen für die NULL-Zulässigkeit) nicht dem implizit implementierten Member "object? IValueC
onverter.Convert(object? value, Type targetType, object? parameter, CultureInfo culture)".
    G:\auto-exporter\src\AutoExporter.Tray\ViewModels\Converters.cs(16,23): warning CS8767: Die NULL-Zulässigkeit von Verweistypen im Typ des Parameters "parameter" von "object ErrorBrushConverter.Convert
(object value, Type targetType, object parameter, CultureInfo culture)" entspricht (möglicherweise aufgrund von Attributen für die NULL-Zulässigkeit) nicht dem implizit implementierten Member "object? IVa
lueConverter.Convert(object? value, Type targetType, object? parameter, CultureInfo culture)".
    G:\auto-exporter\src\AutoExporter.Tray\ViewModels\Converters.cs(19,23): warning CS8767: Die NULL-Zulässigkeit von Verweistypen im Typ des Parameters "value" von "object ErrorBrushConverter.ConvertBack
(object value, Type targetType, object parameter, CultureInfo culture)" entspricht (möglicherweise aufgrund von Attributen für die NULL-Zulässigkeit) nicht dem implizit implementierten Member "object? IVa
lueConverter.ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)".
    G:\auto-exporter\src\AutoExporter.Tray\ViewModels\Converters.cs(28,23): warning CS8767: Die NULL-Zulässigkeit von Verweistypen im Typ des Parameters "parameter" von "object EnumToBoolConverter.Convert
(object value, Type targetType, object parameter, CultureInfo culture)" entspricht (möglicherweise aufgrund von Attributen für die NULL-Zulässigkeit) nicht dem implizit implementierten Member "object? IVa
lueConverter.Convert(object? value, Type targetType, object? parameter, CultureInfo culture)".
    G:\auto-exporter\src\AutoExporter.Tray\ViewModels\Converters.cs(19,23): warning CS8767: Die NULL-Zulässigkeit von Verweistypen im Typ des Parameters "parameter" von "object ErrorBrushConverter.Convert
Back(object value, Type targetType, object parameter, CultureInfo culture)" entspricht (möglicherweise aufgrund von Attributen für die NULL-Zulässigkeit) nicht dem implizit implementierten Member "object?
 IValueConverter.ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)".
    G:\auto-exporter\src\AutoExporter.Tray\ViewModels\Converters.cs(31,23): warning CS8767: Die NULL-Zulässigkeit von Verweistypen im Typ des Parameters "value" von "object EnumToBoolConverter.ConvertBack
(object value, Type targetType, object parameter, CultureInfo culture)" entspricht (möglicherweise aufgrund von Attributen für die NULL-Zulässigkeit) nicht dem implizit implementierten Member "object? IVa
lueConverter.ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)".
    G:\auto-exporter\src\AutoExporter.Tray\ViewModels\Converters.cs(31,23): warning CS8767: Die NULL-Zulässigkeit von Verweistypen im Typ des Parameters "parameter" von "object EnumToBoolConverter.Convert
Back(object value, Type targetType, object parameter, CultureInfo culture)" entspricht (möglicherweise aufgrund von Attributen für die NULL-Zulässigkeit) nicht dem implizit implementierten Member "object?
 IValueConverter.ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)".
    G:\auto-exporter\src\AutoExporter.Tray\ViewModels\Converters.cs(47,23): warning CS8767: Die NULL-Zulässigkeit von Verweistypen im Typ des Parameters "value" von "object BusyConverter.Convert(object va
lue, Type targetType, object parameter, CultureInfo culture)" entspricht (möglicherweise aufgrund von Attributen für die NULL-Zulässigkeit) nicht dem implizit implementierten Member "object? IValueConvert
er.Convert(object? value, Type targetType, object? parameter, CultureInfo culture)".
    G:\auto-exporter\src\AutoExporter.Tray\ViewModels\Converters.cs(47,23): warning CS8767: Die NULL-Zulässigkeit von Verweistypen im Typ des Parameters "parameter" von "object BusyConverter.Convert(objec
t value, Type targetType, object parameter, CultureInfo culture)" entspricht (möglicherweise aufgrund von Attributen für die NULL-Zulässigkeit) nicht dem implizit implementierten Member "object? IValueCon
verter.Convert(object? value, Type targetType, object? parameter, CultureInfo culture)".
    G:\auto-exporter\src\AutoExporter.Tray\ViewModels\Converters.cs(50,23): warning CS8767: Die NULL-Zulässigkeit von Verweistypen im Typ des Parameters "value" von "object BusyConverter.ConvertBack(objec
t value, Type targetType, object parameter, CultureInfo culture)" entspricht (möglicherweise aufgrund von Attributen für die NULL-Zulässigkeit) nicht dem implizit implementierten Member "object? IValueCon
verter.ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)".
    G:\auto-exporter\src\AutoExporter.Tray\ViewModels\Converters.cs(50,23): warning CS8767: Die NULL-Zulässigkeit von Verweistypen im Typ des Parameters "parameter" von "object BusyConverter.ConvertBack(o
bject value, Type targetType, object parameter, CultureInfo culture)" entspricht (möglicherweise aufgrund von Attributen für die NULL-Zulässigkeit) nicht dem implizit implementierten Member "object? IValu
eConverter.ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)".
    G:\auto-exporter\src\AutoExporter.Tray\Services\Log.cs(17,67): warning CS8625: Ein NULL-Literal kann nicht in einen Non-Nullable-Verweistyp konvertiert werden.
    G:\auto-exporter\src\AutoExporter.Tray\Services\Log.cs(18,67): warning CS8625: Ein NULL-Literal kann nicht in einen Non-Nullable-Verweistyp konvertiert werden.