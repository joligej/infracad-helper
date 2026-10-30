using System.IO;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using NlcsLegenda.Core;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Core.Application;
using AcWindows = Autodesk.AutoCAD.ApplicationServices.Application;

namespace NlcsLegenda.Plugin;

public partial class Commands
{
    [CommandMethod("NLCSLEGENDAVASTZETTEN", CommandFlags.Modal)]
    public void SetLegendComposition()
    {
        var doc = AcApp.DocumentManager.MdiActiveDocument;
        if (doc is null || !RequireInteractive(doc.Editor, "NLCSLEGENDAVASTZETTEN")) return;
        try
        {
            if (!ResolveMutationTarget(doc.Editor, doc.Database, out var target, pickVerb: "samenstelling vastzetten van")) return;
            using var tr = doc.Database.TransactionManager.StartTransaction();
            var registry = LegendStore.Load(doc.Database, tr);
            var definition = registry.FindById(target.LegendId) ?? throw new InvalidOperationException("Legenda ontbreekt.");
            var excluded = LegendManagement.CollectManagedIds(doc.Database, tr, registry);
            var selected = definition.Scope == LegendScope.Selection && definition.Settings.SourceViewportHandles.Count == 0
                ? LegendManagement.ResolveHandles(doc.Database, definition.SourceHandles, out _) : null;
            var analysis = DrawingAnalyzer.Analyze(doc.Database, tr, definition.Settings, selected, excludedIds: excluded);
            LegendRowSamples.Seed(doc.Database, tr, definition, analysis);
            analysis = ManagedLegendExamples.Prepare(doc.Database, tr, definition, analysis);
            var check = definition.Clone(); if (!check.CompositionLocked) LegendComposition.Lock(check);
            var proposals = LegendComposition.Apply(check, analysis.Entries).Proposals;
            using var dialog = new CompositionDialog(definition, proposals, entry => LegendComposition.ApprovalProblem(check, entry, analysis.Entries));
            if (AcWindows.ShowModalDialog(dialog) != System.Windows.Forms.DialogResult.OK) return;
            if (dialog.Locked && !definition.CompositionLocked) LegendComposition.Lock(definition);
            definition.CompositionLocked = dialog.Locked;
            foreach (var entry in dialog.Approved.Where(e => !definition.ApprovedRows.Any(r => r.OrderKey == LegendOrder.Key(e))))
                definition.ApprovedRows.Add(new SavedLegendRow { Description = entry.DisplayDescription, Status = entry.Status,
                        CustomStatusName = entry.CustomStatusName, OrderKey = LegendOrder.Key(entry), Sources = entry.Sources.ToList(),
                        ExclusionKeys = entry.ExclusionKeys.ToList(), Appearance = LegendUpdatePlan.Appearance(entry) });
            if (BuildManagedLegend(doc.Database, tr, registry, definition, out _, out var note) != UpdateResult.Updated)
                throw new InvalidOperationException("De legenda kon niet worden bijgewerkt.");
            LegendStore.Save(doc.Database, tr, registry); tr.Commit(); PurgePending(doc.Database); doc.Editor.Regen();
            doc.Editor.WriteMessage("\nSamenstelling opgeslagen. " + note);
        }
        catch (System.Exception ex) when (!ExceptionSafety.IsFatal(ex)) { doc.Editor.WriteMessage("\nSamenstelling niet gewijzigd: " + ex.Message); }
    }

    [CommandMethod("NLCSLEGENDAVERPLAATSEN", CommandFlags.Modal)]
    public void MoveManagedLegend()
    {
        var doc = AcApp.DocumentManager.MdiActiveDocument;
        if (doc is null || !RequireInteractive(doc.Editor, "NLCSLEGENDAVERPLAATSEN")) return;
        try
        {
            if (!ResolveMutationTarget(doc.Editor, doc.Database, out var target, pickVerb: "verplaatsen")) return;
            var point = doc.Editor.GetPoint("\nLinkerbovenpunt van de legenda in de huidige model- of papierruimte: ");
            if (point.Status != PromptStatus.OK) return;
            if (!doc.Database.TileMode && Convert.ToInt32(AcApp.GetSystemVariable("CVPORT")) != 1)
                throw new InvalidOperationException("Ga eerst naar papierruimte (PSPACE) of naar het modeltabblad.");
            using var tr = doc.Database.TransactionManager.StartTransaction();
            var registry = LegendStore.Load(doc.Database, tr);
            var definition = registry.FindById(target.LegendId) ?? throw new InvalidOperationException("Legenda ontbreekt.");
            MoveLegend(doc.Database, tr, definition, doc.Database.CurrentSpaceId, point.Value.TransformBy(doc.Editor.CurrentUserCoordinateSystem));
            LegendStore.Save(doc.Database, tr, registry); tr.Commit(); doc.Editor.Regen();
            doc.Editor.WriteMessage("\nLegenda verplaatst; papiermaten, inhoud en instellingen behouden.");
        }
        catch (System.Exception ex) when (!ExceptionSafety.IsFatal(ex)) { doc.Editor.WriteMessage("\nLegenda niet verplaatst: " + ex.Message); }
    }

    internal static void MoveLegend(Database db, Transaction tr, LegendDefinition definition, ObjectId destination, Point3d topLeft)
    {
        var target = (BlockTableRecord)tr.GetObject(destination, OpenMode.ForWrite);
        if (!target.IsLayout) throw new InvalidOperationException("Kies modelruimte of een papierlayout.");
        bool paper = !((Layout)tr.GetObject(target.LayoutId, OpenMode.ForRead)).ModelType;
        double oldScale = LegendViewport.IsPaperSpace(db, tr, definition) ? 1000 : definition.Settings.Scale;
        double newScale = paper ? 1000 : definition.Settings.Scale;
        if (!LegendManagement.TryGetGroupExtents(db, tr, definition.GroupName, out var bounds)) throw new InvalidOperationException("Legenda heeft geen geometrie.");
        var groups = (DBDictionary)tr.GetObject(db.GroupDictionaryId, OpenMode.ForRead);
        var group = (Group)tr.GetObject(groups.GetAt(definition.GroupName), OpenMode.ForWrite);
        var old = group.GetAllEntityIds().Where(id => !id.IsNull && id.IsValid && !id.IsErased).ToArray();
        if (old.Length == 0 || old.Any(id => ((Entity)tr.GetObject(id, OpenMode.ForRead)).OwnerId != ((Entity)tr.GetObject(old[0], OpenMode.ForRead)).OwnerId))
            throw new InvalidOperationException("De legenda ligt niet volledig in één ruimte.");
        ObjectId[] current = old;
        if (((Entity)tr.GetObject(old[0], OpenMode.ForRead)).OwnerId != destination)
        {
            using var ids = new ObjectIdCollection(old); using var mapping = new IdMapping();
            group.Remove(ids);
            db.DeepCloneObjects(ids, destination, mapping, false);
            current = old.Select(id => mapping[id].Value).ToArray();
            using var moved = new ObjectIdCollection(current); group.Append(moved);
            foreach (var id in old) tr.GetObject(id, OpenMode.ForWrite).Erase();
        }
        var from = new Point3d(bounds.MinPoint.X, bounds.MaxPoint.Y, bounds.MaxPoint.Z);
        var transform = Matrix3d.Displacement(topLeft - from) * Matrix3d.Scaling(newScale / oldScale, from);
        foreach (var id in current) ((Entity)tr.GetObject(id, OpenMode.ForWrite)).TransformBy(transform);
        SetMovedFooter(db, tr, current, definition.Settings, paper);
        definition.SpaceHandle = target.Handle.ToString();
        if (paper)
            foreach (var viewport in LegendViewport.Linked(db, tr, definition.Id).ToArray()) { viewport.UpgradeOpen(); viewport.Erase(); }
        else if (!LegendViewport.Sync(db, tr, definition)) throw new InvalidOperationException("Het gekoppelde legendavenster is te klein.");
    }

    private static void SetMovedFooter(Database db, Transaction tr, IEnumerable<ObjectId> ids, LegendSettings settings, bool paper)
    {
        if (!settings.IncludeFooter) return;
        string scale = settings.FormatScale(), schematic = "Voorbeelden schematisch";
        foreach (var id in ids)
        {
            var entity = (Entity)tr.GetObject(id, OpenMode.ForWrite);
            if (entity is BlockReference reference)
            {
                var original = (BlockTableRecord)tr.GetObject(reference.BlockTableRecord, OpenMode.ForRead);
                var table = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForWrite);
                using var copy = new BlockTableRecord { Name = "NLCS_LEGENDA_" + Guid.NewGuid().ToString("N"), Origin = original.Origin };
                table.Add(copy); tr.AddNewlyCreatedDBObject(copy, true);
                using var content = new ObjectIdCollection(original.Cast<ObjectId>().ToArray()); using var mapping = new IdMapping();
                db.DeepCloneObjects(content, copy.ObjectId, mapping, false); reference.BlockTableRecord = copy.ObjectId;
                SetMovedFooter(db, tr, copy.Cast<ObjectId>(), settings, paper);
            }
            else if (entity is MText text && LegendManagement.RowToken(text) is null)
            {
                string from = paper ? scale : schematic, to = paper ? schematic : scale;
                if (text.Contents.StartsWith(from, StringComparison.Ordinal)) text.Contents = to + text.Contents[from.Length..];
            }
        }
    }

    [CommandMethod("NLCSLEGENDAVERGELIJKEN", CommandFlags.Modal)]
    public void CompareSheetLegends()
    {
        var doc = AcApp.DocumentManager.MdiActiveDocument;
        if (doc is null || !RequireInteractive(doc.Editor, "NLCSLEGENDAVERGELIJKEN")) return;
        try
        {
            using var files = new System.Windows.Forms.OpenFileDialog { Title = "Andere tekenbladen vergelijken (alleen lezen)",
                Filter = "Tekeningen (*.dwg)|*.dwg", Multiselect = true, CheckFileExists = true };
            if (files.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;
            using var tr = doc.Database.TransactionManager.StartTransaction();
            var registry = LegendStore.Load(doc.Database, tr);
            var active = LegendManagement.ActiveRegistry(doc.Database, tr, registry);
            var rows = active.Legends.SelectMany(l => ManagedLegendExamples.CurrentRows(doc.Database, tr, l)
                .Select(r => new SheetLegendRow(Path.GetFileName(doc.Name), l.Id, r))).ToList();
            foreach (var file in files.FileNames.Distinct(StringComparer.OrdinalIgnoreCase)
                .Where(file => !Path.GetFullPath(file).Equals(Path.GetFullPath(doc.Name), StringComparison.OrdinalIgnoreCase)))
            {
                using var source = new Database(false, true);
                source.ReadDwgFile(file, FileOpenMode.OpenForReadAndAllShare, false, ""); source.CloseInput(true);
                using var read = source.TransactionManager.StartTransaction();
                var stored = LegendManagement.ActiveRegistry(source, read, LegendStore.Load(source, read));
                rows.AddRange(stored.Legends.SelectMany(l => ManagedLegendExamples.CurrentRows(source, read, l)
                    .Select(r => new SheetLegendRow(Path.GetFullPath(file), l.Id, r))));
            }
            if (active.Legends.Count == 0) throw new InvalidOperationException("De huidige tekening bevat geen beheerde legenda.");
            var differences = LegendSheetComparison.Compare(rows, active.Legends.SelectMany(l => l.ComparisonExceptions));
            using var dialog = new SheetComparisonDialog(differences);
            if (AcWindows.ShowModalDialog(dialog) != System.Windows.Forms.DialogResult.OK) return;
            var keys = differences.Select(d => d.Key).ToHashSet();
            foreach (var legend in active.Legends)
            {
                legend.ComparisonExceptions.RemoveAll(e => keys.Contains(e.Key));
                legend.ComparisonExceptions.AddRange(dialog.Exceptions);
            }
            LegendStore.Save(doc.Database, tr, registry); tr.Commit();
            doc.Editor.WriteMessage("\nVergelijking afgerond; uitzonderingen in deze tekening opgeslagen.");
        }
        catch (System.Exception ex) when (!ExceptionSafety.IsFatal(ex)) { doc.Editor.WriteMessage("\nBladvergelijking afgebroken: " + ex.Message); }
    }
}
