using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using NlcsLegenda.Core;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Core.Application;

namespace NlcsLegenda.Plugin;

public partial class Commands
{
    [CommandMethod("NLCSLEGENDARESOURCETEST", CommandFlags.Modal)]
    public void NlcsLegendaResourceTest()
    {
        var doc = AcApp.DocumentManager.MdiActiveDocument;
        if (doc is null) return;
        DiagnosticRun.Execute(() =>
        {
            using var db = new Database(true, true);
            using var tr = db.TransactionManager.StartTransaction();
            var model = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForWrite);
            using var transient = new TransientObjects();
            var loose = new Line(Point3d.Origin, new Point3d(1, 0, 0));
            var registered = new Line(Point3d.Origin, new Point3d(2, 0, 0));
            transient.Items.Add(loose);
            transient.Items.Add(registered);
            var id = model.AppendEntity(registered);
            tr.AddNewlyCreatedDBObject(registered, true);
            transient.Dispose();
            transient.Dispose();
            if (!loose.IsDisposed || registered.IsDisposed || registered.IsErased
                || Math.Abs(((Line)tr.GetObject(id, OpenMode.ForRead)).Length - 2) > 1e-9)
                throw new InvalidOperationException("Losse objecten niet vrijgegeven of transactieobject voortijdig gesloten.");
            using var erased = new Line(Point3d.Origin, new Point3d(3, 0, 0));
            var erasedId = model.AppendEntity(erased);
            tr.AddNewlyCreatedDBObject(erased, true);
            erased.Erase();
            var ids = new[] { ObjectId.Null, erasedId, id };
            if (LegendManagement.Entities(ids, tr, OpenMode.ForRead).Single().ObjectId != id)
                throw new InvalidOperationException("Verwijderde groepsleden zijn niet overgeslagen.");
            LegendManagement.AddToGroup(db, tr, "RESOURCE", new[] { id });
            if (!LegendManagement.TryGetGroupExtents(db, tr, "RESOURCE", out var extents)
                || Math.Abs(extents.MaxPoint.X - extents.MinPoint.X - 2) > 1e-9)
                throw new InvalidOperationException("De geregistreerde groep is voortijdig gesloten.");
            var resolved = LegendManagement.ResolveHandles(db, new[] { id.Handle.ToString(), "ongeldig", "FFFFFFFFFFFFFFFFF" }, out var missing);
            if (resolved.Length != 1 || resolved[0] != id || missing != 2)
                throw new InvalidOperationException("Ongeldige handles blokkeren de geldige selectie.");
            tr.Commit();
            doc.Editor.WriteMessage("\nRESOURCE losse objecten vrij, transactieobject behouden, dubbel opruimen veilig -> OK");
        }, message => doc.Editor.WriteMessage(message));
    }
}
