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
    [CommandMethod("NLCSLEGENDACOLORCOMPOSITIONTEST", CommandFlags.Modal)]
    public void CompositionRegression()
    {
        var doc = AcApp.DocumentManager.MdiActiveDocument;
        DiagnosticRun.Execute(() =>
        {
            const string layerName = "N-WE-VH-RIJWIELPAD-V-200";
            var file = Path.Join(Path.GetDirectoryName(doc.Database.Filename)!, "composition-source.dwg");
            using (var source = new Database(true, true))
            {
                using var tr = source.TransactionManager.StartTransaction();
                var layerId = ReviewLayer(source, tr, layerName);
                var model = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(source), OpenMode.ForWrite);
                for (int i = 0; i < 2; i++)
                {
                    using var boundary = new Polyline();
                    boundary.AddVertexAt(0, new Point2d(i * 20.0, 0), 0, 0, 0);
                    boundary.AddVertexAt(1, new Point2d(i * 20.0 + 10, 0), 0, 0, 0);
                    boundary.AddVertexAt(2, new Point2d(i * 20.0 + 10, 1), 0, 0, 0);
                    boundary.AddVertexAt(3, new Point2d(i * 20.0, 1), 0, 0, 0);
                    boundary.Closed = true;
                    model.AppendEntity(boundary); tr.AddNewlyCreatedDBObject(boundary, true);
                    using var hatch = new Hatch { LayerId = layerId };
                    model.AppendEntity(hatch); tr.AddNewlyCreatedDBObject(hatch, true);
                    hatch.SetHatchPattern(HatchPatternType.PreDefined, "SOLID");
                    hatch.Color = Color.FromColorIndex(ColorMethod.ByAci, (short)(i == 0 ? 1 : 256));
                    using var loop = new ObjectIdCollection(new[] { boundary.ObjectId });
                    hatch.AppendLoop(HatchLoopTypes.Outermost, loop);
                    hatch.EvaluateHatch(true);
                }
                tr.Commit();
                source.SaveAs(file, DwgVersion.Current);
            }
            AddKlicXref(doc.Database, file, "COLOR_A", 1);
            AddKlicXref(doc.Database, file, "COLOR_B", 1);
            using (var tr = doc.Database.TransactionManager.StartTransaction())
            {
                var layers = (LayerTable)tr.GetObject(doc.Database.LayerTableId, OpenMode.ForRead);
                foreach (var (name, color) in new[] { ("COLOR_A", (short)3), ("COLOR_B", (short)5) })
                    ((LayerTableRecord)tr.GetObject(layers[name + "|" + layerName], OpenMode.ForWrite)).Color =
                        Color.FromColorIndex(ColorMethod.ByAci, color);
                tr.Commit();
            }
            using (var tr = doc.Database.TransactionManager.StartTransaction())
            {
                var s = new LegendSettings { IncludeXrefLayers = true, Scale = 500 };
                var libraryFile = Path.ChangeExtension(file, ".examples.dwg");
                using (var library = new Database(true, true))
                {
                    using var lt = library.TransactionManager.StartTransaction();
                    var blocks = (BlockTable)lt.GetObject(library.BlockTableId, OpenMode.ForWrite);
                    using var example = new BlockTableRecord { Name = "COLOR_TEMPLATE" };
                    blocks.Add(example); lt.AddNewlyCreatedDBObject(example, true);
                    using var line = new Line(Point3d.Origin, new Point3d(12, 0, 0)) { ColorIndex = 6 };
                    example.AppendEntity(line); lt.AddNewlyCreatedDBObject(line, true);
                    ExampleLibrary.WriteCatalog(library, lt, new[] {
                        new LegendExample(example.Name, "Rijwielpad", "Verharding", 500, new[] { "NIEUW|WE|VH|RIJWIELPAD" }) });
                    lt.Commit(); library.SaveAs(libraryFile, DwgVersion.Current);
                }
                s.ExampleLibraryPath = libraryFile;
                var analysis = DrawingAnalyzer.Analyze(doc.Database, tr, s, trackSources: true);
                var entries = analysis.Entries.Where(e => e.Element == "RIJWIELPAD").ToArray();
                if (entries.Length != 3 || entries.Select(LegendSettings.SelectionKey).Distinct().Count() != 3)
                    throw new InvalidOperationException("Kleurvarianten niet afzonderlijk beschikbaar.");
                if (Math.Abs(entries.Sum(e => e.Metric.Area) - 40) > 0.001)
                    throw new InvalidOperationException("Oppervlakken niet behouden.");
                if (entries.Any(e => analysis.EntryObjects[LegendSettings.SelectionKey(e)].Count == 0))
                    throw new InvalidOperationException("Aanklikbare bronobjecten ontbreken.");
                var id = LegendBuilder.BuildBlock(doc.Database, tr, analysis, s, out var count, out var issues);
                if (count != 3 || issues.Count > 0) throw new InvalidOperationException("Legenda niet opgebouwd.");
                var drawn = ((BlockTableRecord)tr.GetObject(id, OpenMode.ForRead)).Cast<ObjectId>()
                    .Select(e => tr.GetObject(e, OpenMode.ForRead)).OfType<Entity>().ToArray();
                var fills = drawn.OfType<Hatch>().Where(h => h.Layer.StartsWith(layerName)).ToArray();
                if (!fills.Select(h => h.Color.ColorIndex).Order().SequenceEqual(new short[] { 1, 3, 5 }))
                    throw new InvalidOperationException("Handmatige kleur of xref-laagkleur verloren.");
                if (fills.Any(h => Math.Abs(h.GeometricExtents.MaxPoint.X - h.GeometricExtents.MinPoint.X - 12) > 0.001
                    || Math.Abs(h.GeometricExtents.MaxPoint.Y - h.GeometricExtents.MinPoint.Y - 2.5) > 0.001))
                    throw new InvalidOperationException("TAUW-maten op 1:500 wijken af.");
                var heading = drawn.OfType<MText>().Single(t => t.Text == "OPMERKINGEN");
                if (!heading.Color.IsByLayer) throw new InvalidOperationException("Opmerkingenkop volgt de laagkleur niet.");
                var headingLayer = (LayerTableRecord)tr.GetObject(heading.LayerId, OpenMode.ForWrite);
                if (headingLayer.Color.ColorIndex != 130) throw new InvalidOperationException("Templatekleur van de opmerkingenkop wijkt af.");
                headingLayer.Color = Color.FromColorIndex(ColorMethod.ByAci, 6);
                var rebuilt = LegendBuilder.BuildBlock(doc.Database, tr, analysis, s, out _, out _);
                var rebuiltHeading = ((BlockTableRecord)tr.GetObject(rebuilt, OpenMode.ForRead)).Cast<ObjectId>()
                    .Select(e => tr.GetObject(e, OpenMode.ForRead)).OfType<MText>().Single(t => t.Text == "OPMERKINGEN");
                if (!rebuiltHeading.Color.IsByLayer || rebuiltHeading.LayerId != heading.LayerId || headingLayer.Color.ColorIndex != 6)
                    throw new InvalidOperationException("Eigen laagkleur verloren bij bijwerken.");
                var group = "NLCS-Legenda-composition";
                var model = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(doc.Database), OpenMode.ForWrite);
                using var br = new BlockReference(Point3d.Origin, id);
                model.AppendEntity(br); tr.AddNewlyCreatedDBObject(br, true);
                FinalizePlacement(tr, doc.Database, br, s, group);
                var definition = new LegendDefinition { Name = "Kleurproef", GroupName = group, Settings = s };
                using var viewport = new Viewport { Width = 160, Height = 220, CenterPoint = new Point3d(100, 120, 0) };
                var papers = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockPaperSpaceId(doc.Database), OpenMode.ForWrite);
                papers.AppendEntity(viewport); tr.AddNewlyCreatedDBObject(viewport, true);
                LegendViewport.Link(viewport, tr, definition.Id, automatic: false);
                LegendViewport.Sync(doc.Database, tr, definition);
                if (Math.Abs(viewport.CustomScale - 2) > 1e-8 || !viewport.Locked)
                    throw new InvalidOperationException("Viewport niet op 1:500 vergrendeld.");
                viewport.Width = 150;
                if (!LegendViewport.Align(viewport, s, new Extents3d(Point3d.Origin, new Point3d(73, 20, 0)), false)
                    || Math.Abs(viewport.ViewCenter.X - 36.5) > 1e-8)
                    throw new InvalidOperationException("Marge snijdt een passend voorbeeld af.");
                viewport.Width = 160;
                definition.Settings.Scale = 1000;
                LegendViewport.Sync(doc.Database, tr, definition);
                if (Math.Abs(viewport.CustomScale - 1) > 1e-8 || Math.Abs(viewport.Width - 160) > 1e-8)
                    throw new InvalidOperationException("Viewport volgt schaalwijziging niet.");
                s.ExcludedEntries.Add(LegendSettings.SelectionKey(entries[0]));
                if (DrawingAnalyzer.Analyze(doc.Database, tr, s, excludedIds: new[] { br.ObjectId }).Entries.Count != 2)
                    throw new InvalidOperationException("Uitsluiten van één kleur raakt andere varianten.");
                tr.Abort();
            }
            doc.Editor.WriteMessage("\nCOLORCOMPOSITION kleuren 1/3/5, kleurselectie, TAUW 24x5 mm, laagkleur en gekoppelde schaal -> OK");
        }, text => doc.Editor.WriteMessage(text), "CompositionRegression");
    }
}
