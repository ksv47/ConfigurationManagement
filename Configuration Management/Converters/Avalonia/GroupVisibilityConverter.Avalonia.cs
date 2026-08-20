#if LINUX
using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia.Controls;
using Avalonia.Data.Converters;
using Configuration_Management.ViewModels;

namespace Configuration_Management.Converters
{
    /// <summary>
    /// Avalonia-версия: возвращает Visibility строки базы в зависимости от состояния свёрнутости её группы.
    /// Принимает имя группы и MainViewModel; возвращает Collapsed, если группа свёрнута.
    /// </summary>
    public class GroupVisibilityConverter : IMultiValueConverter
    {
        public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
        {
            if (values is { Count: >= 2 } && values[0] is string groupName && values[1] is MainViewModel vm)
                return vm.IsGroupCollapsed(groupName) ? false : true;
            return true;
        }

        public IList<object?>? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}
#endif