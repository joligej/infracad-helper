using Autodesk.AutoCAD.DatabaseServices;
using NlcsLegenda.Core;

namespace NlcsLegenda.Plugin;

public partial class Commands
{
    private static List<EntryCheckItem> BuildCompositionItems(
        Database db, LegendSettings settings, string? legendId = null)
    {
        var probe = settings.Clone();
        probe.ExcludedEntries.Clear();
        probe.MergedDimensions.Clear();
        probe.MergeIdenticalStatuses = false;
        probe.ManualEntries.Clear();
        probe.BlankEntries.Clear();
        probe.IncludedStatuses = Enum.GetValues<NlcsStatus>().ToHashSet();

        using var tr = db.TransactionManager.StartTransaction();
        var registry = LegendStore.Load(db, tr);
        var excluded = LegendManagement.CollectManagedIds(db, tr, registry);
        ObjectId[]? selection = null;
        if (legendId is not null)
        {
            var definition = registry.FindById(legendId)
                ?? throw new InvalidOperationException("Legenda niet meer gevonden.");
            if (definition.Scope == LegendScope.Selection)
                selection = LegendManagement.ResolveHandles(db, definition.SourceHandles, out _)
                    .Where(id => !excluded.Contains(id)).ToArray();
        }
        var catalog = legendId is null ? LoadGlobalCatalog() : DescriptionCatalog.Default();
        var analysis = DrawingAnalyzer.Analyze(db, tr, probe, selection, catalog, excluded);
        var items = analysis.Entries
            .Select(e => new EntryCheckItem(LegendSettings.EntryKey(e),
                $"[{e.Status.DisplayName()}] {e.DisplayDescription}", EntryGroupLabel(e)))
            .GroupBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .OrderBy(x => x.Group, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(x => x.Label, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        tr.Commit();
        return items;
    }
}
