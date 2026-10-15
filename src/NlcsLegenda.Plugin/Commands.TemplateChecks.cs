using System.IO;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using NlcsLegenda.Core;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Core.Application;

namespace NlcsLegenda.Plugin;

public partial class Commands
{
    [CommandMethod("NLCSLEGENDATEMPLATECONTRACTTEST", CommandFlags.Modal)]
    public void NlcsLegendaTemplateContractTest()
    {
        var doc = AcApp.DocumentManager.MdiActiveDocument;
        var db = doc.Database;
        try
        {
            foreach (double scale in new[] { 200d, 500d, 1000d })
            {
                using var tr = db.TransactionManager.StartTransaction();
                var labels = new[] { "Geometrie", "Systeemgrens", "Kadastrale grens", "Onderhoudgrens" };
                var entries = labels.Select((text, i) => new LegendEntry
                {
                    Status = NlcsStatus.Bestaand, Discipline = "WE", Hoofdgroep = "VH", Element = text, Description = text,
                    LayersByType = new() { [NlcsDrawType.Geometrie] = "TEMPLATE_LINE_" + i }
                }).ToArray();
                var settings = new LegendSettings { Scale = scale };
                var id = LegendBuilder.BuildBlock(db, tr, new AnalysisResult { Entries = entries }, settings, out int rows, out var issues);
                var entities = ((BlockTableRecord)tr.GetObject(id, OpenMode.ForRead)).Cast<ObjectId>()
                    .Select(i => tr.GetObject(i, OpenMode.ForRead)).OfType<Entity>().ToList();
                var lines = entities.OfType<Polyline>().Where(p => p.Layer.StartsWith("TEMPLATE_LINE_", StringComparison.Ordinal)).OrderBy(p => p.Layer).ToArray();
                double paper = 1000 / scale;
                void Near(double actual, double expected, string name, double tolerance = 0.002)
                {
                    if (Math.Abs(actual - expected) > tolerance) throw new InvalidOperationException($"{name}: {actual:R}, verwacht {expected:R}.");
                }
                if (rows != 4 || issues.Count != 0 || lines.Length != 4) throw new InvalidOperationException("Vier voorbeeldregels ontbreken.");
                double[] lineY = { -12.52588838, -18.83579787, -25.14570736, -31.45561685 };
                for (int i = 0; i < 4; i++)
                {
                    Near(lines[i].Length * paper, 23.99999162, "Lijnbreedte");
                    Near(lines[i].GetPoint3dAt(0).Y * paper + 5, lineY[i], "Regelpositie");
                }
                var texts = entities.OfType<MText>().ToArray();
                var body = texts.Single(t => t.Text == "Geometrie");
                Near(body.TextHeight * paper, 2.5, "T25");
                Near(body.Location.X * paper - 0.25, 29.71881311, "Tekstkolom");
                Near(body.Location.Y * paper + 5 - 2.5, -13.2356127, "Tekstbasislijn");
                var title = texts.Single(t => t.Text == "LEGENDA");
                var header = texts.Single(t => t.Text == "Bestaande situatie:");
                Near(title.TextHeight * paper, 5, "T50");
                Near((title.Location.Y - title.TextHeight - header.Location.Y + header.TextHeight) * paper, 6.92570534, "Titel-kop");
                var style = (TextStyleTableRecord)tr.GetObject(body.TextStyleId, OpenMode.ForRead);
                if (!Path.GetFileName(style.FileName).Equals("NLCS-ISO.ttf", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Verkeerd lettertype.");
                if (entities.OfType<Polyline>().Any(p => p.Layer == settings.FrameLayer && p.Closed && p.NumberOfVertices == 4
                    && Math.Abs(p.Length * paper - 58) < 0.01)) throw new InvalidOperationException("Hulpkader wordt geplot.");
                var helperLayer = (LayerTableRecord)tr.GetObject(((LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead))[settings.SwatchFrameLayer], OpenMode.ForRead);
                if (helperLayer.IsPlottable || entities.OfType<Polyline>().Count(p => p.Layer == settings.SwatchFrameLayer) != 4)
                    throw new InvalidOperationException("Modelhulpkaders ontbreken of zijn plotbaar.");
                var bar = entities.OfType<Polyline>().Single(p => p.Layer == settings.FrameLayer && p.Closed
                    && Math.Abs((p.GeometricExtents.MaxPoint.X - p.GeometricExtents.MinPoint.X) * paper - 40) < 0.01);
                Near((bar.GeometricExtents.MaxPoint.Y - bar.GeometricExtents.MinPoint.Y) * paper, 2, "Schaalbalkhoogte");
                var fills = entities.OfType<Hatch>().Where(h => h.Layer == settings.FrameLayer).ToArray();
                if (fills.Length != 4) throw new InvalidOperationException("Schaalbalk heeft niet vier halve vullingen.");
                Near(fills.Sum(h => h.Area) * paper * paper, 40, "Schaalbalkvulling");
                var notes = texts.Single(t => t.Text == "OPMERKINGEN");
                Near(notes.TextHeight * paper, 5, "Opmerkingenkop");
                var remarks = texts.Single(t => t.Text.Contains("Maten in meters", StringComparison.Ordinal));
                if (!remarks.Contents.StartsWith("\\pxi-2,l2;", StringComparison.Ordinal))
                    throw new InvalidOperationException("Inspringing opmerkingen wijkt af.");
                Near(remarks.ActualHeight * paper, 15.75, "Vier regels opmerkingen");
                var footer = texts.Single(t => t.Text.StartsWith("Schaal 1:", StringComparison.Ordinal));
                Near(footer.TextHeight * paper, 1.8, "Schaaltekst");
                if (footer.Location.Y >= bar.GeometricExtents.MinPoint.Y || notes.Location.Y <= bar.GeometricExtents.MaxPoint.Y)
                    throw new InvalidOperationException("Volgorde opmerkingen/schaalbalk/schaaltekst onjuist.");
                tr.Abort();
            }
            doc.Editor.WriteMessage("\nTEMPLATECONTRACT DWT-meetpunten, lege hoststijl, notities en schaalbalk op 1:200/500/1000 -> OK");
        }
        catch (System.Exception ex) when (ex is InvalidOperationException or IOException or ArgumentException or Autodesk.AutoCAD.Runtime.Exception) { doc.Editor.WriteMessage($"\nTEMPLATECONTRACT FAIL: {ex}"); }
    }

    [CommandMethod("NLCSLEGENDASCALEVARIANTTEST", CommandFlags.Modal)]
    public void NlcsLegendaScaleVariantTest()
    {
        var doc = AcApp.DocumentManager.MdiActiveDocument;
        var db = doc.Database;
        var path = Path.Join(Path.GetDirectoryName(db.Filename)!, "scale-variant-source.dwg");
        try
        {
            using (var source = new Database(true, true))
            {
                source.Insunits = UnitsValue.Meters;
                using (var tr = source.TransactionManager.StartTransaction())
                {
                    var bt = (BlockTable)tr.GetObject(source.BlockTableId, OpenMode.ForWrite);
                    var def = new BlockTableRecord { Name = "SCALE_VARIANT_CIRCLE" };
                    var defId = bt.Add(def); tr.AddNewlyCreatedDBObject(def, true);
                    using var circle = new Circle(Point3d.Origin, Vector3d.ZAxis, 2);
                    def.AppendEntity(circle); tr.AddNewlyCreatedDBObject(circle, true);
                    var layers = (LayerTable)tr.GetObject(source.LayerTableId, OpenMode.ForWrite);
                    var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(source), OpenMode.ForWrite);
                    foreach (int scale in new[] { 200, 500 })
                    {
                        var sl = new LayerTableRecord { Name = $"N-WE-IE-SCALE_SYMBOL-S-{scale}" };
                        var sid = layers.Add(sl); tr.AddNewlyCreatedDBObject(sl, true);
                        using var br = new BlockReference(new Point3d(scale, 0, 0), defId) { LayerId = sid, ScaleFactors = new Scale3d(scale / 1000d) };
                        ms.AppendEntity(br); tr.AddNewlyCreatedDBObject(br, true);
                        var hl = new LayerTableRecord { Name = $"N-WE-VH-SCALE_HATCH-A-{scale}" };
                        var hid = layers.Add(hl); tr.AddNewlyCreatedDBObject(hl, true);
                        using var hatch = new Hatch { LayerId = hid };
                        hatch.SetHatchPattern(HatchPatternType.PreDefined, "ANSI31");
                        ms.AppendEntity(hatch); tr.AddNewlyCreatedDBObject(hatch, true);
                        hatch.AppendLoop(HatchLoopTypes.External | HatchLoopTypes.Polyline,
                            new Point2dCollection { new(scale, 10), new(scale+4, 10), new(scale+4, 14), new(scale, 14) },
                            new DoubleCollection { 0, 0, 0, 0 });
                        hatch.PatternScale = scale / 1000d;
                        hatch.EvaluateHatch(true);
                    }
                    tr.Commit();
                }
                source.SaveAs(path, DwgVersion.Current);
            }
            var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path)));
            var xref = db.AttachXref(path, "SCALE_SOURCE");
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForWrite);
                using var br = new BlockReference(Point3d.Origin, xref); ms.AppendEntity(br); tr.AddNewlyCreatedDBObject(br, true);
                tr.Commit();
            }
            foreach (double scale in new[] { 200d, 500d, 1000d })
            {
                using var tr = db.TransactionManager.StartTransaction();
                var settings = new LegendSettings { Scale = scale, IncludeXrefLayers = true };
                var analysis = DrawingAnalyzer.Analyze(db, tr, settings);
                var id = LegendBuilder.BuildBlock(db, tr, analysis, settings, out _, out var issues);
                var entities = ((BlockTableRecord)tr.GetObject(id, OpenMode.ForRead)).Cast<ObjectId>()
                    .Select(i => tr.GetObject(i, OpenMode.ForRead)).OfType<Entity>().ToList();
                var circles = entities.OfType<Circle>().ToArray();
                var hatches = entities.OfType<Hatch>().Where(h => h.Layer.Contains("SCALE_HATCH", StringComparison.Ordinal)).ToArray();
                if (issues.Count != 0 || circles.Length == 0 || hatches.Length == 0
                    || circles.Any(c => Math.Abs(c.Diameter * 1000 / scale - 4) > 0.001)
                    || hatches.Any(h => Math.Abs(h.PatternScale * 1000 / scale - 1) > 0.001))
                    throw new InvalidOperationException($"Schaalvariant 1:{scale}: symbool niet 4 mm of patroon niet 1 mm.");
                tr.Abort();
            }
            if (hash != Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path)))) throw new InvalidOperationException("Bron gewijzigd.");
            doc.Editor.WriteMessage("\nSCALEVARIANT xref 1:200/500 naar 1:200/500/1000: symbool 4mm, patroon 1mm, bron ongewijzigd -> OK");
        }
        catch (System.Exception ex) when (ex is InvalidOperationException or IOException or ArgumentException or Autodesk.AutoCAD.Runtime.Exception) { doc.Editor.WriteMessage($"\nSCALEVARIANT FAIL: {ex}"); }
    }

    [CommandMethod("NLCSLEGENDALIVETARGETTEST", CommandFlags.Modal)]
    public void NlcsLegendaLiveTargetTest()
    {
        var doc = AcApp.DocumentManager.MdiActiveDocument;
        var db = doc.Database;
        try
        {
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var registry = new LegendRegistry();
                var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForWrite);
                ObjectId Add(string name)
                {
                    var def = new LegendDefinition { Name = name, GroupName = LegendRegistry.NewGroupName() };
                    using var line = new Line(Point3d.Origin, new Point3d(5, 0, 0));
                    var id = ms.AppendEntity(line); tr.AddNewlyCreatedDBObject(line, true);
                    LegendManagement.AddToGroup(db, tr, def.GroupName, new[] { id });
                    registry.Add(def); return id;
                }
                Add("A"); Add("B");
                LegendStore.Save(db, tr, registry); tr.Commit();
            }
            doc.Editor.WriteMessage("\nLIVETARGET twee legenda's aangemaakt");
        }
        catch (System.Exception ex) when (ex is InvalidOperationException or IOException or ArgumentException or Autodesk.AutoCAD.Runtime.Exception) { doc.Editor.WriteMessage($"\nLIVETARGET FAIL: {ex}"); }
    }
    [CommandMethod("NLCSLEGENDALIVETARGETERASE", CommandFlags.Modal)]
    public void NlcsLegendaLiveTargetErase()
    {
        var doc = AcApp.DocumentManager.MdiActiveDocument;
        var db = doc.Database;
        try
        {
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var registry = LegendStore.Load(db, tr);
                var def = registry.Legends.Single(d => d.Name == "B");
                var gd = (DBDictionary)tr.GetObject(db.GroupDictionaryId, OpenMode.ForRead);
                var group = (Group)tr.GetObject(gd.GetAt(def.GroupName), OpenMode.ForRead);
                foreach (var id in group.GetAllEntityIds()) ((Entity)tr.GetObject(id, OpenMode.ForWrite)).Erase();
                tr.Commit();
            }
            if (!ResolveMutationTarget(doc.Editor, db, out var target) || target.IsGlobal || target.ContextLabel != "Legenda: A")
                throw new InvalidOperationException("Eén zichtbare legenda wordt niet automatisch gekozen.");
            NlcsLegendaInfo();
            doc.Editor.WriteMessage("\nLIVETARGET wissen: alleen A gekozen -> OK");
        }
        catch (System.Exception ex) when (ex is InvalidOperationException or IOException or ArgumentException or Autodesk.AutoCAD.Runtime.Exception) { doc.Editor.WriteMessage($"\nLIVETARGET FAIL: {ex}"); }
    }

    [CommandMethod("NLCSLEGENDALIVETARGETVERIFY", CommandFlags.Modal)]
    public void NlcsLegendaLiveTargetVerify()
    {
        var doc = AcApp.DocumentManager.MdiActiveDocument;
        var db = doc.Database;
        try
        {
            using var tr = db.TransactionManager.StartTransaction();
            var registry = LegendStore.Load(db, tr);
            if (registry.Legends.Count != 2 || LegendManagement.ActiveRegistry(db, tr, registry).Legends.Count != 2)
                throw new InvalidOperationException("UNDO herstelt niet beide legenda's.");
            tr.Commit();
            doc.Editor.WriteMessage("\nLIVETARGET UNDO: A/B bereikbaar; metadata behouden -> OK");
        }
        catch (System.Exception ex) when (ex is InvalidOperationException or IOException or ArgumentException or Autodesk.AutoCAD.Runtime.Exception) { doc.Editor.WriteMessage($"\nLIVETARGET FAIL: {ex}"); }
    }


    [CommandMethod("NLCSLEGENDALIVEEMPTYTEST", CommandFlags.Modal)]
    public void NlcsLegendaLiveEmptyTest()
    {
        var doc = AcApp.DocumentManager.MdiActiveDocument;
        var db = doc.Database;
        try
        {
            NlcsLegendaLiveTargetTest();
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var registry = LegendStore.Load(db, tr);
                foreach (var id in LegendManagement.CollectManagedIds(db, tr, registry))
                    ((Entity)tr.GetObject(id, OpenMode.ForWrite)).Erase();
                var layers = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForWrite);
                using var layer = new LayerTableRecord { Name = "B-WE-VH-LIVEEMPTY-G" };
                var layerId = layers.Add(layer); tr.AddNewlyCreatedDBObject(layer, true);
                var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForWrite);
                using var line = new Line(Point3d.Origin, new Point3d(5, 0, 0)) { LayerId = layerId };
                ms.AppendEntity(line); tr.AddNewlyCreatedDBObject(line, true);
                tr.Commit();
            }
            var path = Path.Join(Path.GetDirectoryName(db.Filename)!, Path.GetFileNameWithoutExtension(db.Filename) + "_NLCS-legenda.json");
            if (File.Exists(path)) throw new InvalidOperationException("Exportbestand bestaat al voor de proef.");
            NlcsLegendaInfo();
            NlcsLegendaUpdate();
            NlcsLegendaExport();
            if (!File.Exists(path)) throw new InvalidOperationException("Geen export nadat alle legenda's gewist zijn.");
            using var json = System.Text.Json.JsonDocument.Parse(File.ReadAllText(path));
            if (json.RootElement.GetArrayLength() != 1 || Math.Abs(json.RootElement[0].GetProperty("lengteM").GetDouble() - 5) > 1e-9)
                throw new InvalidOperationException("Export zonder aanwezige legenda bevat niet de bronlijn van 5 meter.");
            using var verify = db.TransactionManager.StartTransaction();
            var stored = LegendStore.Load(db, verify);
            if (stored.Legends.Count != 2 || LegendManagement.ActiveRegistry(db, verify, stored).Legends.Count != 0)
                throw new InvalidOperationException("Overzicht of export heeft Undo-metadata gewijzigd.");
            verify.Commit();
            doc.Editor.WriteMessage("\nLIVEEMPTY geen actieve legenda: bronexport 5m, metadata behouden -> OK");
        }
        catch (System.Exception ex) when (ex is InvalidOperationException or IOException or ArgumentException or Autodesk.AutoCAD.Runtime.Exception or System.Text.Json.JsonException)
        { doc.Editor.WriteMessage($"\nLIVEEMPTY FAIL: {ex}"); }
    }

}
