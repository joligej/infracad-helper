namespace NlcsLegenda.Core;

public sealed class MergeRule
{
    public bool Klic { get; set; }
    public NlcsDrawType Type { get; set; }
    public List<GroupDimension> Dimensions { get; set; } = new();

    public MergeRule Clone() => new() { Klic = Klic, Type = Type, Dimensions = new(Dimensions) };
    public static bool Supports(NlcsDrawType type) => type is NlcsDrawType.Geometrie or NlcsDrawType.Vlak
        or NlcsDrawType.Arcering or NlcsDrawType.Vlakvulling or NlcsDrawType.Symbool;
}
