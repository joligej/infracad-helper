using System.IO;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using NlcsLegenda.Core;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Core.Application;

namespace NlcsLegenda.Plugin;

public partial class Commands
{
    [CommandMethod("NLCSLEGENDASYMBOLORIENTATIONTEST", CommandFlags.Modal)]
    public void SymbolOrientationTest()
    {
        var doc = AcApp.DocumentManager.MdiActiveDocument;
        if (doc is null) return;
        DiagnosticRun.Execute(() =>
        {
            var db = doc.Database;
            var expected = new Dictionary<string, Vector3d>();
            var sourceAxes = new Dictionary<string, (Vector3d X, Vector3d Y)>();
            void Seed(Database target, string prefix)
            {
                using var tr = target.TransactionManager.StartTransaction();
                var layers = (LayerTable)tr.GetObject(target.LayerTableId, OpenMode.ForWrite);
                var blocks = (BlockTable)tr.GetObject(target.BlockTableId, OpenMode.ForWrite);
                var model = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(target), OpenMode.ForWrite);
                foreach (var (label, angle, scale, section) in new[] {
                    ("GEWOON", .63, new Scale3d(2, 3, 1), false),
                    ("KWARTSLAG", Math.PI / 2, new Scale3d(1), false),
                    ("SPIEGEL", .91, new Scale3d(-2, 3, 1), false),
                    ("LAAG", .72, new Scale3d(1), true),
                    ("GEKANTELD", .47, new Scale3d(1), false),
                    ("NAAM", 1.18, new Scale3d(1), true) })
                {
                    string element = prefix + "_" + label;
                    using var layer = new LayerTableRecord { Name = "N-WE-AL-" + element + (label == "LAAG" ? "-SD" : "-S") };
                    layers.Add(layer); tr.AddNewlyCreatedDBObject(layer, true);
                    using var block = new BlockTableRecord { Name = "SAL-" + element + (label == "NAAM" ? "-SODMM" : "") };
                    blocks.Add(block); tr.AddNewlyCreatedDBObject(block, true);
                    foreach (var line in new[] { new Point3d(4, 0, 0), new Point3d(0, 1, 0) }.Select(end => new Line(Point3d.Origin, end)))
                    {
                        using (line)
                        {
                            block.AppendEntity(line); tr.AddNewlyCreatedDBObject(line, true);
                        }
                    }
                    using var reference = new BlockReference(new Point3d(expected.Count * 30.0, 0, 0), block.ObjectId) { LayerId = layer.ObjectId, Rotation = angle, ScaleFactors = scale };
                    if (label == "GEKANTELD") reference.Normal = new Vector3d(0, 1, 1).GetNormal();
                    model.AppendEntity(reference); tr.AddNewlyCreatedDBObject(reference, true);
                    sourceAxes[element] = (new Vector3d(4, 0, 0).TransformBy(reference.BlockTransform), Vector3d.YAxis.TransformBy(reference.BlockTransform));
                    expected[element] = section || label == "GEKANTELD" ? Vector3d.XAxis.TransformBy(reference.BlockTransform).GetNormal()
                        : Vector3d.XAxis * Math.Sign(scale.X);
                }
                tr.Commit();
            }
            Seed(db, "HOST");
            string source = Path.Join(Path.GetDirectoryName(db.Filename)!, "orientation-source.dwg");
            using (var xref = new Database(true, true)) { Seed(xref, "XREF"); xref.SaveAs(source, DwgVersion.Current); }
            var hash = System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(source));
            var xrefId = db.AttachXref(source, "ORIENTATION");
            string scaledSource = Path.Join(Path.GetDirectoryName(db.Filename)!, "orientation-scaled-source.dwg");
            using (var scaled = new Database(true, true)) { Seed(scaled, "SHEAR"); scaled.SaveAs(scaledSource, DwgVersion.Current); }
            var scaledHash = System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(scaledSource));
            var scaledId = db.AttachXref(scaledSource, "ORIENTATION_SCALED");
            Matrix3d scaledTransform;
            Matrix3d xrefTransform;
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var model = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForWrite);
                using var reference = new BlockReference(new Point3d(0, 50, 0), xrefId) { Rotation = .42, ScaleFactors = new Scale3d(1.5) };
                model.AppendEntity(reference); tr.AddNewlyCreatedDBObject(reference, true); xrefTransform = reference.BlockTransform;
                using var stretched = new BlockReference(new Point3d(0, 100, 0), scaledId) { Rotation = .42, ScaleFactors = new Scale3d(2, 1, 1) };
                model.AppendEntity(stretched); tr.AddNewlyCreatedDBObject(stretched, true); scaledTransform = stretched.BlockTransform;
                tr.Commit();
            }
            foreach (var key in sourceAxes.Keys.Where(k => !k.StartsWith("HOST_", StringComparison.Ordinal)).ToArray())
            {
                var axes = sourceAxes[key];
                var parent = key.StartsWith("XREF_", StringComparison.Ordinal) ? xrefTransform : scaledTransform;
                sourceAxes[key] = (axes.X.TransformBy(parent), axes.Y.TransformBy(parent));
            }
            foreach (var key in new[] { "XREF_LAAG", "XREF_NAAM", "XREF_GEKANTELD", "SHEAR_LAAG", "SHEAR_NAAM", "SHEAR_GEKANTELD" })
                expected[key] = sourceAxes[key].X.GetNormal();

            static List<Vector3d> Lines(Transaction tr, IEnumerable<ObjectId> ids, Matrix3d transform)
            {
                var result = new List<Vector3d>();
                foreach (var id in ids)
                {
                    if (tr.GetObject(id, OpenMode.ForRead) is Line line)
                        result.Add((line.EndPoint - line.StartPoint).TransformBy(transform));
                    else if (tr.GetObject(id, OpenMode.ForRead) is BlockReference block)
                        result.AddRange(Lines(tr, ((BlockTableRecord)tr.GetObject(block.BlockTableRecord, OpenMode.ForRead)).Cast<ObjectId>(), transform * block.BlockTransform));
                }
                return result;
            }
            void CheckShape(string element, List<Vector3d> lines, bool fitted)
            {
                var axes = lines.OrderByDescending(v => v.Length).ToArray();
                var before = sourceAxes[element];
                if (axes.Length != 2 || Math.Abs(axes[0].Length / axes[1].Length - before.X.Length / before.Y.Length) > 1e-7
                    || Math.Abs(axes[0].GetNormal().DotProduct(axes[1].GetNormal()) - before.X.GetNormal().DotProduct(before.Y.GetNormal())) > 1e-7
                    || !fitted && Math.Abs(axes[0].Length - before.X.Length) > 1e-7)
                    throw new InvalidOperationException("Bronvorm of maat veranderd: " + element);
            }
            var settings = new LegendSettings { IncludeXrefLayers = true, IncludeRemarks = false, IncludeScaleBar = false, IncludeFooter = false, Scale = 500, ExcludedHoofdgroepen = new() };
            for (int repeat = 0; repeat < 2; repeat++)
            {
                using var tr = db.TransactionManager.StartTransaction();
                var analysis = DrawingAnalyzer.Analyze(db, tr, settings);
                if (analysis.Entries.Count != expected.Count) throw new InvalidOperationException("Symboolregels " + analysis.Entries.Count + "/" + expected.Count + ": " + string.Join(",", analysis.Entries.Select(e => e.Element)));
                int checkedRows = 0;
                LegendBuilder.BuildBlock(db, tr, analysis, settings, out _, out var issues, (entry, ids) =>
                {
                    var lines = Lines(tr, ids, Matrix3d.Identity);
                    CheckShape(entry.Element, lines, false);
                    var vector = lines.OrderByDescending(v => v.Length).First().GetNormal();
                    if (!vector.IsEqualTo(expected[entry.Element], new Tolerance(1e-7, 1e-7)))
                        throw new InvalidOperationException("Verkeerde symboolstand: " + entry.Element + " " + vector);
                    checkedRows++;
                });
                if (issues.Count > 0 || checkedRows != expected.Count) throw new InvalidOperationException("Niet alle voorbeelden getekend.");
                foreach (var sample in analysis.SymbolSamples.Values)
                {
                    var previous = sample with { OriginalOrientation = null };
                    if (sample.SelectionIdentity != previous.SelectionIdentity || sample.GroupIdentity(settings) != previous.GroupIdentity(settings))
                        throw new InvalidOperationException("Bronkeuzes veranderen door de legendastand.");
                }
                tr.Commit();
            }
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var analysis = DrawingAnalyzer.Analyze(db, tr, settings);
                var model = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForRead);
                ObjectId Container(ObjectId definition) => model.Cast<ObjectId>().Single(id => tr.GetObject(id, OpenMode.ForRead) is BlockReference b && b.BlockTableRecord == definition);
                foreach (var row in analysis.Entries)
                {
                    var rowSample = analysis.SymbolSamples[row.SymbolLayer!];
                    var containers = row.Element.StartsWith("XREF_", StringComparison.Ordinal) ? new[] { Container(xrefId) }
                        : row.Element.StartsWith("SHEAR_", StringComparison.Ordinal) ? new[] { Container(scaledId) } : Array.Empty<ObjectId>();
                    var link = LinkedLegendExamples.Create(db, tr, rowSample.ReferenceId, containers);
                    var linked = LinkedLegendExamples.Resolve(db, tr, link);
                    string name = LinkedLegendExamples.Capture(db, tr, linked, link, settings);
                    var table = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                    var lines = Lines(tr, ((BlockTableRecord)tr.GetObject(table[name], OpenMode.ForRead)).Cast<ObjectId>(), Matrix3d.Identity);
                    CheckShape(row.Element, lines, true);
                    var direction = lines.OrderByDescending(v => v.Length).First().GetNormal();
                    if (!direction.IsEqualTo(expected[row.Element], new Tolerance(1e-7, 1e-7)))
                        throw new InvalidOperationException("Verkeerde gekoppelde symboolstand: " + row.Element);
                }
                var entry = analysis.Entries.Single(e => e.Element == "HOST_GEWOON");
                var sample = analysis.SymbolSamples[entry.SymbolLayer!];
                using var outer = new BlockReference(Point3d.Origin, ((BlockReference)tr.GetObject(sample.ReferenceId, OpenMode.ForRead)).BlockTableRecord)
                { Rotation = .47, ScaleFactors = new Scale3d(2, -3, 1), LayerId = db.Clayer };
                using var inner = new BlockReference(Point3d.Origin, outer.BlockTableRecord)
                { Rotation = .91, ScaleFactors = new Scale3d(-4, 5, 1), LayerId = db.Clayer };
                var nested = SymbolOrientation.Root.Nested(outer, tr).Nested(inner, tr).Transform;
                var originalAxes = SymbolTransform.From(outer.BlockTransform * inner.BlockTransform);
                if (nested.XAxis.X >= 0 || nested.YAxis.Y >= 0 || Math.Abs(nested.XAxis.Y) > 1e-7
                    || Math.Abs(nested.XAxis.Length - originalAxes.XAxis.Length) > 1e-7
                    || Math.Abs(nested.YAxis.Length - originalAxes.YAxis.Length) > 1e-7
                    || Math.Abs(nested.XAxis.DotProduct(nested.YAxis) - originalAxes.XAxis.DotProduct(originalAxes.YAxis)) > 1e-7)
                    throw new InvalidOperationException("Geneste spiegeling of schaal verandert.");
                var captured = LegendRowSamples.Capture(db, tr, new[] { sample.ReferenceId }, symbolTransform: sample.LegendTransform);
                var original = LegendRowSamples.Capture(db, tr, new[] { sample.ReferenceId });
                var blocks = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                Vector3d Direction(string name) => Lines(tr, ((BlockTableRecord)tr.GetObject(blocks[name], OpenMode.ForRead)).Cast<ObjectId>(), Matrix3d.Identity).OrderByDescending(v => v.Length).First().GetNormal();
                if (!Direction(captured).IsEqualTo(Vector3d.XAxis, new Tolerance(1e-7, 1e-7)) || Direction(original).IsEqualTo(Vector3d.XAxis, new Tolerance(1e-7, 1e-7)))
                    throw new InvalidOperationException("Gekoppeld en vast voorbeeld krijgen dezelfde stand.");
                LegendContinuity.SetChoice(settings, entry, ChoiceScope.Variant, excluded: true);
                if (DrawingAnalyzer.Analyze(db, tr, settings).Entries.Any(e => e.Element == "HOST_GEWOON"))
                    throw new InvalidOperationException("Uitsluiting niet behouden.");
                tr.Abort();
            }
            if (!hash.SequenceEqual(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(source)))
                || !scaledHash.SequenceEqual(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(scaledSource)))) throw new InvalidOperationException("Bron-xref gewijzigd.");
            doc.Editor.WriteMessage("\nSYMBOLORIENTATIONTEST gewone stand, spiegeling, doorsneden, xrefs, herhalen en keuzes -> OK");
        }, message => doc.Editor.WriteMessage(message), nameof(SymbolOrientationTest));
    }
}
