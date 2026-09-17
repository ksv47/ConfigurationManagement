#if LINUX
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Reflection;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Styling;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Configuration_Management.Controls;
using Configuration_Management.Localization;
using Configuration_Management.Models;
using Configuration_Management.Services;
using Configuration_Management.Themes;
using Configuration_Management.ViewModels;

namespace Configuration_Management
{
    /// <summary>
    /// Диалог настроек приложения (Avalonia/Linux), часть «Язык интерфейса».
    /// Отдельных членов верхнего уровня здесь нет: выбор языка строится внутри
    /// BuildRoot на вкладке «Настройки», а пересборка при смене языка выполняется
    /// в Core (OnLanguageChanged). Файл создан зеркально WPF-partial
    /// SettingsWindow.Language.cs, чтобы структура вкладок совпадала.
    /// </summary>
    public partial class SettingsWindow
    {
    }
}
#endif