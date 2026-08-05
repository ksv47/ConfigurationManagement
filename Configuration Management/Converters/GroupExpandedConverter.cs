using System.Globalization;
using System.Windows.Data;
using Configuration_Management.ViewModels;

namespace Configuration_Management.Converters;

/// <summary>
/// Конвертер для определения состояния развёрнутости группы в списке баз.
/// Принимает имя группы и MainViewModel, возвращает true, если группа развёрнута.
/// </summary>
public class GroupExpandedConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length >= 2 && values[0] is string groupName && values[1] is MainViewModel vm)
        {
            return !vm.IsGroupCollapsed(groupName);
        }
        return true;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}