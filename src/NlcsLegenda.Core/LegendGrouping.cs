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
        DescriptionCatalog? catalog = null,
        Func<string, string?>? renderIdentity = null,
        Func<string, DescriptionSource?>? descriptionSourceOf = null,
        Func<string, string?>? symbolRenderIdentity = null,
        Func<string, IReadOnlyList<KlicObjectData>>? klicObjects = null)
    {
        // eigen legendateksten boven de catalogus
        var descriptions = catalog ?? DescriptionCatalog.Default();
        if (settings.DescriptionOverrides.Elementen.Count > 0)
        {
            descriptions = descriptions.Clone();
            descriptions.MergeFrom(settings.DescriptionOverrides);
        }
        var groups = new Dictionary<string, List<NlcsLayerName>>();

        // uitgeschakelde soorten ook buiten de hoeveelheden
        foreach (var layer in usedLayers.Where(layer => settings.IsIncluded(layer)
            && SwatchTypes.Contains(layer.DrawType) && settings.IsDrawTypeIncluded(layer.DrawType)))
        {

            var splitKey = layer.GroupKey + "\u0001" + layer.Netbeheerder.ToUpperInvariant() + "\u0001" + layer.KlicGroupKey + "\u0001" + layer.RenderIdentity;
            if (!groups.TryGetValue(splitKey, out var list))
            {
                list = new List<NlcsLayerName>();
                groups[splitKey] = list;
            }
            list.Add(layer);
        }

        var entries = new List<LegendEntry>(groups.Count);
        foreach (var group in groups.Values)
        {
            var byType = new Dictionary<NlcsDrawType, string>();
            foreach (var layer in group
                .OrderBy(l => l.Scale ?? 0)
                .ThenBy(l => l.LocalName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(l => l.Raw, StringComparer.OrdinalIgnoreCase))
            {
                byType.TryAdd(layer.DrawType, layer.EffectiveRenderLayerName);
            }

            var representative = group
                .OrderBy(l => TypePriority(l.DrawType))
                .ThenBy(l => l.Scale ?? 0)
                .ThenBy(l => l.LocalName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(l => l.Raw, StringComparer.OrdinalIgnoreCase)
                .First();

            var metric = LayerMetric.Empty;
            if (metrics is not null)
                foreach (var contribution in group.Select(layer => metrics.GetValueOrDefault(layer.EffectiveRenderLayerName)))
                    metric += contribution;

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
                Netbeheerders = string.IsNullOrEmpty(representative.Netbeheerder) ? Array.Empty<string>() : new[] { representative.Netbeheerder },
                KlicGroupKey = representative.KlicGroupKey,
                KlicObjects = KlicSummary.Combine(group.SelectMany(l => klicObjects?.Invoke(l.EffectiveRenderLayerName) ?? Array.Empty<KlicObjectData>())),
                KlicDescriptionFields = settings.Klic.DescriptionFields.ToArray(),
                Description = ResolveDescription(
                    group, representative, settings, layerDescription, descriptions, descriptionSourceOf, blockName, out var descSource),
                DescriptionSource = descSource,
                LayersByType = byType,
                Metric = metric,
                SymbolBlockName = blockName,
                SourceLayers = group.SelectMany(layer => layer.SourceLayers.Count > 0
                    ? layer.SourceLayers
                    : new[] { layer.Source }).Distinct().ToArray(),
                RenderIdentity = RenderId(byType, renderIdentity, blockName, symbolRenderIdentity)
            });
        }

        foreach (var manual in settings.ManualEntries.Where(manual => manual.IsValid && settings.IsDrawTypeIncluded(manual.Type)))
        {
            var basic = manual.ToLegendEntry();
            var custom = settings.FindCustomStatus(LegendSettings.EntryKey(basic))?.Name;
            if (custom is null && !settings.IncludedStatuses.Contains(manual.Status))
                continue;
            entries.Add(custom is null ? basic : manual.ToLegendEntry(custom));
        }

        foreach (var blank in settings.BlankEntries)
        {
            var basic = blank.ToLegendEntry();
            var custom = settings.FindCustomStatus(LegendSettings.EntryKey(basic))?.Name;
            if (custom is null && !settings.IncludedStatuses.Contains(blank.Status))
                continue;
            entries.Add(custom is null ? basic : blank.ToLegendEntry(custom));
        }

        return Sort(Merge(entries, settings), settings);
    }

    private static string RenderId(
        Dictionary<NlcsDrawType, string> byType, Func<string, string?>? renderIdentity, string? block,
        Func<string, string?>? symbolRenderIdentity)
    {
        if (renderIdentity is null)
            return string.Empty;
        var parts = new List<string>();
        foreach (var kv in byType.OrderBy(k => k.Key))
            parts.Add($"{kv.Key}={renderIdentity(kv.Value) ?? "?"}");
        if (byType.TryGetValue(NlcsDrawType.Symbool, out var symbolLayer))
        {
            var identity = symbolRenderIdentity?.Invoke(symbolLayer);
            if (!string.IsNullOrEmpty(identity))
                parts.Add(identity);
            else if (!string.IsNullOrEmpty(block))
            {
                var separator = block.LastIndexOf('|');
                parts.Add("S=" + (separator < 0 ? block : block[(separator + 1)..]).Trim());
            }
        }
        return string.Join(";", parts);
    }

    private static List<LegendEntry> Merge(List<LegendEntry> entries, LegendSettings settings)
    {
        bool mergeStatus = settings.MergeIdenticalStatuses;
        if (settings.MergedDimensions.Count == 0 && !mergeStatus)
            return entries;

        // blanco en handmatige regels buiten de samenvoeging
        var separate = entries.Where(e => e.IsBlank || e.DescriptionSource == DescriptionSource.Handmatig).ToList();
        var mergeable = separate.Count == 0 ? entries : entries.Except(separate).ToList();

        string KeyOf(LegendEntry e)
        {
            var props = ElementProperties.From(e.Element, e.Netbeheerders.FirstOrDefault() ?? "");
            // statussen alleen samenvoegen bij gelijk uiterlijk
            var statusPart = mergeStatus && e.RenderIdentity.Length > 0
                ? "R:" + e.RenderIdentity
                : e.StatusGroupId;
            var renderPart = e.RenderIdentity.Length > 0 ? e.RenderIdentity : string.Empty;
            var content = ElementProperties.IsKlic(e.Discipline, e.Hoofdgroep)
                ? props.MergeKey(settings.MergedDimensions) : e.Element;
            return $"{statusPart}|{e.Discipline}|{e.Hoofdgroep}|{content}|{e.KlicGroupKey}|{renderPart}";
        }

        var merged = new List<LegendEntry>();
        var byKey = new Dictionary<string, List<LegendEntry>>();
        foreach (var e in mergeable)
        {
            var key = KeyOf(e);
            if (!byKey.TryGetValue(key, out var list))
            {
                list = new List<LegendEntry>();
                byKey[key] = list;
                merged.Add(e);
            }
            list.Add(e);
        }

        for (int i = 0; i < merged.Count; i++)
        {
            var group = byKey[KeyOf(merged[i])];
            if (group.Count > 1)
                merged[i] = Combine(group);
        }
        merged.AddRange(separate);
        return merged;
    }

    private static LegendEntry Combine(List<LegendEntry> group)
    {
        var rep = group.OrderBy(e => ElementProperties.From(e.Element).Nummer.Length == 0 ? 0 : 1)
                       .ThenBy(e => e.Element, StringComparer.OrdinalIgnoreCase).First();
        var metric = LayerMetric.Empty;
        foreach (var e in group) metric += e.Metric;
        return new LegendEntry
        {
            Status = rep.Status,
            CustomStatusName = rep.CustomStatusName,
            Discipline = rep.Discipline,
            Hoofdgroep = rep.Hoofdgroep,
            Element = rep.Element,
            KlicObjects = KlicSummary.Combine(group.SelectMany(e => e.KlicObjects)),
            KlicGroupKey = rep.KlicGroupKey,
            KlicDescriptionFields = rep.KlicDescriptionFields,
            Description = rep.Description,
            DescriptionSource = rep.DescriptionSource,
            LayersByType = rep.LayersByType,
            SourceLayers = group.SelectMany(e => e.SourceLayers).Distinct().ToArray(),
            Netbeheerders = group.SelectMany(e => e.Netbeheerders).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(p => p, StringComparer.CurrentCultureIgnoreCase).ToArray(),
            Metric = metric,
            SymbolBlockName = rep.SymbolBlockName,
            RenderIdentity = rep.RenderIdentity,
            MergedMembers = group.Select(e => e.Element).ToList()
        };
    }

    public static List<LegendEntry> Sort(IEnumerable<LegendEntry> source, LegendSettings settings)
    {
        var entries = source.ToList();
        var mode = settings.SortMode;
        if (mode == LegendSortMode.Handmatig)
        {
            var automatic = settings.Clone();
            automatic.SortMode = LegendSortMode.Status;
            return LegendOrder.Apply(Sort(entries, automatic), settings.ManualEntryOrder);
        }
        var ordered = entries
            .OrderBy(e => StatusBand(e, settings))
            .ThenBy(e => e.Status.SortOrder());
        return mode switch
        {
            LegendSortMode.Naam =>
                ordered.ThenBy(e => e.Description, StringComparer.CurrentCultureIgnoreCase)
                       .ThenBy(e => e.RenderIdentity, StringComparer.Ordinal)
                       .ThenBy(e => e.Element, StringComparer.OrdinalIgnoreCase)
                       .ThenBy(e => e.PrimaryLayer, StringComparer.OrdinalIgnoreCase).ToList(),
            LegendSortMode.Hoeveelheid =>
                ordered.ThenByDescending(e => e.QuantitySortValue())
                       .ThenBy(e => e.Description, StringComparer.CurrentCultureIgnoreCase)
                       .ThenBy(e => e.RenderIdentity, StringComparer.Ordinal)
                       .ThenBy(e => e.Element, StringComparer.OrdinalIgnoreCase)
                       .ThenBy(e => e.PrimaryLayer, StringComparer.OrdinalIgnoreCase).ToList(),
            _ =>
                ordered.ThenBy(e => e.Hoofdgroep, StringComparer.OrdinalIgnoreCase)
                       .ThenBy(e => e.Description, StringComparer.CurrentCultureIgnoreCase)
                       .ThenBy(e => e.RenderIdentity, StringComparer.Ordinal)
                       .ThenBy(e => e.Element, StringComparer.OrdinalIgnoreCase)
                       .ThenBy(e => e.PrimaryLayer, StringComparer.OrdinalIgnoreCase).ToList()
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
        Func<string, string?>? layerDescription, DescriptionCatalog catalog,
        Func<string, DescriptionSource?>? descriptionSourceOf, string? blockName, out DescriptionSource source)
    {
        // tekstvolgorde: eigen tekst, bron, catalogus, nette laagnaam
        if (settings.DescriptionOverrides.Elementen.Count > 0)
        {
            var overridden = settings.DescriptionOverrides.Describe(
                representative, settings.IncludeGeneralDescription, out var overrideMatched, settings.GeneralSeparator);
            if (overrideMatched)
            {
                source = DescriptionSource.Catalogus;
                return overridden;
            }
        }

        if (layerDescription is not null)
        {
            foreach (var layer in group.OrderBy(l => TypePriority(l.DrawType)))
            {
                var desc = layerDescription(layer.EffectiveRenderLayerName);
                if (string.IsNullOrWhiteSpace(desc))
                    continue;
                if (settings.SuppressKlicPlaceholders && PlaceholderText.IsGeneric(desc))
                    continue;
                source = descriptionSourceOf?.Invoke(layer.EffectiveRenderLayerName) ?? DescriptionSource.Laagbeschrijving;
                if (source == DescriptionSource.Laagbeschrijving)
                {
                    desc = LayerDescription.Clean(desc, layer);
                    if (desc is null) continue;
                }
                return desc.Trim();
            }
        }

        if (SymbolDescription.Describe(blockName, representative) is { } symbolText)
        {
            source = DescriptionSource.Bloknaam;
            return settings.IncludeGeneralDescription
                ? StandardTexts.HoofdgroepName(representative.Hoofdgroep) + settings.GeneralSeparator + symbolText
                : symbolText;
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
