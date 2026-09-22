using System.Text.Json;

namespace NlcsLegenda.Core;

public sealed class DescriptionEntry
{
    public string? Algemeen { get; set; }

    public string Specifiek { get; set; } = string.Empty;
}

public sealed class DescriptionCatalog
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public Dictionary<string, DescriptionEntry> Elementen { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);

    public static DescriptionCatalog Default()
    {
        var catalog = new DescriptionCatalog();
        foreach (var kv in StandardTexts.DefaultElementDescriptions)
        {
            var sep = kv.Key.IndexOf('|');
            var hoofdgroep = sep >= 0 ? kv.Key[..sep] : string.Empty;
            catalog.Elementen[kv.Key] = new DescriptionEntry
            {
                Algemeen = StandardTexts.HoofdgroepName(hoofdgroep),
                Specifiek = kv.Value
            };
        }
        return catalog;
    }

    public void MergeFrom(DescriptionCatalog? other)
    {
        if (other is null) return;
        foreach (var kv in other.Elementen)
            Elementen[kv.Key] = kv.Value;
    }

    public DescriptionCatalog Diff(DescriptionCatalog baseline)
    {
        var result = new DescriptionCatalog();
        foreach (var kv in Elementen)
        {
            if (baseline.Elementen.TryGetValue(kv.Key, out var b) &&
                string.Equals(b.Specifiek ?? string.Empty, kv.Value.Specifiek ?? string.Empty, StringComparison.Ordinal) &&
                string.Equals(b.Algemeen ?? string.Empty, kv.Value.Algemeen ?? string.Empty, StringComparison.Ordinal))
            {
                continue;
            }
            result.Elementen[kv.Key] = kv.Value;
        }
        return result;
    }

    public string Describe(NlcsLayerName layer, bool includeGeneral, string separator = " - ")
        => Describe(layer, includeGeneral, out _, separator);

    public string Describe(NlcsLayerName layer, bool includeGeneral, out bool matched, string separator = " - ")
    {
        var element = layer.Element.ToUpperInvariant();
        matched = true;

        if (Elementen.TryGetValue($"{layer.Hoofdgroep}|{element}", out var exact))
            return Compose(exact.Algemeen, exact.Specifiek, layer.Hoofdgroep, includeGeneral, separator);

        foreach (var kv in Elementen)
        {
            var sep = kv.Key.IndexOf('|');
            if (sep < 0) continue;
            var hoofdgroep = kv.Key[..sep];
            var prefix = kv.Key[(sep + 1)..];
            if (hoofdgroep.Equals(layer.Hoofdgroep, StringComparison.OrdinalIgnoreCase) &&
                element.StartsWith(prefix + "_", StringComparison.OrdinalIgnoreCase))
            {
                var variant = StandardTexts.Humanize(element[prefix.Length..]).Trim();
                var specifiek = string.IsNullOrEmpty(variant)
                    ? kv.Value.Specifiek
                    : $"{kv.Value.Specifiek} {variant}";
                return Compose(kv.Value.Algemeen, specifiek, layer.Hoofdgroep, includeGeneral, separator);
            }
        }

        matched = false;
        return Compose(null, StandardTexts.Humanize(layer.Element), layer.Hoofdgroep, includeGeneral, separator);
    }

    private static string Compose(string? algemeen, string specifiek, string hoofdgroep, bool includeGeneral, string separator)
    {
        if (!includeGeneral)
            return specifiek;
        var general = string.IsNullOrWhiteSpace(algemeen) ? StandardTexts.HoofdgroepName(hoofdgroep) : algemeen!;
        return string.IsNullOrWhiteSpace(general) ? specifiek : $"{general}{separator}{specifiek}";
    }

    public static DescriptionCatalog Load(string path)
    {
        try
        {
            if (File.Exists(path))
                return JsonSerializer.Deserialize<DescriptionCatalog>(File.ReadAllText(path), Options)
                       ?? new DescriptionCatalog();
        }
        catch
        {
            // Ongeldig bestand: negeren.
        }
        return new DescriptionCatalog();
    }

    public void Save(string path) =>
        File.WriteAllText(path, JsonSerializer.Serialize(this, Options));

    public string ToJson() => JsonSerializer.Serialize(this, Options);

    public static DescriptionCatalog FromJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return new DescriptionCatalog();
        try
        {
            return JsonSerializer.Deserialize<DescriptionCatalog>(json, Options) ?? new DescriptionCatalog();
        }
        catch
        {
            return new DescriptionCatalog();
        }
    }
}
