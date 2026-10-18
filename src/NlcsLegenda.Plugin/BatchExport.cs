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
            db.CloseInput(true);
            db.ResolveXrefs(useThreadEngine: false, doNewOnly: false);
            using var tr = db.TransactionManager.StartTransaction();
            var registry = LegendStore.Load(db, tr);
            var excluded = LegendManagement.CollectManagedIds(db, tr, registry);
            var analysis = DrawingAnalyzer.Analyze(db, tr, settings, catalog: catalog, excludedIds: excluded);
            // blanco invulregels buiten de bronexport
            var entries = analysis.Entries.Where(e => !e.IsBlank).ToList();
            tr.Commit();
            return new BatchDrawingResult { Drawing = name, Path = path, Entries = entries };
        }
        catch (Exception ex) when (!ExceptionSafety.IsFatal(ex))
        {
            return new BatchDrawingResult { Drawing = name, Path = path, Error = ex.Message };
        }
    }
}
