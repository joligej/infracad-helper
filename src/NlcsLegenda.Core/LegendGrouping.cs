namespace NlcsLegenda.Core;

public static class LegendGrouping
{
    private static readonly NlcsDrawType[] SwatchTypes =
    {
        NlcsDrawType.Geometrie, NlcsDrawType.Vlak, NlcsDrawType.Arcering,
        NlcsDrawType.Vlakvulling, NlcsDrawType.Symbool
    };

    public static IReadOnlyList<LegendEntry> Build(
        IEnumerable<NlcsLayerName> usedLayers, LegendSettings settings,
        Func<string, string?>? layerDescription = null,
        IReadOnlyDictionary<string, LayerMetric>? metrics = null,
        Func<string, string?>? symbolBlock = null,
        DescriptionCatalog? catalog = null)
    {
        var descriptions = catalog ?? DescriptionCatalog.Default();
        var groups = new Dictionary<string, List<NlcsLayerName>>();

        foreach (var layer in usedLayers)
        {
            if (!settings.IsIncluded(layer)) continue;
            if (!SwatchTypes.Contains(layer.DrawType)) continue;
            // Uitgeschakelde elementsoorten tellen ook niet mee voor hoeveelheden.
            if (!settings.IsDrawTypeIncluded(layer.DrawType)) continue;

            if (!groups.TryGetValue(layer.GroupKey, out var list))
            {
                list = new List<NlcsLayerName>();
                groups[layer.GroupKey] = list;
            }
            list.Add(layer);
        }

        var entries = new List<LegendEntry>(groups.Count);
        foreach (var group in groups.Values)
        {
            var byType = new Dictionary<NlcsDrawType, string>();
            foreach (var layer in group.OrderBy(l => l.Scale ?? 0))
            {
                if (!byType.ContainsKey(layer.DrawType))
                    byType[layer.DrawType] = layer.LocalName;
            }

            var representative = group
                .OrderBy(l => TypePriority(l.DrawType))
                .ThenBy(l => l.Scale ?? 0)
                .First();

            var metric = LayerMetric.Empty;
            if (metrics is not null)
                foreach (var layer in group)
                    if (metrics.TryGetValue(layer.LocalName, out var m))
                        metric += m;

            string? blockName = null;
            if (symbolBlock is not null && byType.TryGetValue(NlcsDrawType.Symbool, out var symLayer))
                blockName = symbolBlock(symLayer);

            entries.Add(new LegendEntry
            {
                Status = representative.Status,
                CustomStatusName = settings.FindCustomStatus(LegendSettings.EntryKey(representative))?.Name,
                Discipline = representative.Discipline,
                Hoofdgroep = representative.Hoofdgroep,
                Element = representative.Element,
                Description = ResolveDescription(
                    group, representative, settings, layerDescription, descriptions, out var descSource),
                DescriptionSource = descSource,
                LayersByType = byType,
                Metric = metric,
                SymbolBlockName = blockName
            });
        }

        foreach (var manual in settings.ManualEntries)
        {
            if (!manual.IsValid) continue;
            if (!settings.IsDrawTypeIncluded(manual.Type)) continue;
            var basic = manual.ToLegendEntry();
            var custom = settings.FindCustomStatus(LegendSettings.EntryKey(basic))?.Name;
            entries.Add(custom is null ? basic : manual.ToLegendEntry(custom));
        }

        return Sort(entries, settings);
    }

    private static List<LegendEntry> Sort(List<LegendEntry> entries, LegendSettings settings)
    {
        var mode = settings.SortMode;
        // Eigen statussen komen als eigen sorteerband na de vaste NLCS-statussen.
        var ordered = entries
            .OrderBy(e => StatusBand(e, settings))
            .ThenBy(e => e.Status.SortOrder());
        return mode switch
        {
            LegendSortMode.Naam =>
                ordered.ThenBy(e => e.Description, StringComparer.CurrentCultureIgnoreCase).ToList(),
            LegendSortMode.Hoeveelheid =>
                ordered.ThenByDescending(e => e.QuantitySortValue())
                       .ThenBy(e => e.Description, StringComparer.CurrentCultureIgnoreCase).ToList(),
            _ =>
                ordered.ThenBy(e => e.Hoofdgroep, StringComparer.OrdinalIgnoreCase)
                       .ThenBy(e => e.Description, StringComparer.CurrentCultureIgnoreCase).ToList()
        };
    }

    private static int StatusBand(LegendEntry entry, LegendSettings settings)
    {
        if (entry.CustomStatusName is null)
            return entry.Status.SortOrder();
        int idx = settings.CustomStatuses.FindIndex(
            c => string.Equals(c.Name, entry.CustomStatusName, StringComparison.OrdinalIgnoreCase));
        return 100 + (idx < 0 ? int.MaxValue - 100 : idx);
    }

    private static string ResolveDescription(
        List<NlcsLayerName> group, NlcsLayerName representative, LegendSettings settings,
        Func<string, string?>? layerDescription, DescriptionCatalog catalog, out DescriptionSource source)
    {
        if (settings.TextOverrides.TryGetValue(representative.Element, out var byElement))
        {
            source = DescriptionSource.EigenTekst;
            return byElement;
        }
        foreach (var layer in group)
            if (settings.TextOverrides.TryGetValue(layer.LocalName, out var byLayer))
            {
                source = DescriptionSource.EigenTekst;
                return byLayer;
            }

        if (layerDescription is not null)
        {
            foreach (var layer in group.OrderBy(l => TypePriority(l.DrawType)))
            {
                var desc = layerDescription(layer.LocalName);
                if (!string.IsNullOrWhiteSpace(desc))
                {
                    source = DescriptionSource.Laagbeschrijving;
                    return desc.Trim();
                }
            }
        }

        var text = catalog.Describe(
            representative, settings.IncludeGeneralDescription, out var matched, settings.GeneralSeparator);
        source = matched ? DescriptionSource.Catalogus : DescriptionSource.Laagnaam;
        return text;
    }

    private static int TypePriority(NlcsDrawType type) => type switch
    {
        NlcsDrawType.Geometrie => 0,
        NlcsDrawType.Vlak => 1,
        NlcsDrawType.Arcering => 2,
        NlcsDrawType.Vlakvulling => 3,
        NlcsDrawType.Symbool => 4,
        _ => 5
    };
}
