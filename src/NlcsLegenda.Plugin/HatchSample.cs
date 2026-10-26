using Autodesk.AutoCAD.Colors;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Color = Autodesk.AutoCAD.Colors.Color;

namespace NlcsLegenda.Plugin;

public readonly record struct TransparencyValue(TransparencyMethod Method, byte Alpha)
{
    public static TransparencyValue From(Transparency transparency)
    {
        var method = transparency.IsByAlpha ? TransparencyMethod.ByAlpha
            : transparency.IsByBlock ? TransparencyMethod.ByBlock
            : transparency.IsByLayer ? TransparencyMethod.ByLayer
            : TransparencyMethod.ErrorValue;
        byte alpha = method == TransparencyMethod.ByAlpha ? transparency.Alpha : (byte)0;
        return new TransparencyValue(method, alpha);
    }

    public Transparency ToAutoCadValue() => Method == TransparencyMethod.ByAlpha
        ? new Transparency(Alpha)
        : new Transparency(Method);

    public string Signature => $"{Method}:{Alpha}";
}

public sealed class HatchSample
{
    private sealed record PatternLine(double Angle, double X, double Y, double OffsetX, double OffsetY, double[] Dashes);

    private PatternLine[] _definition = Array.Empty<PatternLine>();

    internal string DefinitionSignature(double factor, double rotation = 0)
    {
        double cos = Math.Cos(rotation), sin = Math.Sin(rotation);
        string Number(double value) => Math.Round(value, 9).ToString("R", System.Globalization.CultureInfo.InvariantCulture);
        return string.Join(";", _definition.Select(line => string.Join(",", new[] {
            rotation == 0 ? line.Angle : (line.Angle + rotation + 2 * Math.PI) % (2 * Math.PI),
            (line.X * cos - line.Y * sin) * factor, (line.X * sin + line.Y * cos) * factor,
            (line.OffsetX * cos - line.OffsetY * sin) * factor, (line.OffsetX * sin + line.OffsetY * cos) * factor
        }.Concat(line.Dashes.Select(d => d * factor)).Select(Number))));
    }

    internal string SourceXref { get; set; } = string.Empty;

    internal ObjectId SourceId { get; private set; }

    internal Matrix3d SourceTransform { get; private set; } = Matrix3d.Identity;

    public string PatternName { get; set; } = "SOLID";

    public HatchPatternType PatternType { get; set; } = HatchPatternType.PreDefined;

    public double PatternScale { get; set; } = 1.0;

    public int? SourceScale { get; set; }

    public double TargetScale { get; set; } = 1000;

    public double ScaleForLegend(double legendScale) => SourceScale is > 0 ? legendScale / SourceScale.Value : 1.0;

    public double PatternAngle { get; set; }

    public bool IsSolid { get; set; }

    public double PatternSpace { get; set; } = 1.0;

    public bool PatternDouble { get; set; }

    public Color? Color { get; set; }

    public Color? BackgroundColor { get; set; }

    public TransparencyValue Transparency { get; set; } =
        new(TransparencyMethod.ByAlpha, 255);

    public static HatchSample From(Hatch hatch, Matrix3d transform)
    {
        if (transform.IsEqualTo(Matrix3d.Identity)) return From(hatch);
        using var plane = hatch.GetPlane();
        var normal = plane.Normal.GetNormal();
        var u = normal.GetPerpendicularVector().GetNormal();
        var v = normal.CrossProduct(u).GetNormal();
        var a = u.TransformBy(transform);
        var b = v.TransformBy(transform);
        double scale = a.Length;
        bool uniform = scale > 1e-12 && Math.Abs(scale - b.Length) <= scale * 1e-9
            && Math.Abs(a.DotProduct(b)) <= scale * scale * 1e-9;
        if (uniform)
        {
            // patroonmaat uit het bronvlak, onafhankelijk van de z-schaal
            transform = Matrix3d.AlignCoordinateSystem(plane.PointOnPlane, u, v, normal,
                plane.PointOnPlane.TransformBy(transform), a, b, a.CrossProduct(b).GetNormal() * scale);
        }
        else if (!hatch.IsSolidFill)
            throw new InvalidOperationException($"Arcering '{hatch.PatternName}' is ongelijk geschaald in het bronvlak; één patroonhoek en schaal kunnen dit niet weergeven.");
        using var copy = (Hatch)hatch.GetTransformedCopy(transform);
        var sample = From(copy);
        sample.SourceId = hatch.ObjectId;
        sample.SourceTransform = transform;
        return sample;
    }

    public static HatchSample From(Hatch hatch)
    {
        var patternName = hatch.PatternName.Trim();
        var isSolid = hatch.IsSolidFill;
        var scale = hatch.PatternScale;
        var angle = hatch.PatternAngle;
        if (!isSolid && (!double.IsFinite(scale) || scale <= 0))
            throw new InvalidOperationException($"Arcering '{patternName}' heeft een ongeldige schaal.");
        if (!isSolid && !double.IsFinite(angle))
            throw new InvalidOperationException($"Arcering '{patternName}' heeft een ongeldige hoek.");

        const double fullTurn = 2 * Math.PI;
        if (!isSolid)
        {
            angle %= fullTurn;
            if (angle < 0)
                angle += fullTurn;
        }

        var definitions = new List<PatternLine>();
        double baseX = 0, baseY = 0;
        for (int i = 0; i < hatch.NumberOfPatternDefinitions; i++)
        {
            var line = hatch.GetPatternDefinitionAt(i);
            if (i == 0) { baseX = line.BaseX; baseY = line.BaseY; }
            definitions.Add(new PatternLine(line.Angle, line.BaseX - baseX, line.BaseY - baseY,
                line.OffsetX, line.OffsetY, line.GetDashes().Cast<double>().ToArray()));
        }
        return new HatchSample
        {
            _definition = definitions.ToArray(),
            SourceId = hatch.ObjectId,
            PatternName = isSolid ? "SOLID" : patternName,
            PatternType = isSolid ? HatchPatternType.PreDefined : hatch.PatternType,
            PatternScale = isSolid ? 1.0 : scale,
            PatternAngle = isSolid ? 0.0 : angle,
            IsSolid = isSolid,
            PatternSpace = hatch.PatternType == HatchPatternType.UserDefined ? hatch.PatternSpace : 1.0,
            PatternDouble = hatch.PatternType == HatchPatternType.UserDefined && hatch.PatternDouble,
            Color = hatch.Color,
            BackgroundColor = hatch.BackgroundColor,
            Transparency = TransparencyValue.From(hatch.Transparency)
        };
    }
}
