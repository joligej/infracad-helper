namespace NlcsLegenda.Core;

public enum TriState
{
    Off,
    On,
    Partial
}

public sealed class CompositionTree
{
    private readonly HashSet<string> _unlistedExclusions = new(StringComparer.OrdinalIgnoreCase);

    public sealed class Leaf
    {
        public string Key { get; init; } = string.Empty;
        public string Label { get; init; } = string.Empty;
        public bool Included { get; set; } = true;
    }

    public sealed class Group
    {
        public string Label { get; init; } = string.Empty;
        public List<Leaf> Leaves { get; } = new();

        public TriState State
        {
            get
            {
                int on = Leaves.Count(l => l.Included);
                if (on == 0) return TriState.Off;
                return on == Leaves.Count ? TriState.On : TriState.Partial;
            }
        }
    }

    public List<Group> Groups { get; } = new();

    public static CompositionTree Build(
        IEnumerable<(string Key, string Label, string Group)> items, ISet<string> excluded)
    {
        var available = items.ToList();
        var expanded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var key in excluded)
        {
            var members = available.Where(i => (string.Equals(LegendSettings.BaseEntryKey(i.Key), key, StringComparison.OrdinalIgnoreCase) || string.Equals(LegendSettings.WithoutKlicGroup(i.Key), key, StringComparison.OrdinalIgnoreCase))).Select(i => i.Key).ToArray();
            if (members.Length == 0) expanded.Add(key);
            else expanded.UnionWith(members);
        }
        excluded = expanded;
        var tree = new CompositionTree();
        tree._unlistedExclusions.UnionWith(excluded);
        var byLabel = new Dictionary<string, Group>(StringComparer.CurrentCultureIgnoreCase);
        foreach (var (key, label, group) in available)
        {
            var groupLabel = string.IsNullOrWhiteSpace(group) ? "Overig" : group.Trim();
            if (!byLabel.TryGetValue(groupLabel, out var g))
            {
                g = new Group { Label = groupLabel };
                byLabel[groupLabel] = g;
                tree.Groups.Add(g);
            }
            tree._unlistedExclusions.Remove(key);
            g.Leaves.Add(new Leaf
            {
                Key = key,
                Label = label,
                Included = !excluded.Contains(key)
            });
        }
        return tree;
    }

    public void SetGroup(Group group, bool included)
    {
        foreach (var leaf in group.Leaves)
            leaf.Included = included;
    }

    public IEnumerable<Leaf> AllLeaves() => Groups.SelectMany(g => g.Leaves);

    // uitsluitingen van tijdelijk ontbrekende bronnen behouden
    public HashSet<string> ExcludedKeys()
    {
        var set = new HashSet<string>(_unlistedExclusions, StringComparer.OrdinalIgnoreCase);
        foreach (var leaf in AllLeaves())
            if (!leaf.Included)
                set.Add(leaf.Key);
        return set;
    }

    public IEnumerable<Group> Filter(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return Groups;
        var needle = text.Trim();
        return Groups.Where(g =>
            g.Label.Contains(needle, StringComparison.CurrentCultureIgnoreCase)
            || g.Leaves.Any(l => l.Label.Contains(needle, StringComparison.CurrentCultureIgnoreCase)));
    }
}
