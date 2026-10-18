using Autodesk.AutoCAD.DatabaseServices;
using NlcsLegenda.Core;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Core.Application;
using AcWindows = Autodesk.AutoCAD.ApplicationServices.Application;

namespace NlcsLegenda.Plugin;

public partial class Commands
{
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private void ShowLegendOverview()
    {
        var doc = AcApp.DocumentManager.MdiActiveDocument;
        if (doc is null) return;
        try
        {
            var items = new List<OverviewLegend>();
            using (var tr = doc.Database.TransactionManager.StartTransaction())
            {
                var registry = LegendManagement.ActiveRegistry(doc.Database, tr, LegendStore.Load(doc.Database, tr));
                foreach (var legend in registry.Legends)
                    items.Add(new OverviewLegend { Id = legend.Id, Name = legend.Name, Settings = legend.Settings.Clone(),
                        Entries = Array.Empty<LegendEntry>(), NeedsAnalysis = true });
                if (items.Count == 0)
                {
                    var settings = LoadGlobalDefaults();
                    items.Add(new OverviewLegend { Id = "", Name = "Hele tekening (voorbeeld)", Settings = settings,
                        Entries = Array.Empty<LegendEntry>(), NeedsAnalysis = true });
                }
                tr.Commit();
            }
            using var dialog = new OverviewDialog(items, changed =>
            {
                var error = SaveOverviewOrder(doc.Database, changed);
                if (error is null) { doc.Editor.Regen(); AcApp.UpdateScreen(); }
                return error;
            }, item => AnalyzeOverview(doc.Database, item), path =>
            { using var tr = doc.Database.TransactionManager.StartTransaction(); using var library = new ExampleLibrary(path, doc.Database, tr); return library.Examples; });
            AcWindows.ShowModalDialog(dialog);
        }
        catch (System.Exception ex) when (!ExceptionSafety.IsFatal(ex)) { doc.Editor.WriteMessage($"\nOverzicht kon niet worden geopend: {ex.Message}"); }
    }

    private static IReadOnlyList<LegendEntry> AnalyzeOverview(Database db, OverviewLegend item)
    {
        using var tr = db.TransactionManager.StartTransaction();
        var registry = LegendManagement.ActiveRegistry(db, tr, LegendStore.Load(db, tr));
        var excluded = LegendManagement.CollectManagedIds(db, tr, registry);
        var definition = registry.FindById(item.Id);
        if (item.Id.Length > 0 && definition is null) throw new InvalidOperationException("Deze legenda is niet meer aanwezig.");
        var selected = definition?.Scope == LegendScope.Selection
            ? LegendManagement.ResolveHandles(db, definition.SourceHandles, out _).Where(id => !excluded.Contains(id)).ToArray() : null;
        return selected is { Length: 0 } ? Array.Empty<LegendEntry>()
            : DrawingAnalyzer.Analyze(db, tr, item.Settings, selected, DescriptionCatalog.Default(), excluded).Entries;
    }

    private static string? SaveOverviewOrder(Database db, IReadOnlyList<OverviewLegend> changed)
    {
        try
        {
            using var tr = db.TransactionManager.StartTransaction();
            var registry = LegendStore.Load(db, tr);
            var active = LegendManagement.ActiveRegistry(db, tr, registry);
            foreach (var item in changed)
            {
                if (active.FindById(item.Id) is null) return "Een legenda is niet meer aanwezig. Open Overzicht opnieuw.";
                var target = registry.FindById(item.Id)!;
                target.Settings.Klic = item.Settings.Klic.Clone();
                target.Settings.ExampleLibraryPath = item.Settings.ExampleLibraryPath;
                target.Settings.ExampleOverrides = new Dictionary<string, string>(item.Settings.ExampleOverrides, StringComparer.Ordinal);
                target.Settings.ManualEntries = item.Settings.ManualEntries.Select(e => e.Clone()).ToList();
                target.Settings.SortMode = item.Settings.SortMode;
                target.Settings.ManualEntryOrder = new List<string>(item.Settings.ManualEntryOrder);
                if (BuildManagedLegend(db, tr, registry, target, out _, out _) != UpdateResult.Updated)
                    return $"'{item.Name}' heeft geen bruikbare bronregels. Er is niets gewijzigd.";
            }
            LegendStore.Save(db, tr, registry);
            tr.Commit();
        }
        catch (System.Exception ex) when (!ExceptionSafety.IsFatal(ex)) { return $"De volgorde is niet opgeslagen: {ex.Message}"; }
        PurgePending(db);
        return null;
    }
}
