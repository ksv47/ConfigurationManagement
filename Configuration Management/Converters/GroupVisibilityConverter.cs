using System.Globalization;
using System.Windows;
using System.Windows.Data;
using Configuration_Management.ViewModels;

namespace Configuration_Management.Converters;

/// <summary>
/// Возвращает Visibility строки базы в зависимости от состояния свёрнутости её группы.
/// Принимает имя группы и MainViewModel; возвращает Collapsed, если группа свёрнута.
/// </summary>
public class GroupVisibilityConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length >= 2 && values[0] is string groupName && values[1] is MainViewModel vm)
        {
            return vm.IsGroupCollapsed(groupName) ? Visibility.Collapsed : Visibility.Visible;
        }
        return Visibility.Visible;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}