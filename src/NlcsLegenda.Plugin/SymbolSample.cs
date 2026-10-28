using Autodesk.AutoCAD.Geometry;

namespace NlcsLegenda.Plugin;

public sealed record SymbolSample(string BlockName, string DefinitionIdentity, SymbolTransform Transform)
{
    public Autodesk.AutoCAD.DatabaseServices.ObjectId ReferenceId { get; init; }

    public bool IsPrimitive { get; init; }
    public Autodesk.AutoCAD.DatabaseServices.ObjectId ViewIdentity { get; init; }
    public Autodesk.AutoCAD.DatabaseServices.ObjectId SourceViewportId { get; init; }
    public IReadOnlySet<Autodesk.AutoCAD.DatabaseServices.ObjectId> FrozenLayers { get; init; } = new HashSet<Autodesk.AutoCAD.DatabaseServices.ObjectId>();
    public bool OmitManholeNumber { get; init; }
    public string Variant { get; init; } = "";

    public string NormalizedDefinitionIdentity { get; init; } = string.Empty;

    public string GroupIdentity(NlcsLegenda.Core.LegendSettings settings)
    {
        var transform = settings.MergeSymbolSizes ? RenderTransform.WithoutSize() : RenderTransform;
        if (settings.MergeSymbolRotations) transform = transform.WithoutRotation();
        string definition = settings.MergeSymbolSizes && NormalizedDefinitionIdentity.Length > 0
            ? NormalizedDefinitionIdentity : DefinitionIdentity;
        return $"S:{(Variant.Length > 0 ? Variant + ":" : "")}{definition}:{transform.Signature}"
            + (settings.MergeSymbolAttributes || AttributeIdentity.Length == 0 ? "" : ":" + AttributeIdentity);
    }

    public string SourceXref { get; init; } = string.Empty;

    public string AttributeIdentity { get; init; } = string.Empty;

    public LayerDisplayStyle? InsertionStyle { get; init; }

    public LayerDisplayStyle? InsertionLayerStyle { get; init; }

    // bronmaat behouden zonder expliciete schaalvariant
    public int? SourceScale { get; init; }

    public double TargetScale { get; init; } = 1000;

    public SymbolTransform RenderTransform => SourceScale is > 1
        ? Transform.Divide(SourceScale.Value / TargetScale) : Transform;

    public string SelectionIdentity =>
        $"S:{DefinitionIdentity}:{(SourceScale is > 1 ? Transform.Divide(SourceScale.Value / 1000.0) : Transform).Signature}"
        + (AttributeIdentity.Length == 0 ? string.Empty : ":" + AttributeIdentity);

    public string RenderIdentity =>
        $"S:{(Variant.Length > 0 ? Variant + ":" : "")}{DefinitionIdentity}:{RenderTransform.Signature}" + (AttributeIdentity.Length == 0 ? string.Empty : ":" + AttributeIdentity);
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

    public SymbolTransform WithoutSize()
    {
        double size = Math.Max(XAxis.Length, YAxis.Length);
        return size > 1e-12 ? new(XAxis / size, YAxis / size, ZAxis.GetNormal()) : this;
    }

    public SymbolTransform WithoutRotation()
    {
        // alleen rotatie in het tekeningvlak; spiegeling en vervorming behouden
        if (Math.Abs(XAxis.Z) > 1e-9 || Math.Abs(YAxis.Z) > 1e-9 || XAxis.Length < 1e-12) return this;
        var rotation = Matrix3d.Rotation(-Math.Atan2(XAxis.Y, XAxis.X), Vector3d.ZAxis, Point3d.Origin);
        return new(XAxis.TransformBy(rotation), YAxis.TransformBy(rotation), ZAxis.TransformBy(rotation));
    }

    public SymbolTransform Divide(double factor) => new(XAxis / factor, YAxis / factor, ZAxis / factor);

    public Matrix3d ToMatrix() => Matrix3d.AlignCoordinateSystem(
        Point3d.Origin, Vector3d.XAxis, Vector3d.YAxis, Vector3d.ZAxis,
        Point3d.Origin, XAxis, YAxis, ZAxis);

    private static string Component(double value)
    {
        double rounded = Math.Round(value, 9, MidpointRounding.AwayFromZero);
        return rounded == 0 ? "0" : rounded.ToString("0.#########", System.Globalization.CultureInfo.InvariantCulture);
    }
}
