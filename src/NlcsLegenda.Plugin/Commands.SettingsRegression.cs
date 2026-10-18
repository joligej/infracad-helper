using System.IO;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using NlcsLegenda.Core;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Core.Application;

namespace NlcsLegenda.Plugin;

public partial class Commands
{
    [CommandMethod("NLCSLEGENDASETTINGREGRESSION", CommandFlags.Modal)]
    public void NlcsLegendaSettingRegression()
    {
        var doc = AcApp.DocumentManager.MdiActiveDocument;
        var db = doc.Database;
        DiagnosticRun.Execute(() =>
        {
            using var globalSnapshot = new GlobalSettingsSnapshot();
            if (ResolveMutationTarget(doc.Editor, db, out _))
                throw new InvalidOperationException("Lokale instelling valt terug op globaal.");
            var initial = new LegendSettings
            {
                Scale = 200,
                IncludeRemarks = false,
                IncludeScaleBar = false,
                ManualEntries = new() { new() { Layer = "LOCAL_LINE", Description = "Eigen lijn" } }
            };
            string aId, bId;
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var registry = LegendStore.Load(db, tr);
                var a = IsoDef(registry, initial.Clone()); a.Name = "Lokale A"; registry.Add(a); aId = a.Id;
                var b = IsoDef(registry, initial.Clone()); b.Name = "Lokale B"; registry.Add(b); bId = b.Id;
                if (BuildManagedLegend(db, tr, registry, a, out _, out _) != UpdateResult.Updated
                    || BuildManagedLegend(db, tr, registry, b, out _, out _) != UpdateResult.Updated)
                    throw new InvalidOperationException("Testlegenda ontbreekt.");
                LegendStore.Save(db, tr, registry);
                tr.Commit();
            }
            string State(string id)
            {
                using var tr = db.TransactionManager.StartTransaction();
                var def = LegendStore.Load(db, tr).FindById(id)!;
                var groups = (DBDictionary)tr.GetObject(db.GroupDictionaryId, OpenMode.ForRead);
                var group = (Group)tr.GetObject(groups.GetAt(def.GroupName), OpenMode.ForRead);
                return def.Settings.ToJson() + string.Join("|", group.GetAllEntityIds().Select(i => i.Handle.ToString()));
            }
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var layers = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForWrite);
                using var layer = new LayerTableRecord { Name = "N-WE-GR-BOOM-G", Description = "Omschrijving uit de bron" };
                layers.Add(layer); tr.AddNewlyCreatedDBObject(layer, true); tr.Commit();
            }
            var choiceSettings = initial.Clone();
            choiceSettings.DescriptionOverrides.Elementen["GR|BOOM"] = new() { Specifiek = "Mijn boom" };
            if (BuildLayerChoices(db, choiceSettings).Single(c => c.Layer == "N-WE-GR-BOOM-G").Description != "Mijn boom")
                throw new InvalidOperationException("Laagkeuze negeert de eigen omschrijving.");
            SaveGlobalDefaults(initial);
            string globalBefore = File.ReadAllText(ConfigPath) + File.ReadAllText(DescriptionsPath);
            string aBefore = State(aId), bBefore = State(bId);
            var changed = initial.Clone(); changed.Scale = 500; changed.ManualEntries[0].Description = "Direct bijgewerkt";
            changed.Klic.GroupFields = new() { "Materiaal", "DiameterM" };
            if (ApplySettingsAndRebuild(doc.Editor, db, aId, changed) is { } error) throw new InvalidOperationException(error);
            if (State(aId) == aBefore || State(bId) != bBefore)
                throw new InvalidOperationException("Lokale wijziging ontbreekt of beïnvloedt B.");
            if (globalBefore != File.ReadAllText(ConfigPath) + File.ReadAllText(DescriptionsPath))
                throw new InvalidOperationException("Lokale wijziging beïnvloedt globale standaard.");
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var def = LegendStore.Load(db, tr).FindById(aId)!;
                var groups = (DBDictionary)tr.GetObject(db.GroupDictionaryId, OpenMode.ForRead);
                var entities = ((Group)tr.GetObject(groups.GetAt(def.GroupName), OpenMode.ForRead)).GetAllEntityIds()
                    .SelectMany(i => RegressionEntities(tr, i)).ToArray();
                if (!entities.OfType<MText>().Any(t => t.Text.Contains("Direct bijgewerkt") && Math.Abs(t.TextHeight - 1.25) < 1e-7)
                    || !def.Settings.Klic.GroupFields.SequenceEqual(changed.Klic.GroupFields))
                    throw new InvalidOperationException("Nieuwe tekst, schaal of KLIC-keuze niet direct toegepast.");
            }
            string savedA = State(aId);
            var global = initial.Clone(); global.Scale = 1000; SaveGlobalDefaults(global);
            if (State(aId) != savedA || State(bId) != bBefore || LoadGlobalDefaults().Scale != 1000)
                throw new InvalidOperationException("Globaal en lokaal zijn niet gescheiden.");
            var empty = new LegendSettings { IncludeRemarks = false, IncludeScaleBar = false };
            if (ApplySettingsAndRebuild(doc.Editor, db, aId, empty) is null || State(aId) != savedA)
                throw new InvalidOperationException("Lege wijziging meldt geen fout of overschrijft vorige legenda.");
            var invalid = changed.Clone(); invalid.ManualEntries[0].Type = NlcsDrawType.Symbool; invalid.ManualEntries[0].SymbolBlock = "ONTBREEKT";
            if (ApplySettingsAndRebuild(doc.Editor, db, aId, invalid) is null || State(aId) != savedA || State(bId) != bBefore)
                throw new InvalidOperationException("Mislukte opbouw liet gewijzigde instellingen of geometrie achter.");
            doc.Editor.WriteMessage("\nSETTINGREGRESSION lokale A direct getekend, B en globale standaard gescheiden, fouten teruggedraaid -> OK");
        }, message => doc.Editor.WriteMessage(message), "NlcsLegendaSettingRegression");
    }

    [CommandMethod("NLCSLEGENDAFRAMEREGRESSION", CommandFlags.Modal)]
    public void NlcsLegendaFrameRegression()
    {
        var doc = AcApp.DocumentManager.MdiActiveDocument;
        var db = doc.Database;
        DiagnosticRun.Execute(() =>
        {
            using var tr = db.TransactionManager.StartTransaction();
            var blocks = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForWrite);
            using var symbol = new BlockTableRecord { Name = "BREED_SYMBOOL" };
            blocks.Add(symbol); tr.AddNewlyCreatedDBObject(symbol, true);
            using var line = new Line(new Point3d(-40, 0, 0), new Point3d(40, 0, 0));
            symbol.AppendEntity(line); tr.AddNewlyCreatedDBObject(line, true);
            var entries = new[] {
                new LegendEntry { Status = NlcsStatus.Nieuw, Discipline = "WE", Hoofdgroep = "GR", Element = "GRAS",
                    Description = "Gras", LayersByType = new() { [NlcsDrawType.Vlakvulling] = "REGRESSION_FILL" }, Metric = new LayerMetric { Area = 230 } },
                new ManualEntry { Layer = "REGRESSION_SYMBOL", Type = NlcsDrawType.Symbool, Description = "Breed symbool", SymbolBlock = "BREED_SYMBOOL" }.ToLegendEntry(),
                new LegendEntry { Status = NlcsStatus.Nieuw, Discipline = "WE", Hoofdgroep = "OG", Element = "LANG",
                    Description = new string('W', 96), LayersByType = new() { [NlcsDrawType.Geometrie] = "REGRESSION_LINE" }, Metric = new LayerMetric { Length = 12345 } }
            };
            var settings = new LegendSettings
            {
                Scale = 200,
                DrawSwatchFrame = true,
                IncludeQuantities = true,
                IncludeRemarks = false,
                IncludeFooter = false,
                IncludeScaleBar = false,
                IncludeGeneralDescription = false
            };
            var id = LegendBuilder.BuildBlock(db, tr, new AnalysisResult { Entries = entries }, settings, out int rows, out var issues);
            if (rows != 3 || issues.Count != 0) throw new InvalidOperationException("Testregels ontbreken.");
            var block = (BlockTableRecord)tr.GetObject(id, OpenMode.ForWrite); block.Name = "REGRESSION_FRAMES";
            using var reference = new BlockReference(Point3d.Origin, id);
            var model = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForWrite);
            model.AppendEntity(reference); tr.AddNewlyCreatedDBObject(reference, true);
            tr.Commit();
            VerifyFrames(db);
            doc.Editor.WriteMessage("\nFRAMEREGRESSION breed symbool, gevuld kader en lange tekst zonder botsing -> OK");
        }, message => doc.Editor.WriteMessage(message), "NlcsLegendaFrameRegression");
    }

    private static IEnumerable<Entity> RegressionEntities(Transaction tr, ObjectId id)
    {
        if (tr.GetObject(id, OpenMode.ForRead) is not Entity entity) yield break;
        if (entity is BlockReference block)
        {
            foreach (ObjectId child in (BlockTableRecord)tr.GetObject(block.BlockTableRecord, OpenMode.ForRead))
                foreach (var part in RegressionEntities(tr, child)) yield return part;
        }
        else yield return entity;
    }
    private static void VerifyFrames(Database db)
    {
        using var tr = db.TransactionManager.StartTransaction();
        var blocks = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
        var entities = ((BlockTableRecord)tr.GetObject(blocks["REGRESSION_FRAMES"], OpenMode.ForRead)).Cast<ObjectId>()
            .SelectMany(i => RegressionEntities(tr, i)).ToArray();
        var fill = entities.OfType<Hatch>().Single(h => h.Layer == "REGRESSION_FILL").GeometricExtents;
        var frames = entities.OfType<Polyline>().Where(e => e.Layer == new LegendSettings().SwatchFrameLayer).ToArray();
        var frame = frames.Single(f => Math.Abs(f.GeometricExtents.MinPoint.Y - fill.MinPoint.Y) < 1e-7).GeometricExtents;
        if (frame.MinPoint.DistanceTo(fill.MinPoint) > 1e-7 || frame.MaxPoint.DistanceTo(fill.MaxPoint) > 1e-7
            || Math.Abs(frame.MaxPoint.X - frame.MinPoint.X - 4.8) > 1e-7)
            throw new InvalidOperationException("Vulling en kader wijken af van 24 x 5 mm.");
        if (!frames.Any(f => Math.Abs(f.GeometricExtents.MaxPoint.X - f.GeometricExtents.MinPoint.X - 16) < 1e-7))
            throw new InvalidOperationException("Breed symbool vervormd of afgesneden.");
        var text = entities.OfType<MText>().Single(t => t.Text.StartsWith("WWWW", StringComparison.Ordinal));
        if (text.ActualWidth > text.Width + 1e-7 || text.Text.Replace("\r", "").Replace("\n", "").Replace(" ", "") != new string('W', 96))
            throw new InvalidOperationException("Lange eigen tekst buiten de kolom of tekst verloren.");
        var quantity = entities.OfType<MText>().Single(t => t.Text.Contains("12.345"));
        if (text.GeometricExtents.MaxPoint.X >= quantity.GeometricExtents.MinPoint.X)
            throw new InvalidOperationException("Tekst overlapt hoeveelheid.");
    }

    [CommandMethod("NLCSLEGENDAHATCHAREAREGRESSION", CommandFlags.Modal)]
    public void NlcsLegendaHatchAreaRegression()
    {
        var doc = AcApp.DocumentManager.MdiActiveDocument;
        DiagnosticRun.Execute(() =>
        {
            foreach (var (style, expected) in new[] { (HatchStyle.Normal, 68d), (HatchStyle.Outer, 64d), (HatchStyle.Ignore, 100d) })
            {
                using var hatch = new Hatch(); hatch.SetHatchPattern(HatchPatternType.PreDefined, "SOLID"); hatch.HatchStyle = style;
                void Add(double lo, double hi, HatchLoopTypes type)
                {
                    var points = new Point2dCollection { new(lo, lo), new(hi, lo), new(hi, hi), new(lo, hi), new(lo, lo) };
                    hatch.AppendLoop(type | HatchLoopTypes.Polyline, points, new DoubleCollection(new double[5]));
                }
                Add(0, 10, HatchLoopTypes.External); Add(2, 8, HatchLoopTypes.Outermost); Add(4, 6, HatchLoopTypes.Default);
                hatch.EvaluateHatch(true);
                double before = hatch.Area;
                double actual = DrawingAnalyzer.RecalculateHatchArea(hatch);
                if (Math.Abs(actual - expected) > 1e-7 || Math.Abs(before - hatch.Area) > 1e-7 || hatch.NumberOfLoops != 3 || hatch.HatchStyle != style)
                    throw new InvalidOperationException($"Arcering {style}: {actual}, verwacht {expected}; bron {before}.");
            }
            using (var tr = doc.Database.TransactionManager.StartTransaction())
            {
                using var invalid = new Hatch();
                invalid.SetDatabaseDefaults(doc.Database);
                invalid.LayerId = ReviewLayer(doc.Database, tr, "N-WE-VH-ONMEETBAAR-A");
                var model = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(doc.Database), OpenMode.ForWrite);
                model.AppendEntity(invalid); tr.AddNewlyCreatedDBObject(invalid, true);
                invalid.SetHatchPattern(HatchPatternType.PreDefined, "SOLID");
                var settings = new LegendSettings();
                var analysis = DrawingAnalyzer.Analyze(doc.Database, tr, settings, new[] { invalid.ObjectId });
                var entry = analysis.Entries.Single();
                if (!entry.Metric.HasUnknownArea || entry.QuantityText() != "? m²" || !entry.Metric.AreaIssue!.Contains(invalid.Handle.ToString()))
                    throw new InvalidOperationException("Onmeetbare arcering heeft een schijnbaar geldige hoeveelheid.");
                LegendBuilder.BuildBlock(doc.Database, tr, analysis, settings, out int rows, out var issues);
                if (rows != 1 || issues.Count != 0 || invalid.NumberOfLoops != 0)
                    throw new InvalidOperationException("Onmeetbare arcering verhindert legenda of wijzigt bron.");
                tr.Abort();
            }
            doc.Editor.WriteMessage("\nHATCHAREAREGRESSION eilanden en gaten, drie stijlen en bron behouden -> OK");
        }, message => doc.Editor.WriteMessage(message), "NlcsLegendaHatchAreaRegression");
    }

    [CommandMethod("NLCSLEGENDAFRAMEVERIFY", CommandFlags.Modal)]
    public void NlcsLegendaFrameVerify()
    {
        var doc = AcApp.DocumentManager.MdiActiveDocument;
        DiagnosticRun.Execute(() =>
        { VerifyFrames(doc.Database); doc.Editor.WriteMessage("\nFRAMEREGRESSION na heropenen -> OK"); }, message => doc.Editor.WriteMessage(message), "NlcsLegendaFrameVerify");
    }
}
