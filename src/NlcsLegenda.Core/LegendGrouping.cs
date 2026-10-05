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
        Func<string, DescriptionSource?>? descriptionSourceOf = null)
    {
        // Per-legenda omschrijvingen liggen bovenop de (globale) catalogus. Dit is de enige plek
        // waar ze worden toegepast, zodat elke aanroeper dezelfde effectieve teksten krijgt.
        var descriptions = catalog ?? DescriptionCatalog.Default();
        if (settings.DescriptionOverrides.Elementen.Count > 0)
        {
            descriptions = descriptions.Clone();
            descriptions.MergeFrom(settings.DescriptionOverrides);
        }
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
                    group, representative, settings, layerDescription, descriptions, descriptionSourceOf, out var descSource),
                DescriptionSource = descSource,
                LayersByType = byType,
                Metric = metric,
                SymbolBlockName = blockName,
                RenderIdentity = RenderId(byType, renderIdentity, blockName)
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

        return Sort(Merge(entries, settings), settings);
    }

    // Visuele identiteit: per elementsoort de bronlaag-look (kleur/linetype/lineweight) plus
    // symbool. Lege identiteit als er geen laag-info beschikbaar is (dan nooit samenvoegen).
    private static string RenderId(
        Dictionary<NlcsDrawType, string> byType, Func<string, string?>? renderIdentity, string? block)
    {
        if (renderIdentity is null)
            return string.Empty;
        var parts = new List<string>();
        foreach (var kv in byType.OrderBy(k => k.Key))
            parts.Add($"{kv.Key}={renderIdentity(kv.Value) ?? "?"}");
        if (!string.IsNullOrEmpty(block))
            parts.Add("S=" + block);
        return string.Join(";", parts);
    }

    // KLIC-groepering: voegt regels samen die alleen verschillen in een eigenschap die op
    // "samenvoegen" staat. Met "gelijke statussen samenvoegen" vallen ook verschillende
    // statussen samen, maar alleen bij identieke render-identiteit. Hoeveelheden tellen op,
    // de eerste regel levert de weergave, bronleden blijven bewaard. Omkeerbaar.
    private static List<LegendEntry> Merge(List<LegendEntry> entries, LegendSettings settings)
    {
        bool mergeStatus = settings.MergeIdenticalStatuses;
        if (settings.MergedDimensions.Count == 0 && !mergeStatus)
            return entries;

        string KeyOf(LegendEntry e)
        {
            var props = ElementProperties.From(e.Element);
            // Bij statussamenvoeging vervangt de render-identiteit de status in de sleutel,
            // zodat alleen visueel identieke regels over statussen heen samenvallen.
            var statusPart = mergeStatus && e.RenderIdentity.Length > 0
                ? "R:" + e.RenderIdentity
                : e.StatusGroupId;
            return $"{statusPart}|{e.Discipline}|{e.Hoofdgroep}|{props.MergeKey(settings.MergedDimensions)}";
        }

        var merged = new List<LegendEntry>();
        var byKey = new Dictionary<string, List<LegendEntry>>();
        foreach (var e in entries)
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
            Description = rep.Description,
            DescriptionSource = rep.DescriptionSource,
            LayersByType = rep.LayersByType,
            Metric = metric,
            SymbolBlockName = rep.SymbolBlockName,
            RenderIdentity = rep.RenderIdentity,
            MergedMembers = group.Select(e => e.Element).ToList()
        };
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

    // Volgorde van omschrijvingsbronnen (één plek bepaalt de precedence):
    //   1. laagbeschrijving uit de tekening;
    //   2. effectieve catalogus = ingebouwde referentie + per-legenda DescriptionOverrides;
    //   3. nette laagnaam.
    // Het canonieke per-legenda model is DescriptionOverrides (zit in de catalogus verwerkt).
    private static string ResolveDescription(
        List<NlcsLayerName> group, NlcsLayerName representative, LegendSettings settings,
        Func<string, string?>? layerDescription, DescriptionCatalog catalog,
        Func<string, DescriptionSource?>? descriptionSourceOf, out DescriptionSource source)
    {
        if (layerDescription is not null)
        {
            foreach (var layer in group.OrderBy(l => TypePriority(l.DrawType)))
            {
                var desc = layerDescription(layer.LocalName);
                if (string.IsNullOrWhiteSpace(desc))
                    continue;
                // KLIC-symbolen dragen soms lege attribuut-tags als omschrijving mee; die
                // slaan we over en vallen terug op de catalogus/laagnaam.
                if (settings.SuppressKlicPlaceholders && PlaceholderText.IsGeneric(desc))
                    continue;
                // Een eigen-laagkoppeling levert de tekst uit de regel (niet uit de tekening);
                // dan is EigenKoppeling de juiste herkomst in plaats van laagbeschrijving.
                source = descriptionSourceOf?.Invoke(layer.LocalName) ?? DescriptionSource.Laagbeschrijving;
                return desc.Trim();
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
