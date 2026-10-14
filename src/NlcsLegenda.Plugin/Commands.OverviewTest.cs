using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using NlcsLegenda.Core;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Core.Application;

namespace NlcsLegenda.Plugin;

public partial class Commands
{
    [CommandMethod("NLCSLEGENDAOVERVIEWTEST", CommandFlags.Modal)]
    public void NlcsLegendaOverviewTest()
    {
        var doc = AcApp.DocumentManager.MdiActiveDocument;
        if (doc is null) return;
        var db = doc.Database;
        try
        {
            string aId, bId, bBefore;
            List<LegendEntry> entries;
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var model = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForWrite);
                foreach (string name in new[] { "A", "B", "C" })
                {
                    using var line = new Line(Point3d.Origin, new Point3d(5, 0, 0));
                    line.LayerId = ReviewLayer(db, tr, $"N-WE-VH-OVERVIEW{name}-G");
                    model.AppendEntity(line); tr.AddNewlyCreatedDBObject(line, true);
                }
                var registry = LegendStore.Load(db, tr);
                var settings = new LegendSettings { IncludeRemarks = false, IncludeScaleBar = false };
                foreach (string name in new[] { "A", "B", "C" })
                    settings.DescriptionOverrides.Elementen[$"VH|OVERVIEW{name}"] = new DescriptionEntry { Specifiek = name };
                var a = IsoDef(registry, settings); a.Name = "Volgorde A"; registry.Add(a); aId = a.Id;
                var b = IsoDef(registry, settings.Clone()); b.Name = "Volgorde B"; registry.Add(b); bId = b.Id;
                if (BuildManagedLegend(db, tr, registry, a, out _, out _) != UpdateResult.Updated
                    || BuildManagedLegend(db, tr, registry, b, out _, out _) != UpdateResult.Updated)
                    throw new InvalidOperationException("Voorbeeldlegenda's ontbreken.");
                entries = DrawingAnalyzer.Analyze(db, tr, a.Settings, catalog: DescriptionCatalog.Default(), excludedIds: LegendManagement.CollectManagedIds(db, tr, registry)).Entries.ToList();
                bBefore = b.Settings.ToJson();
                LegendStore.Save(db, tr, registry);
                tr.Commit();
            }
            var manual = new LegendSettings { SortMode = LegendSortMode.Handmatig };
            manual.ManualEntryOrder = LegendOrder.Remember(Array.Empty<string>(), entries.AsEnumerable().Reverse());
            var change = new OverviewLegend { Id = aId, Name = "A", Settings = manual, Entries = entries };
            var error = SaveOverviewOrder(db, new[] { change });
            if (error is not null) throw new InvalidOperationException(error);
            string before;
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var registry = LegendStore.Load(db, tr);
                if (registry.FindById(bId)!.Settings.ToJson() != bBefore)
                    throw new InvalidOperationException("Instellingen van B veranderd.");
                before = registry.ToJson();
                tr.Commit();
            }
            change.Settings.SortMode = LegendSortMode.Naam;
            var invalid = new OverviewLegend { Id = "ontbreekt", Name = "Ontbreekt", Settings = manual, Entries = entries };
            if (SaveOverviewOrder(db, new[] { change, invalid }) is null)
                throw new InvalidOperationException("Ongeldige tweede legenda werd niet geweigerd.");
            using (var tr = db.TransactionManager.StartTransaction())
            {
                if (LegendStore.Load(db, tr).ToJson() != before)
                    throw new InvalidOperationException("Mislukte wijziging liet metadata achter.");
                tr.Commit();
            }
            VerifyOverviewDrawing(db);
            doc.Editor.WriteMessage("\nOVERVIEW handmatige volgorde, echte geometrie, B-isolatie en rollback -> OK");
        }
        catch (System.Exception ex) when (ex is Autodesk.AutoCAD.Runtime.Exception or InvalidOperationException or ArgumentException)
        { doc.Editor.WriteMessage($"\nOVERVIEW FAIL: {ex}"); }
    }

    [CommandMethod("NLCSLEGENDAOVERVIEWVERIFY", CommandFlags.Modal)]
    public void NlcsLegendaOverviewVerify()
    {
        var doc = AcApp.DocumentManager.MdiActiveDocument;
        if (doc is null) return;
        try
        {
            VerifyOverviewDrawing(doc.Database);
            doc.Editor.WriteMessage("\nOVERVIEW na heropenen en bijwerken: C/B/A, B gelijk -> OK");
        }
        catch (System.Exception ex) when (ex is Autodesk.AutoCAD.Runtime.Exception or InvalidOperationException or ArgumentException)
        { doc.Editor.WriteMessage($"\nOVERVIEW VERIFY FAIL: {ex}"); }
    }

    private static void VerifyOverviewDrawing(Database db)
    {
        using var tr = db.TransactionManager.StartTransaction();
        var registry = LegendStore.Load(db, tr);
        var a = registry.Legends.Single(l => l.Name == "Volgorde A");
        var b = registry.Legends.Single(l => l.Name == "Volgorde B");
        if (a.Settings.SortMode != LegendSortMode.Handmatig || b.Settings.SortMode != LegendSortMode.Status)
            throw new InvalidOperationException("Sorteerkeuze niet bewaard.");
        if (BuildManagedLegend(db, tr, registry, a, out _, out _) != UpdateResult.Updated)
            throw new InvalidOperationException("Bijwerken mislukt.");
        var excluded = LegendManagement.CollectManagedIds(db, tr, registry);
        var analysis = DrawingAnalyzer.Analyze(db, tr, a.Settings, catalog: DescriptionCatalog.Default(), excludedIds: excluded);
        if (!analysis.Entries.Select(e => e.Description).SequenceEqual(new[] { "C", "B", "A" }))
            throw new InvalidOperationException("Analysevolgorde verschilt.");
        var groups = (DBDictionary)tr.GetObject(db.GroupDictionaryId, OpenMode.ForRead);
        var group = (Group)tr.GetObject(groups.GetAt(a.GroupName), OpenMode.ForRead);
        var texts = new List<MText>();
        void ReadText(ObjectId id)
        {
            if (id.IsNull || id.IsErased) return;
            var entity = tr.GetObject(id, OpenMode.ForRead);
            if (entity is MText text && new[] { "A", "B", "C" }.Contains(text.Text)) texts.Add(text);
            else if (entity is BlockReference block)
                foreach (ObjectId child in (BlockTableRecord)tr.GetObject(block.BlockTableRecord, OpenMode.ForRead)) ReadText(child);
        }
        foreach (var id in group.GetAllEntityIds()) ReadText(id);
        if (!texts.OrderByDescending(t => t.Location.Y).Select(t => t.Text).SequenceEqual(new[] { "C", "B", "A" }))
            throw new InvalidOperationException("Getekende tekstvolgorde verschilt: " + string.Join(" / ", texts.Select(t => t.Text + " @ " + t.Location.Y)));
        LegendStore.Save(db, tr, registry);
        tr.Commit();
    }
}
