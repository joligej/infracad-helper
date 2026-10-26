using System.Globalization;
using System.Text.Json;

namespace NlcsLegenda.Core;

public enum KlicSource { InfraCAD, PropertySets, ObjectData }

public sealed record KlicRecord(KlicSource Source, string Table, IReadOnlyDictionary<string, string> Values);

public sealed record KlicField(string Key, string Label, string Unit = "", double Factor = 1, bool Positive = false)
{
    public override string ToString() => Label;
}

public static class KlicFields
{
    public static IReadOnlyList<KlicField> All { get; } = new KlicField[]
    {
        new("Netbeheerder", "Netbeheerder"), new("Thema", "Thema"), new("Materiaal", "Materiaal"),
        new("DiameterM", "Diameter", "mm", 1000, true), new("BuisDrukBar", "Druk", "bar", 1, true),
        new("OperatingVoltageV", "Bedrijfsspanning", "V", 1, true), new("NominalVoltageV", "Nominale spanning", "V", 1, true),
        new("ProductType", "Medium"), new("CurrentStatus", "KLIC-status"), new("LeidingType", "Leidingtype"),
        new("ContainerType", "Containertype"), new("AantalKabelsLeidingen", "Kabels/leidingen in container", "", 1, true),
        new("DuctBreedteM", "Containerbreedte", "m", 1, true), new("VerticalePositie", "Verticale positie"),
        new("DiepteLeggingM", "Opgegeven diepte", "m", 1, true),
        new("StandaardDiepteLeggingM", "Standaarddiepte (niet gemeten)", "m", 1, true),
        new("NauwkeurigheidXY", "XY-nauwkeurigheid"), new("Aanname", "Aanname volgens bron"),
        new("KLICMelding", "KLIC-melding"), new("AanvraagDatum", "Aanvraagdatum"), new("AanvraagSoort", "Aanvraagsoort"),
        new("AppurtenanceType", "Netcomponent"), new("BovengrondsZichtbaar", "Bovengronds zichtbaar"),
        new("Label", "Bronlabel"), new("Omschrijving", "Bronomschrijving"), new("Toelichting", "Toelichting"),
        new("EisVoorzorgsmaatregel", "Voorzorgsmaatregel"), new("EVToelichting", "Toelichting voorzorgsmaatregel"),
        new("ContactVoorzorgsmaatregel", "Contact voorzorgsmaatregel"), new("Bijlage", "Bijlage"),
        new("ExtraDetailInfoType", "Soort bijlage"), new("AuthorityRole", "Rol netbeheerder"), new("Disclaimer", "Bronvermelding")
    };
    private static readonly Dictionary<string, KlicField> ByKey = All.ToDictionary(f => f.Key, StringComparer.Ordinal);
    public static KlicField? Find(string key) => ByKey.GetValueOrDefault(key);
    public static List<string> Valid(IEnumerable<string>? fields) => (fields ?? Array.Empty<string>()).Where(ByKey.ContainsKey).Distinct().ToList();
    public static string SourceLabel(KlicSource source) => source switch
    {
        KlicSource.PropertySets => "Property Sets", KlicSource.ObjectData => "Object Data", _ => "InfraCAD"
    };

    public static string Canonical(string key, string value)
    {
        value = value.Trim();
        if (Find(key) is { } field && (field.Unit.Length > 0 || field.Positive)
            && decimal.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
            return number.ToString("G29", CultureInfo.InvariantCulture);
        return value.ToUpperInvariant();
    }

    public static string Format(string key, string value)
    {
        value = value.Trim();
        if (value.Length == 0 || value.Equals("Onbekend", StringComparison.OrdinalIgnoreCase)) return "Niet opgegeven";
        if (Find(key) is not { } field || (!field.Positive && field.Unit.Length == 0)) return value;
        if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double number) || !double.IsFinite(number))
            return value + " (ongeldige getalwaarde)";
        if (field.Positive && number <= 0) return "Niet opgegeven (" + value + ")";
        return (number * field.Factor).ToString("0.#########", CultureInfo.GetCultureInfo("nl-NL"))
            + (field.Unit.Length == 0 ? "" : " " + field.Unit);
    }
}

// alleen waarden bewaren, geen cad-objecten of transacties
public sealed record KlicObjectData(string Drawing, string Handle, string Xref,
    IReadOnlyList<KlicRecord> Records, IReadOnlyList<string> ReadIssues, KlicSource PreferredSource)
{
    public string Identity => JsonSerializer.Serialize(new[] { Drawing.ToUpperInvariant(), Handle.ToUpperInvariant(), Xref.ToUpperInvariant() });
    public bool HasData => Records.Count > 0;

    public IReadOnlyList<KlicRecord> Selected(string key)
    {
        if (!HasData) return Array.Empty<KlicRecord>();
        var tables = new Dictionary<string, List<KlicRecord>>(StringComparer.OrdinalIgnoreCase);
        foreach (var record in Records)
        {
            if (!record.Values.ContainsKey(key)) continue;
            if (!tables.TryGetValue(record.Table, out var group)) tables.Add(record.Table, group = new());
            group.Add(record);
        }
        var result = new List<KlicRecord>();
        foreach (var group in tables.Values)
        {
            var source = group[0].Source;
            foreach (var record in group)
            {
                if (record.Source == PreferredSource) { source = PreferredSource; break; }
                if (record.Source < source) source = record.Source;
            }
            foreach (var record in group)
                if (record.Source == source) result.Add(record);
        }
        return result;
    }

    public IReadOnlyList<string> Values(string key)
    {
        var records = Selected(key);
        if (records.Count == 0) return Array.Empty<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var values = new List<string>();
        foreach (var record in records)
        {
            string value = record.Values[key].Trim();
            if (seen.Add(KlicFields.Canonical(key, value))) values.Add(value);
        }
        return values.OrderBy(v => v, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public string Provider => !HasData ? "" : string.Join(" / ", Values("Netbeheerder").Where(v => v.Length > 0));

    public IEnumerable<string> Conflicts
    {
        get
        {
            var tables = new Dictionary<string, (string Raw, string Canonical)>(StringComparer.OrdinalIgnoreCase);
            foreach (string key in Records.SelectMany(r => r.Values.Keys).Distinct())
            {
                tables.Clear();
                foreach (var record in Records)
                {
                    if (!record.Values.TryGetValue(key, out var value)) continue;
                    if (tables.TryGetValue(record.Table, out var previous))
                    {
                        if (previous.Raw == value || previous.Canonical == KlicFields.Canonical(key, value)) continue;
                        yield return key;
                        break;
                    }
                    tables.Add(record.Table, (value, KlicFields.Canonical(key, value)));
                }
            }
        }
    }

    public string GroupKey(IEnumerable<string> fields)
    {
        if (!HasData) return "";
        var keys = KlicFields.Valid(fields).Where(k => k is not ("DiameterM" or "Materiaal") || Values(k).Count > 0).OrderBy(k => k, StringComparer.Ordinal).ToArray();
        return keys.Length == 0 || !HasData ? "" : JsonSerializer.Serialize(keys.Select(k =>
            new { key = k, values = Values(k).Select(v => KlicFields.Canonical(k, v)).Order(StringComparer.Ordinal).ToArray() }));
    }
}

public static class KlicSummary
{
    public static string WithoutDiameter(string group) => WithoutFields(group, new[] { "DiameterM" });

    public static string WithoutMergedFields(string group, IReadOnlyCollection<GroupDimension> dimensions) =>
        WithoutFields(group, dimensions.SelectMany(d => d switch {
            GroupDimension.Diameter => new[] { "DiameterM" }, GroupDimension.Materiaal => new[] { "Materiaal" },
            _ => Array.Empty<string>()
        }).ToArray());

    private static string WithoutFields(string group, IReadOnlyCollection<string> ignored)
    {
        if (group.Length == 0 || ignored.Count == 0) return group;
        try
        {
            using var json = JsonDocument.Parse(group);
            if (json.RootElement.ValueKind != JsonValueKind.Array) return group;
            var fields = json.RootElement.EnumerateArray().Where(f => f.ValueKind != JsonValueKind.Object
                || !f.EnumerateObject().Any(p => p.Name.Equals("key", StringComparison.OrdinalIgnoreCase)
                    && p.Value.ValueKind == JsonValueKind.String && ignored.Contains(p.Value.GetString(), StringComparer.OrdinalIgnoreCase))).ToArray();
            return fields.Length == 0 ? "" : JsonSerializer.Serialize(fields);
        }
        catch (JsonException) { return group; }
    }

    public static IReadOnlyList<KlicObjectData> Combine(IEnumerable<KlicObjectData> objects) =>
        objects.DistinctBy(o => o.Identity).ToArray();

    public static string Field(IEnumerable<KlicObjectData> objects, string key)
    {
        var data = objects.ToArray();
        if (!data.Any(o => o.HasData)) return "";
        return string.Join("; ", data.SelectMany(o => o.Values(key) is { Count: > 0 } values ? values : new[] { "" })
            .Select(v => KlicFields.Format(key, v)).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.CurrentCultureIgnoreCase));
    }

    public static string Issues(IEnumerable<KlicObjectData> objects) => string.Join("; ", objects.SelectMany(o =>
        o.ReadIssues.Concat(o.Conflicts.Select(k => "Bronverschil: " + (KlicFields.Find(k)?.Label ?? k))))
        .Distinct().Order(StringComparer.CurrentCultureIgnoreCase));

    public static string Sources(IEnumerable<KlicObjectData> objects, string key) => string.Join("; ",
        objects.SelectMany(o => o.Selected(key)).Select(r => KlicFields.SourceLabel(r.Source)).Distinct().Order());

    public static string Description(IEnumerable<KlicObjectData> objects, IEnumerable<string> fields) =>
        string.Join(" · ", KlicFields.Valid(fields).Select(k => (Key: k, Value: Field(objects, k)))
            .Where(x => x.Value.Length > 0).Select(x => KlicFields.Find(x.Key)!.Label + ": " + x.Value));
}
