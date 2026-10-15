using System.Text.Json.Serialization;

namespace NlcsLegenda.Core;

public enum LegendScope
{
    WholeDrawing,
    Selection
}

public static class LegendScopeExtensions
{
    public static string ToDisplay(this LegendScope scope) => scope switch
    {
        LegendScope.Selection => "selectie",
        _ => "hele tekening"
    };
}

public sealed class LegendDefinition
{
    [JsonRequired]
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string Name { get; set; } = string.Empty;

    [JsonRequired]
    public LegendScope Scope { get; set; } = LegendScope.WholeDrawing;

    // vaste bronhandles; object-id verandert bij heropenen
    [JsonRequired]
    public List<string> SourceHandles { get; set; } = new();

    [JsonRequired]
    public string GroupName { get; set; } = string.Empty;

    [JsonRequired]
    public LegendSettings Settings { get; set; } = new();

    public string CreatedWithVersion { get; set; } = string.Empty;

    public LegendDefinition Clone() => new()
    {
        Id = Id,
        Name = Name,
        Scope = Scope,
        SourceHandles = new List<string>(SourceHandles),
        GroupName = GroupName,
        Settings = Settings.Clone(),
        CreatedWithVersion = CreatedWithVersion
    };
}
