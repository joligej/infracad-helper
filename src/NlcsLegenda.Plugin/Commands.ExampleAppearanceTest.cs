using System.IO;
using Autodesk.AutoCAD.Colors;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using NlcsLegenda.Core;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Core.Application;
using Color = Autodesk.AutoCAD.Colors.Color;

namespace NlcsLegenda.Plugin;

public partial class Commands
{
    [CommandMethod("NLCSLEGENDAEXAMPLEAPPEARANCETEST", CommandFlags.Modal)]
    public void ExampleAppearanceTest()
    {
        var doc = AcApp.DocumentManager.MdiActiveDocument;
        string library = Path.Join(Path.GetDirectoryName(doc.Database.Filename)!, "appearance-library.dwg");
        DiagnosticRun.Execute(() =>
        {
            using (var source = new Database(true, true))
            {
                using var tr = source.TransactionManager.StartTransaction();
                var blocks = (BlockTable)tr.GetObject(source.BlockTableId, OpenMode.ForWrite);
                using var square = new BlockTableRecord { Name = "EXAMPLE_SQUARE" };
                blocks.Add(square); tr.AddNewlyCreatedDBObject(square, true);
                using var edge = new Polyline { ColorIndex = 3 };
                edge.AddVertexAt(0, new Point2d(-1, -1), 0, 0, 0);
                edge.AddVertexAt(1, new Point2d(1, -1), 0, 0, 0);
                edge.AddVertexAt(2, new Point2d(1, 1), 0, 0, 0);
                edge.AddVertexAt(3, new Point2d(-1, 1), 0, 0, 0);
                edge.Closed = true; square.AppendEntity(edge); tr.AddNewlyCreatedDBObject(edge, true);
                using var pattern = new BlockTableRecord { Name = "EXAMPLE_PATTERN" };
                blocks.Add(pattern); tr.AddNewlyCreatedDBObject(pattern, true);
                using var patternHatch = AddAppearanceHatch(pattern, tr, 5);
                ExampleLibrary.WriteCatalog(source, tr, new[] {
                    new LegendExample(square.Name, "Boom", "Test", 500, new[] { "NIEUW|WE|GR|BOOM" }),
                    new LegendExample(pattern.Name, "Asfalt", "Test", 500, new[] { "NIEUW|WE|VH|ASFALT" }) });
                tr.Commit(); source.SaveAs(library, DwgVersion.Current);
            }
            try
            {
                using var tr = doc.Database.TransactionManager.StartTransaction();
                var model = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(doc.Database), OpenMode.ForWrite);
                var treeLayer = ReviewLayer(doc.Database, tr, "N-WE-GR-BOOM-S");
                using var circle = new Circle(Point3d.Origin, Vector3d.ZAxis, .25) { LayerId = treeLayer, ColorIndex = 3 };
                model.AppendEntity(circle); tr.AddNewlyCreatedDBObject(circle, true);
                using var hatch = AddAppearanceHatch(model, tr, 3);
                hatch.LayerId = ReviewLayer(doc.Database, tr, "N-WE-VH-ASFALT-A");
                var settings = new LegendSettings { Scale = 500, ExampleLibraryPath = library,
                    IncludeTitle = false, IncludeText = false, IncludeGroupHeaders = false,
                    IncludeFooter = false, IncludeRemarks = false, IncludeScaleBar = false, DrawSwatchFrame = false };
                var analysis = DrawingAnalyzer.Analyze(doc.Database, tr, settings, new[] { circle.ObjectId, hatch.ObjectId });
                if (analysis.Entries.Count != 2) throw new InvalidOperationException("Twee bronvoorbeelden verwacht.");
                var automatic = LegendBuilder.BuildBlock(doc.Database, tr, analysis, settings, out _, out _);
                var drawn = ((BlockTableRecord)tr.GetObject(automatic, OpenMode.ForRead)).Cast<ObjectId>()
                    .Select(id => tr.GetObject(id, OpenMode.ForRead)).OfType<Entity>().ToArray();
                if (!drawn.OfType<Circle>().Any(c => Math.Abs(c.Radius - .25) < 1e-9)
                    || !drawn.OfType<Hatch>().Any(h => h.BackgroundColor.ColorIndex == 3))
                    throw new InvalidOperationException("Automatisch voorbeeld vervangt bronvorm, maat of achtergrondkleur.");
                foreach (var entry in analysis.Entries)
                    settings.ExampleOverrides[LegendOrder.Key(entry)] = entry.SymbolLayer is not null ? "EXAMPLE_SQUARE" : "EXAMPLE_PATTERN";
                var selected = LegendBuilder.BuildBlock(doc.Database, tr, analysis, settings, out _, out _);
                var references = ((BlockTableRecord)tr.GetObject(selected, OpenMode.ForRead)).Cast<ObjectId>()
                    .Select(id => tr.GetObject(id, OpenMode.ForRead)).OfType<BlockReference>().ToArray();
                if (references.Length != 2
                    || !references.SelectMany(r => ((BlockTableRecord)tr.GetObject(r.BlockTableRecord, OpenMode.ForRead)).Cast<ObjectId>())
                        .Select(id => tr.GetObject(id, OpenMode.ForRead)).OfType<Hatch>().Any(h => h.BackgroundColor.ColorIndex == 5))
                    throw new InvalidOperationException("Expliciete voorbeeldkeuze niet toegepast.");
                tr.Abort();
            }
            finally { File.Delete(library); }
            doc.Editor.WriteMessage("\nEXAMPLEAPPEARANCE bronvorm, maat en achtergrondkleur behouden; expliciete keuze toegepast -> OK");
        }, message => doc.Editor.WriteMessage(message), "ExampleAppearanceTest");
    }

    private static Hatch AddAppearanceHatch(BlockTableRecord target, Transaction tr, short background)
    {
        var hatch = new Hatch { ColorIndex = 1 };
        target.AppendEntity(hatch); tr.AddNewlyCreatedDBObject(hatch, true);
        hatch.SetHatchPattern(HatchPatternType.PreDefined, "ANSI31");
        hatch.PatternScale = .1;
        hatch.BackgroundColor = Color.FromColorIndex(ColorMethod.ByAci, background);
        var points = new Point2dCollection { new(0, 0), new(2, 0), new(2, 1), new(0, 1), new(0, 0) };
        var bulges = new DoubleCollection { 0, 0, 0, 0, 0 };
        hatch.AppendLoop(HatchLoopTypes.External | HatchLoopTypes.Polyline, points, bulges); hatch.EvaluateHatch(true);
        return hatch;
    }
}
