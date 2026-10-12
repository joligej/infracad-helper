using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using NlcsLegenda.Core;

namespace NlcsLegenda.Plugin;

public sealed class AnalysisResult
{
    public IReadOnlyList<LegendEntry> Entries { get; init; } = Array.Empty<LegendEntry>();

    public IReadOnlyDictionary<string, HatchSample> HatchSamples { get; init; } =
        new Dictionary<string, HatchSample>();

    public IReadOnlyDictionary<string, SymbolSample> SymbolSamples { get; init; } =
        new Dictionary<string, SymbolSample>();

    // Effectieve laag-look per renderlaag. Een xref-laag bestaat in
    // de host als "xref|laag" en heeft daar de echte kleur; de swatch neemt die over zodat een
    // legenda uit een xref-only kader niet in standaardwit verschijnt.
    public IReadOnlyDictionary<string, LayerDisplayStyle> LayerStyles { get; init; } =
        new Dictionary<string, LayerDisplayStyle>();

    public int UsedNlcsLayerCount { get; init; }

    public int UsedCustomLayerCount { get; init; }

    public int DescribedLayerCount { get; init; }

    public int AttrDescribedLayerCount { get; init; }

    public int ExcludedNlcsLayerCount { get; init; }
}

// Wat een laag zichtbaar maakt in de legenda: effectieve kleur, linetype, lijndikte en transparantie.
// Losse data (geen ObjectId's), zodat het veilig tussen bron-database en host-database reist.
public sealed class LayerDisplayStyle
{
    public required Autodesk.AutoCAD.Colors.Color Color { get; init; }

    public string Linetype { get; init; } = "Continuous";

    public LineWeight LineWeight { get; init; } = LineWeight.ByLineWeightDefault;

    public TransparencyValue Transparency { get; init; } =
        new(Autodesk.AutoCAD.Colors.TransparencyMethod.ByAlpha, 0);
}

public static class DrawingAnalyzer
{
    private sealed class Collector
    {
        public readonly Dictionary<string, NlcsLayerName> Parsed = new(StringComparer.OrdinalIgnoreCase);
        public readonly HashSet<string> NonNlcs = new(StringComparer.OrdinalIgnoreCase);
        public readonly Dictionary<string, HatchSample> Hatches = new(StringComparer.OrdinalIgnoreCase);
        public readonly Dictionary<string, LayerMetric> Metrics = new(StringComparer.OrdinalIgnoreCase);
        public readonly Dictionary<string, SymbolSample> SymbolSamples = new(StringComparer.OrdinalIgnoreCase);
        public readonly Dictionary<ObjectId, string> SymbolDefinitionIdentities = new();
        // Per laag de getelde OMSCHRIJVING-attribuutwaarden van KLIC-symbolen; de meest
        // voorkomende niet-lege waarde is de echte omschrijving uit de tekening.
        public readonly Dictionary<string, Dictionary<string, int>> AttrOms =
            new(StringComparer.OrdinalIgnoreCase);
        public readonly HashSet<string> ExcludedNlcs = new(StringComparer.OrdinalIgnoreCase);
        public readonly Dictionary<string, bool> Visible = new(StringComparer.OrdinalIgnoreCase);
        // Omschrijving per eigen-laag-sleutel (komt uit de regel, niet uit de NLCS-catalogus).
        public readonly Dictionary<string, string> CustomDesc = new(StringComparer.OrdinalIgnoreCase);
        // Per-bron buckets voor AnySource-regels: sleutel -> (regel, bron-id). Na de collectie
        // worden ze op render-identiteit samengevoegd: gelijk uiterlijk = één regel met opgetelde
        // hoeveelheid, zichtbaar verschillend = aparte regels per bron (geen first-wins).
        public readonly Dictionary<string, (CustomLayerRule Rule, string SourceId)> AnyBuckets =
            new(StringComparer.Ordinal);
        // Standaard-NLCS-lagen per echte (bron-)laagnaam. Na de collectie worden ze op render-
        // identiteit samengevoegd: dezelfde logische laag uit bronnen met gelijke weergave wordt
        // één regel met opgetelde hoeveelheid, zichtbaar verschillende bronnen blijven apart.
        public readonly HashSet<string> NlcsRawKeys = new(StringComparer.OrdinalIgnoreCase);
        public HashSet<ObjectId> Excluded = new();
        public LayerTable? Layers;
    }

    // Veiligheidslimiet tegen ontspoorde recursie; echte cycli worden met een pad-set
    // afgevangen, dus deze grens hoeft niet laag te zijn.
    private const int MaxDepth = 16;

    public static AnalysisResult Analyze(
        Database db, Transaction tr, LegendSettings settings,
        IReadOnlyCollection<ObjectId>? selection = null,
        DescriptionCatalog? catalog = null,
        IReadOnlyCollection<ObjectId>? excludedIds = null)
    {
        var c = new Collector();
        c.Layers = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
        if (excludedIds is { Count: > 0 })
            c.Excluded = new HashSet<ObjectId>(excludedIds);
        // Pad-set voor cyclusdetectie op blokdefinities: een blok dat meerdere keren is
        // ingevoegd, wordt per instance geteld, maar een echte cyclus wordt gestopt.
        var path = new HashSet<ObjectId>();

        if (selection is { Count: > 0 })
        {
            foreach (var id in selection)
                if (tr.GetObject(id, OpenMode.ForRead) is Entity ent)
                    Process(ent, tr, c, settings, path, 0, Matrix3d.Identity, insideIncludedXref: false, sourceXref: string.Empty);
        }
        else
        {
            var msId = SymbolUtilityServices.GetBlockModelSpaceId(db);
            CollectFromBlock(msId, tr, c, settings, path, 0, Matrix3d.Identity, insideIncludedXref: false, sourceXref: string.Empty);
        }

        // AnySource-regels die meerdere bronnen raken samenvoegen op render-identiteit vóór de
        // beschrijvings-/groepeerstap, zodat de swatch niet van de traversalevolgorde afhangt.
        CoalesceAnySource(db, tr, c);

        // Dezelfde logische NLCS-laag uit meerdere bronnen: gelijke weergave valt samen (één regel,
        // opgetelde hoeveelheid), zichtbaar verschillende weergave splitst (aparte regels met eigen
        // hoeveelheid en swatchkleur). Geen first/last-wins.
        CoalesceNlcsSources(db, tr, c);

        var parsedLayers = c.Parsed.Values
            .OrderBy(layer => layer.GroupKey, StringComparer.Ordinal)
            .ThenBy(layer => layer.RenderIdentity, StringComparer.Ordinal)
            .ThenBy(layer => layer.Raw, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var descriptions = ReadLayerDescriptions(db, tr, parsedLayers);
        // De echte OMSCHRIJVING uit de KLIC-symboolattributen krijgt voorrang op de laag-
        // beschrijving (die bij KLIC vaak de lege template "TYPE \ LABEL \ OMSCHRIJVING" is).
        var attrDesc = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var kv in c.AttrOms)
            attrDesc[kv.Key] = kv.Value.OrderByDescending(x => x.Value).ThenBy(x => x.Key, StringComparer.Ordinal).First().Key;

        foreach (var manual in settings.ManualEntries)
        {
            if (!manual.IsValid || manual.Type != NlcsDrawType.Arcering)
                continue;
            var layer = manual.Layer.Trim();
            if (c.Hatches.ContainsKey(layer))
                continue;
            var pattern = string.IsNullOrWhiteSpace(manual.HatchPattern) ? "SOLID" : manual.HatchPattern!.Trim();
            c.Hatches[layer] = new HatchSample
            {
                PatternName = pattern,
                PatternType = HatchPatternType.PreDefined,
                IsSolid = pattern.Equals("SOLID", StringComparison.OrdinalIgnoreCase)
            };
        }

        var renderIds = ReadRenderIds(db, tr, parsedLayers);

        // Render-identiteit per laag = laag-look, gesamplede arcering en de brontransformatie van
        // het symbool. Alle samples bevatten alleen waarden; bron-ObjectIds blijven binnen analyse.
        string RenderId(string name)
        {
            renderIds.TryGetValue(name, out var baseId);
            var hatch = c.Hatches.TryGetValue(name, out var h) ? HatchSignature(h) : string.Empty;
            return (baseId ?? string.Empty) + "|" + hatch;
        }

        // DescriptionOverrides worden in LegendGrouping.Build toegepast (één plek), dus hier
        // alleen de basiscatalogus doorgeven.
        var entries = LegendGrouping.Build(
            parsedLayers, settings,
            name => c.CustomDesc.TryGetValue(name, out var cd) ? cd
                  : attrDesc.TryGetValue(name, out var a) ? a
                  : descriptions.TryGetValue(name, out var d) ? d : null,
            c.Metrics,
            name => c.SymbolSamples.TryGetValue(name, out var sample) ? sample.BlockName : null,
            catalog,
            RenderId,
            name => c.CustomDesc.ContainsKey(name) ? DescriptionSource.EigenKoppeling : (DescriptionSource?)null,
            name => c.SymbolSamples.TryGetValue(name, out var sample) ? sample.RenderIdentity : null);

        return new AnalysisResult
        {
            Entries = entries,
            HatchSamples = c.Hatches,
            SymbolSamples = c.SymbolSamples,
            LayerStyles = ReadLayerStyles(db, tr, parsedLayers),
            // c.Parsed bevat nu zowel NLCS- als eigen bronlagen; eigen lagen apart tellen zodat
            // de INFO-melding geen eigen lagen als "NLCS" presenteert.
            UsedNlcsLayerCount = c.Parsed.Count - c.CustomDesc.Count,
            UsedCustomLayerCount = c.CustomDesc.Count,
            DescribedLayerCount = descriptions.Count,
            AttrDescribedLayerCount = attrDesc.Count,
            ExcludedNlcsLayerCount = c.ExcludedNlcs.Count
        };
    }

    private static void CollectFromBlock(
        ObjectId btrId, Transaction tr, Collector c, LegendSettings settings,
        HashSet<ObjectId> path, int depth, Matrix3d transform, bool insideIncludedXref, string sourceXref)
    {
        if (depth > MaxDepth || !path.Add(btrId))
            return;

        var btr = (BlockTableRecord)tr.GetObject(btrId, OpenMode.ForRead);
        foreach (ObjectId id in btr)
            if (tr.GetObject(id, OpenMode.ForRead) is Entity ent)
                Process(ent, tr, c, settings, path, depth, transform, insideIncludedXref, sourceXref);

        path.Remove(btrId);
    }

    private static void Process(
        Entity ent, Transaction tr, Collector c, LegendSettings settings,
        HashSet<ObjectId> path, int depth, Matrix3d transform, bool insideIncludedXref, string sourceXref)
    {
        // Eigen legenda-geometrie telt nooit als bron.
        if (c.Excluded.Contains(ent.ObjectId))
            return;

        Record(ent, tr, c, settings, transform, sourceXref);

        if (ent is BlockReference br && !br.BlockTableRecord.IsNull)
        {
            var def = tr.GetObject(br.BlockTableRecord, OpenMode.ForRead) as BlockTableRecord;
            if (def is { IsLayout: false })
            {
                var nested = br.BlockTransform * transform;
                if (def.IsFromExternalReference)
                {
                    bool included = insideIncludedXref || settings.IsXrefIncluded(def.Name);
                    if (included)
                        CollectFromBlock(br.BlockTableRecord, tr, c, settings, path, depth + 1, nested, insideIncludedXref: true,
                            sourceXref: string.IsNullOrEmpty(sourceXref) ? def.Name : sourceXref);
                }
                else
                {
                    CollectFromBlock(br.BlockTableRecord, tr, c, settings, path, depth + 1, nested, insideIncludedXref, sourceXref);
                }
            }
        }
    }

    private static void Record(Entity ent, Transaction tr, Collector c, LegendSettings settings, Matrix3d transform, string sourceXref)
    {
        var layerName = ent.Layer;

        // Gemeenschappelijke geschiktheid, bron-onafhankelijk: een onzichtbare laag (uit of
        // bevroren) telt nooit mee, of de laag nu NLCS is of een eigen koppeling heeft. Deze
        // controle staat vóór de classificatie zodat zichtbaarheid voor beide gelijk werkt.
        if (!settings.IncludeInvisibleLayers && !IsVisible(layerName, tr, c))
            return;

        // Expliciete eigen-laagkoppeling wint van NLCS-herkenning: precies één interpretatie.
        if (settings.CustomLayerRules.Count > 0
            && TryMatchCustomRule(ent, tr, settings, layerName, sourceXref, out var rule, out var canonical))
        {
            RecordCustom(ent, tr, c, settings, transform, rule!, canonical!);
            return;
        }

        if (c.NonNlcs.Contains(layerName))
            return;

        if (!c.Parsed.TryGetValue(layerName, out var nlcs))
        {
            if (NlcsLayerParser.TryParse(layerName, out var parsed))
            {
                if (settings.IsIncluded(parsed))
                {
                    nlcs = parsed;
                    c.Parsed[layerName] = parsed;
                }
                else
                {
                    // Gefilterde NLCS-lagen apart tellen voor een eerlijke INFO-melding.
                    c.ExcludedNlcs.Add(parsed.LocalName);
                    c.NonNlcs.Add(layerName);
                    return;
                }
            }
            else
            {
                c.NonNlcs.Add(layerName);
                return;
            }
        }

        c.NlcsRawKeys.Add(layerName);
        AddMetric(ent, layerName, c, transform);

        if (ent is Hatch hatch)
            RegisterHatchSample(c.Hatches, layerName, layerName, hatch);

        if (ent is BlockReference symbolRef && nlcs.DrawType == NlcsDrawType.Symbool)
        {
            RegisterSymbolSample(c.SymbolSamples, c.SymbolDefinitionIdentities, layerName, layerName,
                symbolRef, tr, transform);
            // KLIC-symbolen dragen de echte omschrijving in het attribuut OMSCHRIJVING. Tel de
            // niet-lege waarden per laag; de meest voorkomende wint bij de omschrijving.
            var oms = ReadAttribute(symbolRef, tr, "OMSCHRIJVING");
            if (!string.IsNullOrWhiteSpace(oms))
            {
                if (!c.AttrOms.TryGetValue(nlcs.LocalName, out var counts))
                {
                    counts = new Dictionary<string, int>(StringComparer.Ordinal);
                    c.AttrOms[nlcs.LocalName] = counts;
                }
                counts.TryGetValue(oms, out var n);
                counts[oms] = n + 1;
            }
        }
    }

    // Leest de waarde van een attribuut (op tag) uit een blokreferentie; lege string als het
    // attribuut ontbreekt. Constante attributen zitten niet in AttributeCollection en worden
    // hier bewust overgeslagen (die dragen geen tekening-specifieke waarde).
    private static string ReadAttribute(BlockReference br, Transaction tr, string tag)
    {
        try
        {
            foreach (ObjectId attId in br.AttributeCollection)
            {
                if (tr.GetObject(attId, OpenMode.ForRead) is AttributeReference att
                    && string.Equals(att.Tag, tag, StringComparison.OrdinalIgnoreCase))
                    return att.TextString?.Trim() ?? string.Empty;
            }
        }
        catch { /* beschadigde/niet-leesbare attributen overslaan */ }
        return string.Empty;
    }

    private static void AddMetric(Entity ent, string localLayer, Collector c, Matrix3d transform)
    {
        c.Metrics.TryGetValue(localLayer, out var metric);
        bool identity = transform.IsEqualTo(Matrix3d.Identity);

        double length = 0, area = 0;
        switch (ent)
        {
            case Hatch h:
                // Oppervlak schaalt met de determinant van het lineaire deel van de transform.
                area = SafeArea(() => h.Area) * AreaScale(transform);
                break;
            case Curve curve:
                if (identity)
                {
                    length = SafeLength(curve);
                    if (curve.Closed) area = SafeArea(() => curve.Area);
                }
                else
                {
                    // Meet op een getransformeerde kopie, zodat rotatie/schaal uit een
                    // blok-insertie correct in lengte en oppervlak doorwerken.
                    (length, area) = MeasureTransformed(curve, transform);
                }
                break;
        }

        c.Metrics[localLayer] = metric.Add(1, length, area);
    }

    // Zoekt de eerste eigen-laagregel die op dit object past (expliciete volgorde = deterministisch).
    // Matcht op de lokale laagnaam (xref-prefix wordt gestript), de bron (scope), de elementsoort
    // van de entiteit en een optioneel blokfilter.
    private static bool TryMatchCustomRule(
        Entity ent, Transaction tr, LegendSettings settings, string layerName, string sourceXref,
        out CustomLayerRule? rule, out NlcsLayerName? canonical)
    {
        foreach (var r in settings.CustomLayerRules)
        {
            if (!r.Matches(layerName, sourceXref)) continue;
            if (!EntityMatchesType(ent, r.Type)) continue;
            if (r.BlockName is { Length: > 0 })
            {
                var entBlock = ent is BlockReference br ? BlockName(br, tr) : string.Empty;
                if (!r.MatchtBlok(entBlock)) continue;
            }
            rule = r;
            canonical = r.ToCanonical(sourceXref, layerName);
            return true;
        }
        rule = null;
        canonical = null;
        return false;
    }

    // Broncompatibiliteit: een regel telt alleen objecten die bij de elementsoort passen, zodat
    // een gemengde laag geen onzin oplevert (bijv. een blok dat als geometrielengte meetelt).
    internal static bool EntityMatchesType(Entity ent, NlcsDrawType type) => type switch
    {
        NlcsDrawType.Symbool => ent is BlockReference,
        NlcsDrawType.Arcering => ent is Hatch,
        NlcsDrawType.Vlakvulling => ent is Hatch or Curve,
        NlcsDrawType.Vlak => ent is Curve or Hatch,
        NlcsDrawType.Geometrie => ent is Curve,
        _ => true
    };

    private static void RecordCustom(
        Entity ent, Transaction tr, Collector c, LegendSettings settings, Matrix3d transform,
        CustomLayerRule rule, NlcsLayerName canonical)
    {
        string key;
        if (rule.Scope == CustomSourceScope.AnySource)
        {
            // Per bron een aparte bucket; coalescing voegt later gelijk-renderende bronnen samen.
            string sourceId = canonical.IsXref ? canonical.XrefName : string.Empty;
            key = "\u0001" + sourceId + "\u0001" + canonical.LocalName;
            if (!c.Parsed.ContainsKey(key))
            {
                c.Parsed[key] = WithLocalName(canonical, key);
                c.CustomDesc[key] = rule.Description ?? string.Empty;
                c.AnyBuckets[key] = (rule, sourceId);
            }
        }
        else
        {
            key = canonical.LocalName;
            if (!c.Parsed.ContainsKey(key))
            {
                c.Parsed[key] = canonical;
                c.CustomDesc[key] = rule.Description ?? string.Empty;
            }
        }

        AddMetricMasked(ent, key, c, transform, rule.QuantityMode);

        if (ent is Hatch hatch)
            RegisterHatchSample(c.Hatches, key, canonical.Raw, hatch);

        if (ent is BlockReference symbolRef && rule.Type == NlcsDrawType.Symbool)
            RegisterSymbolSample(c.SymbolSamples, c.SymbolDefinitionIdentities, key, canonical.Raw,
                symbolRef, tr, transform);
    }

    private static void RegisterHatchSample(
        Dictionary<string, HatchSample> samples, string key, string sourceLayer, Hatch hatch)
    {
        var sample = HatchSample.From(hatch);
        if (samples.TryGetValue(key, out var existing))
        {
            if (!StringComparer.Ordinal.Equals(HatchSignature(existing), HatchSignature(sample)))
                throw new InvalidOperationException(
                    $"Laag '{sourceLayer}' bevat meerdere hatch-renderingen; de legenda is niet eenduidig.");
            return;
        }
        samples[key] = sample;
    }

    private static void RegisterSymbolSample(
        Dictionary<string, SymbolSample> samples, Dictionary<ObjectId, string> definitionIdentities,
        string key, string sourceLayer,
        BlockReference symbol, Transaction tr, Matrix3d transform)
    {
        string? blockName = BlockName(symbol, tr);
        if (string.IsNullOrWhiteSpace(blockName) || blockName.StartsWith('*'))
            return;
        ObjectId definitionId = SymbolDefinitionId(symbol);
        if (!definitionIdentities.TryGetValue(definitionId, out var definitionIdentity))
        {
            definitionIdentity = SymbolDefinitionIdentity(definitionId, symbol.Database
                ?? throw new InvalidOperationException("Een symboolreferentie heeft geen database-eigenaar."), tr);
            definitionIdentities[definitionId] = definitionIdentity;
        }
        var sample = new SymbolSample(blockName, definitionIdentity, SymbolTransform.From(symbol.BlockTransform * transform));
        if (!samples.TryGetValue(key, out var existing))
        {
            samples[key] = sample;
            return;
        }
        if (!StringComparer.Ordinal.Equals(existing.RenderIdentity, sample.RenderIdentity))
            throw new InvalidOperationException(
                $"Laag '{sourceLayer}' bevat meerdere symboolrenderingen; de legenda is niet eenduidig.");
        if (StringComparer.Ordinal.Compare(sample.BlockName, existing.BlockName) < 0)
            samples[key] = sample;
    }

    private static NlcsLayerName WithLocalName(NlcsLayerName src, string localName) => new()
    {
        Raw = src.Raw,
        LocalName = localName,
        IsXref = src.IsXref,
        XrefName = src.XrefName,
        SourceLayers = src.SourceLayers.Count > 0 ? src.SourceLayers : new[] { src.Source },
        StatusCode = src.StatusCode,
        Status = src.Status,
        Discipline = src.Discipline,
        Hoofdgroep = src.Hoofdgroep,
        Element = src.Element,
        TypeSuffix = src.TypeSuffix,
        DrawType = src.DrawType,
        Scale = src.Scale
    };

    // Voegt de per-bron AnySource-buckets samen. Buckets met gelijke render-identiteit (laagstijl
    // + arcering + symbool) worden één legendaregel met opgetelde hoeveelheid; verschillen de
    // bronnen zichtbaar, dan blijven het aparte regels, elk met een bronaanduiding in het element.
    private static void CoalesceAnySource(Database db, Transaction tr, Collector c)
    {
        if (c.AnyBuckets.Count == 0)
            return;

        var snap = new Dictionary<string, (CustomLayerRule Rule, NlcsLayerName Parsed, string Sig, LayerMetric Metric, HatchSample? Hatch, SymbolSample? Symbol)>(StringComparer.Ordinal);
        foreach (var bk in c.AnyBuckets.Keys)
        {
            var p = c.Parsed[bk];
            string style = ReadStyleSignature(db, tr, p.Raw);
            HatchSample? h = c.Hatches.TryGetValue(bk, out var hv) ? hv : null;
            SymbolSample? sym = c.SymbolSamples.TryGetValue(bk, out var sv) ? sv : null;
            c.Metrics.TryGetValue(bk, out var metric);
            snap[bk] = (c.AnyBuckets[bk].Rule, p, style + "|" + HatchSignature(h) + "|" + SymbolSignature(sym), metric, h, sym);
        }

        foreach (var bk in snap.Keys)
        {
            c.Parsed.Remove(bk);
            c.Metrics.Remove(bk);
            c.Hatches.Remove(bk);
            c.SymbolSamples.Remove(bk);
            c.CustomDesc.Remove(bk);
        }
        c.AnyBuckets.Clear();

        foreach (var logical in snap
            .OrderBy(kv => kv.Value.Parsed.GroupKey, StringComparer.Ordinal)
            .ThenBy(kv => kv.Value.Parsed.SourceIdentity, StringComparer.OrdinalIgnoreCase)
            .ThenBy(kv => kv.Value.Parsed.Raw, StringComparer.OrdinalIgnoreCase)
            .GroupBy(kv =>
                $"{kv.Value.Parsed.StatusCode}|{kv.Value.Parsed.Discipline}|{kv.Value.Parsed.Hoofdgroep}|{kv.Value.Parsed.Element}")
            .OrderBy(group => group.Key, StringComparer.Ordinal))
        {
            var rule = logical.OrderBy(kv => kv.Key, StringComparer.Ordinal).First().Value.Rule;
            var bySig = logical.GroupBy(kv => kv.Value.Sig)
                .OrderBy(group => group.Key, StringComparer.Ordinal)
                .ToList();
            bool divergent = bySig.Count > 1;

            foreach (var sigGroup in bySig)
            {
                var members = sigGroup
                    .OrderBy(m => m.Value.Parsed.SourceIdentity, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(m => m.Value.Parsed.Raw, StringComparer.OrdinalIgnoreCase)
                    .ToList();
                var rep = members[0].Value;
                var sourceIds = members
                    .Select(m => m.Value.Parsed.IsXref ? m.Value.Parsed.XrefName : string.Empty)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(s => s, StringComparer.Ordinal)
                    .ToList();
                string sourceLabel = string.Join(", ",
                    sourceIds.Select(s => s.Length == 0 ? "hoofdtekening" : s));

                // Sleutel = de echte database-laagnaam van de representatieve bron. Die bestaat en
                // is geldig, zodat de renderer de swatch op een bestaande laag met de juiste stijl
                // tekent (een synthetische sleutel met scheidingstekens zou een ongeldige laagnaam
                // opleveren). Bij divergentie verschilt de Raw per partitie, dus de sleutels botsen
                // niet; het element krijgt een bronaanduiding zodat de regels apart blijven.
                string finalKey = rep.Parsed.Raw;
                string element = divergent
                    ? rule.Element + " (" + sourceLabel + ")"
                    : rule.Element;

                var metric = LayerMetric.Empty;
                foreach (var m in members)
                    metric += m.Value.Metric;

                c.Parsed[finalKey] = new NlcsLayerName
                {
                    Raw = rep.Parsed.Raw,
                    LocalName = finalKey,
                    IsXref = rep.Parsed.IsXref,
                    XrefName = rep.Parsed.XrefName,
                    SourceLayers = members.SelectMany(m => m.Value.Parsed.SourceLayers.Count > 0
                        ? m.Value.Parsed.SourceLayers
                        : new[] { m.Value.Parsed.Source }).Distinct().ToArray(),
                    StatusCode = rep.Parsed.StatusCode,
                    Status = rep.Parsed.Status,
                    Discipline = rep.Parsed.Discipline,
                    Hoofdgroep = rep.Parsed.Hoofdgroep,
                    Element = element,
                    TypeSuffix = rep.Parsed.TypeSuffix,
                    DrawType = rep.Parsed.DrawType,
                    Scale = rep.Parsed.Scale
                };
                c.Metrics[finalKey] = metric;
                c.CustomDesc[finalKey] = rule.Description ?? string.Empty;
                if (rep.Hatch is not null)
                    c.Hatches[finalKey] = rep.Hatch;
                if (rep.Symbol is not null)
                    c.SymbolSamples[finalKey] = rep.Symbol;
            }
        }
    }

    // Voegt standaard-NLCS-lagen samen of splitst ze op zichtbare weergave. Per logische identiteit
    // worden de bronnen (hoofdtekening + elke xref) gepartitioneerd op hun gecombineerde render-
    // identiteit (laagstijl + arcering/vulling + symbool, per elementsoort). Bronnen die er hetzelfde
    // uitzien vormen één regel met opgetelde hoeveelheid; bronnen die zichtbaar verschillen blijven
    // aparte regels, elk met een eigen swatchlaag en hoeveelheid. Zo bepaalt niet de volgorde van de
    // traversal (first/last-wins) welke kleur of hoeveelheid een regel krijgt.
    private sealed record NlcsSnap(NlcsLayerName Parsed, string Sig, LayerMetric Metric, HatchSample? Hatch, SymbolSample? Symbol);

    private static void CoalesceNlcsSources(Database db, Transaction tr, Collector c)
    {
        if (c.NlcsRawKeys.Count == 0)
            return;

        var snap = new List<NlcsSnap>();
        foreach (var key in c.NlcsRawKeys)
        {
            if (!c.Parsed.TryGetValue(key, out var p))
                continue;
            string style = ReadStyleSignature(db, tr, p.Raw);
            if (style.Length == 0)
                style = "missing:" + p.Raw;
            HatchSample? h = c.Hatches.TryGetValue(key, out var hv) ? hv : null;
            SymbolSample? sym = c.SymbolSamples.TryGetValue(key, out var sv) ? sv : null;
            c.Metrics.TryGetValue(key, out var metric);
            snap.Add(new NlcsSnap(p, style + "|" + HatchSignature(h) + "|" + SymbolSignature(sym), metric, h, sym));
            c.Parsed.Remove(key);
            c.Metrics.Remove(key);
            c.Hatches.Remove(key);
            c.SymbolSamples.Remove(key);
        }
        c.NlcsRawKeys.Clear();

        foreach (var logical in snap.GroupBy(s => s.Parsed.GroupKey))
        {
            // Per bron (hoofdtekening = "", anders de xref-naam) de gecombineerde render-identiteit
            // over alle elementsoorten; zo tellen bijvoorbeeld een lijn + arcering uit dezelfde bron
            // als één uiterlijk mee en splitsen we niet midden in een element.
            var sources = logical
                .GroupBy(s => s.Parsed.IsXref ? s.Parsed.XrefName : string.Empty, StringComparer.OrdinalIgnoreCase)
                .Select(g =>
                {
                    var layers = g.OrderBy(x => x.Parsed.DrawType)
                        .ThenBy(x => x.Parsed.Scale ?? 0)
                        .ThenBy(x => x.Parsed.Raw, StringComparer.OrdinalIgnoreCase)
                        .ToList();
                    var renderLayers = layers
                        .GroupBy(x => x.Parsed.DrawType)
                        .OrderBy(type => type.Key)
                        .Select(type =>
                        {
                            var styles = type.Select(x => x.Sig)
                                .Distinct(StringComparer.Ordinal)
                                .OrderBy(signature => signature, StringComparer.Ordinal)
                                .ToList();
                            if (styles.Count != 1)
                            {
                                var conflictingLayers = string.Join(", ", type
                                    .Select(x => x.Parsed.Raw)
                                    .Distinct(StringComparer.OrdinalIgnoreCase)
                                    .OrderBy(name => name, StringComparer.OrdinalIgnoreCase));
                                string source = g.Key.Length == 0 ? "hoofdtekening" : g.Key;
                                throw new InvalidOperationException(
                                    $"Bron '{source}' bevat meerdere zichtbare stijlen voor dezelfde NLCS-laag en elementsoort: {conflictingLayers}.");
                            }
                            return $"{(int)type.Key}:{styles[0]}";
                        });
                    return (SourceId: g.Key, Layers: layers, CombinedSig: string.Join(";", renderLayers));
                })
                .OrderBy(source => source.SourceId.Length == 0 ? 0 : 1)
                .ThenBy(source => source.SourceId, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var bySig = sources
                .GroupBy(s => s.CombinedSig)
                .OrderByDescending(g => g.Any(s => s.Layers.Any(l => !l.Parsed.IsXref))) // hoofdtekening eerst
                .ThenByDescending(g => g.Count())                                        // dan de meeste bronnen
                .ThenBy(g => g.Key, StringComparer.Ordinal)                              // dan deterministisch
                .ToList();

            int partition = 0;
            foreach (var sigGroup in bySig)
            {
                var allLayers = sigGroup.SelectMany(s => s.Layers)
                    .OrderBy(layer => layer.Parsed.IsXref ? 1 : 0)
                    .ThenBy(layer => layer.Parsed.XrefName, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(layer => layer.Parsed.Scale ?? 0)
                    .ThenBy(layer => layer.Parsed.Raw, StringComparer.OrdinalIgnoreCase)
                    .ToList();
                foreach (var dt in allLayers.GroupBy(l => l.Parsed.DrawType))
                {
                    var rep = dt.OrderBy(l => l.Parsed.Scale ?? 0)
                        .ThenBy(l => l.Parsed.Raw, StringComparer.OrdinalIgnoreCase)
                        .First();
                    string clean = rep.Parsed.LocalName;
                    // Niet-divergent: de echte NLCS-laagnaam blijft de swatchlaag (bestaand gedrag).
                    // Divergent: elke volgende partitie krijgt een eigen, geldige swatchlaagnaam
                    // zodat de renderer de afwijkende kleur op een aparte laag kan tekenen.
                    string swatchLayer = partition == 0 ? clean : $"{clean} #{partition + 1}";

                    var metric = LayerMetric.Empty;
                    foreach (var m in dt) metric += m.Metric;

                    c.Parsed[swatchLayer] = new NlcsLayerName
                    {
                        Raw = rep.Parsed.Raw,
                        LocalName = clean,
                        IsXref = rep.Parsed.IsXref,
                        XrefName = rep.Parsed.XrefName,
                        SourceLayers = dt.SelectMany(l => l.Parsed.SourceLayers.Count > 0
                            ? l.Parsed.SourceLayers
                            : new[] { l.Parsed.Source }).Distinct().ToArray(),
                        StatusCode = rep.Parsed.StatusCode,
                        Status = rep.Parsed.Status,
                        Discipline = rep.Parsed.Discipline,
                        Hoofdgroep = rep.Parsed.Hoofdgroep,
                        Element = rep.Parsed.Element,
                        TypeSuffix = rep.Parsed.TypeSuffix,
                        DrawType = rep.Parsed.DrawType,
                        Scale = rep.Parsed.Scale,
                        RenderLayerName = swatchLayer,
                        RenderIdentity = sigGroup.Key
                    };
                    c.Metrics[swatchLayer] = metric;
                    if (rep.Hatch is not null)
                        c.Hatches[swatchLayer] = rep.Hatch;
                    if (rep.Symbol is not null)
                        c.SymbolSamples[swatchLayer] = rep.Symbol;
                }
                partition++;
            }
        }
    }

    private static string HatchSignature(HatchSample? hatch)
    {
        if (hatch is null)
            return string.Empty;
        return string.Join(":", "H",
            hatch.PatternType,
            hatch.PatternName.Trim().ToUpperInvariant(),
            NumberSignature(hatch.PatternScale),
            NumberSignature(hatch.PatternAngle),
            hatch.IsSolid,
            hatch.Color?.ToString() ?? string.Empty,
            hatch.Transparency.Signature);
    }

    private static string SymbolSignature(SymbolSample? sample) =>
        sample?.RenderIdentity ?? string.Empty;

    private static ObjectId SymbolDefinitionId(BlockReference reference) =>
        reference.IsDynamicBlock ? reference.DynamicBlockTableRecord : reference.BlockTableRecord;

    private static string SymbolDefinitionIdentity(ObjectId definitionId, Database host, Transaction tr)
    {
        string content = BlockContentSignature(definitionId, host, tr, new HashSet<ObjectId>(), 0);
        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(content)));
    }

    private static string BlockContentSignature(
        ObjectId definitionId, Database host, Transaction tr, HashSet<ObjectId> path, int depth)
    {
        if (depth > MaxDepth || !path.Add(definitionId))
            return "cycle";

        try
        {
            var definition = (BlockTableRecord)tr.GetObject(definitionId, OpenMode.ForRead);
            var entities = new List<string>();
            foreach (ObjectId id in definition)
                if (tr.GetObject(id, OpenMode.ForRead) is Entity entity)
                    entities.Add(SymbolEntitySignature(entity, host, tr, path, depth));
            entities.Sort(StringComparer.Ordinal);
            return string.Join("\n", entities);
        }
        finally
        {
            path.Remove(definitionId);
        }
    }

    private static string SymbolEntitySignature(
        Entity entity, Database host, Transaction tr, HashSet<ObjectId> path, int depth)
    {
        var parts = new List<string>
        {
            entity.GetType().FullName ?? entity.GetType().Name,
            $"{entity.Color.ColorMethod}:{entity.Color.ColorIndex}:{entity.Color.ColorValue.ToArgb()}",
            entity.Linetype,
            entity.LineWeight.ToString(),
            TransparencyValue.From(entity.Transparency).Signature
        };

        switch (entity)
        {
            case Line line:
                parts.Add($"L:{PointSignature(line.StartPoint)}:{PointSignature(line.EndPoint)}");
                break;
            case Circle circle:
                parts.Add($"C:{PointSignature(circle.Center)}:{NumberSignature(circle.Radius)}");
                break;
            case Arc arc:
                parts.Add($"A:{PointSignature(arc.Center)}:{NumberSignature(arc.Radius)}:"
                    + $"{NumberSignature(arc.StartAngle)}:{NumberSignature(arc.EndAngle)}");
                break;
            case Polyline polyline:
                parts.Add($"P:{polyline.Closed}:{NumberSignature(polyline.Elevation)}");
                for (int i = 0; i < polyline.NumberOfVertices; i++)
                    parts.Add($"{PointSignature(polyline.GetPoint2dAt(i))}:"
                        + $"{NumberSignature(polyline.GetBulgeAt(i))}:"
                        + $"{NumberSignature(polyline.GetStartWidthAt(i))}:"
                        + $"{NumberSignature(polyline.GetEndWidthAt(i))}");
                break;
            case DBText text:
                parts.Add($"T:{PointSignature(text.Position)}:{PointSignature(text.AlignmentPoint)}:"
                    + $"{text.TextString}:{NumberSignature(text.Height)}:{NumberSignature(text.Rotation)}:"
                    + $"{NumberSignature(text.WidthFactor)}:{NumberSignature(text.Oblique)}:"
                    + TextStyleSignature(text.TextStyleId, host, tr));
                break;
            case MText text:
                parts.Add($"MT:{PointSignature(text.Location)}:{text.Contents}:{NumberSignature(text.TextHeight)}:"
                    + $"{NumberSignature(text.Width)}:{NumberSignature(text.Rotation)}:"
                    + TextStyleSignature(text.TextStyleId, host, tr));
                break;
            case Hatch hatch:
                parts.Add($"H:{hatch.PatternType}:{hatch.PatternName}:{NumberSignature(hatch.PatternScale)}:"
                    + $"{NumberSignature(hatch.PatternAngle)}:{hatch.NumberOfLoops}");
                break;
            case BlockReference nested:
                parts.Add($"B:{PointSignature(nested.Position)}:"
                    + $"{SymbolTransform.From(nested.BlockTransform).Signature}:"
                    + BlockContentSignature(nested.BlockTableRecord, host, tr, path, depth + 1));
                break;
            default:
                if (entity is Curve curve)
                    parts.Add($"Q:{NumberSignature(SafeLength(curve))}:"
                        + $"{NumberSignature(curve.Closed ? SafeArea(() => curve.Area) : 0.0)}");
                if (TryGetExtents(entity, out var min, out var max))
                    parts.Add($"E:{PointSignature(min)}:{PointSignature(max)}");
                break;
        }
        return string.Join("|", parts);
    }

    private static string TextStyleSignature(ObjectId styleId, Database host, Transaction hostTransaction)
    {
        if (styleId.IsNull || styleId.Database is not { } source)
            return string.Empty;
        if (source == host)
            return hostTransaction.GetObject(styleId, OpenMode.ForRead) is TextStyleTableRecord style
                ? LocalName(style.Name)
                : string.Empty;
        using var sourceTransaction = source.TransactionManager.StartTransaction();
        var sourceStyle = sourceTransaction.GetObject(styleId, OpenMode.ForRead) as TextStyleTableRecord
            ?? throw new InvalidOperationException("Een symbooldefinitie bevat een ongeldige tekststijl.");
        string name = LocalName(sourceStyle.Name);
        sourceTransaction.Commit();
        return name;
    }

    private static string LocalName(string name)
    {
        int separator = name.LastIndexOf('|');
        return (separator < 0 ? name : name[(separator + 1)..]).Trim();
    }

    private static bool TryGetExtents(Entity entity, out Point3d min, out Point3d max)
    {
        try
        {
            var extents = entity.GeometricExtents;
            min = extents.MinPoint;
            max = extents.MaxPoint;
            return true;
        }
        catch
        {
            min = Point3d.Origin;
            max = Point3d.Origin;
            return false;
        }
    }

    private static string PointSignature(Point3d point) =>
        $"{NumberSignature(point.X)},{NumberSignature(point.Y)},{NumberSignature(point.Z)}";

    private static string PointSignature(Point2d point) =>
        $"{NumberSignature(point.X)},{NumberSignature(point.Y)}";

    private static string NumberSignature(double number) =>
        Math.Round(number, 9, MidpointRounding.AwayFromZero)
            .ToString("0.#########", System.Globalization.CultureInfo.InvariantCulture);

    private static string ReadStyleSignature(Database db, Transaction tr, string rawLayer)
    {
        var lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
        if (!lt.Has(rawLayer) || tr.GetObject(lt[rawLayer], OpenMode.ForRead) is not LayerTableRecord ltr)
            return string.Empty;
        string linetype = "Continuous";
        try
        {
            if (tr.GetObject(ltr.LinetypeObjectId, OpenMode.ForRead) is LinetypeTableRecord lype)
                linetype = lype.Name;
        }
        catch { /* standaard */ }
        var transp = TransparencyValue.From(ltr.Transparency);
        return $"{ltr.Color}|{linetype}|{ltr.LineWeight}|{transp.Signature}";
    }

    // Zoals AddMetric, maar met een expliciete hoeveelheidsmodus. Auto gedraagt zich als NLCS;
    // de andere modi tellen alleen de gekozen component zodat de legenda de juiste eenheid toont.
    private static void AddMetricMasked(Entity ent, string key, Collector c, Matrix3d transform, CustomQuantityMode mode)
    {
        if (mode == CustomQuantityMode.Geen)
        {
            // Wel als element registreren (telt mee voor rendering), maar zonder hoeveelheid.
            if (!c.Metrics.ContainsKey(key)) c.Metrics[key] = LayerMetric.Empty;
            return;
        }
        var before = c.Metrics.TryGetValue(key, out var m) ? m : LayerMetric.Empty;
        AddMetric(ent, key, c, transform);
        if (mode == CustomQuantityMode.Auto) return;

        // De toegevoegde component maskeren naar de gekozen modus.
        var after = c.Metrics[key];
        var delta = after.Add(-before.Count, -before.Length, -before.Area);
        var masked = mode switch
        {
            CustomQuantityMode.Aantal => before.Add(delta.Count, 0, 0),
            CustomQuantityMode.Lengte => before.Add(0, delta.Length, 0),
            CustomQuantityMode.Oppervlak => before.Add(0, 0, delta.Area),
            _ => after
        };
        c.Metrics[key] = masked;
    }

    private static (double length, double area) MeasureTransformed(Curve curve, Matrix3d transform)
    {
        try
        {
            using var copy = (Curve)curve.GetTransformedCopy(transform);
            double length = SafeLength(copy);
            double area = copy.Closed ? SafeArea(() => copy.Area) : 0;
            return (length, area);
        }
        catch
        {
            // Kan de kopie niet worden gemaakt/gemeten, val terug op de ongetransformeerde
            // meting met een uniforme schaalbenadering in plaats van een stil fout getal.
            double s = UniformScale(transform);
            double length = SafeLength(curve) * s;
            double area = curve.Closed ? SafeArea(() => curve.Area) * s * s : 0;
            return (length, area);
        }
    }

    // |det| van het 3x3 lineaire deel: de factor waarmee oppervlak schaalt.
    private static double AreaScale(Matrix3d m)
    {
        var cs = m.CoordinateSystem3d;
        var det = cs.Xaxis.DotProduct(cs.Yaxis.CrossProduct(cs.Zaxis));
        return Math.Abs(det) < 1e-12 ? 1.0 : Math.Abs(det);
    }

    private static double UniformScale(Matrix3d m) => Math.Cbrt(AreaScale(m));

    private static double SafeLength(Curve curve)
    {
        try
        {
            return curve.GetDistanceAtParameter(curve.EndParam) -
                   curve.GetDistanceAtParameter(curve.StartParam);
        }
        catch
        {
            return 0.0;
        }
    }

    private static double SafeArea(Func<double> area)
    {
        try { return Math.Abs(area()); }
        catch { return 0.0; }
    }

    internal static string BlockName(BlockReference br, Transaction tr)
    {
        try
        {
            var id = br.IsDynamicBlock ? br.DynamicBlockTableRecord : br.BlockTableRecord;
            return tr.GetObject(id, OpenMode.ForRead) is BlockTableRecord btr ? btr.Name : string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static bool IsVisible(string layerName, Transaction tr, Collector c)
    {
        if (c.Visible.TryGetValue(layerName, out var cached))
            return cached;

        var visible = true;
        if (c.Layers is not null && c.Layers.Has(layerName)
            && tr.GetObject(c.Layers[layerName], OpenMode.ForRead) is LayerTableRecord ltr)
        {
            visible = !ltr.IsOff && !ltr.IsFrozen;
        }
        c.Visible[layerName] = visible;
        return visible;
    }

    // Effectieve laag-look per render-partitie. Een xref-laag staat in de host als "xref|laag";
    // de gekozen partitie levert de bronstijl, ook als een andere bron dezelfde NLCS-naam heeft.
    private static Dictionary<string, LayerDisplayStyle> ReadLayerStyles(
        Database db, Transaction tr, IEnumerable<NlcsLayerName> layers)
    {
        // Na het coalescen is elke RenderLayerName een unieke swatchlaag en levert Raw de bron waaruit
        // de effectieve stijl wordt gelezen. Geen host-wins meer: de render-partitie heeft de
        // representatieve bron al gekozen.
        var styles = new Dictionary<string, LayerDisplayStyle>(StringComparer.OrdinalIgnoreCase);
        var lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
        foreach (var layer in layers)
        {
            if (styles.ContainsKey(layer.EffectiveRenderLayerName) || !lt.Has(layer.Raw))
                continue;
            if (tr.GetObject(lt[layer.Raw], OpenMode.ForRead) is not LayerTableRecord ltr)
                continue;
            string linetype = "Continuous";
            try
            {
                if (tr.GetObject(ltr.LinetypeObjectId, OpenMode.ForRead) is LinetypeTableRecord lte)
                    linetype = lte.Name;
            }
            catch { /* standaard */ }
            styles[layer.EffectiveRenderLayerName] = new LayerDisplayStyle
            {
                Color = ltr.Color,
                Linetype = linetype,
                LineWeight = ltr.LineWeight,
                Transparency = TransparencyValue.From(ltr.Transparency)
            };
        }
        return styles;
    }

    private static Dictionary<string, string> ReadRenderIds(
        Database db, Transaction tr, IEnumerable<NlcsLayerName> layers)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
        foreach (var layer in layers)
        {
            if (result.ContainsKey(layer.EffectiveRenderLayerName) || !lt.Has(layer.Raw))
                continue;
            if (tr.GetObject(lt[layer.Raw], OpenMode.ForRead) is not LayerTableRecord ltr)
                continue;
            string lt4 = "Continuous";
            try
            {
                if (tr.GetObject(ltr.LinetypeObjectId, OpenMode.ForRead) is LinetypeTableRecord ltype)
                    lt4 = ltype.Name;
            }
            catch { /* standaard */ }
            var transp = TransparencyValue.From(ltr.Transparency);
            result[layer.EffectiveRenderLayerName] = $"{ltr.Color}|{lt4}|{ltr.LineWeight}|{transp.Signature}";
        }
        return result;
    }

    private static Dictionary<string, string> ReadLayerDescriptions(
        Database db, Transaction tr, IEnumerable<NlcsLayerName> layers)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
        foreach (var layer in layers)
        {
            if (result.ContainsKey(layer.LocalName) || !lt.Has(layer.Raw))
                continue;
            if (tr.GetObject(lt[layer.Raw], OpenMode.ForRead) is LayerTableRecord ltr
                && !string.IsNullOrWhiteSpace(ltr.Description))
            {
                result[layer.LocalName] = ltr.Description.Trim();
            }
        }
        return result;
    }
}
