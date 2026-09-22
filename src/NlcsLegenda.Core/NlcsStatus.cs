namespace NlcsLegenda.Core;

public enum NlcsStatus
{
    Nieuw,

    Bestaand,

    Vervallen,

    Tijdelijk,

    Revisie,

    Overig
}

public static class NlcsStatusExtensions
{
    public static NlcsStatus FromCode(string? code) => (code ?? string.Empty).Trim().ToUpperInvariant() switch
    {
        "N" => NlcsStatus.Nieuw,
        "B" => NlcsStatus.Bestaand,
        "V" => NlcsStatus.Vervallen,
        "T" => NlcsStatus.Tijdelijk,
        "R" => NlcsStatus.Revisie,
        _ => NlcsStatus.Overig
    };

    public static string Code(this NlcsStatus status) => status switch
    {
        NlcsStatus.Nieuw => "N",
        NlcsStatus.Bestaand => "B",
        NlcsStatus.Vervallen => "V",
        NlcsStatus.Tijdelijk => "T",
        NlcsStatus.Revisie => "R",
        _ => "X"
    };

    public static int SortOrder(this NlcsStatus status) => status switch
    {
        NlcsStatus.Nieuw => 0,
        NlcsStatus.Bestaand => 1,
        NlcsStatus.Vervallen => 2,
        NlcsStatus.Tijdelijk => 3,
        NlcsStatus.Revisie => 4,
        _ => 5
    };

    public static string DisplayName(this NlcsStatus status) => status switch
    {
        NlcsStatus.Nieuw => "Nieuw",
        NlcsStatus.Bestaand => "Bestaand",
        NlcsStatus.Vervallen => "Vervallen",
        NlcsStatus.Tijdelijk => "Tijdelijk",
        NlcsStatus.Revisie => "Revisie",
        _ => "Overig"
    };
}
