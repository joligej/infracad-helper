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
        Func<string, IReadOnlyList<KlicObjectData>>? klicObjects = null,
        Func<string, string?>? appearanceLabel = null, Func<string, string?>? selectionIdentity = null,
        Func<string, string?>? symbolSelectionIdentity = null, Func<string, string?>? fullRenderIdentity = null,
        Func<string, IReadOnlyList<AppearanceValue>>? appearanceValues = null,
        Func<string, IReadOnlyList<LegendSource>>? sources = null)
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

            var splitKey = layer.GroupKey + "\u0001" + layer.Netbeheerder.ToUpperInvariant() + "\u0001" + layer.KlicGroupKey + "\u0001" + layer.DrawType + "\u0001" + layer.RenderIdentity;
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

            var objects = KlicSummary.Combine(group.SelectMany(l => klicObjects?.Invoke(l.EffectiveRenderLayerName) ?? Array.Empty<KlicObjectData>()));
            entries.Add(new LegendEntry
            {
                Sources = group.SelectMany(l => sources?.Invoke(l.EffectiveRenderLayerName) ?? Array.Empty<LegendSource>()).Distinct().ToArray(),
                Status = representative.Status,
                CustomStatusName = settings.FindCustomStatus(LegendSettings.EntryKey(representative))?.Name,
                Discipline = representative.Discipline,
                Hoofdgroep = representative.Hoofdgroep,
                Element = representative.Element,
                Netbeheerders = string.IsNullOrEmpty(representative.Netbeheerder) ? Array.Empty<string>() : new[] { representative.Netbeheerder },
                KlicGroupKey = representative.KlicGroupKey,
                KlicObjects = objects,
                MergedValues = Array.Empty<string>(),
                AppearanceValues = group.SelectMany(l => appearanceValues?.Invoke(l.EffectiveRenderLayerName) ?? Array.Empty<AppearanceValue>()).Distinct().ToArray(),
                ShowMergedValues = settings.ShowMergedValues,
                KlicDescriptionFields = settings.Klic.DescriptionFields.ToArray(),
                Description = ResolveDescription(
                    group, representative, settings, layerDescription, descriptions, descriptionSourceOf, blockName, out var descSource),
                DescriptionSource = descSource,
                LayersByType = byType,
                SourceRenderLayers = group.Select(layer => layer.EffectiveRenderLayerName).Distinct().ToArray(),
                Metric = metric,
                SymbolBlockName = blockName,
                SourceLayers = group.SelectMany(layer => layer.SourceLayers.Count > 0
                    ? layer.SourceLayers
                    : new[] { layer.Source }).Distinct().ToArray(),
                RenderIdentity = renderIdentity is null ? representative.RenderIdentity : RenderId(byType, renderIdentity, blockName, symbolRenderIdentity),
                FullRenderIdentity = fullRenderIdentity is null ? "" : string.Join(";", byType.OrderBy(k => k.Key).Select(k => $"{k.Key}={fullRenderIdentity(k.Value)}")),
                SelectionIdentity = selectionIdentity is null ? "" : RenderId(byType, selectionIdentity, blockName, symbolSelectionIdentity ?? symbolRenderIdentity),
                LegacySelectionIdentity = selectionIdentity is not null && symbolSelectionIdentity is not null
                    ? RenderId(byType, selectionIdentity, blockName, symbolRenderIdentity) : "",
                AppearanceLabel = string.Join(", ", byType.Values.Select(k => appearanceLabel?.Invoke(k)).Where(t => !string.IsNullOrEmpty(t)).Distinct())
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

        LegendContinuity.Reconcile(settings, entries, final: false);
        LegendContinuity.ApplyChoices(settings, entries);
        settings.RememberEntries(entries);
        entries.RemoveAll(e => e.SourceExcluded ?? settings.IsEntryExcluded(LegendSettings.SelectionKey(e)));
        var merged = Merge(entries, settings);
        LegendContinuity.Reconcile(settings, merged, final: true);
        return Sort(ManualMergeGroup.Apply(merged, settings.ManualMergeGroups, settings.ShowMergedValues), settings);
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
        if (settings.MergedDimensions.Count == 0 && settings.NonKlicMergedDimensions.Count == 0 && settings.MergeRules.Count == 0 && !mergeStatus
            && !settings.MergeSymbolSizes && !settings.MergeSymbolRotations && !settings.MergeSymbolAttributes
            && !settings.MergeLineScales && !settings.MergeHatchScales && !settings.MergeHatchAngles)
            return entries;

        // blanco en handmatige regels buiten de samenvoeging
        var separate = entries.Where(e => e.IsBlank || e.DescriptionSource == DescriptionSource.Handmatig).ToList();
        var mergeable = separate.Count == 0 ? entries : entries.Except(separate).ToList();

        string KeyOf(LegendEntry e, bool acrossStatuses)
        {
            var props = ElementProperties.From(e.Element, e.Netbeheerders.FirstOrDefault() ?? "");
            // statussen alleen samenvoegen bij gelijk uiterlijk
            var statusPart = acrossStatuses && e.RenderIdentity.Length > 0
                ? "R:" + (e.FullRenderIdentity.Length > 0 ? e.FullRenderIdentity : e.RenderIdentity)
                : e.StatusGroupId;
            var renderPart = e.RenderIdentity.Length > 0 ? e.RenderIdentity : string.Empty;
            var dimensions = settings.MergeDimensionsFor(e);
            var content = props.MergeKey(dimensions);
            var metadataDimensions = e.IsKlic && settings.Klic.GroupFields.Contains("Materiaal")
                ? dimensions.Where(d => d != GroupDimension.Materiaal).ToArray() : dimensions;
            var metadata = KlicSummary.WithoutMergedFields(e.KlicGroupKey, metadataDimensions);
            var types = string.Join(",", e.LayersByType.Keys.OrderBy(t => t));
            return $"{statusPart}|{e.Discipline}|{e.Hoofdgroep}|{e.IsKlic}|{content}|{metadata}|{types}|{renderPart}|{(settings.RowChoices.Any(c => c.Description is { Length: > 0 } && c.Matches(e)) ? e.Description : "")}";
        }

        List<LegendEntry> MergeGroups(IEnumerable<LegendEntry> source, bool acrossStatuses)
        {
            var result = new List<LegendEntry>();
            foreach (var members in source.GroupBy(e => KeyOf(e, acrossStatuses)).Select(g => g.ToList()))
            {
                var rep = Representative(members);
                if (members.Count == 1 && rep.KlicObjects.Count == 0) { result.Add(rep); continue; }
                var label = MergedDescription.Create(members, rep, settings.MergeDimensionsFor(rep));
                result.Add(Combine(members, description: label.Text, values: label.Values, showValues: settings.ShowMergedValues));
            }
            return result;
        }

        var merged = MergeGroups(mergeable, acrossStatuses: false);
        if (mergeStatus) merged = MergeGroups(merged, acrossStatuses: true);
        merged.AddRange(separate);
        return merged;
    }

    private static LegendEntry Representative(List<LegendEntry> group) =>
        group.OrderBy(e => ElementProperties.From(e.Element, e.Netbeheerders.FirstOrDefault() ?? "").Nummer.Length == 0 ? 0 : 1)
            .ThenBy(e => e.Element, StringComparer.OrdinalIgnoreCase).First();

    internal static LegendEntry Combine(List<LegendEntry> group, LegendEntry? main = null, string manualId = "", string? description = null, IReadOnlyList<string>? values = null, bool showValues = true)
    {
        var rep = main ?? Representative(group);
        var metric = LayerMetric.Empty;
        foreach (var e in group) metric += e.Metric;
        return new LegendEntry
        {
            Sources = group.SelectMany(e => e.Sources).Distinct().ToArray(),
            SourceNotice = string.Join(" ", group.Select(e => e.SourceNotice).Where(t => t.Length > 0).Distinct()),
            Status = rep.Status,
            CustomStatusName = rep.CustomStatusName,
            Discipline = rep.Discipline,
            Hoofdgroep = rep.Hoofdgroep,
            Element = rep.Element,
            KlicObjects = KlicSummary.Combine(group.SelectMany(e => e.KlicObjects)),
            KlicGroupKey = rep.KlicGroupKey,
            KlicDescriptionFields = rep.KlicDescriptionFields,
            Description = description ?? rep.Description,
            MergedValues = values ?? rep.MergedValues,
            AppearanceValues = group.SelectMany(e => e.AppearanceValues).Distinct().ToArray(),
            ShowMergedValues = showValues,
            SourceEntryKeys = group.SelectMany(e => e.ExclusionKeys).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            DescriptionSource = rep.DescriptionSource,
            LayersByType = rep.LayersByType,
            SourceRenderLayers = group.SelectMany(e => e.SourceRenderLayers.Count > 0 ? (IEnumerable<string>)e.SourceRenderLayers : e.LayersByType.Values).Distinct().ToArray(),
            SourceLayers = group.SelectMany(e => e.SourceLayers).Distinct().ToArray(),
            Netbeheerders = group.SelectMany(e => e.Netbeheerders).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(p => p, StringComparer.CurrentCultureIgnoreCase).ToArray(),
            Metric = metric,
            SymbolBlockName = rep.SymbolBlockName,
            RenderIdentity = rep.RenderIdentity,
            FullRenderIdentity = CombinedAppearance(group),
            ManualHatchPattern = rep.ManualHatchPattern,
            ManualMergeId = manualId,
            AppearanceLabel = rep.AppearanceLabel,
            SelectionIdentity = rep.SelectionIdentity,
            MergedMembers = group.Count == 1 ? rep.MergedMembers : group.SelectMany(e => e.MergedMembers.Count > 0 ? e.MergedMembers : new[] { e.Element }).Distinct().ToList()
        };
    }

    private static string CombinedAppearance(IEnumerable<LegendEntry> entries)
    {
        var identities = entries.Select(e => e.FullRenderIdentity.Length > 0 ? e.FullRenderIdentity : e.RenderIdentity)
            .Distinct(StringComparer.Ordinal).OrderBy(v => v, StringComparer.Ordinal).ToArray();
        return identities.Length == 1 ? identities[0] : System.Text.Json.JsonSerializer.Serialize(identities);
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

        if ((SymbolDescription.Variant(blockName).Length > 0 || settings.GenericManholes && blockName is not null && SymbolDescription.IsManhole(blockName, representative.Element))
            && SymbolDescription.Describe(blockName, representative) is { } semanticText)
        {
            source = DescriptionSource.Bloknaam;
            return semanticText;
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
