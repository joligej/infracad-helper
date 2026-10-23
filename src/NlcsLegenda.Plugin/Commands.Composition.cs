using Autodesk.AutoCAD.DatabaseServices;
using NlcsLegenda.Core;

namespace NlcsLegenda.Plugin;

public partial class Commands
{
    internal static List<LegendEntry> ResolveExclusionPick(Database db, Transaction tr, string legendId,
        AnalysisResult analysis, IReadOnlyList<ObjectId> picked, out bool legendRow)
    {
        var registry = LegendStore.Load(db, tr);
        var local = picked.Where(id => !id.IsNull && id.IsValid && !id.IsErased && id.Database == db).ToArray();
        var owner = local.Select(id => LegendManagement.FindLegendForEntity(db, tr, registry, id)).FirstOrDefault(d => d is not null);
        legendRow = owner is not null;
        if (owner is not null)
        {
            if (owner.Id != legendId) return new();
            foreach (var entity in local.Select(id => tr.GetObject(id, OpenMode.ForRead)).OfType<Entity>())
            {
                var keys = LegendManagement.ReadRowKeys(entity, tr).ToHashSet(StringComparer.OrdinalIgnoreCase);
                if (keys.Count > 0) return analysis.Entries.Where(e => e.ExclusionKeys.Any(keys.Contains)).ToList();
            }
            // oude legenda's krijgen volledige regelsleutels bij het bijwerken
            legendRow = false;
            foreach (var entity in local.Select(id => tr.GetObject(id, OpenMode.ForRead)).OfType<Entity>())
                if (LegendManagement.ReadRow(entity) is { } row)
                    return analysis.Entries.Where(e => e.Hoofdgroep == row.Hoofdgroep && e.Element == row.Element).ToList();
            return new();
        }
        var ids = picked.ToHashSet();
        foreach (var entity in local.Select(id => tr.GetObject(id, OpenMode.ForRead)).OfType<AttributeReference>()) ids.Add(entity.OwnerId);
        return analysis.Entries.Where(e => analysis.EntryObjects.TryGetValue(LegendSettings.SelectionKey(e), out var sources)
            && sources.Any(ids.Contains)).ToList();
    }

    private static List<EntryCheckItem> BuildCompositionItems(
        Database db, LegendSettings settings, string? legendId = null)
    {
        var probe = settings.Clone();
        probe.ExcludedEntries.Clear();
        probe.MergedDimensions.Clear();
        probe.NonKlicMergedDimensions.Clear();
        probe.ManualMergeGroups.Clear();
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
        settings.RememberEntries(analysis.Entries);
        var items = analysis.Entries
            .Select(e => new EntryCheckItem(LegendSettings.SelectionKey(e),
                $"[{e.Status.DisplayName()}] {e.DisplayDescription}" + AppearanceLabel(e, analysis), EntryGroupLabel(e),
                string.Join(" ", e.SourceLayers.Select(l => l.SourceLayerName).Concat(e.LayersByType.Values))))
            .GroupBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .OrderBy(x => x.Group, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(x => x.Label, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        tr.Commit();
        return items;
    }

    private static string AppearanceLabel(LegendEntry entry, AnalysisResult analysis)
    {
        var styles = entry.LayersByType.Values.Where(analysis.LayerStyles.ContainsKey).Select(k => analysis.LayerStyles[k]);
        var labels = styles.Select(s => $"{s.Color} / {LayerNaming.LocalName(s.Linetype)}").Distinct().ToArray();
        return labels.Length == 0 ? "" : " — " + string.Join(", ", labels);
    }

    private static IReadOnlyList<ManualEntry> BuildLayerChoices(Database db, LegendSettings settings)
    {
        using var tr = db.TransactionManager.StartTransaction();
        var excluded = LegendManagement.CollectManagedIds(db, tr, LegendStore.Load(db, tr));
        var blocks = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        var patterns = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        var model = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForRead);
        foreach (ObjectId id in model)
        {
            if (excluded.Contains(id)) continue;
            if (tr.GetObject(id, OpenMode.ForRead) is not Entity entity) continue;
            var names = entity is BlockReference ? blocks : patterns;
            string? name = entity switch
            {
                BlockReference br when !((BlockTableRecord)tr.GetObject(br.BlockTableRecord, OpenMode.ForRead)).IsFromExternalReference => DrawingAnalyzer.BlockName(br, tr),
                Hatch hatch => hatch.PatternName,
                _ => null
            };
            if (name is null) continue;
            if (!names.TryGetValue(entity.Layer, out var values)) names[entity.Layer] = values = new(StringComparer.OrdinalIgnoreCase);
            values.Add(name);
        }
        var catalog = DescriptionCatalog.Default();
        catalog.MergeFrom(settings.DescriptionOverrides);
        var choices = new List<ManualEntry>();
        foreach (ObjectId id in (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead))
        {
            var layer = (LayerTableRecord)tr.GetObject(id, OpenMode.ForRead);
            if (layer.IsDependent || layer.Name.StartsWith("NLCS_LEGENDA", StringComparison.OrdinalIgnoreCase)) continue;
            bool parsed = NlcsLayerParser.TryParse(layer.Name, out var nlcs);
            string description = parsed ? LayerDescription.Clean(layer.Description, nlcs!)
                ?? catalog.Describe(nlcs!, false, out _) : string.IsNullOrWhiteSpace(layer.Description) ? StandardTexts.Humanize(layer.Name) : layer.Description;
            if (parsed)
            {
                string own = settings.DescriptionOverrides.Describe(nlcs!, false, out bool matched);
                if (matched) description = own;
            }
            string? block = blocks.TryGetValue(layer.Name, out var bs) && bs.Count == 1 ? bs.Single() : null;
            string? pattern = patterns.TryGetValue(layer.Name, out var ps) && ps.Count == 1 ? ps.Single() : null;
            choices.Add(new ManualEntry { Layer = layer.Name, Description = description,
                Type = parsed ? nlcs!.DrawType : block is not null ? NlcsDrawType.Symbool : pattern is not null ? NlcsDrawType.Arcering : NlcsDrawType.Geometrie,
                Status = parsed ? nlcs!.Status : NlcsStatus.Nieuw, Hoofdgroep = parsed ? nlcs!.Hoofdgroep : "HM",
                SymbolBlock = block, HatchPattern = pattern });
        }
        tr.Commit();
        return choices.OrderBy(c => c.Layer, StringComparer.OrdinalIgnoreCase).ToArray();
    }
}
