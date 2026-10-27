using System.Text.Json;
using System.Text.Json.Serialization;

namespace NlcsLegenda.Core;

public sealed class LegendRegistry
{
    public const int CurrentSchemaVersion = 2;

    [JsonRequired]
    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    [JsonRequired]
    public List<LegendDefinition> Legends { get; set; } = new();

    [JsonIgnore]
    public bool IsSupported => SchemaVersion == CurrentSchemaVersion;

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

    public string NextDefaultName()
    {
        var used = Legends.Select(l => l.Name.StartsWith("Legenda ", StringComparison.OrdinalIgnoreCase)
            && int.TryParse(l.Name["Legenda ".Length..], out var n) ? (int?)n : null).OfType<int>().ToHashSet();
        int i = 1;
        while (used.Contains(i)) i++;
        return $"Legenda {i}";
    }

    public static string NewGroupName() => "NLCS-Legenda-" + Guid.NewGuid().ToString("N")[..12];

    public string ToJson() => JsonSerializer.Serialize(this, LegendSettings.JsonOptions);

    public void Normalize()
    {
        if (Legends is null)
            throw new JsonException("De lijst met opgeslagen legenda's ontbreekt.");
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var groups = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var l in Legends)
        {
            if (l is null || string.IsNullOrWhiteSpace(l.Id) || !ids.Add(l.Id)
                || (!string.IsNullOrEmpty(l.GroupName) && !groups.Add(l.GroupName)))
                throw new JsonException("Legenda-gegevens bevatten een ontbrekende of dubbele identiteit.");
            l.Name ??= string.Empty;
            if (l.GroupName is null || l.SourceHandles is null || l.Settings is null || !Enum.IsDefined(l.Scope))
                throw new JsonException("De opgeslagen bronselectie, groep of instellingen zijn ongeldig.");
            l.Rows ??= new();
            if (l.Rows.Any(r => r is null || r.Sources is null || r.Sources.Any(s => s is null || !s.IsValid) || r.ExclusionKeys is null
                || string.IsNullOrWhiteSpace(r.Id) || r.OrderKey is null || r.Appearance is null || !Enum.IsDefined(r.Status)
                || r.Description is null || r.SampleBlock is null || r.ExclusionKeys.Any(k => k is null)
                || !double.IsFinite(r.Scale) || r.Scale <= 0))
                throw new JsonException("De bewaarde legendaregels zijn ongeldig.");
            l.Settings.Normalize();
        }
    }

    // ongeldige metadata behouden, geen vervanging door standaardwaarden
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
            registry = parsed;
            if (!parsed.IsSupported)
            {
                error = $"Registry-schemaversie {parsed.SchemaVersion} wordt niet ondersteund door deze plugin.";
                return false;
            }
            parsed.Normalize();
            return true;
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException or ArgumentException)
        {
            error = ex.Message;
            return false;
        }
    }
}
