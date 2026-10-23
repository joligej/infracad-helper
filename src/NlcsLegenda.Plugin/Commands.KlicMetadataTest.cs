using System.IO;
using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using NlcsLegenda.Core;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Core.Application;

namespace NlcsLegenda.Plugin;

public partial class Commands
{
    private static void CreateKlicSource(string path, string first, string second)
    {
        using var db = new Database(true, true);
        using (var tr = db.TransactionManager.StartTransaction())
        {
            var model = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForWrite);
            var xml = new XElement("KLIC");
            foreach (var (length, provider, material, diameter) in new[] { (3d, first, "PE", "0.11"), (5d, second, "PVC", "0.16"), (2d, "", "", "0") })
            {
                var suffix = provider.Length == 0 ? "" : "_" + provider.Replace(".", "").ToUpperInvariant();
                var layerId = ReviewLayer(db, tr, "B-WE-KL-DATA" + suffix + "-G");
                using var line = new Line(Point3d.Origin, new Point3d(length, 0, 0)) { LayerId = layerId };
                model.AppendEntity(line); tr.AddNewlyCreatedDBObject(line, true);
                xml.Add(new XElement("KLIC_Administratief", new XElement("Handle", line.Handle.ToString()), new XElement("Netbeheerder", provider)));
                xml.Add(new XElement("KLIC_LeidingLijn", new XElement("Handle", line.Handle.ToString()),
                    new XElement("Materiaal", material), new XElement("DiameterM", diameter)));
            }
            var dictionary = (DBDictionary)tr.GetObject(db.NamedObjectsDictionaryId, OpenMode.ForWrite);
            using var map = new DBDictionary();
            dictionary.SetAt("INFRACAD_MAP_DATA", map); tr.AddNewlyCreatedDBObject(map, true);
            using var tables = new DBDictionary();
            map.SetAt("TABLES", tables); tr.AddNewlyCreatedDBObject(tables, true);
            using var buffer = new MemoryStream();
            using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
            using (var writer = new StreamWriter(zip.CreateEntry("table.xml").Open(), Encoding.UTF8)) writer.Write(xml.ToString());
            var bytes = buffer.ToArray();
            var values = new List<TypedValue>();
            for (int offset = 0; offset < bytes.Length; offset += 127) values.Add(new TypedValue(310, bytes.Skip(offset).Take(127).ToArray()));
            using var record = new Xrecord(); tables.SetAt("test-table", record); tr.AddNewlyCreatedDBObject(record, true);
            using var data = new ResultBuffer(values.ToArray()); record.Data = data;
            tr.Commit();
        }
        using (var tr = db.TransactionManager.StartTransaction())
        {
            var entries = DrawingAnalyzer.Analyze(db, tr, new LegendSettings { MergedDimensions = new() }).Entries;
            if (entries.Count != 3 || Math.Abs(entries.Sum(e => e.Metric.Length) - 10) > 1e-8)
                throw new InvalidOperationException("Lokale KLIC-partities of hoeveelheden onjuist.");
            var defaults = DrawingAnalyzer.Analyze(db, tr, new LegendSettings()).Entries;
            if (defaults.Count != 1 || defaults[0].Netbeheerders.Count != 2
                || Math.Abs(defaults[0].Metric.Length - 10) > 1e-8)
                throw new InvalidOperationException("KLIC-standaard verliest bronnen of hoeveelheden.");
            tr.Commit();
        }
        db.SaveAs(path, DwgVersion.Current);
    }

    [CommandMethod("NLCSLEGENDAKLICTEST", CommandFlags.Modal)]
    public void NlcsLegendaKlicTest()
    {
        var doc = AcApp.DocumentManager.MdiActiveDocument;
        var db = doc.Database;
        DiagnosticRun.Execute(() =>
        {
            string folder = Path.GetDirectoryName(db.Filename)!;
            string a = Path.Join(folder, "klic-a.dwg"), b = Path.Join(folder, "klic-b.dwg"), outer = Path.Join(folder, "klic-outer.dwg");
            CreateKlicSource(a, "Net A", "Net B"); CreateKlicSource(b, "Net C", "Net D");
            using (var wrapper = new Database(true, true))
            {
                AddKlicXref(wrapper, a, "INNER", 1);
                wrapper.SaveAs(outer, DwgVersion.Current);
            }
            AddKlicXref(db, outer, "OUTER", 2); AddKlicXref(db, b, "SECOND", 1);
            CheckKlicDrawing(db);
            doc.Editor.WriteMessage("\nKLIC lokale en geneste xrefs: 4 netbeheerders, onbekend, 30m, samenvoegen en uitsluiten -> OK");
        }, message => doc.Editor.WriteMessage(message), "NlcsLegendaKlicTest");
    }

    private static void AddKlicXref(Database db, string file, string name, double scale)
    {
        var id = db.AttachXref(file, name);
        using var tr = db.TransactionManager.StartTransaction();
        var model = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForWrite);
        using var reference = new BlockReference(Point3d.Origin, id) { ScaleFactors = new Scale3d(scale) };
        model.AppendEntity(reference); tr.AddNewlyCreatedDBObject(reference, true); tr.Commit();
    }

    private static void CheckKlicDrawing(Database db)
    {
        using var tr = db.TransactionManager.StartTransaction();
        var settings = new LegendSettings { IncludeXrefLayers = true, MergedDimensions = new() };
        var entries = DrawingAnalyzer.Analyze(db, tr, settings).Entries;
        if (entries.Count != 5 || Math.Abs(entries.Sum(e => e.Metric.Length) - 30) > 1e-8)
            throw new InvalidOperationException($"KLIC-partities: {entries.Count}; lengte: {entries.Sum(e => e.Metric.Length)}.");
        foreach (var (name, length) in new[] { ("Net A", 6d), ("Net B", 10d), ("Net C", 3d), ("Net D", 5d) })
            if (Math.Abs(entries.Single(e => e.Netbeheerders.Contains(name)).Metric.Length - length) > 1e-8)
                throw new InvalidOperationException("Netbeheerder aan verkeerde bron gekoppeld.");
        settings.MergedDimensions.Add(GroupDimension.Netbeheerder);
        settings.MergedDimensions.Add(GroupDimension.Diameter);
        var merged = DrawingAnalyzer.Analyze(db, tr, settings);
        if (merged.Entries.Count != 1 || merged.Entries[0].Netbeheerders.Count != 4 || Math.Abs(merged.Entries[0].Metric.Length - 30) > 1e-8)
            throw new InvalidOperationException("Samenvoegen verliest bronnen of hoeveelheden.");
        settings.Klic.GroupFields = new() { "Materiaal", "DiameterM" };
        settings.Klic.DescriptionFields = new() { "Materiaal", "DiameterM" };
        var partitioned = DrawingAnalyzer.Analyze(db, tr, settings);
        if (partitioned.Entries.Count != 3 || Math.Abs(partitioned.Entries.Sum(e => e.Metric.Length) - 30) > 1e-8
            || Math.Abs(partitioned.Entries.Single(e => e.DisplayDescription.Contains("Materiaal: PE")).Metric.Length - 9) > 1e-8
            || !partitioned.Entries.Any(e => e.DisplayDescription.Contains("Diameter: 160 mm")))
            throw new InvalidOperationException("KLIC-veldgroepering verliest metadata of hoeveelheden.");
        settings.SortMode = LegendSortMode.Handmatig;
        var reverse = partitioned.Entries.Reverse().Select(LegendOrder.Key).ToList();
        settings.ManualEntryOrder = reverse;
        var ordered = DrawingAnalyzer.Analyze(db, tr, settings);
        if (!ordered.Entries.Select(LegendOrder.Key).SequenceEqual(reverse))
            throw new InvalidOperationException("Handmatige KLIC-volgorde verloren.");
        var partitionBlock = LegendBuilder.BuildBlock(db, tr, ordered, settings, out var partitionRows, out var partitionIssues);
        var partitionText = ((BlockTableRecord)tr.GetObject(partitionBlock, OpenMode.ForRead)).Cast<ObjectId>()
            .Select(id => tr.GetObject(id, OpenMode.ForRead)).OfType<MText>().Select(t => t.Text).ToArray();
        if (partitionRows != 3 || partitionIssues.Count > 0 || !partitionText.Any(t => t.Contains("Diameter: 110 mm")))
            throw new InvalidOperationException("Gekozen KLIC-velden ontbreken in de getekende legenda.");
        settings.Klic = new KlicOptions();
        settings.SortMode = LegendSortMode.Status;
        settings.MergedDimensions.Clear();
        settings.ExcludedEntries.Add(LegendSettings.EntryKey(entries.Single(e => e.Netbeheerders.Contains("Net B"))));
        var filtered = DrawingAnalyzer.Analyze(db, tr, settings);
        if (filtered.Entries.Count != 4 || Math.Abs(filtered.Entries.Sum(e => e.Metric.Length) - 20) > 1e-8)
            throw new InvalidOperationException("Uitsluiten raakt andere netbeheerder.");
        settings.ExcludedEntries.Clear();
        var visible = DrawingAnalyzer.Analyze(db, tr, settings);
        var block = LegendBuilder.BuildBlock(db, tr, visible, settings, out var rows, out var issues);
        var text = ((BlockTableRecord)tr.GetObject(block, OpenMode.ForRead)).Cast<ObjectId>()
            .Select(id => tr.GetObject(id, OpenMode.ForRead)).OfType<MText>().Select(t => t.Text).ToList();
        if (rows != 5 || issues.Count != 0 || !text.Any(t => t.Contains("Net A")) || !text.Any(t => t.Contains("Net D")))
            throw new InvalidOperationException("Netbeheerders ontbreken in getekende legenda.");
        tr.Abort();
    }

    [CommandMethod("NLCSLEGENDAKLICVERIFY", CommandFlags.Modal)]
    public void NlcsLegendaKlicVerify()
    {
        var doc = AcApp.DocumentManager.MdiActiveDocument;
        DiagnosticRun.Execute(() =>
        { CheckKlicDrawing(doc.Database); doc.Editor.WriteMessage("\nKLIC na heropenen: netbeheerders en 30m behouden -> OK"); }, message => doc.Editor.WriteMessage(message), "NlcsLegendaKlicVerify");
    }
}
