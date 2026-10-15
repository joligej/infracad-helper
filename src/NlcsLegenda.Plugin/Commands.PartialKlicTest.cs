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
    [CommandMethod("NLCSLEGENDAPARTIALKLICTEST", CommandFlags.Modal)]
    public void NlcsLegendaPartialKlicTest()
    {
        var doc = AcApp.DocumentManager.MdiActiveDocument;
        if (doc is null) return;
        try
        {
            string path = Path.Join(Path.GetDirectoryName(doc.Database.Filename), "partial-klic-source.dwg");
            using (var source = new Database(true, true))
            {
                using (var tr = source.TransactionManager.StartTransaction())
                {
                    var layer = ReviewLayer(source, tr, "B-WE-KL-WATER-G");
                    var model = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(source), OpenMode.ForWrite);
                    using var known = new Line(Point3d.Origin, new Point3d(3, 0, 0)) { LayerId = layer };
                    using var missing = new Line(new Point3d(0, 1, 0), new Point3d(5, 1, 0)) { LayerId = layer };
                    model.AppendEntity(known); tr.AddNewlyCreatedDBObject(known, true);
                    model.AppendEntity(missing); tr.AddNewlyCreatedDBObject(missing, true);
                    var xml = new XElement("KLIC", new XElement("KLIC_LeidingLijn",
                        new XElement("Handle", known.Handle.ToString()), new XElement("Materiaal", "PE")));
                    var dictionary = (DBDictionary)tr.GetObject(source.NamedObjectsDictionaryId, OpenMode.ForWrite);
                    using var map = new DBDictionary(); dictionary.SetAt("INFRACAD_MAP_DATA", map); tr.AddNewlyCreatedDBObject(map, true);
                    using var tables = new DBDictionary(); map.SetAt("TABLES", tables); tr.AddNewlyCreatedDBObject(tables, true);
                    using var buffer = new MemoryStream();
                    using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, true))
                    using (var writer = new StreamWriter(zip.CreateEntry("table.xml").Open(), Encoding.UTF8)) writer.Write(xml.ToString());
                    var bytes = buffer.ToArray();
                    var values = new List<TypedValue>();
                    for (int offset = 0; offset < bytes.Length; offset += 127)
                        values.Add(new TypedValue(310, bytes.Skip(offset).Take(127).ToArray()));
                    using var record = new Xrecord(); tables.SetAt("partial", record); tr.AddNewlyCreatedDBObject(record, true);
                    using var data = new ResultBuffer(values.ToArray()); record.Data = data;
                    tr.Commit();
                }
                CheckPartialKlic(source, 8);
                source.SaveAs(path, DwgVersion.Current);
            }
            using var host = new Database(true, true);
            AddKlicXref(host, path, "KLIC", 2);
            CheckPartialKlic(host, 16);
            doc.Editor.WriteMessage("\nPARTIALKLIC lokaal en xref: ontbrekende gegevens zichtbaar, 8/16m behouden -> OK");
        }
        catch (System.Exception ex) { doc.Editor.WriteMessage($"\nPARTIALKLIC FAIL: {ex}"); }
    }

    private static void CheckPartialKlic(Database db, double expectedLength)
    {
        var previous = HostApplicationServices.WorkingDatabase;
        try
        {
            HostApplicationServices.WorkingDatabase = db;
            using var tr = db.TransactionManager.StartTransaction();
            var settings = new LegendSettings { IncludeXrefLayers = true };
            settings.Klic.DescriptionFields.Add("Materiaal");
            var analysis = DrawingAnalyzer.Analyze(db, tr, settings);
            var entry = analysis.Entries.Single();
            if (entry.KlicObjects.Count != 2 || entry.KlicObjects.Count(o => o.HasData) != 1
                || Math.Abs(entry.Metric.Length - expectedLength) > 1e-8
                || !entry.DisplayDescription.Contains("Materiaal: Niet opgegeven; PE"))
                throw new InvalidOperationException("Ontbrekende KLIC-gegevens of bronlengte niet behouden.");
            var block = LegendBuilder.BuildBlock(db, tr, analysis, settings, out var rows, out var issues);
            var texts = ((BlockTableRecord)tr.GetObject(block, OpenMode.ForRead)).Cast<ObjectId>()
                .Select(id => tr.GetObject(id, OpenMode.ForRead)).OfType<MText>().Select(t => t.Text);
            if (rows != 1 || issues.Count > 0 || !texts.Any(t => t.Contains("Materiaal: Niet opgegeven; PE")))
                throw new InvalidOperationException("Ontbrekende KLIC-gegevens niet zichtbaar in de getekende legenda.");
            tr.Abort();
        }
        finally { HostApplicationServices.WorkingDatabase = previous; }
    }
}
