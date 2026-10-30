namespace NlcsLegenda.Core;

public sealed record CivilExampleComponent(string Name, string Label, string ProfileViewHandle);

public enum ExampleMode { FollowSource, Fixed }

public sealed record ExampleDisplay(string Color, string Linetype, int LineWeight, int Transparency)
{
    [System.Text.Json.Serialization.JsonIgnore]
    public string Description
    {
        get
        {
            string color = Color == "foreground" ? "wit/zwart volgens achtergrond" : int.TryParse(Color, out int argb) ? "#" + (argb & 0xFFFFFF).ToString("X6") : Color;
            string line = Linetype == "Continuous" ? "Continuous" : "lijnpatroon";
            string weight = LineWeight < 0 ? "standaard lijndikte" : (LineWeight / 100.0).ToString("0.00", System.Globalization.CultureInfo.GetCultureInfo("nl-NL")) + " mm";
            return color + ", " + line + ", " + weight + ", " + Math.Round(Transparency * 100.0 / 255) + "% transparant";
        }
    }

    public static string Difference(IEnumerable<ExampleDisplay> source, IEnumerable<ExampleDisplay> example)
    {
        var a = source.ToArray(); var b = example.ToArray();
        if (a.Length == 0 || b.Length == 0) return "Weergave nog niet gecontroleerd.";
        var fields = new List<string>();
        void Compare<T>(string name, Func<ExampleDisplay, T> value)
        {
            if (!a.Select(value).ToHashSet().SetEquals(b.Select(value))) fields.Add(name);
        }
        Compare("kleur", x => x.Color); Compare("lijntype", x => x.Linetype);
        Compare("lijndikte", x => x.LineWeight); Compare("transparantie", x => x.Transparency);
        if (fields.Count == 0 && !a.ToHashSet().SetEquals(b)) fields.Add("combinatie van kleur en lijnweergave");
        return string.Join(", ", fields);
    }
}

public sealed class LegendObjectLink
{
    public string DrawingId { get; set; } = "";
    public List<string> ContainerHandles { get; set; } = new();
    public string ObjectHandle { get; set; } = "";
    public string Component { get; set; } = "";
    public string ProfileViewHandle { get; set; } = "";
    public string MapViewportHandle { get; set; } = "";
    public string Label { get; set; } = "";
    public LegendObjectLink Clone() => new() { DrawingId = DrawingId, ContainerHandles = new(ContainerHandles),
        ObjectHandle = ObjectHandle, Component = Component, ProfileViewHandle = ProfileViewHandle, MapViewportHandle = MapViewportHandle, Label = Label };
}

public sealed record SheetComparisonException(string Key, string Fingerprint);
public sealed record SheetLegendRow(string Sheet, string LegendId, SavedLegendRow Row);
public sealed record SheetDifference(string Key, string Fingerprint, string Description, string Details, bool Accepted);

public static class LegendSheetComparison
{
    public static IReadOnlyList<SheetDifference> Compare(IEnumerable<SheetLegendRow> rows,
        IEnumerable<SheetComparisonException> exceptions)
    {
        var accepted = exceptions.ToHashSet();
        var allRows = rows.ToArray();
        var results = new List<SheetDifference>();
        foreach (var row in allRows.Where(r => r.Row.Sources.Count == 0 && r.Row.ComparisonKey.Length == 0))
        {
            string key = "ONGEKOPPELD|" + row.Sheet + "|" + row.LegendId + "|" + row.Row.Id;
            string fingerprint = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(row.Row.Description + "|" + DisplayKey(row.Row))));
            results.Add(new(key, fingerprint, row.Row.Description,
                row.Sheet + ": niet vergeleken; deze eigen regel heeft geen bronkoppeling of gedeelde onderdeelnaam. Stel die in via Overzicht > Regel aanpassen of Gekoppeld voorbeeld.",
                accepted.Contains(new(key, fingerprint))));
        }
        // bronlaag, objectsoort en status; kleur is juist het te vergelijken verschil
        foreach (var group in allRows.Where(r => r.Row.Sources.Count > 0 || r.Row.ComparisonKey.Length > 0).SelectMany(r => Keys(r.Row).Select(k => (Key: k, Item: r)))
            .GroupBy(p => p.Key, StringComparer.OrdinalIgnoreCase))
        {
            var items = group.Select(p => p.Item).Distinct().OrderBy(r => r.Sheet, StringComparer.Ordinal).ThenBy(r => r.LegendId, StringComparer.Ordinal).ThenBy(r => r.Row.Description, StringComparer.Ordinal).ThenBy(r => DisplayKey(r.Row), StringComparer.Ordinal).ToArray();
            if (items.Select(r => (r.Sheet, r.LegendId)).Distinct().Count() < 2) continue;
            var sheets = items.GroupBy(r => (r.Sheet, r.LegendId)).ToArray();
            string Set(IEnumerable<string> values) => System.Text.Json.JsonSerializer.Serialize(values.Distinct().OrderBy(v => v, StringComparer.Ordinal));
            bool descriptions = sheets.Select(g => Set(g.Select(r => r.Row.Description.Trim()))).Distinct(StringComparer.Ordinal).Count() > 1;
            bool appearance = sheets.Select(g => Set(g.Select(r => DisplayKey(r.Row)))).Distinct(StringComparer.Ordinal).Count() > 1;
            if (!descriptions && !appearance) continue;
            string details = string.Join(Environment.NewLine, items.Select(r => r.Sheet + " · " + r.Row.Description
                + " · " + (r.Row.ExampleMode == ExampleMode.Fixed ? "vast voorbeeld" : "bron volgen")
                + " · " + string.Join("; ", r.Row.ExampleDisplay.Select(d => d.Description))));
            string fingerprint = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(
                string.Join("\n", items.Select(r => r.Sheet + "|" + r.LegendId + "|" + r.Row.Description + "|" + DisplayKey(r.Row))))));
            results.Add(new(group.Key, fingerprint, items[0].Row.Description,
                (descriptions ? "Andere omschrijving. " : "") + (appearance ? "Andere weergave. " : "") + Environment.NewLine + details,
                accepted.Contains(new(group.Key, fingerprint))));
        }
        return results;
    }

    private static string DisplayKey(SavedLegendRow row) => row.ExampleDisplay.Count > 0
        ? System.Text.Json.JsonSerializer.Serialize(row.ExampleDisplay.Distinct().OrderBy(d => d.Color).ThenBy(d => d.Linetype).ThenBy(d => d.LineWeight).ThenBy(d => d.Transparency))
        : row.Appearance;

    private static IEnumerable<string> Keys(SavedLegendRow row) => row.ComparisonKey.Length > 0
        ? new[] { "ONDERDEEL|" + row.Status + "|" + row.ComparisonKey.Trim().ToUpperInvariant() }
        : row.Sources.Count > 0
        ? row.Sources.Select(s => string.Join("|", LayerNaming.LocalName(s.Layer).ToUpperInvariant(), s.Type, s.Status,
            LayerNaming.LocalName(s.Symbol).ToUpperInvariant())).Distinct()
        : new[] { "EIGEN|" + row.Status + "|" + row.OrderKey };
}
