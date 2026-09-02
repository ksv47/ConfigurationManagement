#if LINUX
using System;
using System.IO;
using System.Reflection;
using Avalonia.Controls;
using Avalonia.Media.Imaging;

namespace Configuration_Management.Services
{
    /// <summary>
    /// Единая точка загрузки значка приложения (app.ico) для заголовка окна
    /// и системного трея (Avalonia/Linux). Декодирование .ico в Avalonia/Skia
    /// ненадёжно, поэтому в приоритете сам файл app.ico на диске, а затем —
    /// встроенные PNG-ресурсы, которые являются растровым рендером app.ico
    /// (app_icon_preview.png / tray_icon_preview.png).
    /// </summary>
    public static class AppIconLoader
    {
        private static Bitmap? TryLoadFromFile(string path)
        {
            try
            {
                if (File.Exists(path))
                    return new Bitmap(path);
            }
            catch
            {
                // Формат не декодируется (например ICO) — пробуем следующий источник.
            }
            return null;
        }

        private static Bitmap? TryLoadFromResource(string resourceName)
        {
            try
            {
                var asm = Assembly.GetExecutingAssembly();
                using var stream = asm.GetManifestResourceStream(resourceName);
                if (stream is not null)
                    return new Bitmap(stream);
            }
            catch
            {
                // Ресурс отсутствует или не декодируется — пробуем следующий.
            }
            return null;
        }

        /// <summary>Значок приложения для заголовка окна и трея, либо null, если источников нет.</summary>
        public static WindowIcon? LoadAppIcon()
            => LoadAppBitmap() is { } bitmap ? new WindowIcon(bitmap) : null;

        /// <summary>
        /// Тот же значок картинкой: шапка главного окна рисует его как обычный Image
        /// (MainWindow.xaml:191-197), а WindowIcon доступа к растру не даёт. Каждый
        /// вызов читает свой экземпляр, чтобы картинка и значок окна не делили объект.
        /// </summary>
        public static Bitmap? LoadAppBitmap()
        {
            // 1) Сам app.ico рядом с приложением (если лежит в выходном каталоге и декодируется).
            foreach (var dir in new[] { AppContext.BaseDirectory, Environment.CurrentDirectory })
            {
                if (TryLoadFromFile(System.IO.Path.Combine(dir, "app.ico")) is { } ico)
                    return ico;
            }

            // 2) Встроенные PNG-ресурсы — растровый рендер app.ico.
            if (TryLoadFromResource("app_icon_preview.png") is { } png)
                return png;
            if (TryLoadFromResource("tray_icon_preview.png") is { } tray)
                return tray;

            // 3) PNG-файлы на диске рядом с приложением.
            foreach (var dir in new[] { AppContext.BaseDirectory, Environment.CurrentDirectory })
            {
                if (TryLoadFromFile(System.IO.Path.Combine(dir, "app_icon_preview.png")) is { } p)
                    return p;
                if (TryLoadFromFile(System.IO.Path.Combine(dir, "tray_icon_preview.png")) is { } t)
                    return t;
            }

            return null;
        }
    }
}
#endif