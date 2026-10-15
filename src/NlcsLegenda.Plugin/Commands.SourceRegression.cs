using System.IO;
using Autodesk.AutoCAD.Colors;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using NlcsLegenda.Core;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Core.Application;

namespace NlcsLegenda.Plugin;

public partial class Commands
{
    [CommandMethod("NLCSLEGENDASOURCETEST", CommandFlags.Modal)]
    public void SourceRegressionTest()
    {
        var doc = AcApp.DocumentManager.MdiActiveDocument;
        try
        {
            var file = Path.Join(Path.GetDirectoryName(doc.Database.Filename)!, "shared-symbols.dwg");
            using (var source = new Database(true, true))
            {
                using (var tr = source.TransactionManager.StartTransaction())
                {
                    var model = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(source), OpenMode.ForWrite);
                    var detail = ReviewLayer(source, tr, "DETAIL");
                    var table = (BlockTable)tr.GetObject(source.BlockTableId, OpenMode.ForWrite);
                    var inner = new BlockTableRecord { Name = "INNER" };
                    var innerId = table.Add(inner); tr.AddNewlyCreatedDBObject(inner, true);
                    var circle = new Circle(Point3d.Origin, Vector3d.ZAxis, 1) { LayerId = detail };
                    inner.AppendEntity(circle); tr.AddNewlyCreatedDBObject(circle, true);
                    var outer = new BlockTableRecord { Name = "OUTER" };
                    var outerId = table.Add(outer); tr.AddNewlyCreatedDBObject(outer, true);
                    var nested = new BlockReference(Point3d.Origin, innerId);
                    outer.AppendEntity(nested); tr.AddNewlyCreatedDBObject(nested, true);
                    foreach (var (name, id) in new[] { ("SYMBOLA", innerId), ("SYMBOLB", outerId) })
                    {
                        var layer = ReviewLayer(source, tr, "N-WE-IE-" + name + "-S");
                        var reference = new BlockReference(Point3d.Origin, id) { LayerId = layer };
                        model.AppendEntity(reference); tr.AddNewlyCreatedDBObject(reference, true);
                    }
                    foreach (var (name, description) in new[] {
                        ("N-WE-VH-KANTOPSLUITING_TROTTOIRBAND-G", "NLCS-basis: N-WE-VH-KANTOPSLUITING_TROTTOIRBAND-G"),
                        ("B-WE-KL-DATA-G", "KLIC") })
                    {
                        var layer = ReviewLayer(source, tr, name);
                        ((LayerTableRecord)tr.GetObject(layer, OpenMode.ForWrite)).Description = description;
                        var line = new Line(Point3d.Origin, new Point3d(5, 0, 0)) { LayerId = layer };
                        model.AppendEntity(line); tr.AddNewlyCreatedDBObject(line, true);
                    }
                    tr.Commit();
                }
                source.SaveAs(file, DwgVersion.Current);
            }
            AddKlicXref(doc.Database, file, "A", 1);
            AddKlicXref(doc.Database, file, "B", 1);
            using (var tr = doc.Database.TransactionManager.StartTransaction())
            {
                var layers = (LayerTable)tr.GetObject(doc.Database.LayerTableId, OpenMode.ForRead);
                foreach (var (name, color) in new[] { ("A", (short)1), ("B", (short)3) })
                    ((LayerTableRecord)tr.GetObject(layers[name + "|DETAIL"], OpenMode.ForWrite)).Color = Autodesk.AutoCAD.Colors.Color.FromColorIndex(ColorMethod.ByAci, color);
                tr.Commit();
            }
            CheckSourceRegression(doc.Database);
            doc.Editor.WriteMessage("\nSOURCETEST gedeelde symbolen, xref-overrides en gewone omschrijvingen -> OK");
        }
        catch (System.Exception ex) { doc.Editor.WriteMessage("\nSOURCETEST FAIL: " + ex); }
    }

    [CommandMethod("NLCSLEGENDASOURCEVERIFY", CommandFlags.Modal)]
    public void SourceRegressionVerify()
    {
        var doc = AcApp.DocumentManager.MdiActiveDocument;
        try { CheckSourceRegression(doc.Database); doc.Editor.WriteMessage("\nSOURCETEST na heropenen -> OK"); }
        catch (System.Exception ex) { doc.Editor.WriteMessage("\nSOURCETEST FAIL: " + ex); }
    }

    private static void CheckSourceRegression(Database db)
    {
        using var tr = db.TransactionManager.StartTransaction();
        var settings = new LegendSettings { IncludeXrefLayers = true, IncludeGeneralDescription = false };
        for (int pass = 0; pass < 2; pass++)
        {
            var analysis = DrawingAnalyzer.Analyze(db, tr, settings);
            if (analysis.Entries.Count != 6 || !analysis.Entries.Any(e => e.Description == "Trottoirband")
                || !analysis.Entries.Any(e => e.Description == "Datakabel"))
                throw new InvalidOperationException("Bronvarianten of gewone omschrijvingen ontbreken.");
            var block = LegendBuilder.BuildBlock(db, tr, analysis, settings, out var rows, out var issues);
            var entities = ((BlockTableRecord)tr.GetObject(block, OpenMode.ForRead)).Cast<ObjectId>()
                .Select(id => tr.GetObject(id, OpenMode.ForRead)).OfType<Entity>().ToArray();
            var colors = entities.OfType<Circle>().Select(e => e.Color.ColorIndex).Order().ToArray();
            if (rows != 6 || issues.Count != 0 || !colors.SequenceEqual(new short[] { 1, 1, 3, 3 }))
                throw new InvalidOperationException("Gedeelde symboolonderdelen hebben verkeerde kleuren: " + string.Join(",", colors));
            var text = entities.OfType<MText>().Select(e => e.Text).ToArray();
            if (text.Any(t => t.Contains("NLCS-basis")) || !text.Contains("Trottoirband") || !text.Contains("Datakabel"))
                throw new InvalidOperationException("De getekende tekst bevat technische bronlabels.");
        }
        tr.Abort();
    }
}
