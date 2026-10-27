using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using NlcsLegenda.Core;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Core.Application;
using Color = Autodesk.AutoCAD.Colors.Color;
using ColorMethod = Autodesk.AutoCAD.Colors.ColorMethod;

namespace NlcsLegenda.Plugin;

public partial class Commands
{
    [CommandMethod("NLCSLEGENDACONTINUITYTEST", CommandFlags.Modal)]
    public void TestLegendContinuity()
    {
        var doc = AcApp.DocumentManager.MdiActiveDocument;
        DiagnosticRun.Execute(() =>
        {
            var db = doc.Database;
            using var tr = db.TransactionManager.StartTransaction();
            var layer = ReviewLayer(db, tr, "N-WE-RI-CONTINUITY-G");
            using var first = new Line(Point3d.Origin, new Point3d(10, 0, 0)) { LayerId = layer, ColorIndex = 1 };
            using var second = new Line(new Point3d(0, 2, 0), new Point3d(20, 2, 0)) { LayerId = layer, ColorIndex = 3 };
            ReviewAppend(ReviewModel(db, tr), tr, first); ReviewAppend(ReviewModel(db, tr), tr, second);
            var settings = new LegendSettings { ExplodeOnPlace = false, IncludeQuantities = false };
            var registry = LegendStore.Load(db, tr);
            var def = new LegendDefinition { Name = "Blijvende keuzes", GroupName = LegendRegistry.NewGroupName(), Settings = settings };
            registry.Add(def);
            if (BuildManagedLegend(db, tr, registry, def, out _, out _) != UpdateResult.Updated) throw new InvalidOperationException("Startlegenda ontbreekt.");
            var excludedIds = LegendManagement.CollectManagedIds(db, tr, registry);
            var analysis = DrawingAnalyzer.Analyze(db, tr, settings, excludedIds: excludedIds);
            var a = analysis.Entries.Single(e => e.Metric.Length == 10);
            var b = analysis.Entries.Single(e => e.Metric.Length == 20);
            settings.ExcludedEntries.UnionWith(a.ExclusionKeys);
            LegendContinuity.SetChoice(settings, b, ChoiceScope.Variant, "Eigen transportleiding");
            settings.SortMode = LegendSortMode.Handmatig;
            settings.ManualEntryOrder = new() { LegendOrder.Key(b), LegendOrder.Key(a) };
            first.ColorIndex = second.ColorIndex = 5;
            if (BuildManagedLegend(db, tr, registry, def, out int count, out _) != UpdateResult.Updated || count != 1)
                throw new InvalidOperationException("Kleurconvergentie heeft uitgesloten bron teruggebracht of twee regels vermengd.");
            analysis = DrawingAnalyzer.Analyze(db, tr, settings, excludedIds: LegendManagement.CollectManagedIds(db, tr, registry));
            if (analysis.Entries.Single().Description != "Eigen transportleiding" || Math.Abs(analysis.Entries.Single().Metric.Length - 20) > 1e-8)
                throw new InvalidOperationException("Eigen omschrijving of bronhoeveelheid verloren.");
            second.Erase();
            if (BuildManagedLegend(db, tr, registry, def, out count, out _) != UpdateResult.Updated || count != 1)
                throw new InvalidOperationException("Ontbrekende bronregel is verdwenen.");
            second.Erase(false);
            // eigen hoogtevoorbeeld met tekst en kruisteken
            using var height = new DBText { TextString = "+5.00", Height = 1, Position = new Point3d(40, 4, 0) };
            using var cross = new Line(new Point3d(39, 3, 0), new Point3d(41, 3, 0));
            ReviewAppend(ReviewModel(db, tr), tr, height); ReviewAppend(ReviewModel(db, tr), tr, cross);
            AdoptRow(db, tr, def, new[] { height.ObjectId, cross.ObjectId }, "Eigen hoogte (m NAP)", NlcsStatus.Bestaand);
            height.Erase(); cross.Erase();
            if (BuildManagedLegend(db, tr, registry, def, out count, out _) != UpdateResult.Updated || count != 2)
                throw new InvalidOperationException("Eigen hoogteregel ontbreekt.");
            TestGenericManholes(db, tr);
            TestFittedLabelSpacing(db, tr);
            LegendStore.Save(db, tr, registry); tr.Commit();
            PurgePending(db);
            doc.Editor.WriteMessage("\nCONTINUITY kleurconvergentie, uitsluiting, eigen tekst, ontbrekende bron en eigen hoogte -> OK");
        }, message => doc.Editor.WriteMessage(message), "ContinuityTest");
    }

    private static void TestFittedLabelSpacing(Database db, Transaction tr)
    {
        var settings = new LegendSettings { Scale = 500, IncludeQuantities = false, IncludeRemarks = false,
            IncludeScaleBar = false, ColumnWidthMm = 65 };
        var entry = new ManualEntry { Layer = "0", Type = NlcsDrawType.Geometrie,
            Description = "Lichtmast (beheerder Noord en Zuid)", Status = NlcsStatus.Bestaand }.ToLegendEntry();
        var analysis = new AnalysisResult { Entries = new[] { entry } };
        var block = LegendBuilder.BuildBlock(db, tr, analysis, settings, out _, out _, maximumWidth: settings.ToModel(75));
        var description = ((BlockTableRecord)tr.GetObject(block, OpenMode.ForRead)).Cast<ObjectId>()
            .Select(id => tr.GetObject(id, OpenMode.ForRead)).OfType<MText>().Single(t => t.Text == entry.Description);
        if (description.ActualHeight < description.TextHeight * 1.4 || description.LineSpacingFactor < .9)
            throw new InvalidOperationException("Omgebroken tekst in een vast venster heeft onvoldoende regelafstand.");
    }

    private static void TestGenericManholes(Database db, Transaction tr)
    {
        var layer = ReviewLayer(db, tr, "N-WE-RI-PUT_GWA-S");
        var block = ReviewBlock(db, tr, "SRI-PUT_GWA-SO");
        using var circle = new Circle(Point3d.Origin, Vector3d.ZAxis, 1);
        ReviewAppend(block, tr, circle);
        using var attribute = new AttributeDefinition { Tag = "PUTNUMMER", TextString = "0", Height = .5, Position = Point3d.Origin };
        ReviewAppend(block, tr, attribute);
        var ids = new List<ObjectId>();
        for (int i = 0; i < 2; i++)
        {
            using var reference = new BlockReference(new Point3d(60 + i * 5, 0, 0), block.ObjectId) { LayerId = layer };
            ReviewAppend(ReviewModel(db, tr), tr, reference); ids.Add(reference.ObjectId);
            using var value = new AttributeReference(); value.SetAttributeFromBlock(attribute, reference.BlockTransform); value.TextString = "GW" + (101 + i);
            reference.AttributeCollection.AppendAttribute(value); tr.AddNewlyCreatedDBObject(value, true);
        }
        var settings = new LegendSettings { GenericManholes = true, IncludeQuantities = false };
        var analysis = DrawingAnalyzer.Analyze(db, tr, settings, ids);
        if (analysis.Entries.Count != 1 || analysis.Entries[0].Description != "GWA-put") throw new InvalidOperationException("Putnummers veroorzaken losse regels.");
        var built = LegendBuilder.BuildBlock(db, tr, analysis, settings, out _, out _);
        var texts = ((BlockTableRecord)tr.GetObject(built, OpenMode.ForRead)).Cast<ObjectId>().Select(id => tr.GetObject(id, OpenMode.ForRead)).OfType<MText>().Select(t => t.Text);
        if (texts.Any(t => t.Contains("GW101", StringComparison.Ordinal) || t.Contains("GW102", StringComparison.Ordinal))) throw new InvalidOperationException("Willekeurig putnummer in voorbeeld.");
        settings.GenericManholes = false;
        if (DrawingAnalyzer.Analyze(db, tr, settings, ids).Entries.Count != 2) throw new InvalidOperationException("Afzonderlijke putnummers kunnen niet worden hersteld.");
        foreach (var id in ids) tr.GetObject(id, OpenMode.ForWrite).Erase();
    }

    [CommandMethod("NLCSLEGENDACONTINUITYVERIFY", CommandFlags.Modal)]
    public void VerifyLegendContinuity()
    {
        var doc = AcApp.DocumentManager.MdiActiveDocument;
        DiagnosticRun.Execute(() =>
        {
            using var tr = doc.Database.TransactionManager.StartTransaction();
            var registry = LegendStore.Load(doc.Database, tr); var def = registry.Legends.Single();
            if (def.Settings.ManualEntries.Single().SampleBlock.Length == 0 || def.Rows.Count != 2) throw new InvalidOperationException("Bewaarde regels ontbreken na heropenen.");
            if (BuildManagedLegend(doc.Database, tr, registry, def, out int count, out _) != UpdateResult.Updated || count != 2)
                throw new InvalidOperationException("Bijwerken na heropenen verliest keuzes.");
            LegendStore.Save(doc.Database, tr, registry); tr.Commit(); PurgePending(doc.Database);
            doc.Editor.WriteMessage("\nCONTINUITYVERIFY opgeslagen regels en voorbeelden na heropenen -> OK");
        }, message => doc.Editor.WriteMessage(message), "ContinuityVerify");
    }

    [CommandMethod("NLCSLEGENDACONTINUITYREAL", CommandFlags.Modal)]
    public void TestRealLegendContinuity()
    {
        var doc = AcApp.DocumentManager.MdiActiveDocument;
        DiagnosticRun.Execute(() =>
        {
            var report = new List<object>();
            using var tr = doc.Database.TransactionManager.StartTransaction();
            var registry = LegendStore.Load(doc.Database, tr);
            var active = LegendManagement.ActiveRegistry(doc.Database, tr, registry);
            if (active.Legends.Count == 0) throw new InvalidOperationException("Geen bestaande legenda in de voorbeeldtekening.");
            foreach (var definition in active.Legends)
            {
                var timer = System.Diagnostics.Stopwatch.StartNew();
                var before = definition.Settings.Clone();
                System.IO.File.WriteAllText(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(doc.Database.Filename)!, "real-before.json"),
                    System.Text.Json.JsonSerializer.Serialize(new { definition.Name, definition.Settings,
                        windows = LegendViewport.Linked(doc.Database, tr, definition.Id).Select(v => new { v.Width, v.Height, v.CustomScale }) }, LegendSettings.JsonOptions));
                var probe = DrawingAnalyzer.Analyze(doc.Database, tr, definition.Settings.Clone(), excludedIds: LegendManagement.CollectManagedIds(doc.Database, tr, registry));
                LegendRowSamples.Seed(doc.Database, tr, definition, probe);
                probe = LegendRowSamples.Retain(probe, definition);
                System.IO.File.WriteAllText(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(doc.Database.Filename)!, "real-analysis.json"),
                    System.Text.Json.JsonSerializer.Serialize(probe.Entries.Select(e => new { e.Description, e.RenderIdentity, e.Status, e.PrimaryLayer, e.Retained, e.SampleBlock, Sources = e.SourceLayers }), LegendSettings.JsonOptions));
                var viewports = LegendViewport.Linked(doc.Database, tr, definition.Id).Select(v => (v.ObjectId, v.Width, v.Height, v.CenterPoint)).ToArray();
                if (BuildManagedLegend(doc.Database, tr, registry, definition, out int rows, out var note) != UpdateResult.Updated)
                    throw new InvalidOperationException("Eerste update mislukt.");
                var first = definition.Rows.Select(row => row.Description).ToArray();
                double firstMs = timer.Elapsed.TotalMilliseconds; timer.Restart();
                if (BuildManagedLegend(doc.Database, tr, registry, definition, out int repeated, out _) != UpdateResult.Updated || repeated != rows)
                    throw new InvalidOperationException("Het aantal regels verandert bij herhaald bijwerken.");
                if (!first.SequenceEqual(definition.Rows.Select(row => row.Description))) throw new InvalidOperationException("Omschrijvingen veranderen bij herhaald bijwerken.");
                if (before.RemarksText != definition.Settings.RemarksText || before.ManualEntries.Count != definition.Settings.ManualEntries.Count)
                    throw new InvalidOperationException("Notities of eigen regels gewijzigd.");
                foreach (var item in viewports)
                {
                    var viewport = (Viewport)tr.GetObject(item.ObjectId, OpenMode.ForRead);
                    if (definition.Settings.FitExistingWindow && (viewport.Width != item.Width || viewport.Height != item.Height || viewport.CenterPoint != item.CenterPoint))
                        throw new InvalidOperationException("Bestaand venster verplaatst of vergroot.");
                }
                report.Add(new { definition.Name, rows, repeated, firstMs, repeatMs = timer.Elapsed.TotalMilliseconds, note,
                    descriptions = first, manual = definition.Settings.ManualEntries.Select(m => m.Description).ToArray() });
            }
            LegendStore.Save(doc.Database, tr, registry); tr.Commit(); PurgePending(doc.Database);
            System.IO.File.WriteAllText(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(doc.Database.Filename)!, "real-continuity.json"),
                System.Text.Json.JsonSerializer.Serialize(report, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
            doc.Editor.WriteMessage("\nCONTINUITYREAL herhaalde update, teksten, eigen regels en vensters -> OK");
        }, message => doc.Editor.WriteMessage(message), "ContinuityReal");
    }
}
