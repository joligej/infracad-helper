using System.Globalization;

namespace NlcsLegenda.Core;

internal static class MergedDescription
{
    internal sealed record Label(string Text, IReadOnlyList<string> Values);

    public static Label Create(IReadOnlyList<LegendEntry> entries, LegendEntry main, IReadOnlyCollection<GroupDimension> dimensions)
    {
        string Clean(LegendEntry entry)
        {
            string text = entry.Description;
            foreach (var provider in entry.Netbeheerders) text = ElementProperties.WithoutProviderSuffix(text, provider);
            return text;
        }
        var texts = entries.Select(Clean).Distinct(StringComparer.CurrentCultureIgnoreCase).ToArray();
        string title = Clean(main);
        var values = entries.SelectMany(e => e.MergedValues).ToList();
        if (texts.Length > 1)
        {
            var words = texts.Select(t => t.Split(' ', StringSplitOptions.RemoveEmptyEntries)).ToArray();
            int prefix = 0, suffix = 0, shortest = words.Min(w => w.Length);
            while (prefix < shortest && words.All(w => string.Equals(w[prefix], words[0][prefix], StringComparison.CurrentCultureIgnoreCase))) prefix++;
            while (suffix < shortest - prefix && words.All(w => string.Equals(w[^(suffix + 1)], words[0][^(suffix + 1)], StringComparison.CurrentCultureIgnoreCase))) suffix++;
            if (prefix + suffix > 0)
            {
                title = string.Join(" ", words[0].Take(prefix).Concat(words[0].TakeLast(suffix)));
                values.AddRange(words.Select(w => string.Join(" ", w.Skip(prefix).Take(w.Length - prefix - suffix))).Where(v => v.Length > 0));
            }
            else values.AddRange(texts.Where(t => !t.Equals(title, StringComparison.CurrentCultureIgnoreCase)));
        }
        var properties = entries.Select(e => ElementProperties.From(e.Element, e.Netbeheerders.FirstOrDefault() ?? "")).ToArray();
        foreach (var dimension in dimensions.Where(d => d != GroupDimension.Netbeheerder))
        {
            if (dimension == GroupDimension.Soort && texts.Length > 1 && values.Count > 0) continue;
            var variants = properties.Select(p => p.Value(dimension)).Where(v => v.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            if (variants.Length < 2) continue;
            foreach (var variant in variants)
            {
                string display = dimension is GroupDimension.Diameter or GroupDimension.Nummer or GroupDimension.Specificatie
                    ? variant : StandardTexts.Humanize(variant);
                if (!values.Any(v => v.Equals(display, StringComparison.OrdinalIgnoreCase))) values.Add(display);
            }
        }
        if (dimensions.Contains(GroupDimension.Diameter)) values.AddRange(Diameters(entries.SelectMany(e => e.KlicObjects)));
        return new(title, values.Distinct(StringComparer.CurrentCultureIgnoreCase)
            .OrderBy(v => decimal.TryParse(v, NumberStyles.Number, CultureInfo.InvariantCulture, out var number) ? number : decimal.MaxValue)
            .ThenBy(v => v, StringComparer.CurrentCultureIgnoreCase).ToArray());
    }

    internal static IReadOnlyList<string> Diameters(IEnumerable<KlicObjectData> objects)
    {
        var values = objects.SelectMany(o => o.Values("DiameterM"))
            .Select(v => decimal.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) && d > 0 && d <= decimal.MaxValue / 1000
                ? (d * 1000).ToString("G29", CultureInfo.InvariantCulture) : "")
            .Where(v => v.Length > 0).Distinct().OrderBy(v => decimal.Parse(v, CultureInfo.InvariantCulture)).ToArray();
        return values.Length > 1 ? values : Array.Empty<string>();
    }
}
