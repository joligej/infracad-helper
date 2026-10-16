namespace NlcsLegenda.Core;

public sealed record LegendExample(string BlockName, string Name, string Section, double Scale, string[] SourceKeys, double AnchorX = 0, double AnchorY = 0)
{
    public static string SourceKey(LegendEntry entry) => $"{entry.Status}|{entry.Discipline}|{entry.Hoofdgroep}|{entry.Element}".ToUpperInvariant();

    public static LegendExample? Match(IEnumerable<LegendExample> examples, LegendEntry entry, double scale)
    {
        if (entry.IsManual || entry.IsBlank || entry.MergedMembers.Count > 0) return null;
        string key = SourceKey(entry);
        var candidates = examples.Where(e => e.SourceKeys.Length > 0 && e.SourceKeys.All(k => k.Equals(key, StringComparison.OrdinalIgnoreCase))).ToArray();
        if (candidates.Length == 0) return null;
        double distance = candidates.Min(e => Math.Abs(Math.Log(e.Scale / scale)));
        var closest = candidates.Where(e => Math.Abs(Math.Abs(Math.Log(e.Scale / scale)) - distance) < 1e-9).ToArray();
        var named = closest.Where(e => e.Name.Equals(entry.Description, StringComparison.OrdinalIgnoreCase)).ToArray();
        return named.Length == 1 ? named[0] : closest.Length == 1 ? closest[0] : null;
    }
}
