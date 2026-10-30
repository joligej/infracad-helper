using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using NlcsLegenda.Core;

namespace NlcsLegenda.Plugin;

internal static class LegendViewport
{
    private const string Key = "NLCSLEGENDA_VIEWPORT";

    public static void SetLayer(Viewport viewport, Database db, Transaction tr, LegendSettings settings)
    {
        string name = (settings.ViewportLayer ?? "").Trim();
        if (!LayerNaming.IsValid(name)) throw new InvalidOperationException("Kies een geldige, lokale laag voor het viewportkader.");
        var layers = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
        LayerTableRecord layer;
        if (layers.Has(name))
            layer = (LayerTableRecord)tr.GetObject(layers[name], OpenMode.ForWrite);
        else
        {
            layers.UpgradeOpen();
            layer = new LayerTableRecord { Name = name };
            layers.Add(layer);
            tr.AddNewlyCreatedDBObject(layer, true);
        }
        layer.IsPlottable = settings.PlotViewportFrame;
        viewport.LayerId = layer.ObjectId;
    }

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

    public static bool Align(Viewport viewport, LegendSettings settings, Extents3d bounds, bool automatic)
    {
        viewport.Locked = false;
        double scale = 1000 / settings.Scale;
        double margin = settings.ViewportMarginMm;
        if (automatic)
        {
            var previous = ViewportMath.CenterOffset(viewport.Width, viewport.Height, settings.ViewportAnchor);
            var anchor = viewport.CenterPoint - new Vector3d(previous.X, previous.Y, 0);
            var plan = ViewportMath.Compute(bounds.MaxPoint.X - bounds.MinPoint.X,
                bounds.MaxPoint.Y - bounds.MinPoint.Y, settings.Scale, margin);
            viewport.Width = plan.PaperWidthMm;
            viewport.Height = plan.PaperHeightMm;
            var offset = ViewportMath.CenterOffset(viewport.Width, viewport.Height, settings.ViewportAnchor);
            viewport.CenterPoint = anchor + new Vector3d(offset.X, offset.Y, 0);
        }
        viewport.PerspectiveOn = false;
        viewport.ViewDirection = Vector3d.ZAxis;
        viewport.ViewTarget = Point3d.Origin;
        viewport.TwistAngle = 0;
        double width = (bounds.MaxPoint.X - bounds.MinPoint.X) * scale;
        double height = (bounds.MaxPoint.Y - bounds.MinPoint.Y) * scale;
        double marginX = Math.Min(margin, Math.Max(0, (viewport.Width - width) / 2));
        double marginY = Math.Min(margin, Math.Max(0, (viewport.Height - height) / 2));
        viewport.ViewCenter = new Point2d(bounds.MinPoint.X + (viewport.Width / 2 - marginX) / scale,
            bounds.MaxPoint.Y - (viewport.Height / 2 - marginY) / scale);
        viewport.ViewHeight = viewport.Height / scale;
        viewport.CustomScale = scale;
        viewport.Locked = true;
        return width <= viewport.Width + 1e-8 && height <= viewport.Height + 1e-8;
    }

    internal static IEnumerable<Viewport> Linked(Database db, Transaction tr, string legendId)
    {
        foreach (DBDictionaryEntry item in (DBDictionary)tr.GetObject(db.LayoutDictionaryId, OpenMode.ForRead))
        {
            var layout = (Layout)tr.GetObject(item.Value, OpenMode.ForRead);
            if (layout.ModelType) continue;
            foreach (ObjectId id in (BlockTableRecord)tr.GetObject(layout.BlockTableRecordId, OpenMode.ForRead))
                if (tr.GetObject(id, OpenMode.ForRead) is Viewport viewport && Read(viewport, tr) is { } link && link.Id == legendId)
                    yield return viewport;
        }
    }

    internal static bool IsPaperSpace(Database db, Transaction tr, LegendDefinition legend)
    {
        if (legend.SpaceHandle.Length == 0) return false;
        var ids = LegendManagement.ResolveHandles(db, new[] { legend.SpaceHandle }, out _);
        return ids.Length == 1 && tr.GetObject(ids[0], OpenMode.ForRead) is BlockTableRecord { IsLayout: true } space
            && !((Layout)tr.GetObject(space.LayoutId, OpenMode.ForRead)).ModelType;
    }

    internal static LegendSettings FitSettings(Database db, Transaction tr, LegendDefinition legend, out double maximumWidth)
    {
        maximumWidth = double.MaxValue;
        var settings = legend.Settings.Clone();
        settings.PaperSpace = IsPaperSpace(db, tr, legend);
        if (!settings.FitExistingWindow) return settings;
        var viewports = Linked(db, tr, legend.Id).ToArray();
        if (viewports.Length == 0 && (legend.WindowWidthMm <= 0 || legend.WindowHeightMm <= 0)) return settings;
        double width = viewports.Length > 0 ? viewports.Min(v => v.Width) - 2 * settings.ViewportMarginMm : legend.WindowWidthMm;
        double height = viewports.Length > 0 ? viewports.Min(v => v.Height) - 2 * settings.ViewportMarginMm : legend.WindowHeightMm;
        double border = settings.DrawBorder ? 2 * settings.BorderMarginMm : 0;
        double minimum = settings.SwatchWidthMm + settings.TextGapMm + 3 * settings.TextHeightMm
            + (settings.IncludeQuantities ? settings.QuantityColumnWidthMm : 0) + border;
        if (width < minimum || height <= 0) throw new InvalidOperationException("Het legendavenster is te klein voor leesbare tekst. Vergroot het kader; de bestaande legenda blijft staan.");
        settings.MaxLegendHeightMm = settings.MaxLegendHeightMm > 0 ? Math.Min(settings.MaxLegendHeightMm, height) : height;
        settings.ColumnWidthMm = Math.Min(settings.ColumnWidthMm, width - border);
        settings.RemarksWidthMm = Math.Min(settings.RemarksWidthMm, width - border);
        maximumWidth = settings.ToModel(width);
        return settings;
    }

    public static bool Sync(Database db, Transaction tr, LegendDefinition legend)
    {
        if (IsPaperSpace(db, tr, legend)) return true;
        if (!LegendManagement.TryGetGroupExtents(db, tr, legend.GroupName, out var bounds)) return true;
        bool fits = true;
        foreach (DBDictionaryEntry item in (DBDictionary)tr.GetObject(db.LayoutDictionaryId, OpenMode.ForRead))
        {
            var layout = (Layout)tr.GetObject(item.Value, OpenMode.ForRead);
            if (layout.ModelType) continue;
            foreach (ObjectId entityId in (BlockTableRecord)tr.GetObject(layout.BlockTableRecordId, OpenMode.ForRead))
            {
                if (tr.GetObject(entityId, OpenMode.ForRead) is not Viewport viewport) continue;
                if (Read(viewport, tr) is not { } link || link.Id != legend.Id) continue;
                viewport.UpgradeOpen();
                SetLayer(viewport, db, tr, legend.Settings);
                fits &= Align(viewport, legend.Settings, bounds, link.Automatic && !legend.Settings.FitExistingWindow);
            }
        }
        return fits;
    }
}
