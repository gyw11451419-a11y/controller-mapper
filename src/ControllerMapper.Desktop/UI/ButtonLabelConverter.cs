using System.Globalization;
using System.Windows.Data;
using ControllerMapper.Desktop.Core;
using ControllerMapper.Desktop.ViewModels;

namespace ControllerMapper.Desktop.UI;

public sealed class PageVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        ((parameter as string) == "NotHome" ? (value as string) != "Home" : Equals(value, parameter))
            ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}

public sealed class ButtonLabelConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is GamepadButton button ? ControllerButtonLabels.XboxLabel(button) : "未设置";

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}

public sealed class InputButtonLabelConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture) =>
        values.Length > 0 && values[0] is GamepadButton button
            ? ControllerButtonLabels.InputLabel(button,
                values.Length > 1 ? values[1] as DeviceDescriptor : null)
            : "未设置";

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class SelectedButtonConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture) =>
        values.Length == 2 && values[0] is GamepadButton selected && values[1] is GamepadButton button && selected == button;

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) => throw new NotSupportedException();
}

public sealed class PressedButtonConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture) =>
        values.Length == 2 && values[0] is string live && values[1] is GamepadButton button &&
        live.Split('、').Contains(button.ToString(), StringComparer.Ordinal);

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) => throw new NotSupportedException();
}
