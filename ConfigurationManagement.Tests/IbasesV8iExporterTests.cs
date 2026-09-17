using System.Text;
using Configuration_Management.Models;
using Configuration_Management.Services;
using Xunit;

namespace ConfigurationManagement.Tests;

public sealed class IbasesV8iExporterTests
{
    [Fact]
    public void ExportAndImport_UseSectionReferencesForNestedGroups()
    {
        var filePath = Path.Combine(Path.GetTempPath(), $"ibases-{Guid.NewGuid():N}.v8i");
        try
        {
            // Реальный сценарий issue #165: корректная дочерняя секция соседствует с
            // ошибочной секцией, имя которой содержит полный путь как буквальный текст.
            File.WriteAllText(filePath, """
                [Child]
                ID=old-child-id
                Folder=/Parent

                [Parent\Child]
                ID=starter-duplicate-id
                Folder=/

                [Parent]
                ID=parent-id
                Folder=/

                [Database]
                ID=database-id
                Folder=Parent\Child
                Connect=File="C:\database";
                """, Encoding.Default);

            var groups = new List<Group>
            {
                new() { Id = "parent-id", Name = "Parent" },
                new() { Id = "child-id", Name = "Child", ParentId = "parent-id" }
            };
            var infobases = new List<Infobase>
            {
                new()
                {
                    Id = "database-id",
                    Name = "Database",
                    Group = "Parent / Child",
                    Connection = new ConnectionSettings
                    {
                        Type = ConnectionType.File,
                        FilePath = @"C:\database"
                    }
                }
            };

            IbasesV8iExporter.Export(filePath, infobases, groups);
            var firstExport = File.ReadAllText(filePath, Encoding.Default);

            var headers = firstExport
                .Split('\n')
                .Select(line => line.Trim())
                .Where(line => line.StartsWith('[') && line.EndsWith(']'))
                .ToList();

            Assert.Equal(1, headers.Count(header => header == "[Child]"));
            Assert.DoesNotContain("[Parent\\Child]", headers);
            Assert.Contains("[Parent]", headers);

            var nestedSection = GetSection(firstExport, "Child");
            Assert.Contains("ID=child-id", nestedSection);
            Assert.Contains("Folder=/Parent", nestedSection);
            Assert.DoesNotContain("Connect=", nestedSection);

            var rootSection = GetSection(firstExport, "Parent");
            Assert.Contains("Folder=/", rootSection);

            var databaseSection = GetSection(firstExport, "Database");
            Assert.Contains("Folder=/Parent/Child", databaseSection);

            // Повторный экспорт не должен менять файл или возвращать альтернативную форму.
            IbasesV8iExporter.Export(filePath, infobases, groups);
            Assert.Equal(firstExport, File.ReadAllText(filePath, Encoding.Default));

            // Обратный импорт обязан восстановить внутренний полный путь и ParentId.
            var importedBases = new List<Infobase>();
            var importedGroups = new List<Group>();
            IbasesV8iImporter.Import(filePath, importedBases, importedGroups);

            var importedParent = Assert.Single(importedGroups, g => g.Name == "Parent");
            var importedChild = Assert.Single(importedGroups, g => g.Name == "Child");
            Assert.Equal(importedParent.Id, importedChild.ParentId);
            Assert.Equal("Parent / Child", Assert.Single(importedBases).Group);
        }
        finally
        {
            File.Delete(filePath);
        }
    }

    private static string GetSection(string content, string name)
    {
        var marker = $"[{name}]";
        var start = content.IndexOf(marker, StringComparison.Ordinal);
        Assert.True(start >= 0, $"Section {marker} was not found.");

        var next = content.IndexOf("\n[", start + marker.Length, StringComparison.Ordinal);
        return next >= 0 ? content[start..next] : content[start..];
    }
}
