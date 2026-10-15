namespace NlcsLegenda.Core;

public sealed class CustomStatus
{
    public string Name { get; set; } = string.Empty;

    public List<string> Members { get; set; } = new();

    public bool IsValid => !string.IsNullOrWhiteSpace(Name);

    public static bool Assign(IReadOnlyList<CustomStatus> statuses, CustomStatus target, string key)
    {
        bool changed = false;
        foreach (var status in statuses)
            if (!ReferenceEquals(status, target))
                changed |= status.Members.RemoveAll(m => string.Equals(m, key, StringComparison.OrdinalIgnoreCase)) > 0;
        if (!target.Members.Contains(key, StringComparer.OrdinalIgnoreCase))
        {
            target.Members.Add(key);
            changed = true;
        }
        return changed;
    }

    public CustomStatus Clone() => new()
    {
        Name = Name,
        Members = new List<string>(Members)
    };
}
