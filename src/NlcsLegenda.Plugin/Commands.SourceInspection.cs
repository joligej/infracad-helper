using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Runtime;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Core.Application;
using NlcsLegenda.Core;

namespace NlcsLegenda.Plugin;

public partial class Commands
{
    [CommandMethod("NLCSLEGENDASOURCEINSPECTION", CommandFlags.Modal)]
    public void InspectLegendSources()
    {
        var doc = AcApp.DocumentManager.MdiActiveDocument;
        DiagnosticRun.Execute(() =>
        {
            string folder = Environment.GetEnvironmentVariable("NLCS_INSPECT_FOLDER") ?? throw new InvalidOperationException("Bronmap ontbreekt.");
            var files = new List<object>();
            foreach (string path in System.IO.Directory.GetFiles(folder, "*.dwg"))
            {
                using var db = new Database(false, true);
                db.ReadDwgFile(path, FileOpenMode.OpenForReadAndAllShare, true, "");
                db.CloseInput(true);
                using var tr = db.TransactionManager.StartTransaction();
                var blocks = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                var blockData = new List<object>();
                foreach (ObjectId id in blocks)
                {
                    var block = (BlockTableRecord)tr.GetObject(id, OpenMode.ForRead);
                    if (block.IsFromExternalReference) { blockData.Add(new { block.Name, block.PathName, xref = true }); continue; }
                    var entities = block.Cast<ObjectId>().Select(i => tr.GetObject(i, OpenMode.ForRead)).OfType<Entity>().ToArray();
                    var texts = entities.Select(e => e switch { MText m => m.Text, DBText t => t.TextString, _ => "" }).Where(t => t.Length > 0).ToArray();
                    var inserts = entities.OfType<BlockReference>().Select(b => new { name = ((BlockTableRecord)tr.GetObject(b.BlockTableRecord, OpenMode.ForRead)).Name, b.Layer,
                        attributes = b.AttributeCollection.Cast<ObjectId>().Select(i => (AttributeReference)tr.GetObject(i, OpenMode.ForRead)).Select(a => new { a.Tag, a.TextString }).ToArray() }).ToArray();
                    if (block.IsLayout || texts.Length > 0 || block.Name.Contains("BOOM", StringComparison.OrdinalIgnoreCase) || block.Name.Contains("PUT", StringComparison.OrdinalIgnoreCase))
                        blockData.Add(new { block.Name, count = entities.Length, texts, inserts });
                }
                files.Add(new { path, blocks = blockData });
            }
            string output = System.IO.Path.Join(System.IO.Path.GetDirectoryName(doc.Database.Filename)!, "source-inspection.json");
            System.IO.File.WriteAllText(output, System.Text.Json.JsonSerializer.Serialize(files, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
            doc.Editor.WriteMessage("\nSOURCEINSPECTION -> OK");
        }, message => doc.Editor.WriteMessage(message), "SourceInspection");
    }
}
