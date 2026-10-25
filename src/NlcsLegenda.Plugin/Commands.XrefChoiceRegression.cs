using System.IO;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using NlcsLegenda.Core;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Core.Application;

namespace NlcsLegenda.Plugin;

public partial class Commands
{
    [CommandMethod("NLCSLEGENDAXREFCHOICEREGRESSION", CommandFlags.Modal)]
    public void NlcsLegendaXrefChoiceRegression()
    {
        var doc = AcApp.DocumentManager.MdiActiveDocument;
        if (doc is null) return;
        DiagnosticRun.Execute(() =>
        {
            var db = doc.Database;
            var rotation = Matrix3d.Rotation(0.37, Vector3d.ZAxis, Point3d.Origin);
            var mirror = new SymbolTransform(-Vector3d.XAxis, Vector3d.YAxis, Vector3d.ZAxis);
            var rotatedMirror = new SymbolTransform(mirror.XAxis.TransformBy(rotation), mirror.YAxis.TransformBy(rotation), mirror.ZAxis);
            if (rotatedMirror.WithoutRotation().Signature != mirror.WithoutRotation().Signature
                || mirror.WithoutRotation().Signature == SymbolTransform.Identity.Signature)
                throw new InvalidOperationException("Draairichting samenvoegen verliest spiegeling.");
            var tilted = SymbolTransform.From(Matrix3d.Rotation(0.4, Vector3d.XAxis, Point3d.Origin));
            if (tilted.WithoutRotation() != tilted) throw new InvalidOperationException("Draairichting wijzigt gekanteld symbool.");
            string path = Path.Join(Path.GetDirectoryName(db.Filename)!, "locked-symbol-source.dwg");
            const string layerName = "N-WE-GR-BOOM-S";
            using (var source = new Database(true, true))
            {
                using var tr = source.TransactionManager.StartTransaction();
                var layers = (LayerTable)tr.GetObject(source.LayerTableId, OpenMode.ForWrite);
                var layer = new LayerTableRecord { Name = layerName };
                layers.Add(layer); tr.AddNewlyCreatedDBObject(layer, true);
                var blocks = (BlockTable)tr.GetObject(source.BlockTableId, OpenMode.ForWrite);
                var symbol = new BlockTableRecord { Name = "BOOM" };
                blocks.Add(symbol); tr.AddNewlyCreatedDBObject(symbol, true);
                var circle = new Circle(Point3d.Origin, Vector3d.ZAxis, 8) { LayerId = layer.ObjectId };
                symbol.AppendEntity(circle); tr.AddNewlyCreatedDBObject(circle, true);
                var model = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(source), OpenMode.ForWrite);
                foreach (var scale in new[] { new Scale3d(1), new Scale3d(2), new Scale3d(2, 1, 1) })
                {
                    var reference = new BlockReference(new Point3d(scale.X * 50, scale.Y * 50, 0), symbol.ObjectId)
                    { LayerId = layer.ObjectId, ScaleFactors = scale };
                    model.AppendEntity(reference); tr.AddNewlyCreatedDBObject(reference, true);
                }
                var smaller = new BlockTableRecord { Name = "KLEINERE_BOOM" };
                blocks.Add(smaller); tr.AddNewlyCreatedDBObject(smaller, true);
                var smallCircle = new Circle(Point3d.Origin, Vector3d.ZAxis, 4) { LayerId = layer.ObjectId };
                smaller.AppendEntity(smallCircle); tr.AddNewlyCreatedDBObject(smallCircle, true);
                var smallReference = new BlockReference(new Point3d(250, 250, 0), smaller.ObjectId) { LayerId = layer.ObjectId };
                model.AppendEntity(smallReference); tr.AddNewlyCreatedDBObject(smallReference, true);
                var rotated = new BlockReference(new Point3d(300, 300, 0), symbol.ObjectId)
                    { LayerId = layer.ObjectId, Rotation = 0.37 };
                model.AppendEntity(rotated); tr.AddNewlyCreatedDBObject(rotated, true);
                foreach (var (definition, radius) in new[] { (symbol, 0.8), (smaller, 0.4) })
                {
                    var hatch = new Hatch { LayerId = layer.ObjectId };
                    definition.AppendEntity(hatch); tr.AddNewlyCreatedDBObject(hatch, true);
                    hatch.SetHatchPattern(HatchPatternType.PreDefined, "SOLID");
                    var points = new Point2dCollection { new(-radius, 0), new(radius, 0) };
                    var bulges = new DoubleCollection { 1, 1 };
                    hatch.AppendLoop(HatchLoopTypes.External | HatchLoopTypes.Polyline, points, bulges);
                    hatch.EvaluateHatch(true);
                }
                layer.IsLocked = true;
                tr.Commit();
                source.SaveAs(path, DwgVersion.Current);
            }
            File.SetAttributes(path, File.GetAttributes(path) | FileAttributes.ReadOnly);
            var hash = System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path));
            AttachAndInsert(db, path, "DTM_SOURCE");
            var choice = BuildLayerChoices(db, new()).Single(c => c.Layer == "DTM_SOURCE|" + layerName && c.SymbolBlock == "DTM_SOURCE|BOOM");
            if (choice.Type != NlcsDrawType.Symbool || !choice.IsValid)
                throw new InvalidOperationException("Gekwalificeerde symboolkeuze ontbreekt.");
            foreach (var (merge, rotate, expected) in new[] { (false, false, 5), (true, false, 3), (true, true, 2) })
            {
                using var tr = db.TransactionManager.StartTransaction();
                var settings = new LegendSettings { IncludeXrefLayers = true, NormalizeSymbols = true, MergeSymbolSizes = merge, MergeSymbolRotations = rotate,
                    Scale = 500, IncludeRemarks = false, IncludeFooter = false, IncludeScaleBar = false };
                var analysis = DrawingAnalyzer.Analyze(db, tr, settings);
                if (analysis.Entries.Count != expected || analysis.Entries.Sum(e => e.Metric.Count) != 5)
                    throw new InvalidOperationException($"Grootte/rotatie {merge}/{rotate}: {analysis.Entries.Count}/{expected} regels, {analysis.Entries.Sum(e => e.Metric.Count)}/5 objecten.");
                var block = LegendBuilder.BuildBlock(db, tr, analysis, settings, out _, out var issues);
                if (issues.Count != 0) throw new InvalidOperationException("Vergrendelde bron niet getekend.");
                var circles = ((BlockTableRecord)tr.GetObject(block, OpenMode.ForRead)).Cast<ObjectId>()
                    .Select(id => tr.GetObject(id, OpenMode.ForRead)).OfType<Entity>()
                    .Where(e => e is Circle or Ellipse or BlockReference && e.Layer.Contains("BOOM", StringComparison.Ordinal)).ToArray();
                if (circles.Length != analysis.Entries.Count || circles.Any(c =>
                    c.GeometricExtents.MaxPoint.Y - c.GeometricExtents.MinPoint.Y > settings.ToModel(settings.SwatchHeightMm) + 1e-7))
                    throw new InvalidOperationException($"Genormaliseerd symbool: {circles.Length}/{analysis.Entries.Count}; hoogtes: {string.Join(", ", circles.Select(c => c.GeometricExtents.MaxPoint.Y - c.GeometricExtents.MinPoint.Y))}.");
                tr.Abort();
            }
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var settings = new LegendSettings { IncludeXrefLayers = false, NormalizeSymbols = true, ManualEntries = new() { choice } };
                var analysis = DrawingAnalyzer.Analyze(db, tr, settings);
                if (analysis.Entries.Count != 1 || !analysis.Entries[0].IsManual) throw new InvalidOperationException("Eigen xref-regel ontbreekt.");
                LegendBuilder.BuildBlock(db, tr, analysis, settings, out int count, out var issues);
                if (count != 1 || issues.Count != 0) throw new InvalidOperationException("Eigen xref-symbool niet getekend.");
                tr.Abort();
            }
            if (!hash.SequenceEqual(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path))))
                throw new InvalidOperationException("Bronbestand gewijzigd.");
            doc.Editor.WriteMessage("\nXREFCHOICEREGRESSION alleen-lezen/vergrendeld, eigen symbool, grootte en normalisatie -> OK");
        }, message => doc.Editor.WriteMessage(message), nameof(NlcsLegendaXrefChoiceRegression));
    }
}
