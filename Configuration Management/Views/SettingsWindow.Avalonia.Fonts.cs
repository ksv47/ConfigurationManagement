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
    /// Диалог настроек приложения (Avalonia/Linux), часть «Шрифт»: установленные
    /// в системе семейства и модели выбора области и начертания.
    /// </summary>
    public partial class SettingsWindow
    {
        /// <summary>
        /// Установленные в системе семейства, кроме уже перечисленных.
        /// Коллекция заполняется синхронно при первом обращении и после
        /// настройки платформы читается законно; до неё обращение незаконно,
        /// поэтому отказ гасится и список остаётся авторским.
        /// </summary>
        private static IEnumerable<string> InstalledFontFamilies(IReadOnlyCollection<string> already)
        {
            try
            {
                return Avalonia.Media.FontManager.Current.SystemFonts
                    .Select(f => f.Name)
                    .Where(n => !string.IsNullOrWhiteSpace(n)
                                && !already.Contains(n, StringComparer.OrdinalIgnoreCase))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(n => n, StringComparer.CurrentCultureIgnoreCase)
                    .ToArray();
            }
            catch
            {
                return Array.Empty<string>();
            }
        }

        /// <summary>Область интерфейса в списке подвкладки «Шрифт».</summary>
        private sealed class FontScopeItem
        {
            public FontScopeItem(string key) => Key = key;
            public string Key { get; }
            public override string ToString() => ThemeManager.FontScopeDisplayName(Key);
        }

        /// <summary>Начертание шрифта: пара «насыщенность и наклон» с локализованным именем.</summary>
        private sealed class FontFaceItem
        {
            public FontFaceItem(string key, string weight, string style)
            {
                Key = key;
                Weight = weight;
                Style = style;
            }

            public string Key { get; }
            public string Weight { get; }
            public string Style { get; }
            public override string ToString() => LocalizationManager.T(Key);
        }

        private static readonly FontFaceItem[] FontFaces =
        {
            new("Settings.Font.StyleNormal", "Normal", "Normal"),
            new("Settings.Font.StyleBold", "Bold", "Normal"),
            new("Settings.Font.StyleItalic", "Normal", "Italic"),
            new("Settings.Font.StyleBoldItalic", "Bold", "Italic")
        };
    }
}
#endif