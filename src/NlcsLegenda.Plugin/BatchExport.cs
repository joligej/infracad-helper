using System.IO;
using Autodesk.AutoCAD.DatabaseServices;
using NlcsLegenda.Core;

namespace NlcsLegenda.Plugin;

public sealed class BatchDrawingResult
{
    public required string Drawing { get; init; }

    public required string Path { get; init; }

    public IReadOnlyList<LegendEntry> Entries { get; init; } = Array.Empty<LegendEntry>();

    public string? Error { get; init; }
}

// Side-database analyseert DWG's zonder ze in de editor te openen of op te slaan.

internal static class BatchExport
{
    public static List<BatchDrawingResult> AnalyzeFolder(
        string folder, LegendSettings settings, DescriptionCatalog catalog)
    {
        var results = new List<BatchDrawingResult>();
        var files = Directory.EnumerateFiles(folder, "*.dwg", SearchOption.TopDirectoryOnly)
            .OrderBy(f => f, StringComparer.CurrentCultureIgnoreCase);

        foreach (var file in files)
            results.Add(AnalyzeFile(file, settings, catalog));

        return results;
    }

    public static BatchDrawingResult AnalyzeFile(
        string path, LegendSettings settings, DescriptionCatalog catalog)
    {
        var name = Path.GetFileName(path);
        try
        {
            using var db = new Database(buildDefaultDrawing: false, noDocument: true);
            db.ReadDwgFile(path, FileShare.Read, allowCPConversion: true, password: null);
            using var tr = db.TransactionManager.StartTransaction();
            // Beheerde legenda's in de bron-DWG tellen niet mee als brondata.
            var registry = LegendStore.Load(db, tr);
            var excluded = LegendManagement.CollectManagedIds(db, tr, registry);
            var analysis = DrawingAnalyzer.Analyze(db, tr, settings, catalog: catalog, excludedIds: excluded);
            var entries = analysis.Entries;
            tr.Commit();
            return new BatchDrawingResult { Drawing = name, Path = path, Entries = entries };
        }
        catch (Exception ex)
        {
            return new BatchDrawingResult { Drawing = name, Path = path, Error = ex.Message };
        }
    }
}
