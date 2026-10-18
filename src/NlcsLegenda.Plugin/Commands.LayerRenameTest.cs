using NlcsLegenda.Core;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Core.Application;

namespace NlcsLegenda.Plugin;

public partial class Commands
{
    [CommandMethod("NLCSLEGENDALAYERMERGETEST", CommandFlags.Modal)]
    public void NlcsLegendaLayerMergeTest()
    {
        var ed = AcApp.DocumentManager.MdiActiveDocument?.Editor;
        if (ed is null) return;
        int failures = 0;
        void Check(string name, Action<Database, Transaction> action)
        {
            var previous = HostApplicationServices.WorkingDatabase;
            using var db = new Database(true, true);
            HostApplicationServices.WorkingDatabase = db;
            try
            {
                if (!DiagnosticRun.Execute(() =>
                {
                    using var tr = db.TransactionManager.StartTransaction();
                    action(db, tr);
                    tr.Commit();
                    ed.WriteMessage($"\nLAYERMERGE {name} -> OK");
                }, message => ed.WriteMessage(message), "NlcsLegendaLayerMergeTest")) failures++;
            }
            finally { HostApplicationServices.WorkingDatabase = previous; }
        }
        Check("huidige bronlaag behoudt vergrendeling", (db, tr) =>
        {
            var source = ReviewLayer(db, tr, "N-WE-VH-MERGESOURCE-G");
            var target = ReviewLayer(db, tr, "N-WE-VH-MERGETARGET-G");
            db.Clayer = source;
            var model = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForWrite);
            using var line = new Line(Point3d.Origin, new Point3d(3, 0, 0)) { LayerId = source };
            model.AppendEntity(line); tr.AddNewlyCreatedDBObject(line, true);
            var layer = (LayerTableRecord)tr.GetObject(source, OpenMode.ForWrite);
            layer.IsLocked = true;
            var plan = LayerRename.Analyze(db, tr, layer.Name, "N-WE-VH-MERGETARGET-G");
            if (!LayerRename.Apply(db, tr, plan, true, out var error)) throw new InvalidOperationException(error);
            if (!layer.IsLocked || layer.IsErased || db.Clayer != source || line.LayerId != target)
                throw new InvalidOperationException("Laagvergrendeling, huidige laag of doelobject is veranderd.");
        });
        Check("attribuutreferentie telt mee en verhuist mee", (db, tr) =>
        {
            var source = ReviewLayer(db, tr, "N-WE-VH-ATTRSOURCE-G");
            var target = ReviewLayer(db, tr, "N-WE-VH-ATTRTARGET-G");
            var blocks = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForWrite);
            using var block = new BlockTableRecord { Name = "ATTRTEST" };
            var blockId = blocks.Add(block); tr.AddNewlyCreatedDBObject(block, true);
            var model = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForWrite);
            using var reference = new BlockReference(Point3d.Origin, blockId);
            model.AppendEntity(reference); tr.AddNewlyCreatedDBObject(reference, true);
            using var attribute = new AttributeReference { Tag = "OMSCHRIJVING", TextString = "Behoud mij", Height = 1, LayerId = source };
            reference.AttributeCollection.AppendAttribute(attribute); tr.AddNewlyCreatedDBObject(attribute, true);
            var plan = LayerRename.Analyze(db, tr, "N-WE-VH-ATTRSOURCE-G", "N-WE-VH-ATTRTARGET-G");
            if (plan.AffectedEntities != 1) throw new InvalidOperationException("Attribuut ontbreekt in aantal geraakte objecten.");
            if (!LayerRename.Apply(db, tr, plan, true, out var error)) throw new InvalidOperationException(error);
            if (attribute.LayerId != target || attribute.TextString != "Behoud mij")
                throw new InvalidOperationException("Attribuut niet correct behouden op doellaag.");
        });
        Check("alleen hoofdletters is hernoemen", (db, tr) =>
        {
            var source = ReviewLayer(db, tr, "N-WE-VH-MixedCase-G");
            var plan = LayerRename.Analyze(db, tr, "N-WE-VH-MixedCase-G", "N-WE-VH-MIXEDCASE-G");
            if (plan.TargetExists) throw new InvalidOperationException("Bronlaag wordt ten onrechte als andere doellaag gezien.");
            if (!LayerRename.Apply(db, tr, plan, false, out var error)) throw new InvalidOperationException(error);
            if (((LayerTableRecord)tr.GetObject(source, OpenMode.ForRead)).Name != "N-WE-VH-MIXEDCASE-G")
                throw new InvalidOperationException("Nieuwe schrijfwijze niet opgeslagen.");
        });
        ed.WriteMessage($"\nLAYERMERGE totaal: {(failures == 0 ? "OK" : "FAIL")} ({failures} fouten)");
    }
}
