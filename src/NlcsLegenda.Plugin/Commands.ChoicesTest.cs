using System.IO;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using NlcsLegenda.Core;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Core.Application;

namespace NlcsLegenda.Plugin;

public partial class Commands
{
    private static AnalysisResult ChoiceAnalysis(Database db, Transaction tr, LegendRegistry registry, LegendSettings settings)
    {
        var probe = settings.Clone();
        probe.ExcludedEntries.Clear(); probe.MergedDimensions.Clear(); probe.NonKlicMergedDimensions.Clear();
        probe.ManualMergeGroups.Clear(); probe.MergeIdenticalStatuses = false;
        return DrawingAnalyzer.Analyze(db, tr, probe, excludedIds: LegendManagement.CollectManagedIds(db, tr, registry), trackSources: true);
    }

    private static ObjectId[] ChoiceMembers(Database db, Transaction tr, LegendDefinition definition)
    {
        var groups = (DBDictionary)tr.GetObject(db.GroupDictionaryId, OpenMode.ForRead);
        return ((Group)tr.GetObject(groups.GetAt(definition.GroupName), OpenMode.ForRead)).GetAllEntityIds();
    }

    private static void CheckChoiceRows(Database db, Transaction tr, LegendRegistry registry, LegendDefinition definition)
    {
        var analysis = ChoiceAnalysis(db, tr, registry, definition.Settings);
        int checkedRows = 0; bool merged = false, symbol = false, fill = false;
        foreach (var top in ChoiceMembers(db, tr, definition))
        {
            foreach (var entity in RegressionEntities(tr, top))
            {
                var keys = LegendManagement.ReadRowKeys(entity, tr);
                if (keys.Count == 0) continue;
                var candidates = ResolveExclusionPick(db, tr, definition.Id, analysis, new[] { entity.ObjectId, top }, out bool row);
                if (!row || !candidates.SelectMany(e => e.ExclusionKeys).ToHashSet(StringComparer.OrdinalIgnoreCase).SetEquals(keys))
                    throw new InvalidOperationException($"Legendaregel niet aanklikbaar: {entity.GetType().Name} / {keys.Count} sleutels.");
                if (ResolveExclusionPick(db, tr, "andere legenda", analysis, new[] { entity.ObjectId, top }, out _).Count != 0)
                    throw new InvalidOperationException("Aanklikken raakt een andere legenda.");
                merged |= keys.Count > 1;
                symbol |= candidates.Any(e => e.SymbolLayer is not null);
                fill |= entity is Hatch;
                checkedRows++;
            }
        }
        if (checkedRows < 8 || !merged || !symbol || !fill)
            throw new InvalidOperationException($"Onvolledige klikproef {definition.Name}: {checkedRows}, samen={merged}, symbool={symbol}, vulling={fill}.");
    }

    [CommandMethod("NLCSLEGENDACHOICESTEST", CommandFlags.Modal)]
    public void NlcsLegendaChoicesTest()
    {
        var doc = AcApp.DocumentManager.MdiActiveDocument; var db = doc.Database;
        DiagnosticRun.Execute(() =>
        {
            using var globalSnapshot = new GlobalSettingsSnapshot();
            var source = Path.Join(Path.GetDirectoryName(db.Filename)!, "choices-klic.dwg");
            CreateKlicSource(source, "Net A", "Net B"); AddKlicXref(db, source, "SIT-BS-KLIC", 1);
            ObjectId sourceSymbol, sourceCircle, sourceAttribute;
            string aId, bId;
            var defaults = new LegendSettings { IncludeXrefLayers = true, IncludeRemarks = false, IncludeScaleBar = false, ExplodeOnPlace = false };
            defaults.NonKlicMergedDimensions.Add(GroupDimension.Diameter);
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var blocks = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForWrite);
                using var block = new BlockTableRecord { Name = "CHOICES_BOOM" };
                blocks.Add(block); tr.AddNewlyCreatedDBObject(block, true);
                using var circle = new Circle(Point3d.Origin, Vector3d.ZAxis, 1);
                sourceCircle = block.AppendEntity(circle); tr.AddNewlyCreatedDBObject(circle, true);
                var model = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForWrite);
                using var symbol = new BlockReference(new Point3d(20, 10, 0), block.ObjectId) { LayerId = ReviewLayer(db, tr, "N-WE-GR-BOOM-S-200") };
                sourceSymbol = model.AppendEntity(symbol); tr.AddNewlyCreatedDBObject(symbol, true);
                using var attribute = new AttributeReference { Tag = "NAAM", TextString = "Boom", Height = 0.5, Position = symbol.Position };
                sourceAttribute = symbol.AttributeCollection.AppendAttribute(attribute); tr.AddNewlyCreatedDBObject(attribute, true);
                foreach (int diameter in new[] { 200, 300 })
                {
                    using var line = new Line(new Point3d(0, diameter, 0), new Point3d(10, diameter, 0))
                        { LayerId = ReviewLayer(db, tr, $"B-WE-RI-VRIJVERVAL_{diameter}-G") };
                    model.AppendEntity(line); tr.AddNewlyCreatedDBObject(line, true);
                }
                using var area = new Polyline { Closed = true, LayerId = ReviewLayer(db, tr, "N-WE-GR-GRAS-V") };
                area.AddVertexAt(0, new Point2d(30, 0), 0, 0, 0); area.AddVertexAt(1, new Point2d(40, 0), 0, 0, 0);
                area.AddVertexAt(2, new Point2d(40, 10), 0, 0, 0); area.AddVertexAt(3, new Point2d(30, 10), 0, 0, 0);
                model.AppendEntity(area); tr.AddNewlyCreatedDBObject(area, true);
                var registry = LegendStore.Load(db, tr);
                var a = IsoDef(registry, defaults.Clone()); a.Name = "Keuzes A"; aId = a.Id; registry.Add(a);
                var b = IsoDef(registry, defaults.Clone()); b.Name = "Keuzes B"; b.Settings.ExplodeOnPlace = true; bId = b.Id; registry.Add(b);
                foreach (var def in new[] { a, b })
                    if (BuildManagedLegend(db, tr, registry, def, out _, out _) != UpdateResult.Updated) throw new InvalidOperationException("Legenda ontbreekt.");
                LegendStore.Save(db, tr, registry); tr.Commit();
            }
            string symbolKey, otherState;
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var registry = LegendStore.Load(db, tr); var a = registry.FindById(aId)!;
                CheckChoiceRows(db, tr, registry, a); CheckChoiceRows(db, tr, registry, registry.FindById(bId)!);
                var analysis = ChoiceAnalysis(db, tr, registry, a.Settings);
                var picked = ResolveExclusionPick(db, tr, aId, analysis, new[] { sourceCircle, sourceSymbol }, out bool row);
                if (row || picked.Count != 1 || picked[0].SymbolLayer is null) throw new InvalidOperationException("Genest bronsymbool niet herkend.");
                symbolKey = LegendSettings.SelectionKey(picked[0]);
                if (ResolveExclusionPick(db, tr, aId, analysis, new[] { sourceAttribute }, out _).SingleOrDefault() is not { SymbolLayer: not null })
                    throw new InvalidOperationException("Symboolattribuut niet herkend.");
                otherState = registry.FindById(bId)!.Settings.ToJson();
                a.Settings.ExcludedEntries.Add(symbolKey); a.Settings.Scale = 500;
                a.Settings.Title = "Mijn titel"; a.Settings.ShowMergedValues = false;
                for (int i = 0; i < 2; i++)
                    if (BuildManagedLegend(db, tr, registry, a, out _, out _) != UpdateResult.Updated) throw new InvalidOperationException("Bijwerken mislukt.");
                var after = DrawingAnalyzer.Analyze(db, tr, a.Settings, excludedIds: LegendManagement.CollectManagedIds(db, tr, registry));
                if (after.Entries.Any(e => e.SymbolLayer is not null) || !a.Settings.ExcludedEntries.Contains(symbolKey) || a.Settings.Title != "Mijn titel")
                    throw new InvalidOperationException("Uitsluiting of eigen instelling kwijt na schaalwijziging en bijwerken.");
                a.Settings.ExcludedEntries.UnionWith(analysis.Entries.Select(LegendSettings.SelectionKey));
                a.Settings.Title = ""; a.Settings.DrawBorder = false; a.Settings.IncludeTitle = false; a.Settings.IncludeFooter = false;
                a.Settings.ExplodeOnPlace = true;
                if (BuildManagedLegend(db, tr, registry, a, out int rows, out _) != UpdateResult.Updated || rows != 0
                    || ChoiceMembers(db, tr, a).Length == 0)
                    throw new InvalidOperationException("Volledig lege legenda niet behouden.");
                LegendStore.Save(db, tr, registry); tr.Commit();
            }
            SaveGlobalDefaults(defaults);
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var registry = LegendStore.Load(db, tr);
                if (LegendManagement.ActiveRegistry(db, tr, registry).FindById(aId) is null)
                    throw new InvalidOperationException("Lege legenda niet bereikbaar voor resetten.");
                tr.Commit();
            }
            var target = new MutationTarget { Scope = MutationScope.Legend, LegendId = aId, ContextLabel = "Keuzes A" };
            if (ResetLegend(doc.Editor, db, target) is { } error) throw new InvalidOperationException(error);
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var registry = LegendStore.Load(db, tr); var a = registry.FindById(aId)!;
                if (a.Name != "Keuzes A" || a.Settings.ExcludedEntries.Count != 0 || Math.Abs(a.Settings.Scale - defaults.Scale) > 1e-9
                    || a.Settings.Title != defaults.Title || registry.FindById(bId)!.Settings.ToJson() != otherState)
                    throw new InvalidOperationException("Resetten behoudt oude keuzes of raakt andere legenda.");
                CheckChoiceRows(db, tr, registry, a);
                a.Settings.ExcludedEntries.Add(symbolKey); a.Settings.Scale = 500; a.Settings.Title = "Mijn titel";
                if (BuildManagedLegend(db, tr, registry, a, out _, out _) != UpdateResult.Updated) throw new InvalidOperationException("Eindcontrole ontbreekt.");
                LegendStore.Save(db, tr, registry); tr.Commit();
            }
            doc.Editor.WriteMessage("\nCHOICES klikken op bron/symbool/rij/groep, schaalvast uitsluiten, leeg bijwerken en resetten -> OK");
        }, message => doc.Editor.WriteMessage(message), "NlcsLegendaChoicesTest");
    }

    [CommandMethod("NLCSLEGENDACHOICESVERIFY", CommandFlags.Modal)]
    public void NlcsLegendaChoicesVerify()
    {
        var doc = AcApp.DocumentManager.MdiActiveDocument; var db = doc.Database;
        DiagnosticRun.Execute(() =>
        {
            using var tr = db.TransactionManager.StartTransaction();
            var registry = LegendStore.Load(db, tr); var a = registry.Legends.Single(d => d.Name == "Keuzes A");
            if (Math.Abs(a.Settings.Scale - 500) > 1e-9 || a.Settings.Title != "Mijn titel" || a.Settings.ExcludedEntries.Count == 0)
                throw new InvalidOperationException("Lokale keuzes niet bewaard.");
            var before = ChoiceAnalysis(db, tr, registry, a.Settings);
            if (before.Entries.Any(e => e.SymbolLayer is null && a.Settings.IsEntryExcluded(LegendSettings.SelectionKey(e))))
                throw new InvalidOperationException("Een andere objectsoort is onbedoeld uitgesloten.");
            if (BuildManagedLegend(db, tr, registry, a, out _, out _) != UpdateResult.Updated) throw new InvalidOperationException("Bijwerken na heropenen mislukt.");
            if (DrawingAnalyzer.Analyze(db, tr, a.Settings, excludedIds: LegendManagement.CollectManagedIds(db, tr, registry)).Entries.Any(e => e.SymbolLayer is not null))
                throw new InvalidOperationException("Uitgevinkt symbool terug na heropenen.");
            CheckChoiceRows(db, tr, registry, registry.Legends.Single(d => d.Name == "Keuzes B"));
            LegendStore.Save(db, tr, registry); tr.Commit();
            doc.Editor.WriteMessage("\nCHOICES na heropenen: instellingen, uitsluitingen en aanklikbare rijen behouden -> OK");
        }, message => doc.Editor.WriteMessage(message), "NlcsLegendaChoicesVerify");
    }
}
