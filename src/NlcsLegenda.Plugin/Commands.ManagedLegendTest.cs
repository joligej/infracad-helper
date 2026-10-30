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
    [CommandMethod("NLCSLEGENDAMANAGEDTEST", CommandFlags.Modal)]
    public void TestManagedLegends()
    {
        var doc = AcApp.DocumentManager.MdiActiveDocument;
        DiagnosticRun.Execute(() =>
        {
            var db = doc.Database; using var tr = db.TransactionManager.StartTransaction();
            var model = ReviewModel(db, tr);
            ObjectId Line(string name, short color, double y)
            {
                using var line = new Line(new(0, y, 0), new(10, y, 0)) { LayerId = ReviewLayer(db, tr, name), Color = Color.FromColorIndex(ColorMethod.ByAci, color) };
                ReviewAppend(model, tr, line); return line.ObjectId;
            }
            var a = Line("B-WE-RI-GWA-G", 1, 0); var b = Line("N-WE-RI-IT_RIOOL-G", 3, 5);
            var registry = new LegendRegistry();
            var definition = new LegendDefinition { Name = "Vaste legenda", GroupName = LegendRegistry.NewGroupName(),
                Settings = new() { Scale = 500, IncludeRemarks = false, IncludeScaleBar = false, Columns = 1 } };
            registry.Add(definition);
            if (BuildManagedLegend(db, tr, registry, definition, out var initial, out _) != UpdateResult.Updated || initial != 2)
                throw new InvalidOperationException("Eerste samenstelling is niet twee regels.");
            LegendComposition.Lock(definition);
            var approved = definition.ApprovedRows.Select(r => r.Id).ToHashSet();
            Line("N-WE-GR-HAAG-G", 2, 10);
            ((Entity)tr.GetObject(a, OpenMode.ForWrite)).Color = Color.FromColorIndex(ColorMethod.ByAci, 5);
            BuildManagedLegend(db, tr, registry, definition, out var updated, out var note);
            if (updated != 2 || !note.Contains("voorstel") || !approved.SetEquals(definition.Rows.Select(r => r.Id)))
                throw new InvalidOperationException("Vaste samenstelling of regelidentiteit verloren.");
            var blue = definition.Rows.Single(r => r.Sources.Any(s => s.SourceHandle == a.NonForwardedHandle.ToString()));
            if (!blue.ExampleDisplay.Any(v => v.Color == DrawingAnalyzer.ColorSignature(Color.FromColorIndex(ColorMethod.ByAci, 5))))
                throw new InvalidOperationException("Bronkleur niet gevolgd.");
            var bLayer = (LayerTableRecord)tr.GetObject(((Entity)tr.GetObject(b, OpenMode.ForRead)).LayerId, OpenMode.ForWrite);
            bLayer.IsPlottable = false;
            BuildManagedLegend(db, tr, registry, definition, out updated, out _);
            if (updated != 1) throw new InvalidOperationException("Niet-plottende regel is teruggekomen.");
            bLayer.IsPlottable = true;
            BuildManagedLegend(db, tr, registry, definition, out updated, out _);
            if (updated != 2) throw new InvalidOperationException("Goedgekeurde regel niet hervat na inschakelen.");
            var layouts = (DBDictionary)tr.GetObject(db.LayoutDictionaryId, OpenMode.ForRead);
            var layout = layouts.Cast<System.Collections.DictionaryEntry>().Select(p => (Layout)tr.GetObject((ObjectId)p.Value!, OpenMode.ForRead)).First(l => !l.ModelType);
            LegendManagement.TryGetGroupExtents(db, tr, definition.GroupName, out var before);
            var paperOrigin = new Point3d(100, 200, 0);
            MoveLegend(db, tr, definition, layout.BlockTableRecordId, paperOrigin);
            LegendManagement.TryGetGroupExtents(db, tr, definition.GroupName, out var paperBounds);
            double expectedWidth = (before.MaxPoint.X - before.MinPoint.X) * 2;
            if (Math.Abs(paperBounds.MaxPoint.X - paperBounds.MinPoint.X - expectedWidth) > 1e-6) throw new InvalidOperationException("Papiermaat gewijzigd bij verplaatsen.");
            BuildManagedLegend(db, tr, registry, definition, out updated, out _);
            LegendManagement.TryGetGroupExtents(db, tr, definition.GroupName, out paperBounds);
            if (Math.Abs(paperBounds.MaxPoint.X - paperBounds.MinPoint.X - expectedWidth) > 1e-5 || updated != 2)
                throw new InvalidOperationException("Papiermaat of samenstelling gewijzigd bij bijwerken.");
            MoveLegend(db, tr, definition, model.ObjectId, new(1000, 1000, 0));
            BuildManagedLegend(db, tr, registry, definition, out updated, out _);
            LegendManagement.TryGetGroupExtents(db, tr, definition.GroupName, out var back);
            if (Math.Abs(back.MaxPoint.X - back.MinPoint.X - expectedWidth / 2) > 1e-5) throw new InvalidOperationException("Terugkeer naar modelruimte verandert papiermaat.");
            var manualLegend = new LegendDefinition { Name = "Alleen eigen regels", Scope = LegendScope.Selection, GroupName = LegendRegistry.NewGroupName(),
                Settings = new() { Scale = 500, IncludeRemarks = false, IncludeScaleBar = false, Columns = 1 } };
            registry.Add(manualLegend);
            var link = LinkedLegendExamples.Create(db, tr, a, Array.Empty<ObjectId>());
            var source = LinkedLegendExamples.Resolve(db, tr, link);
            var manual = new ManualEntry { Id = "bronlijn", Layer = "0", Description = "Eigen leiding", Type = NlcsDrawType.Symbool,
                SampleBlock = LinkedLegendExamples.Capture(db, tr, source, link, manualLegend.Settings), SampleScale = 500,
                Link = link, ExampleMode = ExampleMode.FollowSource, ComparisonKey = "leiding" };
            manualLegend.Settings.ManualEntries.Add(manual);
            BuildManagedLegend(db, tr, registry, manualLegend, out updated, out note);
            if (updated != 1 || note.Contains("bronobject")) throw new InvalidOperationException("Onjuiste bronmelding bij eigen legenda.");
            ((Entity)tr.GetObject(a, OpenMode.ForWrite)).Color = Color.FromColorIndex(ColorMethod.ByAci, 6);
            BuildManagedLegend(db, tr, registry, manualLegend, out _, out _);
            var own = manualLegend.Rows.Single();
            if (ExampleDisplay.Difference(own.SourceDisplay, own.ExampleDisplay).Length > 0) throw new InvalidOperationException("Eigen voorbeeld volgt bron niet.");
            manual.ExampleMode = ExampleMode.Fixed;
            ((Entity)tr.GetObject(a, OpenMode.ForWrite)).Color = Color.FromColorIndex(ColorMethod.ByAci, 2);
            BuildManagedLegend(db, tr, registry, manualLegend, out _, out _);
            own = manualLegend.Rows.Single();
            if (!ExampleDisplay.Difference(own.SourceDisplay, own.ExampleDisplay).Contains("kleur")) throw new InvalidOperationException("Afwijkend vast voorbeeld niet herkend.");
            LegendStore.Save(db, tr, registry); tr.Commit();
            doc.Editor.WriteMessage("\nMANAGED vaste samenstelling, bronkleuren, niet-plotten, model/papier en gekoppelde eigen regels -> OK");
        }, message => doc.Editor.WriteMessage(message));
    }

    [CommandMethod("NLCSLEGENDAMANAGEDVERIFY", CommandFlags.Modal)]
    public void VerifyManagedLegends()
    {
        var doc = AcApp.DocumentManager.MdiActiveDocument;
        DiagnosticRun.Execute(() =>
        {
            var db = doc.Database; using var tr = db.TransactionManager.StartTransaction();
            var registry = LegendStore.Load(db, tr); var main = registry.Legends.Single(l => l.Name == "Vaste legenda");
            var ids = main.Rows.Select(r => r.Id).ToHashSet();
            BuildManagedLegend(db, tr, registry, main, out var count, out var note);
            if (!main.CompositionLocked || count != 2 || !ids.SetEquals(main.Rows.Select(r => r.Id)) || !note.Contains("voorstel"))
                throw new InvalidOperationException("Vaste keuzes niet behouden na heropenen.");
            var manual = registry.Legends.Single(l => l.Name == "Alleen eigen regels");
            BuildManagedLegend(db, tr, registry, manual, out count, out note);
            if (count != 1 || note.Contains("bronobject") || manual.Settings.ManualEntries.Single().ExampleMode != ExampleMode.Fixed)
                throw new InvalidOperationException("Eigen regel niet behouden na heropenen.");
            var own = manual.Rows.Single();
            if (!ExampleDisplay.Difference(own.SourceDisplay, own.ExampleDisplay).Contains("kleur"))
                throw new InvalidOperationException("Bewaarde kleurafwijking ontbreekt.");
            tr.Commit(); doc.Editor.WriteMessage("\nMANAGEDVERIFY vaste keuzes en broncontrole na heropenen -> OK");
        }, message => doc.Editor.WriteMessage(message));
    }
    [CommandMethod("NLCSLEGENDALINKEDXREFTEST", CommandFlags.Modal)]
    public void TestLinkedXrefExamples()
    {
        var doc = AcApp.DocumentManager.MdiActiveDocument;
        DiagnosticRun.Execute(() =>
        {
            string path = System.IO.Path.Join(System.IO.Path.GetDirectoryName(doc.Database.Filename)!, "linked-example-source.dwg");
            using (var source = new Database(true, true))
            {
                using var setup = source.TransactionManager.StartTransaction();
                var types = (LinetypeTable)setup.GetObject(source.LinetypeTableId, OpenMode.ForWrite);
                using var dash = new LinetypeTableRecord { Name = "LINK_DASH", PatternLength = 2, NumDashes = 2 };
                dash.SetDashLengthAt(0, 1.5); dash.SetDashLengthAt(1, -.5); types.Add(dash); setup.AddNewlyCreatedDBObject(dash, true);
                var layerId = ReviewLayer(source, setup, "B-WE-KL-WATER-G");
                var layer = (LayerTableRecord)setup.GetObject(layerId, OpenMode.ForWrite);
                layer.Color = Color.FromColorIndex(ColorMethod.ByAci, 3); layer.LinetypeObjectId = dash.ObjectId;
                layer.LineWeight = LineWeight.LineWeight035; layer.Transparency = new Autodesk.AutoCAD.Colors.Transparency((byte)170);
                var model = ReviewModel(source, setup);
                using var line = new Line(Point3d.Origin, new(10, 0, 0)) { LayerId = layerId }; ReviewAppend(model, setup, line);
                using var boundary = new Polyline(4) { LayerId = layerId, Closed = true };
                boundary.AddVertexAt(0, new(0, 2), 0, 0, 0); boundary.AddVertexAt(1, new(10, 2), 0, 0, 0);
                boundary.AddVertexAt(2, new(10, 7), 0, 0, 0); boundary.AddVertexAt(3, new(0, 7), 0, 0, 0);
                ReviewAppend(model, setup, boundary);
                using var hatch = new Hatch { LayerId = layerId }; ReviewAppend(model, setup, hatch);
                hatch.SetHatchPattern(HatchPatternType.PreDefined, "ANSI31"); hatch.PatternScale = .5; hatch.Associative = true;
                using var loop = new ObjectIdCollection { boundary.ObjectId }; hatch.AppendLoop(HatchLoopTypes.Outermost, loop); hatch.EvaluateHatch(true);
                setup.Commit(); source.SaveAs(path, DwgVersion.Current);
            }
            string Hash() => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.IO.File.ReadAllBytes(path)));
            string before = Hash(); var db = doc.Database;
            var xref = db.AttachXref(path, "LINK_BRON");
            using var tr = db.TransactionManager.StartTransaction();
            using var insert = new BlockReference(Point3d.Origin, xref); ReviewAppend(ReviewModel(db, tr), tr, insert);
            var layers = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
            var mapped = (LayerTableRecord)tr.GetObject(layers["LINK_BRON|B-WE-KL-WATER-G"], OpenMode.ForWrite);
            mapped.Color = Color.FromColorIndex(ColorMethod.ByAci, 1); mapped.LineWeight = LineWeight.LineWeight050;
            mapped.Transparency = new Autodesk.AutoCAD.Colors.Transparency((byte)100);
            var definition = new LegendDefinition { Name = "Gekoppelde xref", Scope = LegendScope.Selection,
                GroupName = LegendRegistry.NewGroupName(), Settings = new() { Scale = 500, IncludeRemarks = false, IncludeScaleBar = false } };
            var registry = new LegendRegistry(); registry.Add(definition);
            foreach (var entity in ((BlockTableRecord)tr.GetObject(xref, OpenMode.ForRead)).Cast<ObjectId>().Select(id => tr.GetObject(id, OpenMode.ForRead)).OfType<Entity>().Where(e => e is Line or Hatch))
            {
                var link = LinkedLegendExamples.Create(db, tr, entity.ObjectId, new[] { insert.ObjectId });
                var source = LinkedLegendExamples.Resolve(db, tr, link);
                definition.Settings.ManualEntries.Add(new ManualEntry { Id = entity.GetType().Name, Layer = "0", Description = entity.GetType().Name,
                    Type = NlcsDrawType.Symbool, Link = link, ExampleMode = ExampleMode.FollowSource,
                    SampleBlock = LinkedLegendExamples.Capture(db, tr, source, link, definition.Settings), SampleScale = 500 });
            }
            for (int i = 0; i < 2; i++)
            {
                mapped.Color = Color.FromColorIndex(ColorMethod.ByAci, (short)(i + 5));
                BuildManagedLegend(db, tr, registry, definition, out var count, out _);
                if (count != 2) throw new InvalidOperationException("Gekoppelde xref-regels ontbreken.");
                foreach (var row in definition.Rows)
                {
                    if (ExampleDisplay.Difference(row.SourceDisplay, row.ExampleDisplay).Length > 0) throw new InvalidOperationException("Eigen xref-voorbeeld wijkt af van de bronlaag.");
                    var blocks = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                    foreach (ObjectId id in (BlockTableRecord)tr.GetObject(blocks[row.SampleBlock], OpenMode.ForRead))
                    {
                        var entity = (Entity)tr.GetObject(id, OpenMode.ForRead);
                        if (((LinetypeTableRecord)tr.GetObject(entity.LinetypeId, OpenMode.ForRead)).IsDependent || ((LayerTableRecord)tr.GetObject(entity.LayerId, OpenMode.ForRead)).IsDependent)
                            throw new InvalidOperationException("Xref-afhankelijke voorbeeldverwijzing achtergebleven.");
                        if (entity is Hatch h && (h.Associative || h.NumberOfLoops != 1 || h.PatternName != "ANSI31"))
                            throw new InvalidOperationException("Arceringspatroon of begrenzing niet zelfstandig behouden.");
                    }
                }
            }
            var layouts = (DBDictionary)tr.GetObject(db.LayoutDictionaryId, OpenMode.ForRead);
            var layout = layouts.Cast<System.Collections.DictionaryEntry>().Select(p => (Layout)tr.GetObject((ObjectId)p.Value!, OpenMode.ForRead)).First(l => !l.ModelType);
            using var view = new Viewport { CenterPoint = new(50, 50, 0), Width = 80, Height = 80, ViewDirection = Vector3d.ZAxis };
            ReviewAppend((BlockTableRecord)tr.GetObject(layout.BlockTableRecordId, OpenMode.ForWrite), tr, view);
            view.On = true; view.ViewHeight = 80;
            var properties = mapped.GetViewportOverrides(view.ObjectId);
            properties.Color = Color.FromColorIndex(ColorMethod.ByAci, 2); properties.LineWeight = LineWeight.LineWeight025;
            properties.Transparency = new Autodesk.AutoCAD.Colors.Transparency((byte)190);
            foreach (var manual in definition.Settings.ManualEntries) manual.Link!.MapViewportHandle = view.Handle.ToString();
            BuildManagedLegend(db, tr, registry, definition, out _, out _);
            foreach (var row in definition.Rows)
                if (ExampleDisplay.Difference(row.SourceDisplay, row.ExampleDisplay).Length > 0 || !row.ExampleDisplay.All(s => s.Color == DrawingAnalyzer.ColorSignature(properties.Color) && s.LineWeight == 25 && s.Transparency == 65))
                    throw new InvalidOperationException("Gekoppeld voorbeeld volgt de gekozen kaartweergave niet.");
            var automatic = DrawingAnalyzer.Analyze(db, tr, new LegendSettings { IncludeRemarks = false, IncludeXrefLayers = true }, excludedIds: LegendManagement.CollectManagedIds(db, tr, registry));
            var candidate = automatic.Entries.First(e => e.Sources.Any(s => s.Layer.StartsWith("LINK_BRON|", StringComparison.Ordinal)));
            var found = LinkedLegendExamples.Find(db, tr, candidate.Sources);
            if (found.ContainerHandles.Single() != insert.Handle.ToString()) throw new InvalidOperationException("Bronkeuze uit Overzicht verliest de xref-koppeling.");
            var automaticLegend = new LegendDefinition { Name = "Automatische xref", GroupName = LegendRegistry.NewGroupName(), Settings = new() { IncludeXrefLayers = true, IncludeRemarks = false } };
            registry.Add(automaticLegend); BuildManagedLegend(db, tr, registry, automaticLegend, out var visibleCount, out _);
            if (visibleCount == 0) throw new InvalidOperationException("Geen xref-bronnen voor de zichtbaarheidstest.");
            LegendComposition.Lock(automaticLegend);
            insert.Visible = false;
            BuildManagedLegend(db, tr, registry, automaticLegend, out var hiddenCount, out _);
            if (hiddenCount != 0) throw new InvalidOperationException("Verborgen xref keert terug als bewaarde regel.");
            bool hidden = false;
            try { LinkedLegendExamples.Resolve(db, tr, found); }
            catch (InvalidOperationException ex) when (ex.Message.Contains("verborgen")) { hidden = true; }
            if (!hidden) throw new InvalidOperationException("Verborgen bovenliggend bronblok niet herkend.");
            insert.Visible = true;
            if (Hash() != before) throw new InvalidOperationException("Bron-xref gewijzigd.");
            LegendStore.Save(db, tr, registry); tr.Commit();
            doc.Editor.WriteMessage("\nLINKEDXREF zelfstandige lijn en arcering, actuele kaderkleuren en bronbestand behouden -> OK");
        }, message => doc.Editor.WriteMessage(message));
    }

}
