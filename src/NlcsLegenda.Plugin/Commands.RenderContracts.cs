using Autodesk.AutoCAD.Colors;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using NlcsLegenda.Core;
using AcAp = Autodesk.AutoCAD.ApplicationServices.Application;
using Color = Autodesk.AutoCAD.Colors.Color;

namespace NlcsLegenda.Plugin;

public partial class Commands
{
    [CommandMethod("NLCSLEGENDAPROPERTYTEST", CommandFlags.Modal)]
    public void NlcsLegendaPropertyTest()
    {
        var doc = AcAp.DocumentManager.MdiActiveDocument;
        var db = doc.Database;
        var ed = doc.Editor;
        const string layerName = "N-WE-VH-PROPERTY-G";
        try
        {
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var layers = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForWrite);
                var layer = new LayerTableRecord { Name = layerName, Color = Color.FromRgb(30, 80, 160), LineWeight = LineWeight.LineWeight025 };
                var layerId = layers.Add(layer); tr.AddNewlyCreatedDBObject(layer, true);
                var linetypes = (LinetypeTable)tr.GetObject(db.LinetypeTableId, OpenMode.ForWrite);
                var dash = new LinetypeTableRecord { Name = "PROPERTY_DASH", PatternLength = 1, NumDashes = 2 };
                dash.SetDashLengthAt(0, 0.6); dash.SetDashLengthAt(1, -0.4);
                var dashId = linetypes.Add(dash); tr.AddNewlyCreatedDBObject(dash, true);
                var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForWrite);
                // 2+2 identiek; overige verschillen uitsluitend in transparantie, lijndikte,
                // lijntype en lijntypeschaal. De vaste verwachte lengtes zijn 4,3,4,5,6.
                double[] lengths = { 2, 2, 3, 4, 5, 6 };
                for (int i = 0; i < lengths.Length; i++)
                {
                    using var line = new Line(new Point3d(0, i, 0), new Point3d(lengths[i], i, 0)) { LayerId = layerId };
                    if (i == 1) line.Color = Color.FromRgb(30, 80, 160);
                    if (i == 2) line.Transparency = new Transparency((byte)100);
                    if (i == 3) line.LineWeight = LineWeight.LineWeight100;
                    if (i >= 4) line.LinetypeId = dashId;
                    if (i == 5) line.LinetypeScale = 2;
                    ms.AppendEntity(line); tr.AddNewlyCreatedDBObject(line, true);
                }
                var inherit = new LayerTableRecord { Name = "N-WE-VH-INHERIT-G" };
                var inheritId = layers.Add(inherit); tr.AddNewlyCreatedDBObject(inherit, true);
                var blocks = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForWrite);
                var block = new BlockTableRecord { Name = "PROPERTY_BYBLOCK" };
                var blockId = blocks.Add(block); tr.AddNewlyCreatedDBObject(block, true);
                using var child = new Line(Point3d.Origin, new Point3d(7, 0, 0))
                { LayerId = inheritId, ColorIndex = 0, LinetypeId = db.ByBlockLinetype, LineWeight = LineWeight.ByBlock };
                block.AppendEntity(child); tr.AddNewlyCreatedDBObject(child, true);
                using var parent = new BlockReference(new Point3d(0, 20, 0), blockId)
                { Color = Color.FromRgb(120, 80, 20), LinetypeId = dashId, LineWeight = LineWeight.LineWeight050 };
                ms.AppendEntity(parent); tr.AddNewlyCreatedDBObject(parent, true);
                using var explicitLine = new Line(new Point3d(0, 22, 0), new Point3d(8, 22, 0))
                { LayerId = inheritId, Color = parent.Color, LinetypeId = dashId, LineWeight = LineWeight.LineWeight050 };
                ms.AppendEntity(explicitLine); tr.AddNewlyCreatedDBObject(explicitLine, true);
                tr.Commit();
            }
            var settings = new LegendSettings { IncludeQuantities = true, ExplodeOnPlace = false };
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var result = DrawingAnalyzer.Analyze(db, tr, settings);
                var rows = result.Entries.Where(e => e.Element == "PROPERTY").ToList();
                if (rows.Count != 5 || !rows.Select(e => e.Metric.Length).OrderBy(x => x).SequenceEqual(new[] { 3d,4,4,5,6 }))
                    throw new InvalidOperationException("Stijlpartities of hoeveelheden onjuist.");
                var inherited = result.Entries.Single(e => e.Element == "INHERIT");
                if (Math.Abs(inherited.Metric.Length - 15) > 1e-8) throw new InvalidOperationException("ByBlock niet gelijk aan expliciete stijl.");
                tr.Commit();
            }
            int rowsBuilt = BuildPlaceXrefLegend(db, settings, out var error, out var group, out var blockIdBuilt);
            if (rowsBuilt != 6 || error != "geen") throw new InvalidOperationException(error);
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var block = (BlockTableRecord)tr.GetObject(blockIdBuilt, OpenMode.ForRead);
                var swatches = block.Cast<ObjectId>().Select(id => tr.GetObject(id, OpenMode.ForRead)).OfType<Polyline>()
                    .Where(e => e.Layer.StartsWith(layerName, StringComparison.Ordinal)).ToList();
                if (swatches.Count != 5 || swatches.Any(e => e.Color.ColorValue.ToArgb() != Color.FromRgb(30,80,160).ColorValue.ToArgb())
                    || swatches.Count(e => e.Transparency.IsByAlpha && e.Transparency.Alpha == 100) != 1
                    || swatches.Count(e => e.LineWeight == LineWeight.LineWeight100) != 1
                    || swatches.Count(e => e.Linetype == "PROPERTY_DASH") != 2
                    || swatches.Count(e => Math.Abs(e.LinetypeScale - 2) < 1e-8) != 1)
                    throw new InvalidOperationException("Effectieve eigenschappen van echte swatches onjuist.");
                AssertLegendHostOwnership(db, tr, group, blockIdBuilt);
                tr.Commit();
            }
            ed.WriteMessage("\nPROPERTY truecolor/ByLayer/ByBlock/lijndikte/transparantie/lijntype/schaal en echte swatches -> OK");
            var before = ReadPlacementFingerprint(db);
            bool failed = false;
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var registry = LegendStore.Load(db, tr);
                var definition = registry.Legends.Single();
                definition.Settings.ManualEntries.Add(new ManualEntry
                { Layer = "N-WE-VH-FAIL-A", Type = NlcsDrawType.Arcering, Description = "Foutfixture", HatchPattern = "DOES_NOT_EXIST_AUDIT" });
                try { BuildManagedLegend(db, tr, registry, definition, out _, out _); }
                catch (InvalidOperationException) { failed = true; }
                tr.Abort();
            }
            if (!failed || ReadPlacementFingerprint(db) != before)
                throw new InvalidOperationException("Mislukte update laat gedeeltelijke wijzigingen achter.");
            ed.WriteMessage("\nPROPERTY mislukte tekenregel: vorige legenda en registratie behouden -> OK");
        }
        catch (System.Exception ex) { ed.WriteMessage($"\nPROPERTY FAIL: {ex}"); }
    }
    [CommandMethod("NLCSLEGENDASYMBOLEDGETEST", CommandFlags.Modal)]
    public void NlcsLegendaSymbolEdgeTest()
    {
        var doc = AcAp.DocumentManager.MdiActiveDocument;
        var db = doc.Database;
        try
        {
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var layers = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForWrite);
                var layer = new LayerTableRecord { Name = "N-WE-VH-SYMBOL_EDGE-S" };
                var layerId = layers.Add(layer); tr.AddNewlyCreatedDBObject(layer, true);
                var blocks = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForWrite);
                var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForWrite);
                ((LayerTableRecord)tr.GetObject(layers["0"], OpenMode.ForWrite)).IsOff = true;
                for (int i = 0; i < 5; i++)
                {
                    var block = new BlockTableRecord { Name = "EDGE_" + i };
                    var id = blocks.Add(block); tr.AddNewlyCreatedDBObject(block, true);
                    if (i == 1 || i == 2)
                    {
                        using var line = new Line(Point3d.Origin, new Point3d(i == 1 ? 12 : 10, 0, 0)) { Visible = i == 2 };
                        block.AppendEntity(line); tr.AddNewlyCreatedDBObject(line, true);
                    }
                    if (i >= 3)
                    {
                        using var hatch = new Hatch();
                        hatch.SetHatchPattern(HatchPatternType.PreDefined, "SOLID");
                        block.AppendEntity(hatch); tr.AddNewlyCreatedDBObject(hatch, true);
                        // Gelijke bounding box en patroon, maar verschillende contouren.
                        var points = new Point2dCollection { new(0, 0), new(5, 0), new(i == 3 ? 0 : 5, 5) };
                        hatch.AppendLoop(HatchLoopTypes.External | HatchLoopTypes.Polyline, points, new DoubleCollection { 0, 0, 0 });
                        hatch.EvaluateHatch(true);
                    }
                    using var reference = new BlockReference(new Point3d(i * 20d, 0, 0), id) { LayerId = layerId, ScaleFactors = new Scale3d(0.161, 0.161, 0.16) };
                    ms.AppendEntity(reference); tr.AddNewlyCreatedDBObject(reference, true);
                }
                tr.Commit();
            }
            foreach (bool hidden in new[] { false, true })
            {
                using var tr = db.TransactionManager.StartTransaction();
                var settings = new LegendSettings { IncludeInvisibleLayers = hidden };
                var analysis = DrawingAnalyzer.Analyze(db, tr, settings);
                int expected = hidden ? 4 : 3;
                if (analysis.Entries.Count != expected || analysis.Entries.Sum(e => e.Metric.Count) != expected)
                    throw new InvalidOperationException("Lege/verborgen/vormverschillende symbolen verkeerd gegroepeerd.");
                var id = LegendBuilder.BuildBlock(db, tr, analysis, settings, out int rows, out var issues);
                if (rows != expected || issues.Count != 0) throw new InvalidOperationException("Symboolvoorbeelden ontbreken.");
                var entities = ((BlockTableRecord)tr.GetObject(id, OpenMode.ForRead)).Cast<ObjectId>()
                    .Select(e => tr.GetObject(e, OpenMode.ForRead)).OfType<Entity>().ToList();
                if (entities.OfType<Hatch>().Count(e => e.Layer.Contains("SYMBOL_EDGE")) != 2 || entities.OfType<Line>().Count(e => e.Layer.Contains("SYMBOL_EDGE")) != (hidden ? 2 : 1))
                    throw new InvalidOperationException($"Werkelijke symboolgeometrie onjuist hidden={hidden}: " + string.Join(";", entities.Select(e => e.GetType().Name + ":" + e.Layer)));
                tr.Abort();
            }
            doc.Editor.WriteMessage("\nSYMBOLEDGE leeg/verborgen/lijn/verschillende arceringscontouren -> OK");
        }
        catch (System.Exception ex) { doc.Editor.WriteMessage($"\nSYMBOLEDGE FAIL: {ex}"); }
    }

    [CommandMethod("NLCSLEGENDAPOLYOWNERSHIPTEST", CommandFlags.Modal)]
    public void NlcsLegendaPolyOwnershipTest()
    {
        var doc = AcAp.DocumentManager.MdiActiveDocument;
        var db = doc.Database;
        try
        {
            using var tr = db.TransactionManager.StartTransaction();
            var layers = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForWrite);
            var blocks = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForWrite);
            var definition = new BlockTableRecord { Name = "POLY_OWNERSHIP" };
            var defId = blocks.Add(definition); tr.AddNewlyCreatedDBObject(definition, true);
            var vertices = new List<ObjectId>();
            foreach (bool spatial in new[] { false, true })
            {
                var layer = new LayerTableRecord { Name = spatial ? "N-WE-VH-POLY3-G" : "N-WE-VH-POLY2-G" };
                var layerId = layers.Add(layer); tr.AddNewlyCreatedDBObject(layer, true);
                using Curve curve = spatial ? new Polyline3d() : new Polyline2d();
                curve.LayerId = layerId;
                definition.AppendEntity(curve); tr.AddNewlyCreatedDBObject(curve, true);
                foreach (var point in new[] { Point3d.Origin, new Point3d(3,0,0), new Point3d(3,4,0) })
                {
                    if (curve is Polyline2d p2)
                    {
                        using var vertex = new Vertex2d { Position = point };
                        vertices.Add(p2.AppendVertex(vertex)); tr.AddNewlyCreatedDBObject(vertex, true);
                    }
                    else
                    {
                        using var vertex = new PolylineVertex3d(point);
                        vertices.Add(((Polyline3d)curve).AppendVertex(vertex)); tr.AddNewlyCreatedDBObject(vertex, true);
                    }
                }
            }
            var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForWrite);
            using var reference = new BlockReference(Point3d.Origin, defId) { ScaleFactors = new Scale3d(2) };
            ms.AppendEntity(reference); tr.AddNewlyCreatedDBObject(reference, true);
            string Snapshot() => string.Join(";", vertices.Select(id => tr.GetObject(id, OpenMode.ForRead) switch
            { Vertex2d v => v.Position.ToString(), PolylineVertex3d v => v.Position.ToString(), _ => "invalid" }));
            string before = Snapshot();
            for (int i = 0; i < 3; i++)
            {
                var result = DrawingAnalyzer.Analyze(db, tr, new LegendSettings());
                var rows = result.Entries.Where(e => e.Element.StartsWith("POLY", StringComparison.Ordinal)).ToList();
                if (rows.Count != 2 || rows.Any(e => Math.Abs(e.Metric.Length - 14) > 1e-8) || Snapshot() != before)
                    throw new InvalidOperationException("Analyse verandert bronvertices of meet verkeerde lengte.");
            }
            tr.Commit();
            doc.Editor.WriteMessage("\nPOLYOWNERSHIP 2D/3D 14m en bronvertices ongewijzigd na drie analyses -> OK");
        }
        catch (System.Exception ex) { doc.Editor.WriteMessage($"\nPOLYOWNERSHIP FAIL: {ex}"); }
    }

    [CommandMethod("NLCSLEGENDAPAPERSCALETEST", CommandFlags.Modal)]
    public void NlcsLegendaPaperScaleTest()
    {
        var doc = AcAp.DocumentManager.MdiActiveDocument;
        var db = doc.Database;
        try
        {
            foreach (double scale in new[] { 100d, 200d, 500d, 1000d })
            {
                using var tr = db.TransactionManager.StartTransaction();
                var settings = new LegendSettings { Scale = scale, IncludeQuantities = true, DrawSwatchFrame = true, DrawBorder = true, ToonSymbolen = false };
                var analysis = DrawingAnalyzer.Analyze(db, tr, settings);
                var id = LegendBuilder.BuildBlock(db, tr, analysis, settings, out int rowCount, out var issues);
                var block = (BlockTableRecord)tr.GetObject(id, OpenMode.ForRead);
                var entities = block.Cast<ObjectId>().Select(e => tr.GetObject(e, OpenMode.ForRead)).OfType<Entity>().ToList();
                double paper = 1000d / scale;
                bool Near(double a, double b) => Math.Abs(a - b) < 0.03;
                var frames = entities.OfType<Polyline>().Where(e => e.Closed && e.Layer == settings.SwatchFrameLayer)
                    .Select(e => e.GeometricExtents).Where(e => Near((e.MaxPoint.X - e.MinPoint.X) * paper, 24)
                        && Near((e.MaxPoint.Y - e.MinPoint.Y) * paper, 5)).ToList();
                if (rowCount != 3 || frames.Count != 3 || issues.Count != 0)
                    throw new InvalidOperationException("Niet drie lijn-/vlakvakken van 24 x 5 mm.");
                var text = entities.OfType<MText>().ToList();
                if (!text.Any(t => t.Contents.Contains("LEGENDA") && Near(t.TextHeight * paper, 5) && t.Contents.StartsWith("\\L"))
                    || !text.Any(t => t.Contents.Contains("situatie") && Near(t.TextHeight * paper, 2.5) && t.Contents.StartsWith("\\L")))
                    throw new InvalidOperationException("Titel/statuskop wijkt af van 5/2,5 mm met onderstreping.");
                var bodies = text.Where(t => t.Attachment == AttachmentPoint.TopLeft && t.Width > 0
                    && Near(t.TextHeight * paper, 2.5) && frames.Any(f => Near((t.Location.X - f.MaxPoint.X) * paper, 5.9688))).ToList();
                if (bodies.Count != 3 || bodies.Any(t => !frames.Any(f => Math.Abs((t.Location.Y
                    - Math.Max(t.TextHeight * (1 - 0.28388973), t.ActualHeight / 2)
                    - (f.MinPoint.Y + f.MaxPoint.Y) / 2) * paper) < 0.03)))
                    throw new InvalidOperationException($"Tekstbasislijn of afstand wijkt af: bodies={bodies.Count}.");
                var border = entities.OfType<Polyline>().Where(e => e.Closed && e.Layer == settings.FrameLayer)
                    .Select(e => e.GeometricExtents).OrderByDescending(e => (e.MaxPoint.X - e.MinPoint.X) * (e.MaxPoint.Y - e.MinPoint.Y)).First();
                foreach (var entity in entities)
                {
                    var bounds = entity.GeometricExtents;
                    if (bounds.MinPoint.X < border.MinPoint.X - 1e-5 || bounds.MinPoint.Y < border.MinPoint.Y - 1e-5
                        || bounds.MaxPoint.X > border.MaxPoint.X + 1e-5 || bounds.MaxPoint.Y > border.MaxPoint.Y + 1e-5)
                        throw new InvalidOperationException("Uitvoer steekt buiten het kader.");
                }
                tr.Abort();
                doc.Editor.WriteMessage($"\nPAPERSCALE 1:{scale:0} vakken24x5/tekst2.5/kop2.5/titel5/gap5.7188/basislijn/kader -> OK");
            }
        }
        catch (System.Exception ex) { doc.Editor.WriteMessage($"\nPAPERSCALE FAIL: {ex}"); }
    }
}
