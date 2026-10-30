namespace NlcsLegenda.Core;

public sealed record CompositionResult(IReadOnlyList<LegendEntry> Entries, IReadOnlyList<LegendEntry> Proposals,
    IReadOnlyList<LegendChange> Notices);

public static class LegendComposition
{
    public static bool Matches(SavedLegendRow row, LegendEntry entry) => row.Id == entry.ApprovedRowId
        || row.OrderKey == LegendOrder.Key(entry) || LegendContinuity.Related(row.Sources, entry.Sources)
        || row.Manual && entry.IsManual && SameManualRow(row.OrderKey, entry);

    private static bool SameManualRow(string previousKey, LegendEntry entry)
    {
        if (!entry.Element.StartsWith("EIGEN_", StringComparison.Ordinal)) return false;
        try { return System.Text.Json.JsonSerializer.Deserialize<string[]>(previousKey) is { Length: 4 } fields
            && fields[0].Equals(LegendSettings.EntryKey(entry), StringComparison.OrdinalIgnoreCase); }
        catch (System.Text.Json.JsonException) { return false; }
    }

    public static CompositionResult Apply(LegendDefinition definition, IReadOnlyList<LegendEntry> entries)
    {
        if (!definition.CompositionLocked) return new(entries, Array.Empty<LegendEntry>(), Array.Empty<LegendChange>());
        var approved = definition.ApprovedRows;
        var output = new List<LegendEntry>(); var used = new HashSet<LegendEntry>(); var notices = new List<LegendChange>();
        foreach (var row in approved)
        {
            var chosen = definition.Settings.RowChoices.Where(c => c.PreviousRowId == row.Id).ToArray();
            var explicitCandidates = entries.Where(e => chosen.Any(c => c.Matches(e))).ToArray();
            var candidates = explicitCandidates.Length > 0 ? explicitCandidates : entries.Where(e => Matches(row, e)).ToArray();
            var exact = candidates.Where(e => LegendOrder.Key(e) == row.OrderKey).ToArray();
            if (exact.Length == 1) candidates = exact;
            if (candidates.Length == 1 && !used.Contains(candidates[0]))
            {
                var entry = candidates[0]; entry.ApprovedRowId = row.Id;
                output.Add(entry); used.Add(entry);
            }
            else if (candidates.Length > 0)
            {
                var retained = row.RetainedEntry(); retained.MissingSource = false;
                retained.SourceNotice = "Vaste regel: de bron is gesplitst of gecombineerd. Kies het juiste bronvoorbeeld; de vorige regel blijft staan.";
                output.Add(retained);
                notices.Add(new("Bronkeuze nodig", row.Description, retained.SourceNotice));
            }
        }
        // eigen regels zijn een bewuste toevoeging via de gebruiker
        foreach (var entry in entries.Where(e => (e.IsManual || e.IsBlank) && !e.Retained && !used.Contains(e)))
        { output.Add(entry); used.Add(entry); }
        var proposals = entries.Where(e => !used.Contains(e) && !e.Retained).ToArray();
        notices.AddRange(proposals.Select(e => new LegendChange("Voorstel", e.DisplayDescription,
            "Niet toegevoegd: de samenstelling staat vast. " + LegendUpdatePlan.Describe(e))));
        return new(LegendGrouping.Sort(output, definition.Settings), proposals, notices);
    }

    public static string ApprovalProblem(LegendDefinition definition, LegendEntry proposal, IReadOnlyList<LegendEntry> entries)
    {
        foreach (var row in definition.ApprovedRows.Where(r => Matches(r, proposal)))
        {
            var candidates = entries.Where(e => Matches(row, e)).ToArray();
            bool chosen = definition.Settings.RowChoices.Any(c => c.PreviousRowId == row.Id && entries.Any(c.Matches));
            if (candidates.Length > 1 && !candidates.Any(e => LegendOrder.Key(e) == row.OrderKey) && !chosen)
                return "Koppel eerst de bestaande regel '" + row.Description + "' via Overzicht > Bron koppelen aan het juiste voorbeeld. Keur daarna eventuele extra regels goed.";
        }
        return "";
    }

    public static void Lock(LegendDefinition definition)
    {
        definition.CompositionLocked = true;
        definition.ApprovedRows = definition.Rows.Select(r => r.Clone()).ToList();
    }

    public static void Remember(LegendDefinition definition, IReadOnlyList<SavedLegendRow> rows)
    {
        if (!definition.CompositionLocked) return;
        foreach (var row in rows)
        {
            int index = definition.ApprovedRows.FindIndex(r => r.Id == row.Id);
            if (index >= 0) definition.ApprovedRows[index] = row.Clone();
            else if (row.Manual) definition.ApprovedRows.Add(row.Clone());
        }
    }
}
