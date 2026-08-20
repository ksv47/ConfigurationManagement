#if LINUX
using Avalonia.Controls;

namespace Configuration_Management.Controls
{
    /// <summary>
    /// Avalonia-версия контейнера списка баз (Linux). Фактическое содержимое таблицы
    /// размещается в MainWindow; контейнер готов к переносу строк сюда без смены
    /// DataContext (наследуется).
    /// </summary>
    public class InfobaseListView : UserControl
    {
        public InfobaseListView()
        {
            Content = new ContentControl { HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch };
        }
    }
}
#endif