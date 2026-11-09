using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using NlcsLegenda.Core;

namespace NlcsLegenda.Plugin;

internal readonly record struct SymbolOrientation(SymbolTransform Source, bool FlipX, bool KeepSource, IReadOnlyList<SymbolTransform> Steps)
{
    public static SymbolOrientation Root => new(SymbolTransform.Identity, false, false, Array.Empty<SymbolTransform>());

    private Matrix3d Rotation => Matrix3d.Rotation(
        -(Math.Atan2(Source.XAxis.Y, Source.XAxis.X) - (FlipX ? Math.PI : 0)), Vector3d.ZAxis, Point3d.Origin);

    public IReadOnlyList<SymbolTransform> PlacementTransforms => KeepSource
        ? Steps : new[] { SymbolTransform.From(Rotation) }.Concat(Steps).ToArray();

    public SymbolTransform Transform
    {
        get
        {
            if (KeepSource) return Source;
            // pas na alle brontransformaties draaien, met behoud van vervorming
            var rotation = Rotation;
            return new(Source.XAxis.TransformBy(rotation), Source.YAxis.TransformBy(rotation), Source.ZAxis.TransformBy(rotation));
        }
    }

    public SymbolOrientation Nested(BlockReference reference, Transaction tr)
    {
        var original = SymbolTransform.From(reference.BlockTransform);
        var definition = (BlockTableRecord)tr.GetObject(reference.IsDynamicBlock
            ? reference.DynamicBlockTableRecord : reference.BlockTableRecord, OpenMode.ForRead);
        bool section = !definition.IsFromExternalReference && SymbolDescription.IsSection(definition.Name, reference.Layer);
        // een gekanteld symbool niet naar het horizontale vlak dwingen
        bool planar = Math.Abs(original.XAxis.Z) < 1e-9 && Math.Abs(original.YAxis.Z) < 1e-9;
        var rotation = planar
            ? Matrix3d.Rotation(-reference.Rotation, reference.Normal, Point3d.Origin)
            : Matrix3d.Identity;
        bool flipX = original.XAxis.TransformBy(rotation).X < 0;
        return new(SymbolTransform.From(Source.ToMatrix() * original.ToMatrix()), FlipX ^ flipX, KeepSource || section || !planar, Steps.Append(original).ToArray());
    }
}
