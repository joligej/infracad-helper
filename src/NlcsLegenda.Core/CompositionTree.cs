namespace NlcsLegenda.Core;

public enum TriState
{
    Off,
    On,
    Partial
}

// Groepsboom voor het samenstellen: elke regel hoort bij een groep (bijv. KLIC-soort "Data").
// Een groep kan in één klik aan/uit; de groepsstatus (aan/uit/gedeeltelijk) volgt uit de
// kinderen. Puur datamodel zodat de tri-state-logica los van WinForms testbaar is.
public sealed class CompositionTree
{
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

    // Bouwt de boom uit (key, label, groepslabel)-items; volgorde blijft behouden binnen een
    // groep, groepen verschijnen in eerste-voorkomen-volgorde. Een regel staat uit als de
    // sleutel in 'excluded' zit.
    public static CompositionTree Build(
        IEnumerable<(string Key, string Label, string Group)> items, ISet<string> excluded)
    {
        var tree = new CompositionTree();
        var byLabel = new Dictionary<string, Group>(StringComparer.CurrentCultureIgnoreCase);
        foreach (var (key, label, group) in items)
        {
            var groupLabel = string.IsNullOrWhiteSpace(group) ? "Overig" : group.Trim();
            if (!byLabel.TryGetValue(groupLabel, out var g))
            {
                g = new Group { Label = groupLabel };
                byLabel[groupLabel] = g;
                tree.Groups.Add(g);
            }
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

    // De uitgevinkte sleutels; dit is wat als exclusions wordt opgeslagen.
    public HashSet<string> ExcludedKeys()
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var leaf in AllLeaves())
            if (!leaf.Included)
                set.Add(leaf.Key);
        return set;
    }

    // Groepen met minstens één regel waarvan label of groepsnaam de filtertekst bevat.
    // Lege filter geeft alles. Alleen voor weergave; de include-status verandert niet.
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
