using Autodesk.AutoCAD.Colors;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using NlcsLegenda.Core;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Core.Application;

namespace NlcsLegenda.Plugin;

public partial class Commands
{
    [CommandMethod("NLCSLEGENDAMERGEREGRESSION", CommandFlags.Modal)]
    public void NlcsLegendaMergeRegression()
    {
        var doc = AcApp.DocumentManager.MdiActiveDocument;
        if (doc is null) return;
        DiagnosticRun.Execute(() =>
        {
            var db = doc.Database;
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var model = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForWrite);
                var lines = (LinetypeTable)tr.GetObject(db.LinetypeTableId, OpenMode.ForWrite);
                using var lineType = new LinetypeTableRecord { Name = "MERGE_TEST", PatternLength = 1, NumDashes = 2 };
                lineType.SetDashLengthAt(0, 0.5); lineType.SetDashLengthAt(1, -0.5);
                lines.Add(lineType); tr.AddNewlyCreatedDBObject(lineType, true);
                var lineLayer = ReviewLayer(db, tr, "N-WE-VH-MERGELINE-G", 7);
                foreach (var (scale, color, weight) in new[] { (1.0, (short)7, LineWeight.LineWeight025),
                    (2.0, (short)7, LineWeight.LineWeight025), (1.0, (short)1, LineWeight.LineWeight025),
                    (1.0, (short)7, LineWeight.LineWeight050) })
                {
                    using var line = new Line(Point3d.Origin, new Point3d(10, 0, 0)) { LayerId = lineLayer,
                        LinetypeId = lineType.ObjectId, LinetypeScale = scale, ColorIndex = color, LineWeight = weight };
                    model.AppendEntity(line); tr.AddNewlyCreatedDBObject(line, true);
                }
                foreach (var (name, user) in new[] { ("MERGEHATCH", false), ("MERGEUSER", true) })
                {
                    var layer = ReviewLayer(db, tr, $"N-WE-VH-{name}-A", 7);
                    foreach (var (scale, angle, color, pattern) in new[] {
                        (1.0, 0.0, (short)7, "ANSI31"), (2.0, 0.0, (short)7, "ANSI31"),
                        (1.0, 0.6, (short)7, "ANSI31"), (1.0, 0.0, (short)1, "ANSI31"),
                        (1.0, 0.0, (short)7, "ANSI32") })
                    {
                        if (user && pattern == "ANSI32") continue;
                        using var hatch = new Hatch { LayerId = layer, ColorIndex = color };
                        model.AppendEntity(hatch); tr.AddNewlyCreatedDBObject(hatch, true);
                        hatch.SetHatchPattern(user ? HatchPatternType.UserDefined : HatchPatternType.PreDefined, user ? "_USER" : pattern);
                        hatch.PatternScale = user ? 1 : scale;
                        if (user) hatch.PatternSpace = scale;
                        hatch.PatternAngle = angle;
                        hatch.SetHatchPattern(hatch.PatternType, hatch.PatternName);
                        var points = new Point2dCollection { new(0, 0), new(10, 0), new(10, 10), new(0, 10) };
                        hatch.AppendLoop(HatchLoopTypes.External | HatchLoopTypes.Polyline, points, new DoubleCollection { 0, 0, 0, 0 });
                        hatch.EvaluateHatch(true);
                    }
                }
                tr.Commit();
            }
            var baseline = new LegendSettings { IncludeScaleBar = false, IncludeRemarks = false, IncludeFooter = false };
            AnalysisResult Read(LegendSettings s)
            {
                using var tr = db.TransactionManager.StartTransaction();
                return DrawingAnalyzer.Analyze(db, tr, s);
            }
            var original = Read(baseline);
            void Counts(LegendSettings s, int line, int hatch, int user)
            {
                var result = Read(s);
                foreach (var (name, expected) in new[] { ("MERGELINE", line), ("MERGEHATCH", hatch), ("MERGEUSER", user) })
                {
                    var entries = result.Entries.Where(e => e.Element == name).ToArray();
                    if (entries.Length != expected) throw new InvalidOperationException($"{name}: {entries.Length}/{expected} regels. " +
                        string.Join(" / ", entries.Select(e => e.RenderIdentity)));
                    var before = original.Entries.Where(e => e.Element == name).Aggregate(LayerMetric.Empty, (m, e) => m + e.Metric);
                    var after = entries.Aggregate(LayerMetric.Empty, (m, e) => m + e.Metric);
                    if (before != after) throw new InvalidOperationException("Hoeveelheden gewijzigd bij " + name);
                }
                using var tr = db.TransactionManager.StartTransaction();
                LegendBuilder.BuildBlock(db, tr, result, s, out _, out var issues);
                if (issues.Count != 0) throw new InvalidOperationException("Samengevoegd voorbeeld niet getekend: " + string.Join("; ", issues));
                tr.Abort();
            }
            Counts(baseline, 4, 5, 4);
            var changed = baseline.Clone(); changed.MergeLineScales = true;
            Counts(changed, 3, 5, 4);
            changed.MergeHatchScales = true;
            Counts(changed, 3, 4, 3);
            changed.MergeHatchScales = false; changed.MergeHatchAngles = true;
            Counts(changed, 3, 4, 3);
            changed.MergeHatchScales = true;
            Counts(changed, 3, 3, 2);
            var text = Read(changed).Entries.Select(e => e.DisplayDescription).ToArray();
            if (!text.Any(t => t.Contains("lijnschaal 1 / 2")) || !text.Any(t => t.Contains("arceerschaal 1 / 2"))
                || !text.Any(t => t.Contains("lijnafstand 1 / 2")) || !text.Any(t => t.Contains("hoek 0° /")))
                throw new InvalidOperationException("Samengevoegde uiterlijkwaarden ontbreken.");
            changed.ShowMergedValues = false;
            if (Read(changed).Entries.Any(e => e.DisplayDescription.Contains("lijnschaal")))
                throw new InvalidOperationException("Samengevoegde waarden niet verborgen.");
            changed.ShowMergedValues = true;
            changed.MergeIdenticalStatuses = true;
            Counts(changed, 3, 3, 2);
            changed.MergeIdenticalStatuses = false;
            var lineToHide = original.Entries.First(e => e.Element == "MERGELINE" && original.LayerStyles[e.PrimaryLayer].Color.ColorIndex == 7 && Math.Abs(original.LayerStyles[e.PrimaryLayer].LinetypeScale - 2) < 1e-9);
            changed.ExcludedEntries.Add(LegendSettings.SelectionKey(lineToHide));
            if (Read(changed).Entries.Sum(e => e.Metric.Count) != original.Entries.Sum(e => e.Metric.Count) - lineToHide.Metric.Count)
                throw new InvalidOperationException("Samenvoegen verliest een eerdere uitsluiting.");
            changed.ExcludedEntries.Clear();
            Counts(baseline, 4, 5, 4);
            changed.SetMergeDimension(false, NlcsDrawType.Arcering, GroupDimension.Materiaal, true);
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var registry = LegendStore.Load(db, tr);
                var definition = IsoDef(registry, changed); registry.Add(definition);
                if (BuildManagedLegend(db, tr, registry, definition, out _, out _) != UpdateResult.Updated)
                    throw new InvalidOperationException("Testlegenda niet aangemaakt.");
                LegendStore.Save(db, tr, registry);
                tr.Commit();
            }
            doc.Editor.WriteMessage("\nMERGEREGRESSION lijnschaal, arceerschaal/hoek, kleur/patroon, hoeveelheden, uitsluiting en herstel -> OK");
        }, message => doc.Editor.WriteMessage("\nMERGEREGRESSION FAIL: " + message));
    }

    [CommandMethod("NLCSLEGENDAMERGEVERIFY", CommandFlags.Modal)]
    public void NlcsLegendaMergeVerify()
    {
        var doc = AcApp.DocumentManager.MdiActiveDocument;
        if (doc is null) return;
        DiagnosticRun.Execute(() =>
        {
            using var tr = doc.Database.TransactionManager.StartTransaction();
            var registry = LegendStore.Load(doc.Database, tr);
            var definition = registry.Legends.Single();
            var settings = definition.Settings;
            if (!settings.MergeLineScales || !settings.MergeHatchScales || !settings.MergeHatchAngles
                || !settings.MergeDimensionsFor(false, NlcsDrawType.Arcering).Contains(GroupDimension.Materiaal))
                throw new InvalidOperationException("Samenvoegkeuzes niet bewaard.");
            if (BuildManagedLegend(doc.Database, tr, registry, definition, out _, out _) != UpdateResult.Updated)
                throw new InvalidOperationException("Bijwerken na heropenen mislukt.");
            LegendStore.Save(doc.Database, tr, registry); tr.Commit();
            doc.Editor.WriteMessage("\nMERGEREGRESSION na heropenen: keuzes en bijwerken behouden -> OK");
        }, message => doc.Editor.WriteMessage("\nMERGEREGRESSION FAIL: " + message));
    }
}
