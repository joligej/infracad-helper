using System.Text.Json;
using System.Text.Json.Serialization;

namespace NlcsLegenda.Core;

// De beheerde legenda's van één tekening. Wordt als JSON in de DWG bewaard onder een
// eigen NOD-root, los van de oude v1.13 SETTINGS-scope.
public sealed class LegendRegistry
{
    // Verhoog bij een niet-terugwaarts-leesbare wijziging. Een hogere versie dan deze
    // plugin kent, wordt niet overschreven maar als "niet ondersteund" gemeld.
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    public List<LegendDefinition> Legends { get; set; } = new();

    [JsonIgnore]
    public bool IsSupported => SchemaVersion <= CurrentSchemaVersion;

    public LegendDefinition? FindById(string? id) =>
        string.IsNullOrEmpty(id) ? null : Legends.FirstOrDefault(l => l.Id == id);

    public LegendDefinition? FindByGroupName(string? groupName) =>
        string.IsNullOrEmpty(groupName)
            ? null
            : Legends.FirstOrDefault(l => string.Equals(l.GroupName, groupName, StringComparison.OrdinalIgnoreCase));

    public void Add(LegendDefinition definition) => Legends.Add(definition);

    public bool Remove(string id)
    {
        var found = FindById(id);
        return found is not null && Legends.Remove(found);
    }

    // Deterministische standaardnaam "Legenda N", waarbij N het laagste vrije nummer is.
    public string NextDefaultName()
    {
        var used = new HashSet<int>();
        foreach (var l in Legends)
            if (l.Name.StartsWith("Legenda ", StringComparison.OrdinalIgnoreCase)
                && int.TryParse(l.Name["Legenda ".Length..], out var n))
                used.Add(n);
        int i = 1;
        while (used.Contains(i)) i++;
        return $"Legenda {i}";
    }

    // Een uniek group-naamsuffix; kort en zonder tekens die AutoCAD-groupnamen verbieden.
    public static string NewGroupName() => "NLCS-Legenda-" + Guid.NewGuid().ToString("N")[..12];

    public string ToJson() => JsonSerializer.Serialize(this, LegendSettings.JsonOptions);

    public void Normalize()
    {
        Legends ??= new List<LegendDefinition>();
        foreach (var l in Legends)
        {
            l.SourceHandles ??= new List<string>();
            (l.Settings ??= new LegendSettings()).Normalize();
        }
    }

    // Strikt lezen: geeft false bij ongeldige JSON of een niet-ondersteunde nieuwere
    // schemaversie, zodat kritieke legenda-metadata nooit stil door defaults wordt vervangen.
    public static bool TryParse(string? json, out LegendRegistry registry, out string error)
    {
        registry = new LegendRegistry();
        error = string.Empty;
        if (string.IsNullOrWhiteSpace(json))
            return false;
        try
        {
            var parsed = JsonSerializer.Deserialize<LegendRegistry>(json, LegendSettings.JsonOptions);
            if (parsed is null)
            {
                error = "Lege of ongeldige registry.";
                return false;
            }
            parsed.Normalize();
            registry = parsed;
            if (!parsed.IsSupported)
            {
                error = $"Registry-schemaversie {parsed.SchemaVersion} wordt niet ondersteund door deze plugin.";
                return false;
            }
            return true;
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException or ArgumentException)
        {
            error = ex.Message;
            return false;
        }
    }
}
