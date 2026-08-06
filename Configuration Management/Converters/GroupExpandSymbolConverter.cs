using System.Globalization;
using System.Windows.Data;
using Configuration_Management.ViewModels;

namespace Configuration_Management.Converters;

/// <summary>
/// Возвращает символ «−» или «+» для кнопки сворачивания/разворачивания группы.
/// Принимает имя группы и MainViewModel; возвращает «−», если группа развёрнута, «+» — если свёрнута.
/// </summary>
public class GroupExpandSymbolConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length >= 2 && values[0] is string groupName && values[1] is MainViewModel vm)
        {
            return vm.IsGroupCollapsed(groupName) ? "+" : "−";
        }
        return "−";
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}