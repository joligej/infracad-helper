using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using NlcsLegenda.Core;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Core.Application;

namespace NlcsLegenda.Plugin;

public partial class Commands
{
    [CommandMethod("NLCSLEGENDACOMPOSITIONTEST", CommandFlags.Modal)]
    public void NlcsLegendaCompositionTest()
    {
        var doc = AcApp.DocumentManager.MdiActiveDocument;
        if (doc is null) return;
        var db = doc.Database;
        int failures = 0;
        void Check(string name, bool passed)
        {
            if (!passed) failures++;
            doc.Editor.WriteMessage($"\nCOMPOSITION {name} -> {(passed ? "OK" : "FAIL")}");
        }
        if (!DiagnosticRun.Execute(() =>
        {
            ObjectId first;
            string legendId, before;
            var settings = new LegendSettings();
            settings.ManualEntries.Add(new ManualEntry
            {
                Layer = "N-WE-VH-COMPOSITIONMANUAL-G",
                Description = "Alleen in legenda",
                Type = NlcsDrawType.Geometrie
            });
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var model = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForWrite);
                using var a = new Line(Point3d.Origin, new Point3d(3, 0, 0));
                a.LayerId = ReviewLayer(db, tr, "N-WE-VH-COMPOSITIONA-G");
                first = model.AppendEntity(a); tr.AddNewlyCreatedDBObject(a, true);
                using var b = new Line(new Point3d(0, 5, 0), new Point3d(7, 5, 0));
                b.LayerId = ReviewLayer(db, tr, "N-WE-VH-COMPOSITIONB-G");
                model.AppendEntity(b); tr.AddNewlyCreatedDBObject(b, true);
                var registry = LegendStore.Load(db, tr);
                var definition = IsoDef(registry, settings);
                definition.Scope = LegendScope.Selection;
                definition.SourceHandles = LegendManagement.ToHandles(new[] { first });
                legendId = definition.Id;
                registry.Add(definition);
                if (BuildManagedLegend(db, tr, registry, definition, out _, out _) != UpdateResult.Updated)
                    throw new InvalidOperationException("Testlegenda kon niet worden gebouwd.");
                LegendStore.Save(db, tr, registry);
                before = registry.ToJson();
                tr.Commit();
            }
            PurgePending(db);
            var selected = BuildCompositionItems(db, settings, legendId);
            Check("alleen bron van gekozen legenda", selected is { Count: 1 } && selected[0].Key.Contains("COMPOSITIONA"));
            var global = BuildCompositionItems(db, settings);
            Check("globaal zonder eigen legendageometrie", global is { Count: 2 }
                && global.Any(i => i.Key.Contains("COMPOSITIONA")) && global.Any(i => i.Key.Contains("COMPOSITIONB")));
            using (var tr = db.TransactionManager.StartTransaction())
            {
                Check("alleen lezen bewaart metadata", LegendStore.Load(db, tr).ToJson() == before);
                ((Entity)tr.GetObject(first, OpenMode.ForWrite)).Erase();
                tr.Commit();
            }
            Check("verdwenen selectie blijft leeg", BuildCompositionItems(db, settings, legendId) is { Count: 0 });
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var model = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForWrite);
                foreach (var name in new[] { "B-WE-KL-DATA-G", "B-WE-KL-DATA2-G" })
                {
                    using var line = new Line(Point3d.Origin, new Point3d(5, 0, 0));
                    line.LayerId = ReviewLayer(db, tr, name);
                    model.AppendEntity(line); tr.AddNewlyCreatedDBObject(line, true);
                }
                tr.Commit();
            }
            settings.SamenvoegenNummer = true;
            var data = BuildCompositionItems(db, settings).Where(i => i.Group == "DATA").ToList();
            Check("samengevoegde KLIC houdt alle bronkeuzes", data.Count == 2 && data.Any(i => i.Key.EndsWith("DATA2")));
            var tree = CompositionTree.Build(data.Select(i => (i.Key, i.Label, i.Group)), settings.ExcludedEntries);
            tree.SetGroup(tree.Groups.Single(), false);
            Check("DATA in een klik uit", tree.ExcludedKeys().Count == 2);
        }, message => doc.Editor.WriteMessage(message), "NlcsLegendaCompositionTest")) failures++;
        doc.Editor.WriteMessage($"\nCOMPOSITION totaal: {(failures == 0 ? "OK" : "FAIL")} ({failures} fouten)");
    }
}
