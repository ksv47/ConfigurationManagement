#if LINUX
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Configuration_Management.Localization;
using Configuration_Management.Models;

namespace Configuration_Management.Services
{
    /// <summary>
    /// Обслуживание файловой базы на Linux: состояние/существование, физическое
    /// удаление, маркер блокировки (Linux-часть).
    /// </summary>
    public static partial class InfobaseMaintenanceService
    {
        /// <summary>Что известно о файле базы на диске.</summary>
        public enum FileBaseState
        {
            /// <summary>База на месте.</summary>
            Exists,

            /// <summary>Ни каталога, ни файла базы нет.</summary>
            Missing,

            /// <summary>Проверить не удалось: нет прав, ресурс недоступен.</summary>
            Unknown
        }

        /// <summary>
        /// Состояние файловой базы. Отдельный ответ для «проверить не удалось»
        /// нужен потому, что Directory.Exists возвращает ложь и при отказе
        /// в доступе, и при отвалившемся сетевом ресурсе: без этого базы
        /// с недоступного диска попадали бы в список на удаление.
        /// </summary>
        public static FileBaseState GetFileBaseState(Infobase ib)
        {
            if (ib.Connection.Type != ConnectionType.File)
                return FileBaseState.Exists;

            var path = ib.Connection.FilePath?.Trim() ?? string.Empty;
            if (string.IsNullOrEmpty(path))
                return FileBaseState.Missing;

            try
            {
                if (File.Exists(path))
                    return FileBaseState.Exists;

                if (!Directory.Exists(path))
                {
                    // Directory.Exists отвечает ложью и когда каталога нет,
                    // и когда его не прочитать. Различает только запрос
                    // атрибутов: он бросает разные исключения.
                    try
                    {
                        File.GetAttributes(path);
                    }
                    catch (FileNotFoundException)
                    {
                        return FileBaseState.Missing;
                    }
                    catch (DirectoryNotFoundException)
                    {
                        return FileBaseState.Missing;
                    }
                    catch (Exception)
                    {
                        return FileBaseState.Unknown;
                    }

                    return FileBaseState.Missing;
                }

                // Имена файлов на Linux регистрозависимы, поэтому маска
                // сравнивается своими силами, а не через EnumerateFiles.
                var found = Directory
                    .EnumerateFiles(path, "*", SearchOption.TopDirectoryOnly)
                    .Any(f => string.Equals(Path.GetFileName(f), "1Cv8.1CD", StringComparison.OrdinalIgnoreCase));

                return found ? FileBaseState.Exists : FileBaseState.Missing;
            }
            catch (Exception)
            {
                // Нет прав на чтение каталога, ресурс исчез посреди проверки.
                return FileBaseState.Unknown;
            }
        }

        /// <summary>Существует ли файловая база. «Проверить не удалось» считается существованием.</summary>
        public static bool FileBaseExists(Infobase ib) => GetFileBaseState(ib) != FileBaseState.Missing;

        /// <summary>Физически удаляет каталог файловой базы. Возвращает null при успехе или текст ошибки.</summary>
        public static string? TryDeleteFileBasePhysically(Infobase ib)
        {
            if (ib.Connection.Type != ConnectionType.File)
                return LocalizationManager.T("Maint.PhysicalDeleteOnlyFile");

            var dir = GetFileBaseDirectory(ib);
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir))
                return LocalizationManager.T("Maint.FileBaseDirNotFound");

            // Защита от удаления слишком «корневых» путей.
            try
            {
                var full = Path.GetFullPath(dir).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                var roots = new List<string>();
                foreach (var p in new[] { Path.GetPathRoot(full), "/", home })
                {
                    if (string.IsNullOrEmpty(p)) continue;
                    var rr = Path.GetFullPath(p).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                    if (string.Equals(full, rr, StringComparison.OrdinalIgnoreCase))
                        return string.Format(LocalizationManager.T("Maint.CannotDeleteSystemRootFormat"), full);
                }
            }
            catch
            {
                // продолжаем с осторожностью
            }

            // Каталог по ссылке удалять нельзя: на Linux удалилась бы сама ссылка,
            // а файлы базы остались бы на месте, и пользователь считал бы,
            // что база удалена.
            var info = new DirectoryInfo(dir);
            if (info.LinkTarget is not null)
                return string.Format(LocalizationManager.T("Maint.CannotDeleteSymlinkFormat"), dir);

            // Признак файловой базы перепроверяется прямо перед удалением:
            // между проверкой в диалоге и этим шагом каталог мог смениться.
            if (!File.Exists(Path.Combine(dir, "1Cv8.1CD")))
                return LocalizationManager.T("Maint.FileBaseDirNotFound");

            try
            {
                Directory.Delete(dir, recursive: true);
                return null;
            }
            catch (Exception ex)
            {
                return string.Format(LocalizationManager.T("Maint.DeleteFailedFormat"), dir, ex.Message);
            }
        }

        /// <summary>Установить/снять блокировку файловой базы (маркер в каталоге).</summary>
        public static bool SetFileBaseBlocked(Infobase ib, bool blocked)
        {
            var dir = GetFileBaseDirectory(ib);
            if (dir is null) return false;
            var marker = Path.Combine(dir, BlockMarkerFileName);
            try
            {
                if (blocked)
                {
                    if (!File.Exists(marker))
                        File.WriteAllText(marker,
                            $"Blocked by Configuration Management at {DateTime.Now:yyyy-MM-dd HH:mm:ss}\n");
                }
                else if (File.Exists(marker))
                {
                    File.Delete(marker);
                }
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
#endif