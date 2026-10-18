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
            tr.Commit();
            doc.Editor.WriteMessage("\nRESOURCE losse objecten vrij, transactieobject behouden, dubbel opruimen veilig -> OK");
        }, message => doc.Editor.WriteMessage(message));
    }
}
