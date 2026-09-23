namespace NlcsLegenda.Core;

// Waar de bron van een legenda vandaan komt.
public enum LegendScope
{
    WholeDrawing,
    Selection
}

// Eén beheerde legenda in een tekening: stabiele identiteit, bron-scope en een eigen
// instellingen-snapshot. De geometrie hoort bij de AutoCAD-group met naam GroupName.
public sealed class LegendDefinition
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string Name { get; set; } = string.Empty;

    public LegendScope Scope { get; set; } = LegendScope.WholeDrawing;

    // DWG-Handles van de bronobjecten bij Scope=Selection. Handles blijven stabiel in de
    // tekening; ObjectIds niet, dus die worden nooit persistent opgeslagen.
    public List<string> SourceHandles { get; set; } = new();

    // Naam van de AutoCAD-group die de legenda-entiteiten bevat.
    public string GroupName { get; set; } = string.Empty;

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
