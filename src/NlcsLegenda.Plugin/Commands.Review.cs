using Autodesk.AutoCAD.Colors;
using Color = Autodesk.AutoCAD.Colors.Color;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using NlcsLegenda.Core;
using AcAp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace NlcsLegenda.Plugin;

public partial class Commands
{
    [CommandMethod("NLCSLEGENDAREVIEWTEST", CommandFlags.Modal)]
    public void NlcsLegendaReviewTest()
    {
        var doc = AcAp.DocumentManager.MdiActiveDocument;
        int failures = 0;
        void Check(string name, Action<Database, Transaction> test)
        {
            try
            {
                using var tr = doc.Database.TransactionManager.StartTransaction();
                test(doc.Database, tr);
                tr.Abort();
                doc.Editor.WriteMessage($"\nREVIEW {name} -> OK");
            }
            catch (System.Exception ex)
            {
                failures++;
                doc.Editor.WriteMessage($"\nREVIEW {name} -> FAIL: {ex}");
            }
        }
        Check("geldige en ongeldige laagnamen", ReviewLayerNames);
        Check("eigen laag met lijn en symbool", ReviewMixedCustomLayer);
        Check("eigen laag met twee blokfilters", ReviewCustomBlockFilters);
        Check("verschillende 3D-polylijnen", ReviewSymbolPolylineShapes);
        Check("onzichtbare symboolonderdelen", ReviewHiddenSymbolParts);
        Check("laag nul en ByBlock", ReviewSymbolInheritance);
        Check("gebruikersarcering", ReviewUserHatches);
        Check("arcering met boogcontour", ReviewCurvedHatch);
        Check("getransformeerde arcering", (db, tr) => ReviewTransformedHatch(db, tr));
        Check("vlakke arcering met afwijkende Z-schaal", (db, tr) => ReviewTransformedHatch(db, tr, planar: true));
        Check("ongelijk geschaald arceringspatroon", (db, tr) =>
        {
            try { ReviewTransformedHatch(db, tr, stretched: true); }
            catch (InvalidOperationException ex) when (ex.Message.Contains("ongelijk geschaald in het bronvlak", StringComparison.Ordinal)) { return; }
            throw new InvalidOperationException("Een ongelijk geschaald patroon wordt zonder melding benaderd.");
        });
        Check("ongebruikte blokkleur", ReviewUnusedBlockColor);
        Check("lege selectie", ReviewEmptySelection);
        Check("lijncontour met arcering", ReviewAreaOutline);
        Check("omschrijving van samengevoegde lagen", ReviewMergedDescription);
        doc.Editor.WriteMessage($"\nREVIEW totaal: {(failures == 0 ? "OK" : "FAIL")} ({failures} fouten)");
    }

    private static void ReviewLayerNames(Database db, Transaction tr)
    {
        foreach (var name in new[] { "N03-WE-VH-SUBSTATUS-G", "N-RI-RIO-INSPECTIE-G", "N-WE-VH-ONGELDIG-G-99999999999999999999" })
        {
            var layer = ReviewLayer(db, tr, name);
            using var line = new Line(Point3d.Origin, new Point3d(7, 0, 0)) { LayerId = layer };
            ReviewAppend(ReviewModel(db, tr), tr, line);
        }
        var entries = DrawingAnalyzer.Analyze(db, tr, new LegendSettings()).Entries;
        foreach (var element in new[] { "SUBSTATUS", "INSPECTIE" })
            if (Math.Abs(entries.Single(e => e.Element == element).Metric.Length - 7) > 1e-8)
                throw new InvalidOperationException("Een geldige laagnaam wordt niet volledig geanalyseerd.");
        if (entries.Any(e => e.Element == "ONGELDIG"))
            throw new InvalidOperationException("Een ongeldige schaal wordt als bron gebruikt.");
    }

    private static ObjectId ReviewLayer(Database db, Transaction tr, string name, short color = 7)
    {
        var table = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForWrite);
        var layer = new LayerTableRecord { Name = name, Color = Color.FromColorIndex(ColorMethod.ByAci, color) };
        var id = table.Add(layer);
        tr.AddNewlyCreatedDBObject(layer, true);
        return id;
    }

    private static BlockTableRecord ReviewBlock(Database db, Transaction tr, string name)
    {
        var table = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForWrite);
        var block = new BlockTableRecord { Name = name };
        table.Add(block);
        tr.AddNewlyCreatedDBObject(block, true);
        return block;
    }

    private static void ReviewAppend(BlockTableRecord owner, Transaction tr, Entity entity)
    {
        owner.AppendEntity(entity);
        tr.AddNewlyCreatedDBObject(entity, true);
    }

    private static BlockTableRecord ReviewModel(Database db, Transaction tr) =>
        (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForWrite);

    private static void ReviewMixedCustomLayer(Database db, Transaction tr)
    {
        var layer = ReviewLayer(db, tr, "MIXED_REVIEW");
        var block = ReviewBlock(db, tr, "REVIEW_MIXED_SYMBOL");
        using var shape = new Circle(Point3d.Origin, Vector3d.ZAxis, 1);
        ReviewAppend(block, tr, shape);
        using var reference = new BlockReference(Point3d.Origin, block.ObjectId) { LayerId = layer };
        using var line = new Line(Point3d.Origin, new Point3d(9, 0, 0)) { LayerId = layer };
        ReviewAppend(ReviewModel(db, tr), tr, reference);
        ReviewAppend(ReviewModel(db, tr), tr, line);
        var settings = new LegendSettings();
        foreach (var type in new[] { NlcsDrawType.Geometrie, NlcsDrawType.Symbool })
            settings.CustomLayerRules.Add(new CustomLayerRule { Layer = "MIXED_REVIEW", Element = "MIXED", Description = "Gemengde bron", Type = type });
        var result = DrawingAnalyzer.Analyze(db, tr, settings);
        if (result.UsedCustomLayerCount != 1) throw new InvalidOperationException("Een eigen bronlaag wordt meerdere keren als laag geteld.");
        var rows = result.Entries.Where(e => e.Element == "MIXED").ToList();
        if (rows.Count != 1 || rows[0].GeometryLayer is null || rows[0].SymbolLayer is null || Math.Abs(rows[0].Metric.Length - 9) > 1e-8 || rows[0].Metric.Count != 2)
            throw new InvalidOperationException("Lijn of symbool is uit de gecombineerde regel verdwenen.");
        LegendBuilder.BuildBlock(db, tr, result, settings, out _, out _);
    }

    private static void ReviewCustomBlockFilters(Database db, Transaction tr)
    {
        var layer = ReviewLayer(db, tr, "FILTER_REVIEW");
        var settings = new LegendSettings();
        for (int i = 0; i < 2; i++)
        {
            var block = ReviewBlock(db, tr, "REVIEW_FILTER_" + i);
            using var line = new Line(Point3d.Origin, new Point3d(i + 1, 0, 0));
            ReviewAppend(block, tr, line);
            using var reference = new BlockReference(new Point3d(i * 5d, 0, 0), block.ObjectId) { LayerId = layer };
            ReviewAppend(ReviewModel(db, tr), tr, reference);
            settings.CustomLayerRules.Add(new CustomLayerRule { Layer = "FILTER_REVIEW", Element = "FILTER" + i, Description = "Symbool " + i, Type = NlcsDrawType.Symbool, BlockName = block.Name });
        }
        var rows = DrawingAnalyzer.Analyze(db, tr, settings).Entries.Where(e => e.Element.StartsWith("FILTER", StringComparison.Ordinal)).ToList();
        if (rows.Count != 2 || rows.Any(e => e.Metric.Count != 1))
            throw new InvalidOperationException($"Twee afzonderlijke blokfilters leveren {rows.Count} regels op.");
    }

    private static void ReviewSymbolPolylineShapes(Database db, Transaction tr)
    {
        var layer = ReviewLayer(db, tr, "N-WE-VH-REVIEWPOLY-S");
        for (int i = 0; i < 2; i++)
        {
            var block = ReviewBlock(db, tr, "REVIEW_POLY_" + i);
            using var polyline = new Polyline3d();
            ReviewAppend(block, tr, polyline);
            foreach (var point in new[] { new Point3d(0, i * 4d, 0), new Point3d(3, (1 - i) * 4d, 0), new Point3d(6, i * 4d, 0) })
            {
                using var vertex = new PolylineVertex3d(point);
                polyline.AppendVertex(vertex);
                tr.AddNewlyCreatedDBObject(vertex, true);
            }
            using var reference = new BlockReference(new Point3d(i * 10d, 0, 0), block.ObjectId) { LayerId = layer };
            ReviewAppend(ReviewModel(db, tr), tr, reference);
        }
        var rows = DrawingAnalyzer.Analyze(db, tr, new LegendSettings()).Entries.Where(e => e.Element == "REVIEWPOLY").ToList();
        if (rows.Count != 2) throw new InvalidOperationException("Verschillende vormen met gelijke lengte en extents worden samengevoegd.");
    }

    private static void ReviewHiddenSymbolParts(Database db, Transaction tr)
    {
        var layer = ReviewLayer(db, tr, "N-WE-VH-REVIEWHIDDEN-S");
        for (int i = 0; i < 2; i++)
        {
            var block = ReviewBlock(db, tr, "REVIEW_HIDDEN_" + i);
            using var visible = new Line(Point3d.Origin, new Point3d(2, 0, 0));
            using var hidden = new Line(Point3d.Origin, new Point3d(i + 3, 1, 0)) { Visible = false };
            ReviewAppend(block, tr, visible);
            ReviewAppend(block, tr, hidden);
            using var reference = new BlockReference(new Point3d(i * 10d, 0, 0), block.ObjectId) { LayerId = layer };
            ReviewAppend(ReviewModel(db, tr), tr, reference);
        }
        foreach (bool hidden in new[] { false, true })
        {
            var rows = DrawingAnalyzer.Analyze(db, tr, new LegendSettings { IncludeInvisibleLayers = hidden }).Entries.Where(e => e.Element == "REVIEWHIDDEN").ToList();
            int expected = hidden ? 2 : 1;
            if (rows.Count != expected) throw new InvalidOperationException($"Onzichtbaar={hidden}: {rows.Count} regels, verwacht {expected}.");
        }
    }

    private static void ReviewSymbolInheritance(Database db, Transaction tr)
    {
        var layer = ReviewLayer(db, tr, "N-WE-VH-REVIEWINHERIT-S", 3);
        var block = ReviewBlock(db, tr, "REVIEW_INHERIT");
        using var byLayer = new Line(Point3d.Origin, new Point3d(3, 0, 0)) { ColorIndex = 256 };
        using var byBlock = new Line(new Point3d(0, 1, 0), new Point3d(3, 1, 0)) { ColorIndex = 0 };
        ReviewAppend(block, tr, byLayer);
        ReviewAppend(block, tr, byBlock);
        using var reference = new BlockReference(Point3d.Origin, block.ObjectId) { LayerId = layer, ColorIndex = 1 };
        ReviewAppend(ReviewModel(db, tr), tr, reference);
        var settings = new LegendSettings();
        var result = DrawingAnalyzer.Analyze(db, tr, settings);
        var id = LegendBuilder.BuildBlock(db, tr, result, settings, out _, out _);
        var rendered = ((BlockTableRecord)tr.GetObject(id, OpenMode.ForRead)).Cast<ObjectId>().Select(x => tr.GetObject(x, OpenMode.ForRead)).OfType<Line>()
            .Where(e => e.Layer.Contains("REVIEWINHERIT", StringComparison.Ordinal)).ToList();
        if (rendered.Count != 2 || rendered.Count(e => e.ColorIndex == 1) != 1 || rendered.Count(e => e.ColorIndex == 3) != 1)
            throw new InvalidOperationException("ByLayer op laag 0 moet groen blijven; alleen ByBlock neemt rood over.");
    }

    private static void ReviewCurvedHatch(Database db, Transaction tr)
    {
        var layer = ReviewLayer(db, tr, "N-WE-VH-REVIEWCURVED-A");
        using var hatch = new Hatch { LayerId = layer };
        ReviewAppend(ReviewModel(db, tr), tr, hatch);
        hatch.SetHatchPattern(HatchPatternType.PreDefined, "SOLID");
        hatch.AppendLoop(HatchLoopTypes.Polyline, new Point2dCollection { new(-2, 0), new(2, 0) }, new DoubleCollection { 1, 0 });
        hatch.EvaluateHatch(true);
        var entry = DrawingAnalyzer.Analyze(db, tr, new LegendSettings()).Entries.Single(e => e.Element == "REVIEWCURVED");
        if (Math.Abs(entry.Metric.Area - 2 * Math.PI) > 1e-8)
            throw new InvalidOperationException($"Halve cirkel: {entry.Metric.Area:R} m2, verwacht {2 * Math.PI:R}.");
    }

    private static void ReviewUserHatches(Database db, Transaction tr)
    {
        var layer = ReviewLayer(db, tr, "N-WE-VH-REVIEWUSERHATCH-A");
        for (int i = 0; i < 3; i++)
        {
            using var hatch = new Hatch { LayerId = layer };
            ReviewAppend(ReviewModel(db, tr), tr, hatch);
            hatch.PatternSpace = i == 1 ? 2 : 1;
            hatch.PatternDouble = i == 2;
            hatch.SetHatchPattern(HatchPatternType.UserDefined, "_USER");
            hatch.AppendLoop(HatchLoopTypes.External | HatchLoopTypes.Polyline,
                new Point2dCollection { new(0, 0), new(5, 0), new(5, 5), new(0, 5) }, new DoubleCollection { 0, 0, 0, 0 });
            hatch.EvaluateHatch(true);
        }
        var settings = new LegendSettings { HatchScaleFactor = 1 };
        var result = DrawingAnalyzer.Analyze(db, tr, settings);
        if (result.UsedNlcsLayerCount != 1) throw new InvalidOperationException("Een NLCS-bronlaag wordt meerdere keren als laag geteld.");
        if (result.Entries.Count(e => e.Element == "REVIEWUSERHATCH") != 3)
            throw new InvalidOperationException("Afstand en kruispatroon van gebruikersarcering ontbreken in de groepering.");
        var id = LegendBuilder.BuildBlock(db, tr, result, settings, out _, out _);
        var rendered = ((BlockTableRecord)tr.GetObject(id, OpenMode.ForRead)).Cast<ObjectId>().Select(x => tr.GetObject(x, OpenMode.ForRead)).OfType<Hatch>()
            .Where(e => e.Layer.Contains("REVIEWUSERHATCH", StringComparison.Ordinal)).ToList();
        if (rendered.Count != 3 || rendered.Count(e => e.PatternDouble) != 1 || rendered.Count(e => Math.Abs(e.PatternSpace - 2) < 1e-8) != 1)
            throw new InvalidOperationException("De getekende gebruikersarceringen verliezen hun afstand of kruispatroon.");
    }

    private static void ReviewTransformedHatch(Database db, Transaction tr, bool planar = false, bool stretched = false)
    {
        var layer = ReviewLayer(db, tr, "N-WE-VH-REVIEWTRANSFORM-A");
        var block = ReviewBlock(db, tr, "REVIEW_HATCH_TRANSFORM");
        using var hatch = new Hatch { LayerId = layer };
        ReviewAppend(block, tr, hatch);
        hatch.SetHatchPattern(HatchPatternType.PreDefined, "ANSI31");
        hatch.PatternScale = 1.25;
        hatch.PatternAngle = 0.1;
        hatch.AppendLoop(HatchLoopTypes.External | HatchLoopTypes.Polyline,
            new Point2dCollection { new(0, 0), new(5, 0), new(5, 5), new(0, 5) }, new DoubleCollection { 0, 0, 0, 0 });
        hatch.EvaluateHatch(true);
        using var reference = new BlockReference(Point3d.Origin, block.ObjectId) { Rotation = Math.PI / 2, ScaleFactors = stretched ? new Scale3d(2, 3, 1) : planar ? new Scale3d(2, 2, 1) : new Scale3d(2) };
        ReviewAppend(ReviewModel(db, tr), tr, reference);
        var settings = new LegendSettings { HatchScaleFactor = 1 };
        var result = DrawingAnalyzer.Analyze(db, tr, settings);
        var entry = result.Entries.Single(e => e.Element == "REVIEWTRANSFORM");
        var sample = result.HatchSamples[entry.HatchLayer!];
        if (Math.Abs(sample.PatternScale - 2.5) > 1e-8 || Math.Abs(sample.PatternAngle - (0.1 + Math.PI / 2)) > 1e-8 || Math.Abs(entry.Metric.Area - 100) > 1e-8)
            throw new InvalidOperationException($"Rotatie of schaal ontbreekt: scale={sample.PatternScale}, angle={sample.PatternAngle}, area={entry.Metric.Area}.");
        var id = LegendBuilder.BuildBlock(db, tr, result, settings, out _, out _);
        var rendered = ((BlockTableRecord)tr.GetObject(id, OpenMode.ForRead)).Cast<ObjectId>().Select(x => tr.GetObject(x, OpenMode.ForRead)).OfType<Hatch>()
            .Single(e => e.Layer.Contains("REVIEWTRANSFORM", StringComparison.Ordinal));
        if (Math.Abs(rendered.PatternScale - 2.5) > 1e-8 || Math.Abs(rendered.PatternAngle - (0.1 + Math.PI / 2)) > 1e-8)
            throw new InvalidOperationException("De getekende arcering verliest bronrotatie of schaal.");
    }

    private static void ReviewUnusedBlockColor(Database db, Transaction tr)
    {
        var layer = ReviewLayer(db, tr, "N-WE-VH-REVIEWUNUSEDCOLOR-S", 3);
        var block = ReviewBlock(db, tr, "REVIEW_UNUSED_COLOR");
        using var line = new Line(Point3d.Origin, new Point3d(3, 0, 0)) { ColorIndex = 2 };
        ReviewAppend(block, tr, line);
        for (int i = 0; i < 2; i++)
        {
            using var reference = new BlockReference(new Point3d(i * 10d, 0, 0), block.ObjectId) { LayerId = layer, ColorIndex = i == 0 ? 1 : 5 };
            ReviewAppend(ReviewModel(db, tr), tr, reference);
        }
        var rows = DrawingAnalyzer.Analyze(db, tr, new LegendSettings()).Entries.Where(e => e.Element == "REVIEWUNUSEDCOLOR").ToList();
        if (rows.Count != 1 || rows[0].Metric.Count != 2)
            throw new InvalidOperationException("Een ongebruikte insertkleur splitst twee identieke gele symbolen.");
    }

    [CommandMethod("NLCSLEGENDASNAPSHOTTEST", CommandFlags.Modal)]
    public void NlcsLegendaSnapshotTest()
    {
        var doc = AcAp.DocumentManager.MdiActiveDocument;
        var db = doc.Database;
        string path = System.IO.Path.Join(System.IO.Path.GetDirectoryName(db.Filename)!, "review-snapshot-source.dwg");
        const string blockName = "REVIEW_SNAPSHOT_SYMBOL";
        void WriteSource(double radius)
        {
            var previous = HostApplicationServices.WorkingDatabase;
            using var source = new Database(true, true);
            try
            {
                HostApplicationServices.WorkingDatabase = source;
                using (var tr = source.TransactionManager.StartTransaction())
                {
                    var layer = ReviewLayer(source, tr, "N-WE-VH-SNAPSHOT-S");
                    var block = ReviewBlock(source, tr, blockName);
                    using var circle = new Circle(Point3d.Origin, Vector3d.ZAxis, radius);
                    ReviewAppend(block, tr, circle);
                    using var reference = new BlockReference(Point3d.Origin, block.ObjectId) { LayerId = layer };
                    ReviewAppend(ReviewModel(source, tr), tr, reference);
                    tr.Commit();
                }
                source.SaveAs(path, DwgVersion.Current);
            }
            finally { HostApplicationServices.WorkingDatabase = previous; }
        }
        try
        {
            WriteSource(1);
            AttachAndInsert(db, path, "SNAPSHOT");
            var settings = new LegendSettings { IncludeXrefLayers = true, ExplodeOnPlace = false };
            if (BuildPlaceXrefLegend(db, settings, out var error, out _, out _) != 1 || error != "geen")
                throw new InvalidOperationException(error);
            var before = ReadPlacementFingerprint(db);
            WriteSource(2);
            bool rejected = false;
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var registry = LegendStore.Load(db, tr);
                try { BuildManagedLegend(db, tr, registry, registry.Legends.Single(), out _, out _); }
                catch (InvalidOperationException ex) when (ex.ToString().Contains("Herlaad de xref", StringComparison.Ordinal)) { rejected = true; }
                tr.Abort();
            }
            if (!rejected || ReadPlacementFingerprint(db) != before)
                throw new InvalidOperationException("Een gewijzigde bron wordt gemengd met de geladen versie of verandert de vorige legenda.");
            doc.Editor.WriteMessage("\nSNAPSHOT gewijzigde bron afgewezen; bestaande legenda behouden -> OK");
        }
        catch (System.Exception ex) { doc.Editor.WriteMessage($"\nSNAPSHOT FAIL: {ex}"); }
    }

    [CommandMethod("NLCSLEGENDASNAPSHOTVERIFY", CommandFlags.Modal)]
    public void NlcsLegendaSnapshotVerify()
    {
        var doc = AcAp.DocumentManager.MdiActiveDocument;
        var db = doc.Database;
        try
        {
            double Radius()
            {
                using var tr = db.TransactionManager.StartTransaction();
                var registry = LegendStore.Load(db, tr);
                var groups = (DBDictionary)tr.GetObject(db.GroupDictionaryId, OpenMode.ForRead);
                var group = (Group)tr.GetObject(groups.GetAt(registry.Legends.Single().GroupName), OpenMode.ForRead);
                var reference = (BlockReference)tr.GetObject(group.GetAllEntityIds().Cast<ObjectId>().Single(), OpenMode.ForRead);
                return ((BlockTableRecord)tr.GetObject(reference.BlockTableRecord, OpenMode.ForRead)).Cast<ObjectId>()
                    .Select(id => tr.GetObject(id, OpenMode.ForRead)).OfType<Circle>().Single().Radius;
            }
            if (Math.Abs(Radius() - 1.0) > 1e-8) throw new InvalidOperationException("De opgeslagen legenda is al veranderd.");
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var registry = LegendStore.Load(db, tr);
                if (BuildManagedLegend(db, tr, registry, registry.Legends.Single(), out var rows, out _) != UpdateResult.Updated || rows != 1)
                    throw new InvalidOperationException("De nieuwe bron kan niet worden bijgewerkt.");
                LegendStore.Save(db, tr, registry);
                tr.Commit();
            }
            PurgePending(db);
            if (Math.Abs(Radius() - 2.0) > 1e-8) throw new InvalidOperationException("De herladen bron levert niet de nieuwe symboolvorm.");
            doc.Editor.WriteMessage("\nSNAPSHOTVERIFY herladen bron; nieuwe symboolvorm -> OK");
        }
        catch (System.Exception ex) { doc.Editor.WriteMessage($"\nSNAPSHOTVERIFY FAIL: {ex}"); }
    }

    [CommandMethod("NLCSLEGENDAEXPORTREVIEWTEST", CommandFlags.Modal)]
    public void NlcsLegendaExportReviewTest()
    {
        var doc = AcAp.DocumentManager.MdiActiveDocument;
        var db = doc.Database;
        using var userSettings = new GlobalSettingsSnapshot();
        try
        {
            ObjectId selected;
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var layer = ReviewLayer(db, tr, "N-WE-VH-REVIEWEXPORT-G");
                using var line = new Line(Point3d.Origin, new Point3d(5, 0, 0)) { LayerId = layer };
                using var other = new Line(new Point3d(0, 1, 0), new Point3d(30, 1, 0)) { LayerId = layer };
                ReviewAppend(ReviewModel(db, tr), tr, line);
                ReviewAppend(ReviewModel(db, tr), tr, other);
                selected = line.ObjectId;
                var registry = LegendStore.Load(db, tr);
                var definition = new LegendDefinition
                {
                    Name = "Review export", Scope = LegendScope.Selection,
                    SourceHandles = LegendManagement.ToHandles(new[] { selected }), GroupName = LegendRegistry.NewGroupName(),
                    Settings = new LegendSettings { ExplodeOnPlace = false }
                };
                registry.Add(definition);
                if (BuildManagedLegend(db, tr, registry, definition, out var rows, out _) != UpdateResult.Updated || rows != 1)
                    throw new InvalidOperationException("Selectielegenda kon niet worden gemaakt.");
                LegendStore.Save(db, tr, registry);
                tr.Commit();
            }
            PurgePending(db);
            var global = LoadGlobalDescriptionDefaults();
            global.Elementen["VH|REVIEWEXPORT"] = new DescriptionEntry { Specifiek = "GLOBALE WIJZIGING NA PLAATSEN" };
            SaveGlobalCatalog(global);
            NlcsLegendaInfo();
            NlcsLegendaExport();
            string baseName = System.IO.Path.Join(System.IO.Path.GetDirectoryName(db.Filename)!,
                System.IO.Path.GetFileNameWithoutExtension(db.Filename) + "_Legenda-" + LegendPresets.SafeFileName("Review export"));
            var json = System.IO.File.ReadAllText(baseName + ".json");
            using var result = System.Text.Json.JsonDocument.Parse(json);
            bool descriptions = !json.Contains("GLOBALE WIJZIGING", StringComparison.Ordinal)
                && result.RootElement.GetArrayLength() == 1 && Math.Abs(result.RootElement[0].GetProperty("lengteM").GetDouble() - 5) < 1e-8;
            System.IO.File.Move(baseName + ".json", baseName + ".before-delete.json");
            System.IO.File.Move(baseName + ".csv", baseName + ".before-delete.csv");
            using (var tr = db.TransactionManager.StartTransaction())
            {
                ((Entity)tr.GetObject(selected, OpenMode.ForWrite)).Erase();
                tr.Commit();
            }
            NlcsLegendaExport();
            bool emptySelection = !System.IO.File.Exists(baseName + ".json") && !System.IO.File.Exists(baseName + ".csv");
            if (!descriptions || !emptySelection)
                throw new InvalidOperationException($"Eigen omschrijvingen={descriptions}; verdwenen selectie blijft leeg={emptySelection}.");
            doc.Editor.WriteMessage("\nEXPORTREVIEW eigen omschrijvingen en lege bronselectie -> OK");
        }
        catch (System.Exception ex) { doc.Editor.WriteMessage($"\nEXPORTREVIEW FAIL: {ex}"); }

    }


    private static void ReviewAreaOutline(Database db, Transaction tr)
    {
        var lineLayer = ReviewLayer(db, tr, "N-WE-VH-REVIEWAREA-G");
        var hatchLayer = ReviewLayer(db, tr, "N-WE-VH-REVIEWAREA-A");
        using var outline = new Polyline { LayerId = lineLayer, Closed = true };
        var points = new Point2dCollection { new(0, 0), new(5, 0), new(5, 5), new(0, 5) };
        for (int i = 0; i < points.Count; i++) outline.AddVertexAt(i, points[i], 0, 0, 0);
        ReviewAppend(ReviewModel(db, tr), tr, outline);
        using var hatch = new Hatch { LayerId = hatchLayer };
        ReviewAppend(ReviewModel(db, tr), tr, hatch);
        hatch.SetHatchPattern(HatchPatternType.PreDefined, "SOLID");
        hatch.AppendLoop(HatchLoopTypes.External | HatchLoopTypes.Polyline, points, new DoubleCollection { 0, 0, 0, 0 });
        hatch.EvaluateHatch(true);
        for (int i = 0; i < 3; i++)
        {
            var entry = DrawingAnalyzer.Analyze(db, tr, new LegendSettings()).Entries.Single(e => e.Element == "REVIEWAREA");
            if (Math.Abs(entry.Metric.Area - 25) > 1e-8 || Math.Abs(entry.Metric.Length - 20) > 1e-8)
                throw new InvalidOperationException($"Contour en arcering: {entry.Metric.Area} m2 / {entry.Metric.Length} m; verwacht 25 / 20.");
            if (hatch.NumberOfLoops != 1 || Math.Abs(hatch.Area - 25) > 1e-8 || outline.NumberOfVertices != 4)
                throw new InvalidOperationException("Analyseren heeft nog niet opgeslagen brongeometrie gewijzigd.");
        }
    }

    private static void ReviewMergedDescription(Database db, Transaction tr)
    {
        for (int i = 0; i < 2; i++)
        {
            var layer = ReviewLayer(db, tr, "N-WE-VH-REVIEWDESC-G-" + (i == 0 ? "200" : "500"));
            if (i == 1) ((LayerTableRecord)tr.GetObject(layer, OpenMode.ForWrite)).Description = "Gedeelde bronbeschrijving";
            using var line = new Line(Point3d.Origin, new Point3d(5, 0, 0)) { LayerId = layer };
            ReviewAppend(ReviewModel(db, tr), tr, line);
        }
        var entry = DrawingAnalyzer.Analyze(db, tr, new LegendSettings()).Entries.Single(e => e.Element == "REVIEWDESC");
        if (entry.Description != "Gedeelde bronbeschrijving" || Math.Abs(entry.Metric.Length - 10) > 1e-8)
            throw new InvalidOperationException("Een omschrijving op de tweede samengevoegde bronlaag is verloren.");
    }

    private static void ReviewEmptySelection(Database db, Transaction tr)
    {
        var layer = ReviewLayer(db, tr, "N-WE-VH-REVIEWEMPTY-G");
        using var line = new Line(Point3d.Origin, new Point3d(25, 0, 0)) { LayerId = layer };
        ReviewAppend(ReviewModel(db, tr), tr, line);
        var result = DrawingAnalyzer.Analyze(db, tr, new LegendSettings(), Array.Empty<ObjectId>());
        if (result.Entries.Count != 0) throw new InvalidOperationException("Een lege selectie wordt de hele tekening.");
    }

    [CommandMethod("NLCSLEGENDAMETADATATEST", CommandFlags.Modal)]
    public void NlcsLegendaMetadataTest()
    {
        var doc = AcAp.DocumentManager.MdiActiveDocument;
        try
        {
            var directory = System.IO.Path.Join(System.IO.Path.GetDirectoryName(doc.Database.Filename)!, "metadata");
            System.IO.Directory.CreateDirectory(directory);
            var invalid = new[]
            {
                "", "{ kapot", """{"schemaVersion":2,"legends":[null]}""",
                """{"schemaVersion":999,"legends":[]}""",
                "{}", """{"schemaVersion":2}""", """{"schemaVersion":2,"legends":null}""",
                """{"schemaVersion":2,"legends":[{"scope":1,"sourceHandles":["1A"],"groupName":"g","settings":{}}]}""",
                """{"schemaVersion":2,"legends":[{"id":"a","sourceHandles":["1A"],"groupName":"g","settings":{}}]}""",
                """{"schemaVersion":2,"legends":[{"id":"a","scope":1,"sourceHandles":["1A"],"groupName":"g","settings":null}]}""",
                """{"schemaVersion":2,"legends":[{"id":"a","scope":1,"sourceHandles":null,"groupName":"g","settings":{}}]}""",
                """{"schemaVersion":2,"legends":[{"id":"a","scope":99,"sourceHandles":["1A"],"groupName":"g","settings":{}}]}"""
            };
            for (int i = 0; i <= invalid.Length; i++)
            {
                using var db = new Database(true, true);
                using (var tr = db.TransactionManager.StartTransaction())
                {
                    var layer = ReviewLayer(db, tr, "N-WE-VH-INVOER-G");
                    using var line = new Line(Point3d.Origin, new Point3d(7, 0, 0)) { LayerId = layer };
                    ReviewAppend(ReviewModel(db, tr), tr, line);
                    if (i < invalid.Length)
                    {
                        var nod = (DBDictionary)tr.GetObject(db.NamedObjectsDictionaryId, OpenMode.ForWrite);
                        using var root = new DBDictionary();
                        nod.SetAt("NLCSLEGENDA_REGISTRY", root);
                        tr.AddNewlyCreatedDBObject(root, true);
                        using var record = new Xrecord();
                        root.SetAt("REGISTRY", record);
                        tr.AddNewlyCreatedDBObject(record, true);
                        using var data = new ResultBuffer(new TypedValue((int)DxfCode.Text, invalid[i]));
                        record.Data = data;
                        bool refused = false;
                        try { LegendStore.Save(db, tr, new LegendRegistry()); }
                        catch (NotSupportedException) { refused = true; }
                        using var retained = record.Data;
                        var raw = retained is null ? string.Empty : string.Concat(retained.AsArray().Select(v => v.Value as string));
                        if (!refused || raw != invalid[i])
                            throw new InvalidOperationException("Onleesbare registratie is overschreven.");
                    }
                    tr.Commit();
                }
                db.SaveAs(System.IO.Path.Join(directory, i + ".dwg"), DwgVersion.Current);
            }
            var hashes = System.IO.Directory.GetFiles(directory, "*.dwg").ToDictionary(p => p,
                p => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.IO.File.ReadAllBytes(p))));
            var results = BatchExport.AnalyzeFolder(directory, new LegendSettings(), DescriptionCatalog.Default());
            var good = results.Single(r => r.Drawing == invalid.Length + ".dwg");
            bool unchanged = hashes.All(p => p.Value == Convert.ToHexString(
                System.Security.Cryptography.SHA256.HashData(System.IO.File.ReadAllBytes(p.Key))));
            if (results.Count != invalid.Length + 1 || results.Count(r => r.Error is not null) != invalid.Length || good.Error is not null
                || good.Entries.Count != 1 || Math.Abs(good.Entries[0].Metric.Length - 7) > 1e-8 || !unchanged)
                throw new InvalidOperationException("Batch accepteert onleesbare gegevens of wijzigt een bronbestand.");
            doc.Editor.WriteMessage("\nMETADATA 12 onleesbare tekeningen afgewezen; geldige tekening 7m; bronbestanden ongewijzigd -> OK");
        }
        catch (System.Exception ex) { doc.Editor.WriteMessage($"\nMETADATA FAIL: {ex}"); }
    }
}
