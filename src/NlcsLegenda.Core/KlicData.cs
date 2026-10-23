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
        var available = Records.Where(r => r.Values.ContainsKey(key)).ToList();
        if (available.Count == 0) return Array.Empty<KlicRecord>();
        return available.GroupBy(r => r.Table, StringComparer.OrdinalIgnoreCase).SelectMany(table =>
        {
            var source = table.Any(r => r.Source == PreferredSource) ? PreferredSource : table.Select(r => r.Source).Order().First();
            return table.Where(r => r.Source == source);
        }).ToArray();
    }

    public IReadOnlyList<string> Values(string key) => Selected(key).Select(r => r.Values[key].Trim())
        .DistinctBy(v => KlicFields.Canonical(key, v)).OrderBy(v => v, StringComparer.OrdinalIgnoreCase).ToArray();

    public string Provider => string.Join(" / ", Values("Netbeheerder").Where(v => v.Length > 0));

    public IEnumerable<string> Conflicts => Records.SelectMany(r => r.Values.Keys).Distinct().Where(key =>
        Records.Where(r => r.Values.ContainsKey(key)).GroupBy(r => r.Table, StringComparer.OrdinalIgnoreCase)
            .Any(table => table.Select(r => KlicFields.Canonical(key, r.Values[key])).Distinct().Skip(1).Any()));

    public string GroupKey(IEnumerable<string> fields)
    {
        var keys = KlicFields.Valid(fields).Where(k => k != "DiameterM" || Values(k).Count > 0).OrderBy(k => k, StringComparer.Ordinal).ToArray();
        return keys.Length == 0 || !HasData ? "" : JsonSerializer.Serialize(keys.Select(k =>
            new { key = k, values = Values(k).Select(v => KlicFields.Canonical(k, v)).Order(StringComparer.Ordinal).ToArray() }));
    }
}

public static class KlicSummary
{
    public static string WithoutDiameter(string group)
    {
        if (group.Length == 0) return group;
        try
        {
            using var json = JsonDocument.Parse(group);
            if (json.RootElement.ValueKind != JsonValueKind.Array) return group;
            var fields = json.RootElement.EnumerateArray().Where(f => f.ValueKind != JsonValueKind.Object
                || !f.TryGetProperty("key", out var key) || key.ValueKind != JsonValueKind.String || key.GetString() != "DiameterM").ToArray();
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
