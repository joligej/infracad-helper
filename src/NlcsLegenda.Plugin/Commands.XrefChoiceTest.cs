using System.IO;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Runtime;
using NlcsLegenda.Core;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Core.Application;

namespace NlcsLegenda.Plugin;

public partial class Commands
{
    [CommandMethod("NLCSLEGENDAXREFCHOICETEST", CommandFlags.Modal)]
    public void XrefChoiceTest()
    {
        var doc = AcApp.DocumentManager.MdiActiveDocument;
        DiagnosticRun.Execute(() =>
        {
            var db = doc.Database;
            string folder = Path.GetDirectoryName(db.Filename)!;
            string a = Path.Join(folder, "choice-a.dwg"), b = Path.Join(folder, "choice-b.dwg"), outer = Path.Join(folder, "choice-outer.dwg");
            CreateKlicSource(a, "Net A", "Net B");
            CreateKlicSource(b, "Net C", "Net D");
            using (var wrapper = new Database(true, true))
            {
                AddKlicXref(wrapper, a, "INNER", 1);
                wrapper.SaveAs(outer, DwgVersion.Current);
            }
            AddKlicXref(db, outer, "OUTER", 2);
            AddKlicXref(db, b, "SECOND", 1);
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var layers = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForWrite);
                using var record = new LayerTableRecord { Name = "N-WE-KL-KOPPELING-S" };
                var layer = layers.Add(record);
                tr.AddNewlyCreatedDBObject(record, true);
                var model = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForRead);
                foreach (ObjectId id in model)
                    if (tr.GetObject(id, OpenMode.ForWrite) is BlockReference reference) reference.LayerId = layer;
                tr.Commit();
            }
            CheckXrefChoices(db);
            doc.Editor.WriteMessage("\nXREFCHOICE xref-inserts, opgeslagen keuzes en geneste KLIC-bronnen -> OK");
        }, message => doc.Editor.WriteMessage(message), "XrefChoiceTest");
    }

    [CommandMethod("NLCSLEGENDAXREFCHOICEVERIFY", CommandFlags.Modal)]
    public void XrefChoiceVerify()
    {
        var doc = AcApp.DocumentManager.MdiActiveDocument;
        DiagnosticRun.Execute(() =>
        { CheckXrefChoices(doc.Database); doc.Editor.WriteMessage("\nXREFCHOICE na heropenen: bronkeuzes en KLIC-hoeveelheden -> OK"); }, message => doc.Editor.WriteMessage(message), "XrefChoiceVerify");
    }

    private static void CheckXrefChoices(Database db)
    {
        using var tr = db.TransactionManager.StartTransaction();
        var settings = new LegendSettings { MergedDimensions = new() };
        void Check(string name, int rows, double length, int providers)
        {
            var saved = LegendSettings.FromJson(settings.ToJson());
            var result = DrawingAnalyzer.Analyze(db, tr, saved);
            if (result.Entries.Count != rows || Math.Abs(result.Entries.Sum(e => e.Metric.Length) - length) > 1e-8
                || result.Entries.SelectMany(e => e.Netbeheerders).Distinct().Count() != providers
                || result.Entries.Any(e => e.Element == "KOPPELING"))
                throw new InvalidOperationException(name + ": onjuiste bronkeuze of hoeveelheid");
        }
        Check("alles uit", 0, 0, 0);
        settings.XrefInclusion["OUTER"] = true;
        Check("alleen bovenliggende bron", 3, 20, 2);
        settings.SetAllXrefs(false);
        Check("alles uit na eerdere uitzondering", 0, 0, 0);
        settings.XrefInclusion["OUTER"] = false;
        settings.SetAllXrefs(true);
        Check("alles aan na eerdere uitzondering", 5, 30, 4);
        settings.XrefInclusion["OUTER|INNER"] = false;
        Check("geneste bron uit", 3, 10, 2);
        settings.XrefInclusion["OUTER"] = false;
        settings.XrefInclusion["OUTER|INNER"] = true;
        Check("uitgeschakelde bovenliggende bron", 3, 10, 2);
        settings.SetAllXrefs(true);
        settings.MergedDimensions = LegendSettings.DefaultKlicMergeDimensions();
        Check("standaard samenvoeging", 1, 30, 4);
        settings.XrefInclusion["OUTER"] = false;
        Check("standaard met één bron uit", 1, 10, 2);
        tr.Abort();
    }
}
