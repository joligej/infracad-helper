using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using NlcsLegenda.Core;

namespace NlcsLegenda.Plugin;

internal sealed class SourceViewport
{
    public ObjectId Id { get; }
    private readonly Matrix3d _toPaper;
    private readonly Extents3d _paperBounds;
    private readonly HashSet<ObjectId> _frozen;
    private readonly Curve? _clip;
    public bool Appearance { get; }
    public IReadOnlySet<ObjectId> FrozenLayers => _frozen;

    private SourceViewport(Viewport viewport, Transaction tr, bool appearance)
    {
        if (!viewport.On) throw new InvalidOperationException("Een gekozen kaartvenster staat uit. Schakel het in of kies een ander bronvenster.");
        if (viewport.PerspectiveOn) throw new InvalidOperationException("Kies een kaartvenster zonder perspectief.");
        Id = viewport.ObjectId; Appearance = appearance;
        var toWorld = Matrix3d.Rotation(-viewport.TwistAngle, viewport.ViewDirection, viewport.ViewTarget)
            * Matrix3d.Displacement(viewport.ViewTarget.GetAsVector()) * Matrix3d.PlaneToWorld(viewport.ViewDirection);
        _toPaper = Matrix3d.Displacement(viewport.CenterPoint.GetAsVector())
            * Matrix3d.Scaling(viewport.CustomScale, Point3d.Origin)
            * Matrix3d.Displacement(new Vector3d(-viewport.ViewCenter.X, -viewport.ViewCenter.Y, 0)) * toWorld.Inverse();
        var half = new Vector3d(viewport.Width / 2, viewport.Height / 2, 0);
        _paperBounds = new(viewport.CenterPoint - half, viewport.CenterPoint + half);
        _frozen = viewport.GetFrozenLayers().Cast<ObjectId>().ToHashSet();
        if (viewport.NonRectClipOn && !viewport.NonRectClipEntityId.IsNull)
            _clip = tr.GetObject(viewport.NonRectClipEntityId, OpenMode.ForRead) as Curve
                ?? throw new InvalidOperationException("De kaartbegrenzing is geen ondersteunde gesloten contour.");
    }

    public static SourceViewport[] Read(Database db, Transaction tr, LegendSettings settings)
    {
        var ids = LegendManagement.ResolveHandles(db, settings.SourceViewportHandles, out int missing);
        if (missing > 0) throw new InvalidOperationException($"{missing} gekozen kaartvenster(s) ontbreken. Kies opnieuw met NLCSLEGENDABRONVENSTER.");
        return ids.Select(id => tr.GetObject(id, OpenMode.ForRead) is Viewport v
            ? new SourceViewport(v, tr, settings.UseViewportAppearance)
            : throw new InvalidOperationException("Een opgeslagen bronvenster is geen viewport meer.")).ToArray();
    }

    public bool Frozen(ObjectId layer) => _frozen.Contains(layer);

    public bool Intersects(Entity entity, Transaction tr, Matrix3d transform)
    {
        if (!LegendManagement.TryGetEntityExtents(tr, entity, out var bounds)) return true;
        bounds.TransformBy(_toPaper * transform);
        if (bounds.MaxPoint.X < _paperBounds.MinPoint.X || bounds.MinPoint.X > _paperBounds.MaxPoint.X
            || bounds.MaxPoint.Y < _paperBounds.MinPoint.Y || bounds.MinPoint.Y > _paperBounds.MaxPoint.Y) return false;
        if (entity is Curve curve && curve is not Ray && curve is not Xline)
        {
            using var projected = (Curve)curve.GetTransformedCopy(_toPaper * transform);
            var start = projected.StartPoint;
            if (Contains(new Point2d(start.X, start.Y))) return true;
            using var rectangle = new Polyline();
            rectangle.AddVertexAt(0, new(_paperBounds.MinPoint.X, _paperBounds.MinPoint.Y), 0, 0, 0);
            rectangle.AddVertexAt(1, new(_paperBounds.MaxPoint.X, _paperBounds.MinPoint.Y), 0, 0, 0);
            rectangle.AddVertexAt(2, new(_paperBounds.MaxPoint.X, _paperBounds.MaxPoint.Y), 0, 0, 0);
            rectangle.AddVertexAt(3, new(_paperBounds.MinPoint.X, _paperBounds.MaxPoint.Y), 0, 0, 0);
            rectangle.Closed = true;
            using var plane = new Plane(Point3d.Origin, Vector3d.ZAxis);
            using var crossings = new Point3dCollection();
            projected.IntersectWith(_clip ?? rectangle, Intersect.OnBothOperands, plane, crossings, IntPtr.Zero, IntPtr.Zero);
            return crossings.Count > 0;
        }
        if (_clip is null) return true;
        var corners = new[] { new Point2d(bounds.MinPoint.X, bounds.MinPoint.Y), new Point2d(bounds.MaxPoint.X, bounds.MinPoint.Y),
            new Point2d(bounds.MaxPoint.X, bounds.MaxPoint.Y), new Point2d(bounds.MinPoint.X, bounds.MaxPoint.Y) };
        using var box = new Polyline();
        for (int i = 0; i < corners.Length; i++) box.AddVertexAt(i, corners[i], 0, 0, 0);
        box.Closed = true;
        using var hits = new Point3dCollection();
        box.IntersectWith(_clip, Intersect.OnBothOperands, hits, IntPtr.Zero, IntPtr.Zero);
        if (hits.Count > 0) return true;
        if (corners.Any(Contains)) return true;
        var point = _clip.StartPoint;
        return point.X >= bounds.MinPoint.X && point.X <= bounds.MaxPoint.X
            && point.Y >= bounds.MinPoint.Y && point.Y <= bounds.MaxPoint.Y;
    }

    private bool Contains(Point2d point)
    {
        if (_clip is null) return point.X >= _paperBounds.MinPoint.X && point.X <= _paperBounds.MaxPoint.X
            && point.Y >= _paperBounds.MinPoint.Y && point.Y <= _paperBounds.MaxPoint.Y;
        var start = new Point3d(point.X, point.Y, 0);
        if (_clip!.GetClosestPointTo(start, false).DistanceTo(start) < 1e-8) return true;
        using var ray = new Ray { BasePoint = start, UnitDir = new Vector3d(1, 0.123456789, 0).GetNormal() };
        using var hits = new Point3dCollection();
        ray.IntersectWith(_clip, Intersect.OnBothOperands, hits, IntPtr.Zero, IntPtr.Zero);
        return hits.Cast<Point3d>().Distinct().Count() % 2 != 0;
    }

    internal static LayerDisplayStyle LayerStyle(LayerTableRecord layer, Transaction tr, ObjectId viewport = default)
    {
        var color = layer.Color; var weight = layer.LineWeight; var transparency = layer.Transparency; var line = layer.LinetypeObjectId;
        if (!viewport.IsNull && layer.HasOverrides)
        {
            var properties = layer.GetViewportOverrides(viewport);
            if (properties.IsColorOverridden) color = properties.Color;
            if (properties.IsLineWeightOverridden) weight = properties.LineWeight;
            if (properties.IsLinetypeOverridden) line = properties.LinetypeObjectId;
            if (properties.IsTransparencyOverridden) transparency = properties.Transparency;
        }
        return new LayerDisplayStyle { Color = color, Linetype = ((LinetypeTableRecord)tr.GetObject(line, OpenMode.ForRead)).Name,
            LinetypeIdentity = DrawingAnalyzer.LinetypeSignature(line, tr), LineWeight = weight, Transparency = TransparencyValue.From(transparency) };
    }
}
