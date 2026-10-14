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
                var excluded = LegendManagement.CollectManagedIds(doc.Database, tr, registry);
                foreach (var legend in registry.Legends)
                {
                    var selected = legend.Scope == LegendScope.Selection
                        ? LegendManagement.ResolveHandles(doc.Database, legend.SourceHandles, out _).Where(id => !excluded.Contains(id)).ToArray() : null;
                    var entries = selected is { Length: 0 } ? Array.Empty<LegendEntry>()
                        : DrawingAnalyzer.Analyze(doc.Database, tr, legend.Settings, selected, DescriptionCatalog.Default(), excluded).Entries;
                    items.Add(new OverviewLegend { Id = legend.Id, Name = legend.Name, Settings = legend.Settings.Clone(), Entries = entries });
                }
                if (items.Count == 0)
                {
                    var settings = LoadGlobalDefaults();
                    var analysis = DrawingAnalyzer.Analyze(doc.Database, tr, settings, catalog: LoadCatalog(doc.Database), excludedIds: excluded);
                    items.Add(new OverviewLegend { Id = "", Name = "Hele tekening (voorbeeld)", Settings = settings, Entries = analysis.Entries });
                }
                tr.Commit();
            }
            using var dialog = new OverviewDialog(items, changed => SaveOverviewOrder(doc.Database, changed));
            AcWindows.ShowModalDialog(dialog);
        }
        catch (System.Exception ex) { doc.Editor.WriteMessage($"\nOverzicht kon niet worden geopend: {ex.Message}"); }
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
                target.Settings.SortMode = item.Settings.SortMode;
                target.Settings.ManualEntryOrder = new List<string>(item.Settings.ManualEntryOrder);
                if (BuildManagedLegend(db, tr, registry, target, out _, out _) != UpdateResult.Updated)
                    return $"'{item.Name}' heeft geen bruikbare bronregels. Er is niets gewijzigd.";
            }
            LegendStore.Save(db, tr, registry);
            tr.Commit();
        }
        catch (System.Exception ex) { return $"De volgorde is niet opgeslagen: {ex.Message}"; }
        PurgePending(db);
        return null;
    }
}
