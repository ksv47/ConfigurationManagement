#if LINUX
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using Avalonia.Data.Converters;
using Configuration_Management.Models;

namespace Configuration_Management.Converters
{
    /// <summary>
    /// Avalonia-версия: полный путь группы в иерархии (например, «Учёт / Бухгалтерия»).
    /// </summary>
    public class GroupFullPathConverter : IMultiValueConverter
    {
        public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
        {
            var group = values is { Count: > 0 } ? values[0] as Group : null;
            var groups = values is { Count: > 1 } ? values[1] as ObservableCollection<Group> : null;

            if (group is null)
                return string.Empty;

            return GroupHierarchyHelper.GetFullPath(group, groups);
        }

        public IList<object?>? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}
#endif