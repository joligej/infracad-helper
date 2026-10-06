namespace NlcsLegenda.Core;

// Een blanco legendaregel: een bewust lege regel om ruimte te reserveren voor iets dat de
// gebruiker later met de hand toevoegt. Geen bronobject, geen hoeveelheid, geen swatchinhoud;
// alleen (bewerkbare) tekst en eventueel het normale swatchkader. Elke regel heeft een eigen
// stabiele identiteit zodat meerdere blanco's naast elkaar blijven bestaan, ook met dezelfde tekst.
public sealed class BlankEntry
{
    public const string DefaultText = "[blanco]";

    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string Text { get; set; } = DefaultText;

    public NlcsStatus Status { get; set; } = NlcsStatus.Nieuw;

    // De tekst is intern verplicht: een lege tekst valt terug op de placeholder, zodat een
    // blanco regel altijd zichtbaar en beheerbaar blijft in de lijst.
    public string EffectiveText =>
        string.IsNullOrWhiteSpace(Text) ? DefaultText : Text.Trim();

    public BlankEntry Clone() => new() { Id = Id, Text = Text, Status = Status };

    // Kopie met een nieuwe identiteit; voor dupliceren zodat de kopie los staat van het origineel.
    public BlankEntry CloneWithNewId() => new() { Text = Text, Status = Status };

    public LegendEntry ToLegendEntry(string? customStatusName = null) => new()
    {
        Status = Status,
        CustomStatusName = customStatusName,
        Discipline = "XX",
        Hoofdgroep = "HM",
        // Unieke element-sleutel per regel: blanco's met gelijke tekst mogen niet samenvallen.
        Element = "BLANCO_" + Id,
        Description = EffectiveText,
        DescriptionSource = DescriptionSource.Blanco,
        LayersByType = new Dictionary<NlcsDrawType, string>(),
        Metric = LayerMetric.Empty
    };
}
