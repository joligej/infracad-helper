using System.IO;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using NlcsLegenda.Core;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Core.Application;

namespace NlcsLegenda.Plugin;

public partial class Commands
{
    [CommandMethod("NLCSLEGENDAREALSIZETEST", CommandFlags.Modal)]
    public void NlcsLegendaRealSizeTest()
    {
        var doc = AcApp.DocumentManager.MdiActiveDocument;
        var db = doc.Database;
        var path = Path.Combine(Path.GetDirectoryName(db.Filename)!, "real-size-source.dwg");
        try
        {
            using (var source = new Database(true, true))
            {
                source.Insunits = UnitsValue.Meters;
                using var tr = source.TransactionManager.StartTransaction();
                var bt = (BlockTable)tr.GetObject(source.BlockTableId, OpenMode.ForWrite);
                var def = new BlockTableRecord { Name = "REAL_SIZE_RECTANGLE" };
                var defId = bt.Add(def); tr.AddNewlyCreatedDBObject(def, true);
                var rect = new Polyline();
                foreach (var p in new[] { new Point2d(0, 0), new Point2d(12, 0), new Point2d(12, 6), new Point2d(0, 6) })
                    rect.AddVertexAt(rect.NumberOfVertices, p, 0, 0, 0);
                rect.Closed = true; def.AppendEntity(rect); tr.AddNewlyCreatedDBObject(rect, true);
                var layers = (LayerTable)tr.GetObject(source.LayerTableId, OpenMode.ForWrite);
                var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(source), OpenMode.ForWrite);
                foreach (string suffix in new[] { "", "-200" })
                {
                    var layer = new LayerTableRecord { Name = "N-WE-IE-GROOT-S" + suffix };
                    var lid = layers.Add(layer); tr.AddNewlyCreatedDBObject(layer, true);
                    var br = new BlockReference(new Point3d(suffix.Length * 50, 20, 0), defId)
                    { LayerId = lid, ScaleFactors = new Scale3d(2, 3, 1) };
                    ms.AppendEntity(br); tr.AddNewlyCreatedDBObject(br, true);
                }
                tr.Commit();
                source.SaveAs(path, DwgVersion.Current);
            }
            var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path)));
            var xref = db.AttachXref(path, "REAL_SIZE_SOURCE");
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForWrite);
                var br = new BlockReference(Point3d.Origin, xref); ms.AppendEntity(br); tr.AddNewlyCreatedDBObject(br, true);
                tr.Commit();
            }
            foreach (double scale in new[] { 200d, 500d, 1000d })
            {
                using var tr = db.TransactionManager.StartTransaction();
                var settings = new LegendSettings { Scale = scale, IncludeXrefLayers = true, IncludeRemarks = false,
                    IncludeFooter = false, IncludeScaleBar = false };
                var analysis = DrawingAnalyzer.Analyze(db, tr, settings);
                // Op 1:200 zijn beide inserts gelijk. Op andere schalen verschilt alleen de expliciete variant.
                int expectedRows = scale == 200 ? 1 : 2;
                if (analysis.Entries.Count != expectedRows) throw new InvalidOperationException($"Verkeerd samengevoegd op 1:{scale}.");
                var id = LegendBuilder.BuildBlock(db, tr, analysis, settings, out int rows, out var issues);
                var entities = ((BlockTableRecord)tr.GetObject(id, OpenMode.ForRead)).Cast<ObjectId>()
                    .Select(i => tr.GetObject(i, OpenMode.ForRead)).OfType<Entity>().ToList();
                var symbols = entities.Where(p => p.Layer.Contains("GROOT", StringComparison.Ordinal)).GroupBy(e => e.Layer)
                    .Select(group => { var bounds = new Extents3d(); foreach (var e in group) bounds.AddExtents(e.GeometricExtents); return bounds; }).ToArray();
                if (symbols.Length != expectedRows || rows != expectedRows || issues.Count != 0)
                    throw new InvalidOperationException("Symbool ontbreekt.");
                var widths = symbols.Select(p => p.MaxPoint.X - p.MinPoint.X).Order().ToArray();
                var heights = symbols.Select(p => p.MaxPoint.Y - p.MinPoint.Y).Order().ToArray();
                var expected = scale == 200 ? new[] { 1d } : new[] { 1d, scale / 200 };
                for (int i = 0; i < expectedRows; i++)
                    if (Math.Abs(widths[i] - 24 * expected[i]) > 1e-7 || Math.Abs(heights[i] - 18 * expected[i]) > 1e-7)
                        throw new InvalidOperationException("Broninsert vervormd, dubbel geschaald of verkleind.");
                var texts = entities.OfType<MText>().Where(t => !t.Contents.StartsWith("\\L", StringComparison.Ordinal)).ToArray();
                foreach (var symbol in symbols)
                {
                    var b = symbol;
                    if (texts.Any(t => t.GeometricExtents.MinPoint.X < b.MaxPoint.X))
                        throw new InvalidOperationException("Symbool overlapt tekstkolom.");
                }
                var ordered = symbols.OrderByDescending(b => b.MaxPoint.Y).ToArray();
                for (int i = 1; i < ordered.Length; i++)
                    if (ordered[i].MaxPoint.Y >= ordered[i - 1].MinPoint.Y)
                        throw new InvalidOperationException("Symboolrijen overlappen.");
                tr.Abort();
            }
            if (hash != Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path))))
                throw new InvalidOperationException("Bron gewijzigd.");
            var placementSettings = new LegendSettings { IncludeXrefLayers = true, Scale = 500 };
            if (BuildPlaceXrefLegend(db, placementSettings, out var error, out _, out _) != 2 || error != "geen")
                throw new InvalidOperationException("Plaatsing groot symbool mislukt: " + error);
            doc.Editor.WriteMessage("\nREALSIZE grote xref-symbolen: modelmaat, schaalvariant, samenvoeging en vrije tekstruimte -> OK");
        }
        catch (System.Exception ex) { doc.Editor.WriteMessage($"\nREALSIZE FAIL: {ex}"); }
    }
}
