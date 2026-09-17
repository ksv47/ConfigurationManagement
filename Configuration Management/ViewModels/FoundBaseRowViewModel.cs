using System;
using Configuration_Management.Models;

namespace Configuration_Management.ViewModels;

/// <summary>
/// Строка диалога «Поиск потерянных и забытых баз 1С 8» (issue #247): состояние
/// флажка выбора, признак наличия базы в списке приложения (<see cref="InApp"/>)
/// и в реестре 1С ibases.v8i (<see cref="InIbasesV8i"/>), а также отображаемые
/// значения имени каталога, пути, размера и даты последнего изменения.
/// </summary>
public class FoundBaseRowViewModel : ViewModelBase
{
    /// <summary>Найденная на диске база (файл 1Cv8.1CD).</summary>
    public FoundFileBase Base { get; }

    /// <summary>Имя каталога базы (последний сегмент пути).</summary>
    public string Name { get; }

    /// <summary>Путь к файлу базы (каталог, содержащий 1Cv8.1CD).</summary>
    public string Path { get; }

    /// <summary>Форматированный размер файла 1Cv8.1CD.</summary>
    public string SizeText { get; }

    /// <summary>Дата последнего изменения файла 1Cv8.1CD.</summary>
    public string LastWriteText { get; }

    private bool _isChecked;
    private bool _isProcessing;
    private bool _inApp;
    private bool _inIbasesV8i;

    /// <param name="found">Найденная база. Не может быть null.</param>
    public FoundBaseRowViewModel(FoundFileBase found)
    {
        Base = found;
        var dir = (found.DirectoryPath ?? string.Empty).TrimEnd('\\', '/');
        var leaf = System.IO.Path.GetFileName(dir);
        Name = string.IsNullOrWhiteSpace(leaf) ? dir : leaf;
        Path = found.DirectoryPath ?? string.Empty;
        SizeText = Infobase.FormatSize(found.SizeBytes);
        LastWriteText = found.LastWriteTime == default
            ? string.Empty
            : found.LastWriteTime.ToString("dd.MM.yyyy HH:mm");
    }

    /// <summary>Отмечена ли строка для добавления в список баз (флажок).</summary>
    public bool IsChecked
    {
        get => _isChecked;
        set => SetProperty(ref _isChecked, value);
    }

    /// <summary>Идёт ли сейчас обработка строки (флажок неактивен).</summary>
    public bool IsProcessing
    {
        get => _isProcessing;
        set => SetProperty(ref _isProcessing, value);
    }

    /// <summary>База уже присутствует в списке баз приложения.</summary>
    public bool InApp
    {
        get => _inApp;
        set
        {
            if (SetProperty(ref _inApp, value))
                OnPropertyChanged(nameof(NotInApp));
        }
    }

    /// <summary>База отсутствует в списке приложения (доступна для добавления).</summary>
    public bool NotInApp => !_inApp;

    /// <summary>База уже зарегистрирована в файле ibases.v8i (реестр 1С).</summary>
    public bool InIbasesV8i
    {
        get => _inIbasesV8i;
        set => SetProperty(ref _inIbasesV8i, value);
    }

    /// <summary>
    /// Нормализует путь каталога базы для сравнения: полный путь без завершающих
    /// разделителей. Используется и при построении набора существующих путей, и при
    /// проверке каждой найденной базы, поэтому совпадение признаков гарантировано.
    /// </summary>
    public static string NormalizeDir(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return string.Empty;
        try
        {
            return System.IO.Path.GetFullPath(path).TrimEnd('\\', '/');
        }
        catch
        {
            return path.TrimEnd('\\', '/');
        }
    }

    /// <summary>
    /// Компаратор путей каталогов: регистронезависимый на Windows (пути файловых
    /// баз в приложении и ibases.v8i могут отличаться регистром), точный на Linux.
    /// </summary>
    public static StringComparer DirComparer =>
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
}