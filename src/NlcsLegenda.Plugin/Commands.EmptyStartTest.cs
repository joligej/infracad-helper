using System.IO;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using NlcsLegenda.Core;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Core.Application;

namespace NlcsLegenda.Plugin;

public partial class Commands
{
    [CommandMethod("NLCSLEGENDAEMPTYSTARTTEST", CommandFlags.Modal)]
    public void TestEmptyStart()
    {
        var doc = AcApp.DocumentManager.MdiActiveDocument;
        DiagnosticRun.Execute(() =>
        {
            var db = doc.Database;
            string source = Path.Join(Path.GetDirectoryName(db.Filename)!, "empty-source.dwg");
            using (var fixture = new Database(true, true))
            {
                using var build = fixture.TransactionManager.StartTransaction();
                using var line = new Line(Point3d.Origin, new(10, 0, 0)) { LayerId = ReviewLayer(fixture, build, "N-WE-KL-WATER-G", 3) };
                ReviewAppend(ReviewModel(fixture, build), build, line); build.Commit(); fixture.SaveAs(source, DwgVersion.Current);
            }
            var xref = db.AttachXref(source, "empty-source");
            using (var insert = db.TransactionManager.StartTransaction())
            {
                using var reference = new BlockReference(Point3d.Origin, xref);
                ReviewAppend(ReviewModel(db, insert), insert, reference); insert.Commit();
            }
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var settings = new LegendSettings { IncludeXrefLayers = false, IncludeTitle = false, IncludeText = false,
                    IncludeGroupHeaders = false, IncludeRemarks = false, IncludeScaleBar = false, IncludeFooter = false,
                    IncludeDate = false, IncludeTotalsRow = false, DrawBorder = false };
                var registry = LegendStore.Load(db, tr);
                var analysis = DrawingAnalyzer.Analyze(db, tr, settings);
                if (analysis.Entries.Count != 0) throw new InvalidOperationException("Uitgesloten xref toch gelezen.");
                var block = LegendBuilder.BuildBlock(db, tr, analysis, settings, out int count, out var issues);
                if (count != 0 || issues.Count != 0) throw new InvalidOperationException("Lege start niet opgebouwd.");
                using var preview = new BlockReference(Point3d.Origin, block);
                ReviewAppend(ReviewModel(db, tr), tr, preview);
                var definition = PlaceLegendAtPoint(tr, db, registry, settings, null, block, preview, new(100, 100, 0));
                definition.Name = "Lege start";
                var marker = ((BlockTableRecord)tr.GetObject(block, OpenMode.ForRead)).Cast<ObjectId>()
                    .Select(id => tr.GetObject(id, OpenMode.ForRead)).OfType<Entity>().Single();
                if (((LayerTableRecord)tr.GetObject(marker.LayerId, OpenMode.ForRead)).IsPlottable
                    || !LegendManagement.TryGetEntityExtents(tr, preview, out _)
                    || LegendManagement.ActiveRegistry(db, tr, registry).Legends.Count != 1)
                    throw new InvalidOperationException("Lege legenda niet selecteerbaar of hulptekst wordt geplot.");
                LegendStore.Save(db, tr, registry); tr.Commit();
            }
            void Update(Action<LegendDefinition> change, int expected, string stage, bool proposal = false)
            {
                doc.Editor.WriteMessage("\nEMPTYSTART " + stage);
                using var tr = db.TransactionManager.StartTransaction();
                var registry = LegendStore.Load(db, tr);
                var definition = registry.Legends.Single(l => l.Name == "Lege start");
                change(definition);
                if (BuildManagedLegend(db, tr, registry, definition, out int count, out var note) != UpdateResult.Updated
                    || count != expected || (proposal && !note.Contains("voorstel")))
                    throw new InvalidOperationException("Lege start: " + stage + $" ({count} regels).");
                LegendStore.Save(db, tr, registry); tr.Commit();
            }
            Update(d => d.Settings.IncludeXrefLayers = true, 1, "xref aan");
            Update(d => d.Settings.IncludeXrefLayers = false, 0, "xref uit");
            Update(d => d.Scope = LegendScope.Selection, 0, "lege selectie");
            Update(d => { d.Scope = LegendScope.WholeDrawing; d.Settings.IncludeXrefLayers = true; LegendComposition.Lock(d); }, 0, "blanco", true);
            Update(d => d.Settings.BlankEntries.Add(new() { Text = "Eigen toelichting" }), 1, "eigen regel");
            Update(d => d.Settings.BlankEntries.Clear(), 0, "eigen regel verwijderen");
            PurgePending(db);
            doc.Editor.WriteMessage("\nEMPTYSTART lege start, xrefs aan/uit, blanco voorstellen en eigen regels -> OK");
        }, message => doc.Editor.WriteMessage(message), "EmptyStart");
    }

    [CommandMethod("NLCSLEGENDAEMPTYSTARTVERIFY", CommandFlags.Modal)]
    public void VerifyEmptyStart()
    {
        var doc = AcApp.DocumentManager.MdiActiveDocument;
        DiagnosticRun.Execute(() =>
        {
            using var tr = doc.Database.TransactionManager.StartTransaction();
            var registry = LegendStore.Load(doc.Database, tr);
            var definition = LegendManagement.ActiveRegistry(doc.Database, tr, registry).Legends.Single(l => l.Name == "Lege start");
            if (!definition.CompositionLocked || definition.Rows.Count != 0) throw new InvalidOperationException("Blanco keuze niet bewaard.");
            definition.CompositionLocked = false;
            if (BuildManagedLegend(doc.Database, tr, registry, definition, out int count, out _) != UpdateResult.Updated || count != 1)
                throw new InvalidOperationException("Heropende lege legenda niet te vullen.");
            LegendStore.Save(doc.Database, tr, registry); tr.Commit();
            PurgePending(doc.Database);
            doc.Editor.WriteMessage("\nEMPTYSTARTVERIFY lege legenda bereikbaar en bijwerkbaar na heropenen -> OK");
        }, message => doc.Editor.WriteMessage(message), "EmptyStartVerify");
    }

    [CommandMethod("NLCSLEGENDAEMPTYGUISEED", CommandFlags.Modal)]
    public void SeedEmptyGui()
    {
        var db = AcApp.DocumentManager.MdiActiveDocument.Database;
        using var tr = db.TransactionManager.StartTransaction();
        using var line = new Line(Point3d.Origin, new(10, 0, 0)) { LayerId = ReviewLayer(db, tr, "N-WE-KL-WATER-G", 3) };
        ReviewAppend(ReviewModel(db, tr), tr, line); tr.Commit();
    }

    [CommandMethod("NLCSLEGENDAEMPTYGUIVERIFY", CommandFlags.Modal)]
    public void VerifyEmptyGui()
    {
        var doc = AcApp.DocumentManager.MdiActiveDocument;
        DiagnosticRun.Execute(() =>
        {
            using var tr = doc.Database.TransactionManager.StartTransaction();
            var registry = LegendManagement.ActiveRegistry(doc.Database, tr, LegendStore.Load(doc.Database, tr));
            if (registry.Legends.Count != 2 || registry.Legends.Any(d => d.Rows.Count > 0)
                || registry.Legends.Count(d => d.CompositionLocked) != 1)
                throw new InvalidOperationException("Leeg of Blanco niet via het normale commando geplaatst.");
            doc.Editor.WriteMessage("\nEMPTYGUI normaal genereren zonder data en bewust blanco met brondata -> OK");
        }, message => doc.Editor.WriteMessage(message), "EmptyGui");
    }

    [CommandMethod("NLCSLEGENDASOURCEVIEWTEST", CommandFlags.Modal)]
    public void TestSourceViewCatalog()
    {
        var doc = AcApp.DocumentManager.MdiActiveDocument;
        DiagnosticRun.Execute(() =>
        {
            var db = doc.Database;
            string layoutBefore = LayoutManager.Current.CurrentLayout; bool tileBefore = db.TileMode;
            var layoutId = LayoutManager.Current.CreateLayout("Kaartbron proef");
            LayoutManager.Current.CurrentLayout = "Kaartbron proef";
            ObjectId enabledId, disabledId;
            using (var build = db.TransactionManager.StartTransaction())
            {
                var layout = (Layout)build.GetObject(layoutId, OpenMode.ForRead);
                var paper = (BlockTableRecord)build.GetObject(layout.BlockTableRecordId, OpenMode.ForWrite);
                using var enabled = new Viewport { CenterPoint = new(100, 100, 0), Width = 160, Height = 120,
                    ViewDirection = Vector3d.ZAxis, ViewCenter = Point2d.Origin };
                ReviewAppend(paper, build, enabled); enabled.CustomScale = 2; enabled.On = true; enabled.Locked = true; enabledId = enabled.ObjectId;
                using var disabled = new Viewport { CenterPoint = new(250, 100, 0), Width = 80, Height = 60 };
                ReviewAppend(paper, build, disabled); disabled.CustomScale = 1; disabled.On = false; disabledId = disabled.ObjectId;
                build.Commit();
            }
            LayoutManager.Current.CurrentLayout = layoutBefore;
            db.TileMode = tileBefore;
            using var tr = db.TransactionManager.StartTransaction();
            var choices = SourceViewportCatalog.Read(db, tr).Where(v => v.Layout == "Kaartbron proef").ToArray();
            if (choices.Count(v => v.Handle == enabledId.Handle.ToString() && v.Available) != 1
                || choices.Single(v => v.Handle == disabledId.Handle.ToString()).Available)
                throw new InvalidOperationException("Inactieve kaartvensters of uit-stand onjuist.");
            var settings = new LegendSettings { SourceViewportHandles = new() { enabledId.Handle.ToString() } };
            if (SourceViewport.Read(db, tr, settings).Length != 1) throw new InvalidOperationException("Bronvenster niet gelezen.");
            settings.SourceViewportHandles = new() { disabledId.Handle.ToString() };
            bool rejected = false;
            try { SourceViewport.Read(db, tr, settings); }
            catch (InvalidOperationException) { rejected = true; }
            var saved = (Viewport)tr.GetObject(enabledId, OpenMode.ForRead);
            if (!rejected || layoutBefore != LayoutManager.Current.CurrentLayout || tileBefore != db.TileMode
                || saved.CustomScale != 2 || saved.CenterPoint != new Point3d(100, 100, 0) || !saved.Locked)
                throw new InvalidOperationException("Uitgeschakeld venster geaccepteerd of bronweergave gewijzigd.");
            tr.Commit();
            doc.Editor.WriteMessage("\nSOURCEVIEW inactieve layouts, bewuste uit-stand en bronweergave behouden -> OK");
        }, message => doc.Editor.WriteMessage(message), "SourceView");
    }
}
