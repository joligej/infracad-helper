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

    public List<SavedLegendRow> Rows { get; set; } = new();

    public bool CompositionLocked { get; set; }
    public List<SavedLegendRow> ApprovedRows { get; set; } = new();
    public List<SheetComparisonException> ComparisonExceptions { get; set; } = new();

    public string SpaceHandle { get; set; } = "";
    public double WindowWidthMm { get; set; }
    public double WindowHeightMm { get; set; }

    public string CreatedWithVersion { get; set; } = string.Empty;

    public LegendDefinition Clone() => new()
    {
        Id = Id, SpaceHandle = SpaceHandle, WindowWidthMm = WindowWidthMm, WindowHeightMm = WindowHeightMm,
        Name = Name, CompositionLocked = CompositionLocked,
        ApprovedRows = ApprovedRows.Select(r => r.Clone()).ToList(),
        ComparisonExceptions = ComparisonExceptions.ToList(),
        Scope = Scope,
        SourceHandles = new List<string>(SourceHandles),
        GroupName = GroupName,
        Settings = Settings.Clone(),
        Rows = Rows.Select(r => r.Clone()).ToList(),
        CreatedWithVersion = CreatedWithVersion
    };
}
