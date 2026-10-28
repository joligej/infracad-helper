using System.IO;
using System.Security.Cryptography;
using Autodesk.AutoCAD.Colors;
using Color = Autodesk.AutoCAD.Colors.Color;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using NlcsLegenda.Core;
using App = Autodesk.AutoCAD.ApplicationServices.Core.Application;

namespace NlcsLegenda.Plugin;

public partial class Commands
{
    [CommandMethod("NLCSLEGENDALINETYPETEST", CommandFlags.Modal)]
    public void TestXrefLinetypes()
    {
        var doc = App.DocumentManager.MdiActiveDocument;
        DiagnosticRun.Execute(() =>
        {
            var path = Path.Join(Path.GetDirectoryName(doc.Database.Filename)!, "linetype-source.dwg");
            using (var source = new Database(true, true))
            {
                source.LoadLineTypeFile("BATTING", "acadiso.lin");
                using (var tr = source.TransactionManager.StartTransaction())
                {
                    var types = (LinetypeTable)tr.GetObject(source.LinetypeTableId, OpenMode.ForWrite);
                    using var dash = new LinetypeTableRecord { Name = "TEST_DASH", PatternLength = 5, NumDashes = 4 };
                    dash.SetDashLengthAt(0, 2.5); dash.SetDashLengthAt(1, -1.25); dash.SetDashLengthAt(2, 0); dash.SetDashLengthAt(3, -1.25);
                    types.Add(dash); tr.AddNewlyCreatedDBObject(dash, true);
                    var styles = (TextStyleTable)tr.GetObject(source.TextStyleTableId, OpenMode.ForWrite);
                    using var style = new TextStyleTableRecord { Name = "TEST_TEXT", FileName = "txt.shx", XScale = .8, ObliquingAngle = .1 };
                    styles.Add(style); tr.AddNewlyCreatedDBObject(style, true);
                    using var text = new LinetypeTableRecord { Name = "TEST_TEXT_LINE", PatternLength = 4, NumDashes = 2 };
                    text.SetDashLengthAt(0, 3); text.SetDashLengthAt(1, -1); text.SetShapeStyleAt(1, style.ObjectId);
                    text.SetTextAt(1, "GW"); text.SetShapeScaleAt(1, .5); text.SetShapeRotationAt(1, .15);
                    text.SetShapeOffsetAt(1, new Vector2d(-.2, -.15)); text.SetShapeIsUprightAt(1, true);
                    types.Add(text); tr.AddNewlyCreatedDBObject(text, true);
                    foreach (var (name, type, color) in new[] { ("DASH", dash.ObjectId, (short)1), ("TEXT", text.ObjectId, (short)3), ("SHAPE", types["BATTING"], (short)5) })
                    {
                        var layer = (LayerTableRecord)tr.GetObject(ReviewLayer(source, tr, "N-WE-KL-" + name + "-G"), OpenMode.ForWrite);
                        layer.LinetypeObjectId = type; layer.Color = Color.FromColorIndex(ColorMethod.ByAci, color); layer.LineWeight = LineWeight.LineWeight035;
                        using var line = new Line(Point3d.Origin, new Point3d(30, color * 5.0, 0)) { LayerId = layer.ObjectId };
                        ReviewAppend(ReviewModel(source, tr), tr, line);
                    }
                    tr.Commit();
                }
                source.SaveAs(path, DwgVersion.Current);
            }
            File.WriteAllText(path + ".sha256", Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))));
            AttachAndInsert(doc.Database, path, "LINE_SOURCE");
            using (var tr = doc.Database.TransactionManager.StartTransaction())
            {
                var types = (LinetypeTable)tr.GetObject(doc.Database.LinetypeTableId, OpenMode.ForRead);
                var patterns = new[] { "TEST_DASH", "TEST_TEXT_LINE", "BATTING" }.ToDictionary(name => name,
                    name => DrawingAnalyzer.LinetypeSignature(types["LINE_SOURCE|" + name], tr));
                File.WriteAllText(path + ".patterns.json", System.Text.Json.JsonSerializer.Serialize(patterns));
                var settings = new LegendSettings { IncludeXrefLayers = true, ExplodeOnPlace = false, IncludeQuantities = false };
                var registry = LegendStore.Load(doc.Database, tr);
                var legend = new LegendDefinition { Name = "Xref-lijntypen", GroupName = LegendRegistry.NewGroupName(), Settings = settings };
                registry.Add(legend);
                if (BuildManagedLegend(doc.Database, tr, registry, legend, out int rows, out _) != UpdateResult.Updated || rows != 3)
                    throw new InvalidOperationException("Drie nieuwe bronpatronen ontbreken.");
                CheckXrefLinetypes(doc.Database, tr, legend);
                var blocks = (BlockTable)tr.GetObject(doc.Database.BlockTableId, OpenMode.ForRead);
                var dashRow = legend.Rows.Single(r => r.Sources.Any(s => s.Layer.EndsWith("DASH-G", StringComparison.Ordinal)));
                var line = ((BlockTableRecord)tr.GetObject(blocks[dashRow.SampleBlock], OpenMode.ForRead)).Cast<ObjectId>()
                    .Select(id => tr.GetObject(id, OpenMode.ForRead)).OfType<Curve>().First();
                var oldLayer = (LayerTableRecord)tr.GetObject(line.LayerId, OpenMode.ForWrite);
                oldLayer.LinetypeObjectId = types["LINE_SOURCE|TEST_DASH"];
                if (BuildManagedLegend(doc.Database, tr, registry, legend, out rows, out _) != UpdateResult.Updated || rows != 3)
                    throw new InvalidOperationException("Bestaande afhankelijke verwijzing niet hersteld.");
                CheckXrefLinetypes(doc.Database, tr, legend);
                LegendStore.Save(doc.Database, tr, registry);
                tr.Commit();
            }
            doc.Editor.WriteMessage("\nLINETYPES nieuw opgebouwde streep-, tekst- en shapepatronen zelfstandig -> OK");
        }, message => doc.Editor.WriteMessage(message), "LinetypeTest");
    }

    [CommandMethod("NLCSLEGENDALINETYPEVERIFY", CommandFlags.Modal)]
    public void VerifyXrefLinetypes()
    {
        var doc = App.DocumentManager.MdiActiveDocument;
        DiagnosticRun.Execute(() =>
        {
            using var tr = doc.Database.TransactionManager.StartTransaction();
            var registry = LegendStore.Load(doc.Database, tr); var legend = registry.Legends.Single();
            CheckXrefLinetypes(doc.Database, tr, legend);
            var layers = (LayerTable)tr.GetObject(doc.Database.LayerTableId, OpenMode.ForRead);
            foreach (int color in new[] { 2, 4, 6 })
            {
                foreach (string name in new[] { "DASH", "TEXT", "SHAPE" })
                {
                    var layer = (LayerTableRecord)tr.GetObject(layers["LINE_SOURCE|N-WE-KL-" + name + "-G"], OpenMode.ForWrite);
                    layer.Color = Color.FromColorIndex(ColorMethod.ByAci, (short)color);
                    layer.LineWeight = color == 4 ? LineWeight.LineWeight070 : LineWeight.LineWeight050;
                }
                for (int update = 0; update < 2; update++)
                {
                    if (BuildManagedLegend(doc.Database, tr, registry, legend, out int rows, out _) != UpdateResult.Updated || rows != 3)
                        throw new InvalidOperationException("Stijlvariant niet opnieuw opgebouwd.");
                    CheckXrefLinetypes(doc.Database, tr, legend);
                }
            }
            LegendStore.Save(doc.Database, tr, registry); tr.Commit(); PurgePending(doc.Database);
            var path = Path.Join(Path.GetDirectoryName(doc.Database.Filename)!, "linetype-source.dwg");
            if (File.ReadAllText(path + ".sha256") != Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))))
                throw new InvalidOperationException("Bron-xref gewijzigd.");
            doc.Editor.WriteMessage("\nLINETYPEVERIFY patroon, kleur en lijndikte na audit, heropenen en nieuwe varianten -> OK");
        }, message => doc.Editor.WriteMessage(message), "LinetypeVerify");
    }

    [CommandMethod("NLCSLEGENDALINETYPECHECK", CommandFlags.Modal)]
    public void CheckSavedXrefLinetypes()
    {
        var doc = App.DocumentManager.MdiActiveDocument;
        DiagnosticRun.Execute(() =>
        {
            using var tr = doc.Database.TransactionManager.StartTransaction();
            CheckXrefLinetypes(doc.Database, tr, LegendStore.Load(doc.Database, tr).Legends.Single());
            doc.Editor.WriteMessage("\nLINETYPECHECK opgeslagen varianten na tweede heropening -> OK");
        }, message => doc.Editor.WriteMessage(message), "LinetypeCheck");
    }

    private static void CheckXrefLinetypes(Database db, Transaction tr, LegendDefinition legend)
    {
        string path = Path.Join(Path.GetDirectoryName(db.Filename)!, "linetype-source.dwg.patterns.json");
        var patterns = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path))!;
        var blocks = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
        var layers = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
        foreach (var row in legend.Rows)
        {
            var sourceLayer = (LayerTableRecord)tr.GetObject(layers[row.Sources.First().Layer], OpenMode.ForRead);
            var sourceType = (LinetypeTableRecord)tr.GetObject(sourceLayer.LinetypeObjectId, OpenMode.ForRead);
            string expected = patterns[LayerNaming.LocalName(sourceType.Name)];
            if (DrawingAnalyzer.LinetypeSignature(sourceType.ObjectId, tr) != expected)
                throw new InvalidOperationException("Lijnpatroon van geladen bron gewijzigd.");
            var lines = ((BlockTableRecord)tr.GetObject(blocks[row.SampleBlock], OpenMode.ForRead)).Cast<ObjectId>()
                .Select(id => tr.GetObject(id, OpenMode.ForRead)).OfType<Curve>().ToArray();
            if (lines.Length == 0) throw new InvalidOperationException("Lijnvoorbeeld ontbreekt.");
            foreach (var line in lines)
            {
                var layer = (LayerTableRecord)tr.GetObject(line.LayerId, OpenMode.ForRead);
                var type = (LinetypeTableRecord)tr.GetObject(layer.LinetypeObjectId, OpenMode.ForRead);
                if (layer.IsDependent || type.IsDependent || type.ObjectId.OriginalDatabase != db)
                    throw new InvalidOperationException("Lokale legendalaag verwijst naar afhankelijk lijntype: " + type.Name);
                if (DrawingAnalyzer.LinetypeSignature(type.ObjectId, tr) != expected || layer.Color.ColorIndex != sourceLayer.Color.ColorIndex || layer.LineWeight != sourceLayer.LineWeight)
                    throw new InvalidOperationException("Patroon, kleur of lijndikte van bron verloren.");
                for (int i = 0; i < type.NumDashes; i++)
                    if (!type.ShapeStyleAt(i).IsNull && ((TextStyleTableRecord)tr.GetObject(type.ShapeStyleAt(i), OpenMode.ForRead)).IsDependent)
                        throw new InvalidOperationException("Afhankelijke tekst- of shapestijl achtergebleven.");
            }
        }
    }
}
