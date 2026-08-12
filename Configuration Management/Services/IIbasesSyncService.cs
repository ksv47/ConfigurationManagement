using Configuration_Management.Models;

namespace Configuration_Management.Services;

public interface IIbasesSyncService
{
    IbasesImportResult Import(string filePath, IList<Infobase> infobases, IList<Group> groups);
    IbasesExportResult Export(string filePath, IEnumerable<Infobase> infobases, IEnumerable<Group> groups);
}

public sealed class IbasesSyncService : IIbasesSyncService
{
    public IbasesImportResult Import(string filePath, IList<Infobase> infobases, IList<Group> groups) =>
        IbasesV8iImporter.Import(filePath, infobases, groups);

    public IbasesExportResult Export(string filePath, IEnumerable<Infobase> infobases, IEnumerable<Group> groups) =>
        IbasesV8iExporter.Export(filePath, infobases, groups);
}
