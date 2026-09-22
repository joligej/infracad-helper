using Autodesk.AutoCAD.Colors;
using Autodesk.AutoCAD.DatabaseServices;
using Color = Autodesk.AutoCAD.Colors.Color;

namespace NlcsLegenda.Plugin;

public sealed class HatchSample
{
    public string PatternName { get; set; } = "SOLID";

    public HatchPatternType PatternType { get; set; } = HatchPatternType.PreDefined;

    public double PatternScale { get; set; } = 1.0;

    public double PatternAngle { get; set; }

    public bool IsSolid { get; set; }

    public Color? Color { get; set; }

    public static HatchSample From(Hatch hatch)
    {
        var sample = new HatchSample();
        try
        {
            sample.PatternName = hatch.PatternName;
            sample.PatternType = hatch.PatternType;
            sample.PatternScale = hatch.PatternScale <= 0 ? 1.0 : hatch.PatternScale;
            sample.PatternAngle = hatch.PatternAngle;
            sample.IsSolid = string.Equals(hatch.PatternName, "SOLID", StringComparison.OrdinalIgnoreCase);
            sample.Color = hatch.Color;
        }
        catch
        {
            // Bij afwijkende arceringen vallen we terug op de standaardwaarden.
        }
        return sample;
    }
}
