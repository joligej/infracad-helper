using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using NlcsLegenda.Core;

namespace NlcsLegenda.Plugin;

internal static class LegendViewport
{
    private const string Key = "NLCSLEGENDA_VIEWPORT";

    public static void Link(Viewport viewport, Transaction tr, string legendId, bool automatic)
    {
        if (viewport.ExtensionDictionary.IsNull) viewport.CreateExtensionDictionary();
        var dictionary = (DBDictionary)tr.GetObject(viewport.ExtensionDictionary, OpenMode.ForWrite);
        using var data = new ResultBuffer(new TypedValue((int)DxfCode.Text, legendId),
            new TypedValue((int)DxfCode.Int16, (short)(automatic ? 1 : 0)));
        if (dictionary.Contains(Key))
            ((Xrecord)tr.GetObject(dictionary.GetAt(Key), OpenMode.ForWrite)).Data = data;
        else
        {
            using var record = new Xrecord { Data = data };
            dictionary.SetAt(Key, record);
            tr.AddNewlyCreatedDBObject(record, true);
        }
    }

    private static (string Id, bool Automatic)? Read(Viewport viewport, Transaction tr)
    {
        if (viewport.ExtensionDictionary.IsNull) return null;
        var dictionary = (DBDictionary)tr.GetObject(viewport.ExtensionDictionary, OpenMode.ForRead);
        if (!dictionary.Contains(Key)) return null;
        var record = (Xrecord)tr.GetObject(dictionary.GetAt(Key), OpenMode.ForRead);
        using var data = record.Data;
        var values = data?.AsArray();
        return values is { Length: 2 } && values[0].Value is string id
            ? (id, Convert.ToInt16(values[1].Value) == 1) : null;
    }

    public static void Align(Viewport viewport, LegendSettings settings, Extents3d bounds, bool automatic)
    {
        viewport.Locked = false;
        double scale = 1000 / settings.Scale;
        double margin = settings.ViewportMarginMm;
        if (automatic)
        {
            var topLeft = viewport.CenterPoint + new Vector3d(-viewport.Width / 2, viewport.Height / 2, 0);
            var plan = ViewportMath.Compute(bounds.MaxPoint.X - bounds.MinPoint.X,
                bounds.MaxPoint.Y - bounds.MinPoint.Y, settings.Scale, margin);
            viewport.Width = plan.PaperWidthMm;
            viewport.Height = plan.PaperHeightMm;
            viewport.CenterPoint = topLeft + new Vector3d(viewport.Width / 2, -viewport.Height / 2, 0);
        }
        viewport.ViewDirection = Vector3d.ZAxis;
        viewport.ViewTarget = Point3d.Origin;
        viewport.TwistAngle = 0;
        viewport.ViewCenter = new Point2d(bounds.MinPoint.X + (viewport.Width / 2 - margin) / scale,
            bounds.MaxPoint.Y - (viewport.Height / 2 - margin) / scale);
        viewport.CustomScale = scale;
        viewport.Locked = true;
    }

    public static void Sync(Database db, Transaction tr, LegendDefinition legend)
    {
        if (!LegendManagement.TryGetGroupExtents(db, tr, legend.GroupName, out var bounds)) return;
        foreach (DBDictionaryEntry item in (DBDictionary)tr.GetObject(db.LayoutDictionaryId, OpenMode.ForRead))
        {
            var layout = (Layout)tr.GetObject(item.Value, OpenMode.ForRead);
            if (layout.ModelType) continue;
            foreach (ObjectId entityId in (BlockTableRecord)tr.GetObject(layout.BlockTableRecordId, OpenMode.ForRead))
            {
                if (tr.GetObject(entityId, OpenMode.ForRead) is not Viewport viewport) continue;
                if (Read(viewport, tr) is not { } link || link.Id != legend.Id) continue;
                viewport.UpgradeOpen();
                Align(viewport, legend.Settings, bounds, link.Automatic);
            }
        }
    }
}
