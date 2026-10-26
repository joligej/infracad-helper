using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using NlcsLegenda.Core;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Core.Application;

namespace NlcsLegenda.Plugin;

public partial class Commands
{
    [CommandMethod("NLCSLEGENDAVIEWPORTREGRESSION", CommandFlags.Modal)]
    public void NlcsLegendaViewportRegression()
    {
        var doc = AcApp.DocumentManager.MdiActiveDocument;
        if (doc is null) return;
        DiagnosticRun.Execute(() =>
        {
            var db = doc.Database;
            using var tr = db.TransactionManager.StartTransaction();
            var paper = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockPaperSpaceId(db), OpenMode.ForWrite);
            var bounds = new Extents3d(new Point3d(142000, 471000, 0), new Point3d(142070, 471150, 0));
            foreach (var anchor in Enum.GetValues<ViewportAnchor>())
            {
                foreach (var scale in new[] { 200d, 500d, 1000d })
                {
                    var settings = new LegendSettings { Scale = scale, ViewportAnchor = anchor };
                    using var viewport = new Viewport { Width = 150, Height = 200, CenterPoint = new Point3d(700, 400, 0),
                        ViewTarget = new Point3d(160000, 390000, 0), TwistAngle = .3, ViewHeight = 1500 };
                    paper.AppendEntity(viewport); tr.AddNewlyCreatedDBObject(viewport, true);
                    var offset = ViewportMath.CenterOffset(viewport.Width, viewport.Height, anchor);
                    var point = viewport.CenterPoint - new Vector3d(offset.X, offset.Y, 0);
                    LegendViewport.SetLayer(viewport, db, tr, settings);
                    if (!LegendViewport.Align(viewport, settings, bounds, automatic: true)) throw new InvalidOperationException("Automatisch kader past niet.");
                    var after = ViewportMath.CenterOffset(viewport.Width, viewport.Height, anchor);
                    if (point.DistanceTo(viewport.CenterPoint - new Vector3d(after.X, after.Y, 0)) > 1e-8)
                        throw new InvalidOperationException("Plaatsingsanker verschoven.");
                    if (Math.Abs(viewport.CustomScale - 1000 / scale) > 1e-8 || Math.Abs(viewport.ViewHeight - viewport.Height * scale / 1000) > 1e-8
                        || viewport.ViewTarget != Point3d.Origin || viewport.TwistAngle != 0 || !viewport.Locked)
                        throw new InvalidOperationException("Schaal of kijkrichting niet hersteld.");
                    var layer = (LayerTableRecord)tr.GetObject(viewport.LayerId, OpenMode.ForRead);
                    if (layer.Name != "VIEWPORT-NOPLOT" || layer.IsPlottable) throw new InvalidOperationException("Onjuiste viewportlaag.");
                    var center = viewport.CenterPoint;
                    var width = viewport.Width;
                    var height = viewport.Height;
                    LegendViewport.Align(viewport, settings, bounds, automatic: false);
                    if (viewport.CenterPoint != center || viewport.Width != width || viewport.Height != height)
                        throw new InvalidOperationException("Handmatig kader gewijzigd.");
                    settings.ViewportLayer = "Eigen viewportkader";
                    settings.PlotViewportFrame = true;
                    LegendViewport.SetLayer(viewport, db, tr, settings);
                    if (!((LayerTableRecord)tr.GetObject(viewport.LayerId, OpenMode.ForRead)).IsPlottable)
                        throw new InvalidOperationException("Eigen plotinstelling niet toegepast.");
                }
            }
            tr.Abort();
            doc.Editor.WriteMessage("\nVIEWPORTREGRESSION ankers, lagen, handmatig kader en schaal op 200/500/1000 -> OK");
        }, message => doc.Editor.WriteMessage(message), nameof(NlcsLegendaViewportRegression));
    }
}
