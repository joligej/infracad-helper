using Autodesk.AutoCAD.Geometry;

namespace NlcsLegenda.Plugin;

public sealed record SymbolSample(string BlockName, string DefinitionIdentity, SymbolTransform Transform)
{
    public LayerDisplayStyle? InsertionStyle { get; init; }

    public LayerDisplayStyle? InsertionLayerStyle { get; init; }

    // Zonder expliciete schaalvariant blijft de broninsert op zijn modelmaat.
    public int? SourceScale { get; init; }

    public double TargetScale { get; init; } = 1000;

    public SymbolTransform RenderTransform => SourceScale is > 0
        ? Transform.Divide(SourceScale.Value / TargetScale) : Transform;

    public string RenderIdentity =>
        $"S:{DefinitionIdentity}:{RenderTransform.Signature}";
}

public readonly record struct SymbolTransform(Vector3d XAxis, Vector3d YAxis, Vector3d ZAxis)
{
    public static SymbolTransform Identity { get; } =
        new(Vector3d.XAxis, Vector3d.YAxis, Vector3d.ZAxis);

    public static SymbolTransform From(Matrix3d transform)
    {
        var axes = transform.CoordinateSystem3d;
        return new SymbolTransform(axes.Xaxis, axes.Yaxis, axes.Zaxis);
    }

    public string Signature => string.Join(",",
        Component(XAxis.X), Component(XAxis.Y), Component(XAxis.Z),
        Component(YAxis.X), Component(YAxis.Y), Component(YAxis.Z),
        Component(ZAxis.X), Component(ZAxis.Y), Component(ZAxis.Z));

    public SymbolTransform Divide(double factor) => new(XAxis / factor, YAxis / factor, ZAxis / factor);

    public Matrix3d ToMatrix() => Matrix3d.AlignCoordinateSystem(
        Point3d.Origin, Vector3d.XAxis, Vector3d.YAxis, Vector3d.ZAxis,
        Point3d.Origin, XAxis, YAxis, ZAxis);

    private static string Component(double value) =>
        Math.Round(value, 9, MidpointRounding.AwayFromZero)
            .ToString("0.#########", System.Globalization.CultureInfo.InvariantCulture);
}
