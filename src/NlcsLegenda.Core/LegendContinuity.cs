namespace NlcsLegenda.Core;

public static class LegendContinuity
{
    public static bool InclusionException(LegendSettings settings, LegendSource source) => settings.RowChoices
        .Where(c => c.Matches(source) && c.Excluded.HasValue)
        .OrderBy(c => c.Scope == ChoiceScope.Layer ? 0 : c.Scope == ChoiceScope.ObjectKind ? 1 : 2)
        .LastOrDefault()?.Excluded == false;

    public static bool Related(IEnumerable<LegendSource> previous, IEnumerable<LegendSource> current)
    {
        var old = previous.ToArray();
        var ids = old.SelectMany(s => s.IdentityKeys.Select(id => (id, s.Status, s.Type))).ToHashSet();
        var legacy = old.Where(s => s.DrawingId.Length == 0).Select(s => (s.Id, s.Status, s.Type)).ToHashSet();
        return current.Any(s => legacy.Contains((s.Id, s.Status, s.Type))
            || s.IdentityKeys.Any(id => ids.Contains((id, s.Status, s.Type)))
            || s.DrawingId.Length == 0 && old.Any(s.SameObject));
    }

    public static void Reconcile(LegendSettings settings, IReadOnlyList<LegendEntry> entries, bool final)
    {
        var history = final ? settings.FinalSourceRows : settings.SourceRows;
        var sources = entries.SelectMany(e => e.Sources).GroupBy(s => (s.Id, s.Status, s.Type)).ToDictionary(g => g.Key, g => g.First());
        foreach (var row in history)
            for (int i = 0; i < row.Sources.Count; i++)
            {
                var old = row.Sources[i];
                if (old.DrawingId.Length == 0 && sources.TryGetValue((old.Id, old.Status, old.Type), out var current)) row.Sources[i] = current;
            }
        var index = history.SelectMany(r => r.Sources.SelectMany(s => s.IdentityKeys.Select(id => (Id: id, s.Status, s.Type, Row: r))))
            .GroupBy(p => (p.Id, p.Status, p.Type)).ToDictionary(g => g.Key, g => g.Select(p => p.Row).Distinct().ToArray());
        var remap = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var entry in entries.Where(e => e.Sources.Count > 0))
        {
            var previous = entry.Sources.SelectMany(s => s.IdentityKeys.SelectMany(id => index.GetValueOrDefault((id, s.Status, s.Type)) ?? Array.Empty<SourceRow>())).Distinct().ToArray();
            string key = LegendSettings.SelectionKey(entry), order = LegendOrder.Key(entry);
            if (!final && !settings.KnownEntryKeys.Contains(key) && previous.Any(r => settings.IsEntryExcluded(r.SelectionKey))) settings.ExcludedEntries.Add(key);
            var examples = previous.Where(r => settings.ExampleOverrides.ContainsKey(r.OrderKey))
                .Select(r => settings.ExampleOverrides[r.OrderKey]).Distinct().ToArray();
            if (examples.Length == 1 && !settings.ExampleOverrides.ContainsKey(order)) settings.ExampleOverrides[order] = examples[0];
            if (examples.Length > 1) entry.SourceNotice = "Meerdere gekozen voorbeelden; kies het gewenste voorbeeld in Overzicht.";
            foreach (var old in previous)
            {
                if (!remap.TryGetValue(old.OrderKey, out var keys)) remap[old.OrderKey] = keys = new();
                if (!keys.Contains(order)) keys.Add(order);
            }
        }
        if (final)
        {
            settings.ManualEntryOrder = settings.ManualEntryOrder.SelectMany(k => remap.GetValueOrDefault(k) ?? new() { k }).Distinct().ToList();
            foreach (var merge in settings.ManualMergeGroups)
            {
                // hoofdvoorbeeld alleen bij een eenduidige opvolger
                merge.Members = merge.Members.SelectMany(k => remap.GetValueOrDefault(k) ?? new() { k }).Distinct().ToList();
                if (remap.TryGetValue(merge.MainKey, out var main) && main.Count == 1) merge.MainKey = main[0];
            }
        }
        foreach (var row in entries.Where(e => e.Sources.Count > 0).Select(SourceRow.From))
        {
            history.RemoveAll(old => old.SelectionKey == row.SelectionKey);
            history.Add(row);
        }
        // verdwenen bronnen bewaren; vervangen voorgangers niet eindeloos opstapelen
        var currentIds = entries.SelectMany(e => e.Sources).SelectMany(s => s.IdentityKeys.Select(id => (id, s.Status, s.Type))).ToHashSet();
        var keysNow = entries.Select(LegendSettings.SelectionKey).ToHashSet();
        history.RemoveAll(r => !keysNow.Contains(r.SelectionKey) && r.Sources.Count > 0
            && r.Sources.All(s => s.IdentityKeys.Any(id => currentIds.Contains((id, s.Status, s.Type)))));
    }

    public static void ApplyChoices(LegendSettings settings, List<LegendEntry> entries)
    {
        var index = new RowChoiceIndex(settings.RowChoices);
        foreach (var entry in entries)
        {
            var choices = index.Find(entry.Sources).Concat(settings.RowChoices.Where(c => c.SavedRowId.Length > 0 && c.Matches(entry))).Distinct().ToArray();
            foreach (var choice in choices)
            {
                if (choice.Description is { Length: > 0 }) entry.Description = choice.Description;
                if (choice.ComparisonKey is not null) entry.ComparisonKey = choice.ComparisonKey;
                if (choice.Mode.HasValue) entry.ExampleMode = choice.Mode.Value;
                if (choice.AutomaticExample) settings.ExampleOverrides.Remove(LegendOrder.Key(entry));
                else if (choice.Example is not null) settings.ExampleOverrides[LegendOrder.Key(entry)] = choice.Example;
            }
            entry.SourceExcluded = choices.LastOrDefault(c => c.Excluded.HasValue)?.Excluded;
            entry.ExcludedBySource = entry.SourceExcluded == true;
        }
    }

    public static RowChoice SetChoice(LegendSettings settings, LegendEntry entry, ChoiceScope scope,
        string? description = null, bool? excluded = null, string? example = null, bool automaticExample = false)
    {
        string Key(LegendSource source) => scope switch
        {
            ChoiceScope.Layer => source.LayerKey,
            ChoiceScope.ObjectKind => source.Category,
            _ => $"{source.Id}|{source.Type}|{source.Status}"
        };
        var keys = entry.Sources.Select(Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        string savedId = entry.Retained ? entry.Element["BEWAARD_".Length..] : "";
        var choice = settings.RowChoices.LastOrDefault(c => c.Scope == scope && c.SavedRowId == savedId
            && keys.SetEquals(c.Sources.Select(Key)));
        if (choice is null)
        {
            choice = new RowChoice { Sources = entry.Sources.ToList(), Scope = scope, SavedRowId = savedId };
            settings.RowChoices.Add(choice);
        }
        if (description is not null) choice.Description = description;
        if (excluded.HasValue) choice.Excluded = excluded;
        if (example is not null || automaticExample)
        {
            choice.Example = example; choice.AutomaticExample = automaticExample;
            choice.Mode = automaticExample ? ExampleMode.FollowSource : ExampleMode.Fixed;
        }
        return choice;
    }
}
