namespace NlcsLegenda.Core;

public sealed class CustomStatus
{
    public string Name { get; set; } = string.Empty;

    public List<string> Members { get; set; } = new();

    public bool IsValid => !string.IsNullOrWhiteSpace(Name);

    public CustomStatus Clone() => new()
    {
        Name = Name,
        Members = new List<string>(Members)
    };
}
