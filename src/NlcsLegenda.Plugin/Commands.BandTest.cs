using System.IO;
using System.Security.Cryptography;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using NlcsLegenda.Core;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Core.Application;

namespace NlcsLegenda.Plugin;

public partial class Commands
{
    [CommandMethod("NLCSLEGENDABANDTEST", CommandFlags.Modal)]
    public void BandTest()
    {
        var doc = AcApp.DocumentManager.MdiActiveDocument;
        try
        {
            string reference = Environment.GetEnvironmentVariable("NLCS_TEMPLATE_REFERENCE") ?? string.Empty;
            string hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(reference)));
            string fixture = Path.Join(Path.GetDirectoryName(doc.Database.Filename)!, "band-source.dwg");
            using (var source = new Database(false, true))
            using (var copy = new Database(true, true))
            {
                source.ReadDwgFile(reference, FileOpenMode.OpenForReadAndAllShare, true, string.Empty);
                source.CloseInput(true);
                using var sourceTr = source.TransactionManager.StartTransaction();
                using var ids = new ObjectIdCollection();
                foreach (var group in BandReferenceLines(source, sourceTr).Values)
                    foreach (var line in group) ids.Add(line.ObjectId);
                using var mapping = new IdMapping();
                source.WblockCloneObjects(ids, SymbolUtilityServices.GetBlockModelSpaceId(copy), mapping, DuplicateRecordCloning.Ignore, false);
                copy.SaveAs(fixture, DwgVersion.Current);
                using var localIds = new ObjectIdCollection();
                using (var tr = copy.TransactionManager.StartTransaction())
                    foreach (ObjectId id in (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(copy), OpenMode.ForRead)) localIds.Add(id);
                using var local = new IdMapping();
                copy.WblockCloneObjects(localIds, SymbolUtilityServices.GetBlockModelSpaceId(doc.Database), local, DuplicateRecordCloning.Ignore, false);
            }
            AddKlicXref(doc.Database, fixture, "BAND", 1);
            CheckBands(doc.Database, reference);
            if (BuildPlaceXrefLegend(doc.Database, new LegendSettings { Scale = 200, IncludeXrefLayers = true }, out var error, out _, out _) != 4 || error != "geen")
                throw new InvalidOperationException("Plaatsing mislukt: " + error);
            if (hash != Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(reference))))
                throw new InvalidOperationException("Referentietemplate gewijzigd.");
            doc.Editor.WriteMessage("\nBAND drie DWT-varianten, vier lijnen, stijlen en drie schalen lokaal/xref -> OK");
        }
        catch (System.Exception ex) when (ex is IOException or InvalidOperationException or Autodesk.AutoCAD.Runtime.Exception)
        { doc.Editor.WriteMessage("\nBAND FAIL: " + ex); }
    }

    [CommandMethod("NLCSLEGENDABANDVERIFY", CommandFlags.Modal)]
    public void BandVerify()
    {
        var doc = AcApp.DocumentManager.MdiActiveDocument;
        try
        {
            CheckBands(doc.Database, Environment.GetEnvironmentVariable("NLCS_TEMPLATE_REFERENCE") ?? string.Empty);
            doc.Editor.WriteMessage("\nBAND na heropenen -> OK");
        }
        catch (System.Exception ex) when (ex is IOException or InvalidOperationException or Autodesk.AutoCAD.Runtime.Exception)
        { doc.Editor.WriteMessage("\nBAND FAIL: " + ex); }
    }

    private static Dictionary<string, Polyline[]> BandReferenceLines(Database db, Transaction tr)
    {
        var model = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForRead);
        var lines = model.Cast<ObjectId>().Select(id => tr.GetObject(id, OpenMode.ForRead)).OfType<Polyline>()
            .Where(p => p.NumberOfVertices == 2).ToArray();
        var result = new Dictionary<string, Polyline[]>();
        foreach (string size in new[] { "100X300", "115X345", "220X440" })
        {
            string element = "KANTOPSLUITING_SCHEIDINGSBAND_" + size;
            var inner = lines.Where(p => p.Layer == "N-WE-VH-" + element + "-G").ToArray();
            if (inner.Length != 2) throw new InvalidOperationException("Binnenlijnen ontbreken: " + size);
            double x = inner[0].GetPoint2dAt(0).X;
            double y = inner.Average(p => p.GetPoint2dAt(0).Y);
            var edges = lines.Where(p => p.Layer == "N-WE-VH-KANTVERHARDING-G"
                && Math.Abs(p.GetPoint2dAt(0).X - x) < 0.002 && Math.Abs(p.GetPoint2dAt(0).Y - y) < 0.3).ToArray();
            if (edges.Length != 2) throw new InvalidOperationException("Kantlijnen ontbreken: " + size);
            result.Add(element, inner.Concat(edges).OrderBy(p => p.GetPoint2dAt(0).Y).ToArray());
        }
        return result;
    }

    private static void CheckBands(Database db, string reference)
    {
        using var source = new Database(false, true);
        source.ReadDwgFile(reference, FileOpenMode.OpenForReadAndAllShare, true, string.Empty);
        source.CloseInput(true);
        using var sourceTr = source.TransactionManager.StartTransaction();
        var expected = BandReferenceLines(source, sourceTr);
        using var tr = db.TransactionManager.StartTransaction();
        var excluded = LegendManagement.CollectManagedIds(db, tr, LegendStore.Load(db, tr));
        foreach (int sourceMode in new[] { 0, 1, 2 })
        foreach (double scale in new[] { 200d, 500d, 1000d })
        {
            var settings = new LegendSettings { Scale = scale, IncludeXrefLayers = sourceMode != 0, DrawSwatchFrame = false };
            var model = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForRead);
            var xrefIds = model.Cast<ObjectId>().Where(id => tr.GetObject(id, OpenMode.ForRead) is BlockReference br
                && ((BlockTableRecord)tr.GetObject(br.BlockTableRecord, OpenMode.ForRead)).IsFromExternalReference).ToArray();
            var analysis = DrawingAnalyzer.Analyze(db, tr, settings, selection: sourceMode == 2 ? xrefIds : null, excludedIds: excluded);
            var entries = analysis.Entries.Where(e => expected.ContainsKey(e.Element)).ToArray();
            if (entries.Length != 3 || entries.Any(e => e.SourceLayers.Count != (sourceMode == 1 ? 2 : 1)))
                throw new InvalidOperationException("Bronkeuze of groepering onjuist.");
            foreach (var entry in entries)
            {
                var single = new AnalysisResult { Entries = new[] { entry }, LayerStyles = analysis.LayerStyles };
                var blockId = LegendBuilder.BuildBlock(db, tr, single, settings, out _, out _);
                var lines = ((BlockTableRecord)tr.GetObject(blockId, OpenMode.ForRead)).Cast<ObjectId>()
                    .Select(id => tr.GetObject(id, OpenMode.ForRead)).OfType<Polyline>().Where(p => p.NumberOfVertices == 2)
                    .OrderBy(p => p.GetPoint2dAt(0).Y).ToArray();
                if (lines.Length != 4) throw new InvalidOperationException("Geen vier lijnen: " + entry.Element);
                var template = expected[entry.Element];
                double sourceMid = template.Average(p => p.GetPoint2dAt(0).Y);
                double actualMid = lines.Average(p => p.GetPoint2dAt(0).Y);
                for (int i = 0; i < 4; i++)
                {
                    void Near(double a, double b)
                    {
                        if (Math.Abs(a - b) > 0.002) throw new InvalidOperationException($"Maatverschil {entry.Element}: {a:R} / {b:R}");
                    }
                    Near(lines[i].Length / settings.ModelUnitsPerPaperMm, template[i].Length / 0.2);
                    Near((lines[i].GetPoint2dAt(0).Y - actualMid) / settings.ModelUnitsPerPaperMm,
                        (template[i].GetPoint2dAt(0).Y - sourceMid) / 0.2);
                    if (lines[i].GetPoint2dAt(0).X <= lines[i].GetPoint2dAt(1).X)
                        throw new InvalidOperationException("Lijnrichting gewijzigd.");
                    var wanted = DrawingAnalyzer.EntityLayerStyle(template[i], sourceTr);
                    var actual = DrawingAnalyzer.EntityStyle(lines[i], tr, null, DrawingAnalyzer.EntityLayerStyle(lines[i], tr));
                    if (actual.Color.ColorIndex != wanted.Color.ColorIndex || actual.LineWeight != wanted.LineWeight
                        || !LayerNaming.LocalName(actual.Linetype).Equals(wanted.Linetype, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidOperationException("Lijnstijl wijkt af: " + entry.Element);
                }
                double expectedLength = template.Where(p => p.Layer.Contains(entry.Element, StringComparison.Ordinal)).Sum(p => p.Length) * (sourceMode == 1 ? 2 : 1);
                if (Math.Abs(entry.Metric.Length - expectedLength) > 0.002)
                    throw new InvalidOperationException("Voorbeeldlijnen wijzigen de hoeveelheid.");
            }
        }
        tr.Abort();
    }
}
