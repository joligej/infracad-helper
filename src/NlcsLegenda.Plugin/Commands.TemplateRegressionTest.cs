using System.IO;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using NlcsLegenda.Core;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Core.Application;

namespace NlcsLegenda.Plugin;

public partial class Commands
{
    [CommandMethod("NLCSLEGENDATEMPLATEREGRESSIONTEST", CommandFlags.Modal)]
    public void TemplateRegressionTest()
    {
        var doc = AcApp.DocumentManager.MdiActiveDocument;
        DiagnosticRun.Execute(() =>
        {
            var db = doc.Database;
            using var tr = db.TransactionManager.StartTransaction();
            var model = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForWrite);
            ObjectId Layer(string name)
            {
                var layers = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForWrite);
                using var layer = new LayerTableRecord { Name = name }; var id = layers.Add(layer); tr.AddNewlyCreatedDBObject(layer, true); return id;
            }
            ObjectId Add(Entity entity, string layer)
            {
                entity.SetDatabaseDefaults(db); entity.LayerId = Layer(layer);
                var id = model.AppendEntity(entity); tr.AddNewlyCreatedDBObject(entity, true); return id;
            }
            using var wide = new Polyline(); wide.AddVertexAt(0, new Point2d(0, 0), 0, 0, 0); wide.AddVertexAt(1, new Point2d(10, 0), 0, 0, 0); wide.ConstantWidth = .3;
            var wideId = Add(wide, "N-WE-MA-TEST_MARKERING-G");
            using var outline = new Polyline(); outline.AddVertexAt(0, new Point2d(0, 2), 0, 0, 0); outline.AddVertexAt(1, new Point2d(4, 2), 0, 0, 0); outline.AddVertexAt(2, new Point2d(4, 3), 0, 0, 0); outline.AddVertexAt(3, new Point2d(0, 3), 0, 0, 0); outline.AddVertexAt(4, new Point2d(0, 2), 0, 0, 0); outline.Closed = true;
            var outlineId = Add(outline, "N-WE-GR-TEST_BOMENGROEP-S");
            using var sourceCircle = new Circle(new Point3d(10, 4, 0), Vector3d.ZAxis, .5);
            var circleId = Add(sourceCircle, "B-WE-RI-TEST_PUT-G");
            using var edge = new Polyline(); edge.AddVertexAt(0, new Point2d(0, 5), 0, 0, 0); edge.AddVertexAt(1, new Point2d(4, 5), 0, 0, 0); edge.AddVertexAt(2, new Point2d(4, 6), 0, 0, 0); edge.AddVertexAt(3, new Point2d(0, 6), 0, 0, 0); edge.Closed = true;
            var edgeId = Add(edge, "N-WE-VH-TEST_HATCH-GV");
            using var hatch = new Hatch(); var hatchId = Add(hatch, "N-WE-VH-TEST_HATCH-A"); hatch.ColorIndex = 0; ((LayerTableRecord)tr.GetObject(hatch.LayerId, OpenMode.ForWrite)).Color = Autodesk.AutoCAD.Colors.Color.FromColorIndex(Autodesk.AutoCAD.Colors.ColorMethod.ByAci, 254);
            hatch.SetHatchPattern(HatchPatternType.PreDefined, "ANSI31"); hatch.BackgroundColor = Autodesk.AutoCAD.Colors.Color.FromColorIndex(Autodesk.AutoCAD.Colors.ColorMethod.ByLayer, 256); hatch.Associative = true;
            using (var boundaries = new ObjectIdCollection { edgeId }) hatch.AppendLoop(HatchLoopTypes.External, boundaries);
            hatch.EvaluateHatch(true);
            var blocks = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForWrite);
            using var planar = new BlockTableRecord { Name = "TEMPLATE_PLANAR" }; blocks.Add(planar); tr.AddNewlyCreatedDBObject(planar, true);
            using var rim = new Polyline(); rim.AddVertexAt(0, new Point2d(-.1, -.1), 0, 0, 0); rim.AddVertexAt(1, new Point2d(.1, -.1), 0, 0, 0); rim.AddVertexAt(2, new Point2d(.1, .1), 0, 0, 0); rim.Closed = true; rim.ConstantWidth = .02;
            planar.AppendEntity(rim); tr.AddNewlyCreatedDBObject(rim, true);
            using var round = new Circle(Point3d.Origin, Vector3d.ZAxis, .15); planar.AppendEntity(round); tr.AddNewlyCreatedDBObject(round, true);
            using var planarReference = new BlockReference(Point3d.Origin, planar.ObjectId) { ScaleFactors = new Scale3d(6, 6, 1) };
            var planarId = Add(planarReference, "V-WE-IE-TEST_PAAL-S");
            var settings = new LegendSettings { Scale = 200, IncludeTitle = false, IncludeText = false, IncludeGroupHeaders = false, IncludeFooter = false, IncludeRemarks = false, DrawSwatchFrame = false };
            var analysis = DrawingAnalyzer.Analyze(db, tr, settings, new[] { wideId, outlineId, circleId, hatchId, planarId });
            if (analysis.Entries.Count != 5 || analysis.SymbolSamples.Count != 3) throw new InvalidOperationException("Een lijn, losse symboolcurve, cirkel of arcering ontbreekt.");
            var blockId = LegendBuilder.BuildBlock(db, tr, analysis, settings, out int count, out _);
            var entities = ((BlockTableRecord)tr.GetObject(blockId, OpenMode.ForRead)).Cast<ObjectId>().Select(id => (Entity)tr.GetObject(id, OpenMode.ForRead)).ToArray();
            if (count != 5 || !entities.OfType<Polyline>().Any(p => Math.Abs(p.ConstantWidth - .3) < 1e-9)) throw new InvalidOperationException("Markering verliest zijn breedte.");
            if (!entities.OfType<Circle>().Any(c => Math.Abs(c.Radius - .5) < 1e-9)) throw new InvalidOperationException("Rioolput is geen cirkel op bronmaat.");
            if (!entities.OfType<Hatch>().Any(h => !h.Associative && h.PatternName == "ANSI31" && h.ColorIndex == 7 && h.BackgroundColor.ColorIndex == 254)) throw new InvalidOperationException("Associatieve arcering kon niet worden losgekoppeld.");
            if (!entities.OfType<Circle>().Any(c => Math.Abs(c.Radius - .9) < 1e-9)
                || !entities.OfType<Polyline>().Any(p => Math.Abs(p.ConstantWidth - .12) < 1e-9)) throw new InvalidOperationException("Afwijkende z-schaal verandert vlakke symboolgeometrie.");
            if (Math.Abs(planarReference.ScaleFactors.Z - 1) > 1e-9 || Math.Abs(rim.ConstantWidth - .02) > 1e-9) throw new InvalidOperationException("Bronsymbool gewijzigd.");
            if (!hatch.BackgroundColor.IsByLayer || !hatch.Associative || hatch.GetAssociatedObjectIds().Count != 1 || outline.NumberOfVertices != 5) throw new InvalidOperationException("Brongeometrie gewijzigd.");
            tr.Abort();
            doc.Editor.WriteMessage("\nTEMPLATEREGRESSION lijnbreedte, losse symboolcurve, cirkel en associatieve arcering; bron behouden -> OK");
        }, message => doc.Editor.WriteMessage(message), "TemplateRegressionTest");
    }

    [CommandMethod("NLCSLEGENDAEXAMPLETEST", CommandFlags.Modal)]
    public void ExampleTest()
    {
        var doc = AcApp.DocumentManager.MdiActiveDocument;
        string path = Path.Join(Path.GetDirectoryName(doc.Database.Filename)!, "example-library.dwg");
        DiagnosticRun.Execute(() =>
        {
            using (var source = new Database(true, true))
            {
                using var tr = source.TransactionManager.StartTransaction();
                var bt = (BlockTable)tr.GetObject(source.BlockTableId, OpenMode.ForWrite);
                using var block = new BlockTableRecord { Name = "NLCS_VOORBEELD_TEST" }; bt.Add(block); tr.AddNewlyCreatedDBObject(block, true);
                foreach (double y in new[] { -.2, .2 })
                {
                    using var line = new Polyline(); line.SetDatabaseDefaults(source); line.AddVertexAt(0, new Point2d(-2.4, y), 0, 0, 0); line.AddVertexAt(1, new Point2d(2.4, y), 0, 0, 0); line.ColorIndex = 1;
                    block.AppendEntity(line); tr.AddNewlyCreatedDBObject(line, true);
                }
                using var text = new MText { Contents = "A1", TextHeight = .15, Location = Point3d.Origin, ColorIndex = 1 };
                block.AppendEntity(text); tr.AddNewlyCreatedDBObject(text, true);
                using var circle = new Circle(Point3d.Origin, Vector3d.ZAxis, .15) { ColorIndex = 1 };
                block.AppendEntity(circle); tr.AddNewlyCreatedDBObject(circle, true);
                using var mask = new Hatch { Color = Autodesk.AutoCAD.Colors.Color.FromRgb(255, 255, 255) };
                block.AppendEntity(mask); tr.AddNewlyCreatedDBObject(mask, true); mask.SetHatchPattern(HatchPatternType.PreDefined, "SOLID");
                using (var boundary = new ObjectIdCollection { circle.ObjectId }) mask.AppendLoop(HatchLoopTypes.External, boundary);
                mask.EvaluateHatch(true);
                using (var order = new ObjectIdCollection { mask.ObjectId, circle.ObjectId, text.ObjectId })
                    ((DrawOrderTable)tr.GetObject(block.DrawOrderTableId, OpenMode.ForWrite)).SetRelativeDrawOrder(order);
                ExampleLibrary.WriteCatalog(source, tr, new[] { new LegendExample(block.Name, "Testband", "WEG", 200, new[] { LegendExample.SourceKey(ExampleTestEntry()) }) });
                tr.Commit(); source.SaveAs(path, DwgVersion.Current);
            }
            using (var tr = doc.Database.TransactionManager.StartTransaction())
            {
                var settings = ExampleTestSettings(path);
                var entry = ExampleTestEntry();
                var blockId = LegendBuilder.BuildBlock(doc.Database, tr, new AnalysisResult { Entries = new[] { entry } }, settings, out _, out _);
                CheckExampleBlock(doc.Database, tr, blockId);
                settings.ExampleOverrides[LegendOrder.Key(entry)] = "NLCS_VOORBEELD_TEST";
                settings.ExampleLibraryPath = path + ".missing";
                var registry = new LegendRegistry();
                registry.Legends.Add(new LegendDefinition { Id = "example-regression", Name = "Voorbeeldtest", Settings = settings });
                registry.Legends.Add(new LegendDefinition { Id = "example-automatic", Name = "Automatisch voorbeeld", Settings = ExampleTestSettings(path) });
                LegendStore.Save(doc.Database, tr, registry);
                tr.Commit();
            }
            File.Delete(path);
            doc.Editor.WriteMessage("\nEXAMPLE bibliotheekimport en bronmaat 1:200 naar 1:500 -> OK");
        }, message => doc.Editor.WriteMessage(message), "ExampleTest");
    }

    [CommandMethod("NLCSLEGENDAEXAMPLEVERIFY", CommandFlags.Modal)]
    public void ExampleVerify()
    {
        var doc = AcApp.DocumentManager.MdiActiveDocument;
        DiagnosticRun.Execute(() =>
        {
            using var tr = doc.Database.TransactionManager.StartTransaction();
            var settings = LegendStore.Load(doc.Database, tr).FindById("example-regression")!.Settings;
            var blockId = LegendBuilder.BuildBlock(doc.Database, tr, new AnalysisResult { Entries = new[] { ExampleTestEntry() } }, settings, out _, out _);
            CheckExampleBlock(doc.Database, tr, blockId);
            var automatic = LegendStore.Load(doc.Database, tr).FindById("example-automatic")!.Settings;
            blockId = LegendBuilder.BuildBlock(doc.Database, tr, new AnalysisResult { Entries = new[] { ExampleTestEntry() } }, automatic, out _, out _);
            CheckExampleBlock(doc.Database, tr, blockId); tr.Abort();
            doc.Editor.WriteMessage("\nEXAMPLE na heropenen: gekozen voorbeeld zonder bronbibliotheek behouden -> OK");
        }, message => doc.Editor.WriteMessage(message), "ExampleVerify");
    }

    private static LegendEntry ExampleTestEntry() => new() { Status = NlcsStatus.Nieuw, Discipline = "WE", Hoofdgroep = "VH", Element = "EXAMPLE_BAND", Description = "Testband" };
    private static LegendSettings ExampleTestSettings(string path) => new() { Scale = 500, ExampleLibraryPath = path, IncludeTitle = false, IncludeText = false, IncludeGroupHeaders = false, IncludeFooter = false, IncludeRemarks = false, IncludeScaleBar = false, DrawSwatchFrame = false };
    private static void CheckExampleBlock(Database db, Transaction tr, ObjectId blockId)
    {
        var entities = ((BlockTableRecord)tr.GetObject(blockId, OpenMode.ForRead)).Cast<ObjectId>().Select(id => (Entity)tr.GetObject(id, OpenMode.ForRead)).ToArray();
        if (entities.Length != 1 || entities[0] is not BlockReference reference) throw new InvalidOperationException("Voorbeeld is niet als één geheel bewaard.");
        var definition = (BlockTableRecord)tr.GetObject(reference.BlockTableRecord, OpenMode.ForRead);
        var order = ((DrawOrderTable)tr.GetObject(definition.DrawOrderTableId, OpenMode.ForRead)).GetFullDrawOrder(0).Cast<ObjectId>()
            .Select(id => tr.GetObject(id, OpenMode.ForRead)).ToArray();
        if (!(Array.FindIndex(order, e => e is Hatch) < Array.FindIndex(order, e => e is Circle)
            && Array.FindIndex(order, e => e is Circle) < Array.FindIndex(order, e => e is MText { Contents: "A1" })))
            throw new InvalidOperationException("Afdekvlak, omlijning en code staan niet in bronvolgorde.");
        var extents = reference.GeometricExtents;
        if (Math.Abs(extents.MaxPoint.X - extents.MinPoint.X - 12) > 1e-8 || Math.Abs(extents.MaxPoint.Y - extents.MinPoint.Y - 1) > 1e-8) throw new InvalidOperationException("Voorbeeldschaal wijkt af.");
    }
}
