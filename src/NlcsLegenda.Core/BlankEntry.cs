namespace NlcsLegenda.Core;

public sealed class BlankEntry
{
    public const string DefaultText = "[blanco]";

    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string Text { get; set; } = DefaultText;

    public NlcsStatus Status { get; set; } = NlcsStatus.Nieuw;

    public string EffectiveText =>
        string.IsNullOrWhiteSpace(Text) ? DefaultText : Text.Trim();

    public BlankEntry Clone() => new() { Id = Id, Text = Text, Status = Status };

    public BlankEntry CloneWithNewId() => new() { Text = Text, Status = Status };

    public LegendEntry ToLegendEntry(string? customStatusName = null) => new()
    {
        Status = Status,
        CustomStatusName = customStatusName,
        Discipline = "XX",
        Hoofdgroep = "HM",
        // aparte identiteit voor blanco regels met gelijke tekst
        Element = "BLANCO_" + Id,
        Description = EffectiveText,
        DescriptionSource = DescriptionSource.Blanco,
        LayersByType = new Dictionary<NlcsDrawType, string>(),
        Metric = LayerMetric.Empty
    };
}
