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
    [CommandMethod("NLCSLEGENDAPROJECTTEST", CommandFlags.Modal)]
    public void TestProjectLegends()
    {
        var doc = AcApp.DocumentManager.MdiActiveDocument;
        DiagnosticRun.Execute(() =>
        {
            var db = doc.Database;
            using var tr = db.TransactionManager.StartTransaction();
            var model = ReviewModel(db, tr);
            var layer = ReviewLayer(db, tr, "N-WE-VH-PROJECT-G");
            var record = (LayerTableRecord)tr.GetObject(layer, OpenMode.ForWrite); record.Color = Color.FromColorIndex(ColorMethod.ByAci, 3);
            foreach (double x in new[] { 0d, 100d })
            {
                using var line = new Line(new(x, 0, 0), new(x + 10, 0, 0)) { LayerId = layer };
                ReviewAppend(model, tr, line);
            }
            using var diagonal = new Line(new(-100, 10, 4), new(10, 100, 4)) { LayerId = ReviewLayer(db, tr, "N-WE-VH-BUITEN-G") };
            ReviewAppend(model, tr, diagonal);
            var hidden = ReviewLayer(db, tr, "N-WE-VH-VERBORGEN-G"); ((LayerTableRecord)tr.GetObject(hidden, OpenMode.ForWrite)).IsOff = true;
            var noPlot = ReviewLayer(db, tr, "N-WE-VH-NIETPLOTTEN-G"); ((LayerTableRecord)tr.GetObject(noPlot, OpenMode.ForWrite)).IsPlottable = false;
            foreach (var id in new[] { hidden, noPlot })
            {
                using var line = new Line(Point3d.Origin, new(10, 0, 0)) { LayerId = id }; ReviewAppend(model, tr, line);
            }
            var layouts = (DBDictionary)tr.GetObject(db.LayoutDictionaryId, OpenMode.ForRead);
            var paperLayout = layouts.Cast<System.Collections.DictionaryEntry>().Select(e => (Layout)tr.GetObject((ObjectId)e.Value!, OpenMode.ForRead)).First(l => !l.ModelType);
            var paper = (BlockTableRecord)tr.GetObject(paperLayout.BlockTableRecordId, OpenMode.ForWrite);
            using var view = new Viewport { CenterPoint = new(50, 50, 0), Width = 80, Height = 80,
                ViewDirection = Vector3d.ZAxis, ViewTarget = Point3d.Origin, ViewCenter = Point2d.Origin };
            ReviewAppend(paper, tr, view); view.ViewHeight = 80; view.CustomScale = 1; view.On = true; view.ViewCenter = Point2d.Origin; view.Locked = true;
            var properties = record.GetViewportOverrides(view.ObjectId);
            properties.Color = Color.FromColorIndex(ColorMethod.ByAci, 1); properties.LineWeight = LineWeight.LineWeight050;
            properties.Transparency = new Autodesk.AutoCAD.Colors.Transparency((byte)170);
            var settings = new LegendSettings { IncludeRemarks = false, IncludeScaleBar = false, IncludeFooter = false,
                SourceViewportHandles = new() { view.Handle.ToString() } };
            var analysis = DrawingAnalyzer.Analyze(db, tr, settings);
            var entry = analysis.Entries.Single(); var style = analysis.LayerStyles[entry.PrimaryLayer];
            if (entry.Metric.Count != 1 || Math.Abs(entry.Metric.Length - 10) > 1e-8 || style.Color.ColorIndex != 1
                || style.LineWeight != LineWeight.LineWeight050 || style.Transparency.Alpha != 170)
                throw new InvalidOperationException("Kaartselectie of laagweergave wijkt af.");
            if (!analysis.Coverage.Any(c => c.Details.Contains("buiten kaartvenster"))
                || !analysis.Coverage.Any(c => c.Details.Contains("niet plotten")) || !analysis.Coverage.Any(c => c.Details.Contains("bevroren")))
                throw new InvalidOperationException("Verklaring voor ontbrekende regels ontbreekt.");
            settings.UseViewportAppearance = false;
            var global = DrawingAnalyzer.Analyze(db, tr, settings); if (global.LayerStyles[global.Entries.Single().PrimaryLayer].Color.ColorIndex != 3)
                throw new InvalidOperationException("Algemene laagweergave niet toegepast.");
            var light = new AnalysisResult { Entries = global.Entries, LayerStyles = global.LayerStyles.ToDictionary(p => p.Key,
                p => new LayerDisplayStyle { Color = Color.FromRgb(250, 250, 250) }) };
            var lightBlock = (BlockTableRecord)tr.GetObject(LegendBuilder.BuildBlock(db, tr, light, settings, out _, out _), OpenMode.ForRead);
            var frame = lightBlock.Cast<ObjectId>().Select(id => tr.GetObject(id, OpenMode.ForRead)).OfType<Entity>()
                .Single(e => e.Layer == "NLCS_LEGENDA_LICHT_KADER");
            if (!((LayerTableRecord)tr.GetObject(frame.LayerId, OpenMode.ForRead)).IsPlottable)
                throw new InvalidOperationException("Bijna witte staal heeft geen plotbare rand.");
            settings.UseViewportAppearance = true;
            var registry = LegendStore.Load(db, tr);
            var empty = new LegendDefinition { Name = "Leeg", GroupName = LegendRegistry.NewGroupName() }; registry.Add(empty);
            var definition = new LegendDefinition { Name = "Kaartlegenda", GroupName = LegendRegistry.NewGroupName(), Settings = settings }; registry.Add(definition);
            if (BuildManagedLegend(db, tr, registry, definition, out int count, out _) != UpdateResult.Updated || count != 1)
                throw new InvalidOperationException("Kaartlegenda ontbreekt.");
            if (registry.FindById(empty.Id) is not null || record.Color.ColorIndex != 3 || !view.Locked || view.CustomScale != 1 || view.ViewCenter != Point2d.Origin)
                throw new InvalidOperationException($"Lege registratie of bronbehoud onjuist: leeg={registry.FindById(empty.Id) is not null}, kleur={record.Color.ColorIndex}, locked={view.Locked}, schaal={view.CustomScale}, centrum={view.ViewCenter}.");
            using var text = new MText { Contents = "Handmatig hoogtevoorbeeld", TextHeight = 2.5, Location = new(150, 150, 0) };
            using var height = new DBText { TextString = "+4,64 m NAP", Height = 2.5, Position = new(120, 150, 0) };
            using var leader = new Line(new(115, 145, 0), new(120, 150, 0));
            ReviewAppend(paper, tr, text); ReviewAppend(paper, tr, height); ReviewAppend(paper, tr, leader);
            var manual = CreateAdoptedLegend(db, tr, registry, text.ObjectId, new[] { height.ObjectId, leader.ObjectId });
            manual.Settings.ExplodeOnPlace = true;
            AdoptRow(db, tr, manual, new[] { height.ObjectId, leader.ObjectId }, text.Text, NlcsStatus.Bestaand);
            if (BuildManagedLegend(db, tr, registry, manual, out count, out _) != UpdateResult.Updated || count != 1)
                throw new InvalidOperationException("Handmatige legenda niet overgenomen.");
            if (!height.IsErased || !leader.IsErased || !text.IsErased)
                throw new InvalidOperationException("Oude handmatige regel dubbel blijven staan.");
            LegendStore.Save(db, tr, registry); tr.Commit();
            PurgePending(db); CheckProjectLegends(db);
            doc.Editor.WriteMessage("\nPROJECTLEGENDS kaartgebied, bronredenen, laagweergave, leeg herstel en handmatige layoutregel -> OK");
        }, message => doc.Editor.WriteMessage(message), "ProjectLegends");
    }

    [CommandMethod("NLCSLEGENDAPROJECTVERIFY", CommandFlags.Modal)]
    public void VerifyProjectLegends()
    {
        var doc = AcApp.DocumentManager.MdiActiveDocument;
        DiagnosticRun.Execute(() => { CheckProjectLegends(doc.Database); doc.Editor.WriteMessage("\nPROJECTVERIFY kaartkeuze en handmatig voorbeeld na heropenen -> OK"); },
            message => doc.Editor.WriteMessage(message), "ProjectVerify");
    }

    private static void CheckProjectLegends(Database db)
    {
        using var tr = db.TransactionManager.StartTransaction();
        var registry = LegendStore.Load(db, tr);
        foreach (var definition in registry.Legends.Where(l => l.Name is "Kaartlegenda" || l.Settings.ManualEntries.Count > 0))
        {
            if (BuildManagedLegend(db, tr, registry, definition, out int rows, out _) != UpdateResult.Updated || rows != 1)
                throw new InvalidOperationException("Opgeslagen projectlegenda niet gelijk na bijwerken.");
            if (definition.SpaceHandle.Length > 0)
            {
                var group = (Group)tr.GetObject(((DBDictionary)tr.GetObject(db.GroupDictionaryId, OpenMode.ForRead)).GetAt(definition.GroupName), OpenMode.ForRead);
                if (group.GetAllEntityIds().Any(id => ((Entity)tr.GetObject(id, OpenMode.ForRead)).OwnerId.Handle.ToString() != definition.SpaceHandle))
                    throw new InvalidOperationException("Handmatige legenda uit de eigen layout verplaatst.");
            }
        }
        LegendStore.Save(db, tr, registry); tr.Commit(); PurgePending(db);
    }
}
