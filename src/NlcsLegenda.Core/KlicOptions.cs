namespace NlcsLegenda.Core;

public sealed class KlicOptions
{
    public List<string> Columns { get; set; } = new() { "Thema", "Materiaal", "DiameterM" };
    public List<string> DescriptionFields { get; set; } = new();
    public List<string> GroupFields { get; set; } = new();
    public KlicSource PreferredSource { get; set; } = KlicSource.InfraCAD;

    public KlicOptions Clone() => new()
    {
        Columns = KlicFields.Valid(Columns), DescriptionFields = KlicFields.Valid(DescriptionFields),
        GroupFields = KlicFields.Valid(GroupFields), PreferredSource = Enum.IsDefined(PreferredSource) ? PreferredSource : KlicSource.InfraCAD
    };
}
