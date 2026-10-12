using Autodesk.AutoCAD.Colors;
using Autodesk.AutoCAD.DatabaseServices;
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
    public string PatternName { get; set; } = "SOLID";

    public HatchPatternType PatternType { get; set; } = HatchPatternType.PreDefined;

    public double PatternScale { get; set; } = 1.0;

    public double PatternAngle { get; set; }

    public bool IsSolid { get; set; }

    public Color? Color { get; set; }

    public TransparencyValue Transparency { get; set; } =
        new(TransparencyMethod.ByAlpha, 255);

    public static HatchSample From(Hatch hatch)
    {
        var patternName = hatch.PatternName.Trim();
        var isSolid = string.Equals(patternName, "SOLID", StringComparison.OrdinalIgnoreCase);
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

        return new HatchSample
        {
            PatternName = patternName,
            PatternType = isSolid ? HatchPatternType.PreDefined : hatch.PatternType,
            PatternScale = isSolid ? 1.0 : scale,
            PatternAngle = isSolid ? 0.0 : angle,
            IsSolid = isSolid,
            Color = hatch.Color,
            Transparency = TransparencyValue.From(hatch.Transparency)
        };
    }
}
