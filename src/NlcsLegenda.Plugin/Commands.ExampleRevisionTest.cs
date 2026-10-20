using System.IO;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using NlcsLegenda.Core;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Core.Application;

namespace NlcsLegenda.Plugin;

public partial class Commands
{
    [CommandMethod("NLCSLEGENDAEXAMPLEREVISIONTEST", CommandFlags.Modal)]
    public void ExampleRevisionTest()
    {
        var doc = AcApp.DocumentManager.MdiActiveDocument;
        string path = Path.Join(Path.GetDirectoryName(doc.Database.Filename)!, "revision-library.dwg");
        DiagnosticRun.Execute(() =>
        {
            try
            {
                WriteRevisionLibrary(path, 2, 1);
                using var tr = doc.Database.TransactionManager.StartTransaction();
                var blocks = (BlockTable)tr.GetObject(doc.Database.BlockTableId, OpenMode.ForWrite);
                using var collision = new BlockTableRecord { Name = "REVISION_OTHER" };
                blocks.Add(collision); tr.AddNewlyCreatedDBObject(collision, true);
                var settings = ExampleTestSettings(path);
                settings.ExampleOverrides[LegendOrder.Key(ExampleTestEntry())] = "REVISION_EXAMPLE";
                var analysis = new AnalysisResult { Entries = new[] { ExampleTestEntry() } };
                ObjectId Build() => LegendBuilder.BuildBlock(doc.Database, tr, analysis, settings, out _, out _);
                var first = Build();
                var firstReference = RevisionReference(tr, first);
                CheckRevision(tr, firstReference, 5, 1);
                CheckOther(5, 1);
                WriteRevisionLibrary(path, 4, 5);
                var second = Build();
                var secondReference = RevisionReference(tr, second);
                CheckRevision(tr, secondReference, 10, 5);
                CheckOther(10, 5);
                void CheckOther(double width, short color)
                {
                    using var library = new ExampleLibrary(path, doc.Database, tr);
                    var id = library.Import(doc.Database, tr, library.Examples.Single(e => e.BlockName == "REVISION_OTHER"));
                    if (id == collision.ObjectId) throw new InvalidOperationException("Gelijknamig hostblok gebruikt als bibliotheekvoorbeeld.");
                    using var probe = new BlockReference(Point3d.Origin, id) { ScaleFactors = new Scale3d(2.5) };
                    CheckRevision(tr, probe, width, color);
                }
                CheckRevision(tr, firstReference, 5, 1);
                var repeated = RevisionReference(tr, Build());
                if (repeated.BlockTableRecord != secondReference.BlockTableRecord)
                    throw new InvalidOperationException("Ongewijzigde bibliotheek wordt opnieuw geïmporteerd.");
                File.Delete(path);
                CheckRevision(tr, RevisionReference(tr, Build()), 10, 5);
                settings.ExampleOverrides.Clear();
                CheckRevision(tr, RevisionReference(tr, Build()), 10, 5);
                var registry = new LegendRegistry();
                registry.Legends.Add(new LegendDefinition { Id = "example-revision", Name = "Bijgewerkt voorbeeld", Settings = settings });
                LegendStore.Save(doc.Database, tr, registry);
                var model = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(doc.Database), OpenMode.ForWrite);
                using var old = new BlockReference(Point3d.Origin, first);
                model.AppendEntity(old); tr.AddNewlyCreatedDBObject(old, true);
                tr.Commit();
            }
            finally { if (File.Exists(path)) File.Delete(path); }
            doc.Editor.WriteMessage("\nEXAMPLEREVISION nieuwe bron toegepast, oude legenda gelijk en hergebruik zonder bron -> OK");
        }, message => doc.Editor.WriteMessage(message), "ExampleRevisionTest");
    }

    [CommandMethod("NLCSLEGENDAEXAMPLEREVISIONVERIFY", CommandFlags.Modal)]
    public void ExampleRevisionVerify()
    {
        var doc = AcApp.DocumentManager.MdiActiveDocument;
        DiagnosticRun.Execute(() =>
        {
            using var tr = doc.Database.TransactionManager.StartTransaction();
            var settings = LegendStore.Load(doc.Database, tr).FindById("example-revision")!.Settings;
            var result = LegendBuilder.BuildBlock(doc.Database, tr,
                new AnalysisResult { Entries = new[] { ExampleTestEntry() } }, settings, out _, out _);
            CheckRevision(tr, RevisionReference(tr, result), 10, 5);
            var model = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(doc.Database), OpenMode.ForRead);
            var old = model.Cast<ObjectId>().Select(id => tr.GetObject(id, OpenMode.ForRead)).OfType<BlockReference>().Single();
            CheckRevision(tr, RevisionReference(tr, old.BlockTableRecord), 5, 1);
            tr.Abort();
            doc.Editor.WriteMessage("\nEXAMPLEREVISION na heropenen: nieuwste voorbeeld en oude legenda behouden -> OK");
        }, message => doc.Editor.WriteMessage(message), "ExampleRevisionVerify");
    }

    private static void WriteRevisionLibrary(string path, double length, short color)
    {
        if (File.Exists(path)) File.Delete(path);
        using var source = new Database(true, true);
        using var tr = source.TransactionManager.StartTransaction();
        var blocks = (BlockTable)tr.GetObject(source.BlockTableId, OpenMode.ForWrite);
        using var nested = new BlockTableRecord { Name = "REVISION_NESTED" };
        blocks.Add(nested); tr.AddNewlyCreatedDBObject(nested, true);
        using var line = new Line(Point3d.Origin, new Point3d(length, 0, 0)) { ColorIndex = color };
        nested.AppendEntity(line); tr.AddNewlyCreatedDBObject(line, true);
        using var example = new BlockTableRecord { Name = "REVISION_EXAMPLE" };
        blocks.Add(example); tr.AddNewlyCreatedDBObject(example, true);
        using var reference = new BlockReference(Point3d.Origin, nested.ObjectId);
        example.AppendEntity(reference); tr.AddNewlyCreatedDBObject(reference, true);
        using var other = new BlockTableRecord { Name = "REVISION_OTHER" };
        blocks.Add(other); tr.AddNewlyCreatedDBObject(other, true);
        using var otherReference = new BlockReference(Point3d.Origin, nested.ObjectId);
        other.AppendEntity(otherReference); tr.AddNewlyCreatedDBObject(otherReference, true);
        ExampleLibrary.WriteCatalog(source, tr, new[] {
            new LegendExample(other.Name, "Ander voorbeeld", "WEG", 200, Array.Empty<string>()),
            new LegendExample(example.Name, "Testband", "WEG", 200, new[] { LegendExample.SourceKey(ExampleTestEntry()) }) });
        tr.Commit();
        source.SaveAs(path, DwgVersion.Current);
    }

    private static BlockReference RevisionReference(Transaction tr, ObjectId block) =>
        ((BlockTableRecord)tr.GetObject(block, OpenMode.ForRead)).Cast<ObjectId>()
            .Select(id => tr.GetObject(id, OpenMode.ForRead)).OfType<BlockReference>().Single();

    private static void CheckRevision(Transaction tr, BlockReference reference, double width, short color)
    {
        var nested = RevisionReference(tr, reference.BlockTableRecord);
        var line = ((BlockTableRecord)tr.GetObject(nested.BlockTableRecord, OpenMode.ForRead)).Cast<ObjectId>()
            .Select(id => tr.GetObject(id, OpenMode.ForRead)).OfType<Line>().Single();
        if (Math.Abs(reference.GeometricExtents.MaxPoint.X - reference.GeometricExtents.MinPoint.X - width) > 1e-8
            || line.ColorIndex != color)
            throw new InvalidOperationException("Voorbeeld gebruikt een oude bron of een ander genest blok.");
    }
}
