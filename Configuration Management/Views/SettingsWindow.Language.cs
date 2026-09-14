#if WINDOWS
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.IO;
using MaterialDesignThemes.Wpf;
using Configuration_Management.Localization;
using Configuration_Management.Models;
using Configuration_Management.Services;
using Configuration_Management.Themes;
using Configuration_Management.ViewModels;
using Drawing = System.Drawing;
using Forms = System.Windows.Forms;
using Point = System.Windows.Point;

namespace Configuration_Management
{
    public partial class SettingsWindow
    {
        /// <summary>Заполняет список доступных языков интерфейса и выбирает текущий.</summary>
        private void InitializeLanguage()
        {
            if (LanguageComboBox == null)
                return;

            LanguageComboBox.ItemsSource = LocalizationManager.Instance.AvailableLanguages.ToList();
            LanguageComboBox.DisplayMemberPath = "Name";
            LanguageComboBox.SelectedItem = LocalizationManager.Instance.AvailableLanguages
                .FirstOrDefault(l => l.Code == LocalizationManager.Instance.CurrentLanguage);
        }

        private void OnLanguage_Changed(object sender, SelectionChangedEventArgs e)
        {
            // Не применяем язык сразу при выборе: он запоминается и применяется
            // только в обработчике «Сохранить», чтобы «Отмена» не меняла текущий
            // язык и не перезаписывала settings.json (issue #206).
            if (LanguageComboBox?.SelectedItem is LanguageInfo li)
                _pendingLanguageCode = li.Code;
        }
    }
}
#endif