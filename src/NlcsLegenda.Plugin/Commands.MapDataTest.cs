using System.Collections;
using System.Text.Json;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Runtime;
using NlcsLegenda.Core;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Core.Application;

namespace NlcsLegenda.Plugin;

public partial class Commands
{
    [CommandMethod("NLCSLEGENDAMAPDATATEST", CommandFlags.Modal)]
    public void NlcsLegendaMapDataTest()
    {
        var doc = AcApp.DocumentManager.MdiActiveDocument;
        if (doc is null) return;
        DiagnosticRun.Execute(() =>
        {
            if (HostEnvironment.IsCoreConsole)
                throw new InvalidOperationException("Deze proef vereist een geopende KLIC-tekening of hoofdtekening met KLIC-xrefs in Civil 3D/Map.");
            var map = AppDomain.CurrentDomain.GetAssemblies().Single(a => a.GetName().Name == "ManagedMapApi");
            var application = map.GetType("Autodesk.Gis.Map.HostMapApplicationServices", true)!
                .GetProperty("Application")!.GetValue(null)!;
            var project = application.GetType().GetMethod("GetProjectForDB", new[] { typeof(Database) })!
                .Invoke(application, new object[] { doc.Database })!;
            var tables = project.GetType().GetProperty("ODTables")!.GetValue(project)!;
            var getNames = tables.GetType().GetMethod("GetTableNames")!;
            string[] Names() => ((IEnumerable)getNames.Invoke(tables, null)!).Cast<string>().Order().ToArray();
            var names = Names();
            int documents = AcApp.DocumentManager.Count;
            string[]? expected = null;
            int objects = 0, records = 0;
            double? length = null;
            using var tr = doc.Database.TransactionManager.StartTransaction();
            for (int pass = 0; pass < 3; pass++)
            {
                var settings = new LegendSettings { IncludeInvisibleLayers = true, IncludeXrefLayers = true };
                settings.Klic.PreferredSource = KlicSource.ObjectData;
                if (pass == 1) { settings.Klic.GroupFields = new() { "Materiaal" }; settings.MergedDimensions.Remove(GroupDimension.Diameter); }
                var result = DrawingAnalyzer.Analyze(doc.Database, tr, settings);
                var data = result.Entries.SelectMany(e => e.KlicObjects).DistinctBy(o => o.Identity).ToArray();
                var values = data.SelectMany(o => o.Records.Where(r => r.Source == KlicSource.ObjectData)
                    .Select(r => JsonSerializer.Serialize(new { o.Identity, r.Table, Values = r.Values.OrderBy(v => v.Key) }))).Order().ToArray();
                if (values.Length == 0 || data.Any(o => o.ReadIssues.Count > 0))
                    throw new InvalidOperationException($"Object Data: {values.Length} records; " + string.Join("; ", data.SelectMany(o => o.ReadIssues).Distinct()));
                if (AcApp.DocumentManager.Count != documents || AcApp.DocumentManager.MdiActiveDocument != doc)
                    throw new InvalidOperationException("Documentcontext na de analyse niet behouden.");
                if (!names.SequenceEqual(Names()))
                    throw new InvalidOperationException("Map-tabellen na de analyse niet behouden.");
                if (expected is not null && !expected.SequenceEqual(values))
                    throw new InvalidOperationException("Object Data wijkt af bij opnieuw analyseren.");
                double currentLength = result.Entries.Sum(e => e.Metric.Length);
                if (length.HasValue && Math.Abs(currentLength - length.Value) > .001)
                    throw new InvalidOperationException("Hoeveelheden wijken af bij groeperen.");
                expected = values;
                length = currentLength;
                objects = data.Length;
                records = values.Length;
            }
            tr.Abort();
            doc.Editor.WriteMessage($"\nMAPDATA drie analyses: {objects} objecten, {records} records; Map-tabellen en hoeveelheden behouden -> OK");
        }, message => doc.Editor.WriteMessage(message), nameof(NlcsLegendaMapDataTest));
    }
}
