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
            { using var tr = doc.Database.TransactionManager.StartTransaction(); using var library = new ExampleLibrary(path, doc.Database, tr); return library.Examples; }, (form, item, entry) => ShowRowPreview(doc, form, item, entry));
            AcWindows.ShowModalDialog(dialog);
        }
        catch (System.Exception ex) when (!ExceptionSafety.IsFatal(ex)) { doc.Editor.WriteMessage($"\nOverzicht kon niet worden geopend: {ex.Message}"); }
    }

    private static void ShowRowPreview(Autodesk.AutoCAD.ApplicationServices.Document doc,
        System.Windows.Forms.Form form, OverviewLegend item, LegendEntry entry)
    {
        var editor = doc.Editor;
        using var interaction = editor.StartUserInteraction(form.Handle);
        using var oldView = editor.GetCurrentView();
        using var tr = doc.Database.TransactionManager.StartTransaction();
        try
        {
            var settings = item.Settings.Clone();
            settings.IncludeRemarks = settings.IncludeScaleBar = settings.IncludeQuantities = false;
            settings.Title = ""; settings.Columns = 1; settings.MaxLegendHeightMm = 0;
            var registry = LegendStore.Load(doc.Database, tr);
            var excluded = LegendManagement.CollectManagedIds(doc.Database, tr, registry);
            var analysis = DrawingAnalyzer.Analyze(doc.Database, tr, settings, excludedIds: excluded);
            var current = analysis.Entries.FirstOrDefault(e => LegendOrder.Key(e) == LegendOrder.Key(entry)) ?? entry;
            var block = LegendBuilder.BuildBlock(doc.Database, tr, analysis.WithEntries(new[] { current }), settings, out _, out var issues);
            if (issues.Count > 0) throw new InvalidOperationException(issues[0].Reason);
            var space = (BlockTableRecord)tr.GetObject(doc.Database.CurrentSpaceId, OpenMode.ForWrite);
            using var reference = new BlockReference(ComputeInsertPoint(doc.Database, settings), block);
            space.AppendEntity(reference); tr.AddNewlyCreatedDBObject(reference, true);
            var bounds = reference.GeometricExtents;
            using var view = editor.GetCurrentView();
            view.ViewDirection = Autodesk.AutoCAD.Geometry.Vector3d.ZAxis;
            view.Target = Autodesk.AutoCAD.Geometry.Point3d.Origin; view.ViewTwist = 0;
            view.CenterPoint = new Autodesk.AutoCAD.Geometry.Point2d((bounds.MinPoint.X + bounds.MaxPoint.X) / 2,
                (bounds.MinPoint.Y + bounds.MaxPoint.Y) / 2);
            view.Width = Math.Max(.1, bounds.MaxPoint.X - bounds.MinPoint.X) * 1.2;
            view.Height = Math.Max(.1, bounds.MaxPoint.Y - bounds.MinPoint.Y) * 1.5;
            editor.SetCurrentView(view); editor.Regen(); AcApp.UpdateScreen();
            editor.GetString("\nVoorbeeld bekijken; druk Enter om terug te gaan: ");
        }
        finally
        {
            tr.Abort(); editor.SetCurrentView(oldView); editor.Regen();
        }
    }

    private static IReadOnlyList<LegendEntry> AnalyzeOverview(Database db, OverviewLegend item)
    {
        using var tr = db.TransactionManager.StartTransaction();
        var registry = LegendStore.Load(db, tr);
        var excluded = LegendManagement.CollectManagedIds(db, tr, registry);
        var definition = LegendManagement.ActiveRegistry(db, tr, registry).FindById(item.Id);
        if (item.Id.Length > 0 && definition is null) throw new InvalidOperationException("Deze legenda is niet meer aanwezig.");
        var selected = definition?.Scope == LegendScope.Selection
            ? LegendManagement.ResolveHandles(db, definition.SourceHandles, out _).Where(id => !excluded.Contains(id)).ToArray() : null;
        var result = DrawingAnalyzer.Analyze(db, tr, item.Settings, selected, DescriptionCatalog.Default(), excluded);
        if (definition is not null)
        {
            definition.Settings = item.Settings;
            if (definition.Rows.Count == 0)
            {
                LegendRowSamples.Seed(db, tr, definition, result);
                LegendStore.Save(db, tr, registry);
                tr.Commit();
            }
            definition.Settings = item.Settings; result = LegendRowSamples.Retain(result, definition);
        }
        return result.Entries;
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
                target.Settings.RowChoices = item.Settings.RowChoices.Select(c => c.Clone()).ToList();
                target.Settings.SourceRows = item.Settings.SourceRows.Select(r => r.Clone()).ToList();
                target.Settings.FinalSourceRows = item.Settings.FinalSourceRows.Select(r => r.Clone()).ToList();
                target.Settings.ExcludedEntries = new(item.Settings.ExcludedEntries, StringComparer.OrdinalIgnoreCase);
                target.Settings.Klic = item.Settings.Klic.Clone();
                target.Settings.ManualMergeGroups = item.Settings.ManualMergeGroups.Select(g => g.Clone()).ToList();
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
        catch (System.Exception ex) when (!ExceptionSafety.IsFatal(ex)) { return $"De wijzigingen zijn niet opgeslagen: {ex.Message}"; }
        PurgePending(db);
        return null;
    }
}
