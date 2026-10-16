namespace NlcsLegenda.Core;

// papiermaten van de drie samengestelde templatevoorbeelden
public sealed record SeparationBandProfile(double OuterOffsetMm, double InnerOffsetMm)
{
    public static SeparationBandProfile? For(LegendEntry entry)
    {
        if (entry.IsManual || entry.IsBlank || entry.GeometryLayer is null
            || !entry.Hoofdgroep.Equals("VH", StringComparison.OrdinalIgnoreCase)) return null;
        return entry.Element.ToUpperInvariant() switch
        {
            "KANTOPSLUITING_SCHEIDINGSBAND_100X300" => new(0.75, 0.25),
            "KANTOPSLUITING_SCHEIDINGSBAND_115X345" => new(0.875, 0.30),
            "KANTOPSLUITING_SCHEIDINGSBAND_220X440" => new(1.10, 0.55),
            _ => null
        };
    }
}
