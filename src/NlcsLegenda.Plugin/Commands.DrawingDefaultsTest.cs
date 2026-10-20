using System.IO;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using NlcsLegenda.Core;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Core.Application;

namespace NlcsLegenda.Plugin;

public partial class Commands
{
    [CommandMethod("NLCSLEGENDADRAWINGDEFAULTSTEST", CommandFlags.Modal)]
    public void NlcsLegendaDrawingDefaultsTest()
    {
        var doc = AcApp.DocumentManager.MdiActiveDocument;
        if (doc is null) return;
        var original = new[] { "CECOLOR", "CELWEIGHT", "CELTSCALE", "CETRANSPARENCY" }.ToDictionary(k => k, AcApp.GetSystemVariable);
        try
        {
            AcApp.SetSystemVariable("CECOLOR", "1");
            AcApp.SetSystemVariable("CELWEIGHT", (short)200);
            AcApp.SetSystemVariable("CELTSCALE", 3.0);
            AcApp.SetSystemVariable("CETRANSPARENCY", (short)70);
            var db = doc.Database;
            using var tr = db.TransactionManager.StartTransaction();
            foreach (bool explode in new[] { false, true })
            {
                var settings = new LegendSettings { ExplodeOnPlace = explode, ManualEntries = new() {
                    new() { Layer = "EIGEN", Description = "Eigen lijn" },
                    new() { Layer = "EIGEN", Description = "Eigen arcering", Type = NlcsDrawType.Arcering, HatchPattern = "ANSI31" } } };
                var registry = new LegendRegistry();
                var definition = IsoDef(registry, settings); registry.Add(definition);
                if (BuildManagedLegend(db, tr, registry, definition, out _, out _) != UpdateResult.Updated)
                    throw new InvalidOperationException("Opbouw met afwijkende tekeninstellingen mislukt.");
                var dictionary = (DBDictionary)tr.GetObject(db.GroupDictionaryId, OpenMode.ForRead);
                var group = (Group)tr.GetObject(dictionary.GetAt(definition.GroupName), OpenMode.ForRead);
                var entities = group.GetAllEntityIds().SelectMany(id =>
                {
                    var entity = (Entity)tr.GetObject(id, OpenMode.ForRead);
                    return entity is BlockReference reference
                        ? new[] { entity }.Concat(((BlockTableRecord)tr.GetObject(reference.BlockTableRecord, OpenMode.ForRead))
                            .Cast<ObjectId>().Select(child => (Entity)tr.GetObject(child, OpenMode.ForRead)))
                        : new[] { entity };
                }).ToArray();
                var wrong = entities.Where(e => !(e is MText { Text: "OPMERKINGEN" }
                        ? e.Color.ColorIndex == 7 : e.Color.IsByLayer) || e.LineWeight != LineWeight.ByLayer || !e.Transparency.IsByLayer
                    || e.LinetypeId != db.ByLayerLinetype || Math.Abs(e.LinetypeScale - 1) > 1e-9).ToArray();
                if (wrong.Length > 0) throw new InvalidOperationException(string.Join("; ", wrong.Select(e => $"{e.GetType().Name}: kleur={e.Color}, lijndikte={e.LineWeight}, transparantie={e.Transparency}, lijntypeschaal={e.LinetypeScale}")));
            }
            doc.Editor.WriteMessage("\nDRAWINGDEFAULTS legenda volgt eigen lagen; actieve tekeninstellingen behouden -> OK");
        }
        catch (System.Exception ex) when (ex is Autodesk.AutoCAD.Runtime.Exception or InvalidOperationException or ArgumentException)
        { doc.Editor.WriteMessage($"\nDRAWINGDEFAULTS FAIL: {ex}"); }
        finally
        {
            foreach (var (name, value) in original) AcApp.SetSystemVariable(name, value);
            if (original.Any(p => !Equals(p.Value, AcApp.GetSystemVariable(p.Key))))
                throw new InvalidOperationException("Tekeninstellingen niet hersteld.");
        }
    }

    [CommandMethod("NLCSLEGENDAHEIGHTLIMITTEST", CommandFlags.Modal)]
    public void NlcsLegendaHeightLimitTest()
    {
        var doc = AcApp.DocumentManager.MdiActiveDocument;
        if (doc is null) return;
        try
        {
            using var tr = doc.Database.TransactionManager.StartTransaction();
            foreach (double scale in new[] { 200.0, 500.0, 1000.0 })
            {
                foreach (double height in new[] { 100.0, 120.0 })
                {
                    var settings = new LegendSettings { Scale = scale, MaxLegendHeightMm = height, DrawBorder = true,
                        ManualEntries = Enumerable.Range(0, 18).Select(i => new ManualEntry { Layer = "EIGEN", Description = $"Eigen regel {i}" }).ToList() };
                    var analysis = DrawingAnalyzer.Analyze(doc.Database, tr, settings);
                    var block = LegendBuilder.BuildBlock(doc.Database, tr, analysis, settings, out var rows, out _);
                    var extents = new Extents3d();
                    foreach (ObjectId id in (BlockTableRecord)tr.GetObject(block, OpenMode.ForRead))
                        extents.AddExtents(((Entity)tr.GetObject(id, OpenMode.ForRead)).GeometricExtents);
                    double measured = (extents.MaxPoint.Y - extents.MinPoint.Y) / settings.ModelUnitsPerPaperMm;
                    if (rows != 18 || measured > height + 0.01)
                        throw new InvalidOperationException($"Hoogte {measured} mm, toegestaan {height} mm; {rows} regels.");
                }
            }
            var tooSmall = new LegendSettings { MaxLegendHeightMm = 1, ManualEntries = new() { new() { Layer = "EIGEN", Description = "Te hoog" } } };
            bool rejected = false;
            try { LegendBuilder.BuildBlock(doc.Database, tr, DrawingAnalyzer.Analyze(doc.Database, tr, tooSmall), tooSmall, out _, out _); }
            catch (InvalidOperationException ex) when (ex.Message.Contains("maximale hoogte")) { rejected = true; }
            if (!rejected) throw new InvalidOperationException("Onhaalbare hoogte niet afgewezen.");
            doc.Editor.WriteMessage("\nHEIGHTLIMIT koppen, notities, schaalbalk en kader binnen 100/120mm op drie schalen -> OK");
        }
        catch (System.Exception ex) when (ex is Autodesk.AutoCAD.Runtime.Exception or InvalidOperationException or ArgumentException)
        { doc.Editor.WriteMessage($"\nHEIGHTLIMIT FAIL: {ex}"); }
    }

    [CommandMethod("NLCSLEGENDABATCHXREFTEST", CommandFlags.Modal)]
    public void NlcsLegendaBatchXrefTest()
    {
        var doc = AcApp.DocumentManager.MdiActiveDocument;
        if (doc is null) return;
        try
        {
            string directory = Path.GetDirectoryName(doc.Database.Filename)!;
            string source = Path.Join(directory, "batch-source.dwg");
            string middle = Path.Join(directory, "batch-middle.dwg");
            string drawing = Path.Join(directory, "batch-xref.dwg");
            string unloaded = Path.Join(directory, "batch-unloaded.dwg");
            CreateXrefLineDwg(source, "B-WE-KL-WATER-G", 5, 3);
            using (var nested = new Database(true, true))
            {
                AddKlicXref(nested, source, "BRON", 3);
                using (var tr = nested.TransactionManager.StartTransaction())
                {
                    var blocks = (BlockTable)tr.GetObject(nested.BlockTableId, OpenMode.ForRead);
                    ((BlockTableRecord)tr.GetObject(blocks["BRON"], OpenMode.ForWrite)).PathName = Path.GetFileName(source);
                    tr.Commit();
                }
                nested.SaveAs(middle, DwgVersion.Current);
            }
            using (var host = new Database(true, true))
            {
                AddKlicXref(host, middle, "TUSSEN", 2);
                ObjectId reference;
                using (var tr = host.TransactionManager.StartTransaction())
                {
                    var blocks = (BlockTable)tr.GetObject(host.BlockTableId, OpenMode.ForRead);
                    reference = blocks["TUSSEN"];
                    ((BlockTableRecord)tr.GetObject(reference, OpenMode.ForWrite)).PathName = Path.GetFileName(middle);
                    tr.Commit();
                }
                host.SaveAs(drawing, DwgVersion.Current);
                using var references = new ObjectIdCollection(new[] { reference });
                host.UnloadXrefs(references);
                host.SaveAs(unloaded, DwgVersion.Current);
            }
            var hashes = new[] { source, middle, drawing, unloaded }.ToDictionary(p => p, Sha256);
            var settings = new LegendSettings { IncludeXrefLayers = true };
            var result = BatchExport.AnalyzeFile(drawing, settings, DescriptionCatalog.Default());
            if (result.Error is not null || result.Entries.Count != 1 || Math.Abs(result.Entries[0].Metric.Length - 30) > 1e-8)
                throw new InvalidOperationException($"Batch-xref: {result.Error ?? ""}; {result.Entries.Count} regels, {result.Entries.Sum(e => e.Metric.Length)} m in plaats van 30 m.");
            var unavailable = BatchExport.AnalyzeFile(unloaded, settings, DescriptionCatalog.Default());
            if (unavailable.Error is not null || unavailable.Entries.Count != 0)
                throw new InvalidOperationException("Een ontladen batch-xref wordt alsnog meegenomen.");
            settings.SetAllXrefs(false);
            var excluded = BatchExport.AnalyzeFile(drawing, settings, DescriptionCatalog.Default());
            if (excluded.Error is not null || excluded.Entries.Count > 0 || hashes.Any(p => Sha256(p.Key) != p.Value))
                throw new InvalidOperationException("Batch-xrefuitsluiting of bronbehoud mislukt.");
            doc.Editor.WriteMessage("\nBATCHXREF relatieve geneste xref 30m, uitgesloten en ontladen 0m, bestanden ongewijzigd -> OK");
        }
        catch (System.Exception ex) when (ex is Autodesk.AutoCAD.Runtime.Exception or InvalidOperationException or ArgumentException or IOException)
        { doc.Editor.WriteMessage($"\nBATCHXREF FAIL: {ex}"); }
    }
}
