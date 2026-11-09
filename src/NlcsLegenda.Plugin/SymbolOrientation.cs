using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using NlcsLegenda.Core;

namespace NlcsLegenda.Plugin;

internal readonly record struct SymbolOrientation(SymbolTransform Transform, bool KeepSource)
{
    public static SymbolOrientation Root => new(SymbolTransform.Identity, false);

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
        var basis = new SymbolTransform(original.XAxis.TransformBy(rotation), original.YAxis.TransformBy(rotation), original.ZAxis);
        return new(SymbolTransform.From(Transform.ToMatrix() * basis.ToMatrix()), KeepSource || section || !planar);
    }
}
