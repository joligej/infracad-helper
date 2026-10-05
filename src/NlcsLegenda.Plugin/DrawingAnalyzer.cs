using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using NlcsLegenda.Core;

namespace NlcsLegenda.Plugin;

public sealed class AnalysisResult
{
    public IReadOnlyList<LegendEntry> Entries { get; init; } = Array.Empty<LegendEntry>();

    public IReadOnlyDictionary<string, HatchSample> HatchSamples { get; init; } =
        new Dictionary<string, HatchSample>();

    public int UsedNlcsLayerCount { get; init; }

    public int DescribedLayerCount { get; init; }

    public int AttrDescribedLayerCount { get; init; }

    public int ExcludedNlcsLayerCount { get; init; }
}

public static class DrawingAnalyzer
{
    private sealed class Collector
    {
        public readonly Dictionary<string, NlcsLayerName> Parsed = new(StringComparer.OrdinalIgnoreCase);
        public readonly HashSet<string> NonNlcs = new(StringComparer.OrdinalIgnoreCase);
        public readonly Dictionary<string, HatchSample> Hatches = new(StringComparer.OrdinalIgnoreCase);
        public readonly Dictionary<string, LayerMetric> Metrics = new(StringComparer.OrdinalIgnoreCase);
        public readonly Dictionary<string, string> SymbolBlocks = new(StringComparer.OrdinalIgnoreCase);
        // Per laag de getelde OMSCHRIJVING-attribuutwaarden van KLIC-symbolen; de meest
        // voorkomende niet-lege waarde is de echte omschrijving uit de tekening.
        public readonly Dictionary<string, Dictionary<string, int>> AttrOms =
            new(StringComparer.OrdinalIgnoreCase);
        public readonly HashSet<string> ExcludedNlcs = new(StringComparer.OrdinalIgnoreCase);
        public readonly Dictionary<string, bool> Visible = new(StringComparer.OrdinalIgnoreCase);
        // Omschrijving per eigen-laag-sleutel (komt uit de regel, niet uit de NLCS-catalogus).
        public readonly Dictionary<string, string> CustomDesc = new(StringComparer.OrdinalIgnoreCase);
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

        var descriptions = ReadLayerDescriptions(db, tr, c.Parsed.Values);
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

        var renderIds = settings.MergeIdenticalStatuses
            ? ReadRenderIds(db, tr, c.Parsed.Values)
            : null;

        // Render-identiteit per laag = de effectieve legendaweergave: laag-look (kleur/linetype/
        // lineweight/transparantie) plus de gesamplede arcering. De symboolidentiteit is de
        // bloknaam (toegevoegd in LegendGrouping): de renderer tekent elk symboolblok op
        // legendaschaal, onafhankelijk van de bron-transform, dus die hoort hier niet bij.
        string? RenderId(string name)
        {
            if (renderIds is null)
                return null;
            renderIds.TryGetValue(name, out var baseId);
            var hatch = c.Hatches.TryGetValue(name, out var h)
                ? $"|H:{h.PatternName}:{h.PatternScale:0.###}:{h.PatternAngle:0.###}:{h.IsSolid}"
                : string.Empty;
            return (baseId ?? string.Empty) + hatch;
        }

        // DescriptionOverrides worden in LegendGrouping.Build toegepast (één plek), dus hier
        // alleen de basiscatalogus doorgeven.
        var entries = LegendGrouping.Build(
            c.Parsed.Values, settings,
            name => c.CustomDesc.TryGetValue(name, out var cd) ? cd
                  : attrDesc.TryGetValue(name, out var a) ? a
                  : descriptions.TryGetValue(name, out var d) ? d : null,
            c.Metrics,
            name => c.SymbolBlocks.TryGetValue(name, out var b) ? b : null,
            catalog,
            renderIds is null ? null : RenderId);

        return new AnalysisResult
        {
            Entries = entries,
            HatchSamples = c.Hatches,
            UsedNlcsLayerCount = c.Parsed.Count,
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

        // Expliciete eigen-laagkoppeling wint van NLCS-herkenning: precies één interpretatie.
        if (settings.CustomLayerRules.Count > 0
            && TryMatchCustomRule(ent, tr, settings, layerName, sourceXref, out var rule, out var canonical))
        {
            RecordCustom(ent, tr, c, settings, transform, rule!, canonical!);
            return;
        }

        if (c.NonNlcs.Contains(layerName))
            return;

        if (!settings.IncludeInvisibleLayers && !IsVisible(layerName, tr, c))
        {
            c.NonNlcs.Add(layerName);
            return;
        }

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

        AddMetric(ent, nlcs.LocalName, c, transform);

        if (ent is Hatch hatch && !c.Hatches.ContainsKey(layerName))
            c.Hatches[layerName] = HatchSample.From(hatch);

        if (ent is BlockReference symbolRef && nlcs.DrawType == NlcsDrawType.Symbool)
        {
            if (!c.SymbolBlocks.ContainsKey(nlcs.LocalName))
            {
                var name = BlockName(symbolRef, tr);
                if (!string.IsNullOrEmpty(name) && !name.StartsWith('*'))
                    c.SymbolBlocks[nlcs.LocalName] = name;
            }
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
    private static bool TryMatchCustomRule(
        Entity ent, Transaction tr, LegendSettings settings, string layerName, string sourceXref,
        out CustomLayerRule? rule, out NlcsLayerName? canonical)
    {
        foreach (var r in settings.CustomLayerRules)
        {
            if (!r.IsValid || !r.ScopeMatches(sourceXref)) continue;
            if (!string.Equals(layerName, r.Layer, StringComparison.OrdinalIgnoreCase)) continue;
            if (r.BlockName is { Length: > 0 })
            {
                if (ent is not BlockReference br
                    || !string.Equals(BlockName(br, tr), r.BlockName, StringComparison.OrdinalIgnoreCase))
                    continue;
            }
            rule = r;
            canonical = r.ToCanonical(sourceXref);
            return true;
        }
        rule = null;
        canonical = null;
        return false;
    }

    private static void RecordCustom(
        Entity ent, Transaction tr, Collector c, LegendSettings settings, Matrix3d transform,
        CustomLayerRule rule, NlcsLayerName canonical)
    {
        var key = canonical.LocalName;
        if (!c.Parsed.ContainsKey(key))
        {
            c.Parsed[key] = canonical;
            c.CustomDesc[key] = rule.Description ?? string.Empty;
        }

        AddMetricMasked(ent, key, c, transform, rule.QuantityMode);

        if (ent is Hatch hatch && !c.Hatches.ContainsKey(key))
            c.Hatches[key] = HatchSample.From(hatch);

        if (ent is BlockReference symbolRef && rule.Type == NlcsDrawType.Symbool
            && !c.SymbolBlocks.ContainsKey(key))
        {
            var name = BlockName(symbolRef, tr);
            if (!string.IsNullOrEmpty(name) && !name.StartsWith('*'))
                c.SymbolBlocks[key] = name;
        }
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

    private static string BlockName(BlockReference br, Transaction tr)
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

    private static Dictionary<string, string> ReadRenderIds(
        Database db, Transaction tr, IEnumerable<NlcsLayerName> layers)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
        foreach (var layer in layers)
        {
            if (result.ContainsKey(layer.LocalName) || !lt.Has(layer.Raw))
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
            var transp = ltr.Transparency.IsByAlpha ? ltr.Transparency.Alpha.ToString() : "L";
            result[layer.LocalName] = $"{ltr.Color}|{lt4}|{ltr.LineWeight}|{transp}";
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
