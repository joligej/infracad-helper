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

    public string LinetypeIdentity { get; init; } = string.Empty;

    public double LinetypeScale { get; init; } = 1.0;

    public LineWeight LineWeight { get; init; } = LineWeight.ByLineWeightDefault;

    public TransparencyValue Transparency { get; init; } =
        new(Autodesk.AutoCAD.Colors.TransparencyMethod.ByAlpha, 255);
}

public static class DrawingAnalyzer
{
    private sealed class Collector
    {
        public readonly Dictionary<string, LayerDisplayStyle> Styles = new(StringComparer.OrdinalIgnoreCase);
        public readonly Dictionary<string, NlcsLayerName> Parsed = new(StringComparer.OrdinalIgnoreCase);
        public readonly Dictionary<string, HatchSample> Hatches = new(StringComparer.OrdinalIgnoreCase);
        public readonly Dictionary<string, LayerMetric> Metrics = new(StringComparer.OrdinalIgnoreCase);
        public readonly Dictionary<string, SymbolSample> SymbolSamples = new(StringComparer.OrdinalIgnoreCase);
        public readonly Dictionary<(ObjectId, string), string> SymbolDefinitionIdentities = new();
        // Per laag de getelde OMSCHRIJVING-attribuutwaarden van KLIC-symbolen; de meest
        // voorkomende niet-lege waarde is de echte omschrijving uit de tekening.
        public readonly Dictionary<string, Dictionary<string, int>> AttrOms =
            new(StringComparer.OrdinalIgnoreCase);
        public readonly HashSet<string> ExcludedNlcs = new(StringComparer.OrdinalIgnoreCase);
        public readonly Dictionary<string, bool> Visible = new(StringComparer.OrdinalIgnoreCase);
        // Omschrijving per eigen-laag-sleutel (komt uit de regel, niet uit de NLCS-catalogus).
        public readonly Dictionary<string, string> CustomDesc = new(StringComparer.OrdinalIgnoreCase);
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

        // Dezelfde logische NLCS-laag uit meerdere bronnen: gelijke weergave valt samen (één regel,
        // opgetelde hoeveelheid), zichtbaar verschillende weergave splitst (aparte regels met eigen
        // hoeveelheid en swatchkleur). Geen first/last-wins.
        CoalesceNlcsSources(c);

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

        var renderIds = c.Styles.ToDictionary(kv => kv.Key, kv => StyleSignature(kv.Value), StringComparer.OrdinalIgnoreCase);

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
            LayerStyles = c.Styles,
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
        HashSet<ObjectId> path, int depth, Matrix3d transform, bool insideIncludedXref, string sourceXref, LayerDisplayStyle? inheritedStyle = null)
    {
        if (depth > MaxDepth) throw new InvalidOperationException("Te diep geneste blokken; analyse afgebroken om onvolledige hoeveelheden te voorkomen.");
        if (!path.Add(btrId)) return;

        var btr = (BlockTableRecord)tr.GetObject(btrId, OpenMode.ForRead);
        foreach (ObjectId id in btr)
            if (tr.GetObject(id, OpenMode.ForRead) is Entity ent)
                Process(ent, tr, c, settings, path, depth, transform, insideIncludedXref, sourceXref, inheritedStyle);

        path.Remove(btrId);
    }

    private static void Process(
        Entity ent, Transaction tr, Collector c, LegendSettings settings,
        HashSet<ObjectId> path, int depth, Matrix3d transform, bool insideIncludedXref, string sourceXref, LayerDisplayStyle? inheritedStyle = null)
    {
        // Eigen legenda-geometrie telt nooit als bron.
        if (c.Excluded.Contains(ent.ObjectId))
            return;

        if (!settings.IncludeInvisibleLayers && (!ent.Visible || !IsVisible(ent.Layer, tr, c)))
            return;
        var effectiveStyle = EntityStyle(ent, tr, inheritedStyle);
        Record(ent, tr, c, settings, transform, sourceXref, effectiveStyle);

        if (ent is BlockReference br && !br.BlockTableRecord.IsNull)
        {
            var def = tr.GetObject(br.BlockTableRecord, OpenMode.ForRead) as BlockTableRecord;
            if (def is { IsLayout: false })
            {
                var nested = transform * br.BlockTransform;
                if (def.IsFromExternalReference)
                {
                    bool included = insideIncludedXref || settings.IsXrefIncluded(def.Name);
                    if (included)
                        CollectFromBlock(br.BlockTableRecord, tr, c, settings, path, depth + 1, nested, insideIncludedXref: true,
                            sourceXref: string.IsNullOrEmpty(sourceXref) ? def.Name : sourceXref + "|" + LayerNaming.LocalName(def.Name), inheritedStyle: effectiveStyle);
                }
                else
                {
                    CollectFromBlock(br.BlockTableRecord, tr, c, settings, path, depth + 1, nested, insideIncludedXref, sourceXref, effectiveStyle);
                }
            }
        }
    }

    private static void Record(Entity ent, Transaction tr, Collector c, LegendSettings settings,
        Matrix3d transform, string sourceXref, LayerDisplayStyle style)
    {
        var layerName = ent.Layer;
        CustomLayerRule? rule = null;
        NlcsLayerName? parsed;
        if (TryMatchCustomRule(ent, tr, settings, layerName, sourceXref, out rule, out parsed))
        {
            if (!settings.IsIncluded(parsed!)) return;
        }
        else
        {
            if (!NlcsLayerParser.TryParse(layerName, out parsed)) return;
            parsed = CopyLayer(parsed, sourceXref);
            if (!settings.IsIncluded(parsed))
            {
                c.ExcludedNlcs.Add(parsed.LocalName);
                return;
            }
        }
        if (!settings.IsDrawTypeIncluded(parsed!.DrawType) || !EntityMatchesType(ent, parsed.DrawType)) return;

        HatchSample? hatch = ent is Hatch h ? HatchSample.From(h) : null;
        if (hatch is not null)
        {
            hatch.Color = style.Color;
            hatch.Transparency = style.Transparency;
        }
        SymbolSample? symbol = null;
        if (ent is BlockReference br && parsed.DrawType == NlcsDrawType.Symbool)
        {
            if (!HasSymbolGeometry(br.BlockTableRecord, tr, settings.IncludeInvisibleLayers, new HashSet<ObjectId>())) return;
            string blockName = ((BlockTableRecord)tr.GetObject(br.BlockTableRecord, OpenMode.ForRead)).Name;
            if (string.IsNullOrWhiteSpace(blockName)) return;
            var id = br.BlockTableRecord;
            var cacheKey = (id, StyleSignature(style));
            if (!c.SymbolDefinitionIdentities.TryGetValue(cacheKey, out var identity))
            {
                identity = SymbolDefinitionIdentity(id, ent.Database, tr, style);
                c.SymbolDefinitionIdentities[cacheKey] = identity;
            }
            symbol = new SymbolSample(blockName, identity, SymbolTransform.From(transform * br.BlockTransform));
        }
        string appearance = StyleSignature(style) + "|" + HatchSignature(hatch) + "|" + SymbolSignature(symbol);
        string key = parsed.GroupKey + "\u0001" + sourceXref + "\u0001" + layerName + "\u0001" + appearance;
        c.Parsed[key] = parsed;
        c.NlcsRawKeys.Add(key);
        c.Styles[key] = style;
        if (rule is not null)
        {
            c.CustomDesc[key] = rule.Description;
            AddMetricMasked(ent, key, c, transform, rule.QuantityMode);
        }
        else AddMetric(ent, key, c, transform);
        if (hatch is not null) c.Hatches[key] = hatch;
        if (symbol is not null) c.SymbolSamples[key] = symbol;
        if (ent is BlockReference attributed && parsed.DrawType == NlcsDrawType.Symbool)
        {
            var description = ReadAttribute(attributed, tr, "OMSCHRIJVING");
            if (!string.IsNullOrWhiteSpace(description))
            {
                if (!c.AttrOms.TryGetValue(key, out var counts)) c.AttrOms[key] = counts = new(StringComparer.Ordinal);
                counts.TryGetValue(description, out int count);
                counts[description] = count + 1;
            }
        }
    }

    private static bool HasSymbolGeometry(ObjectId id, Transaction tr, bool includeInvisible, HashSet<ObjectId> path)
    {
        if (!path.Add(id)) return false;
        try
        {
            foreach (ObjectId child in (BlockTableRecord)tr.GetObject(id, OpenMode.ForRead))
                if (tr.GetObject(child, OpenMode.ForRead) is Entity entity)
                {
                    var layer = (LayerTableRecord)tr.GetObject(entity.LayerId, OpenMode.ForRead);
                    if (!includeInvisible && (!entity.Visible || layer.IsOff || layer.IsFrozen)) continue;
                    if (entity is not BlockReference nested || HasSymbolGeometry(nested.BlockTableRecord, tr, includeInvisible, path)) return true;
                }
            return false;
        }
        finally { path.Remove(id); }
    }

    private static NlcsLayerName CopyLayer(NlcsLayerName p, string source) => new()
    {
        Raw = p.Raw, LocalName = p.LocalName, IsXref = source.Length > 0,
        XrefName = source, StatusCode = p.StatusCode, Status = p.Status,
        Discipline = p.Discipline, Hoofdgroep = p.Hoofdgroep, Element = p.Element,
        TypeSuffix = p.TypeSuffix, DrawType = p.DrawType, Scale = p.Scale
    };

    private static LayerDisplayStyle EntityStyle(Entity ent, Transaction tr, LayerDisplayStyle? parent)
    {
        var layer = (LayerTableRecord)tr.GetObject(ent.LayerId, OpenMode.ForRead);
        bool inheritLayer = LayerNaming.LocalName(layer.Name) == "0" && parent is not null;
        var color = ent.Color.IsByLayer ? (inheritLayer ? parent!.Color : layer.Color)
            : ent.Color.IsByBlock ? parent?.Color ?? layer.Color : ent.Color;
        if (color.IsByLayer || color.IsByBlock)
            color = Autodesk.AutoCAD.Colors.Color.FromColorIndex(Autodesk.AutoCAD.Colors.ColorMethod.ByAci, 7);
        var ltId = ent.LinetypeId;
        string ltName = ent.Linetype;
        if (LayerNaming.LocalName(ltName).Equals("ByLayer", StringComparison.OrdinalIgnoreCase))
        {
            ltId = layer.LinetypeObjectId;
            ltName = inheritLayer ? parent!.Linetype : ((LinetypeTableRecord)tr.GetObject(ltId, OpenMode.ForRead)).Name;
        }
        bool inheritLine = LayerNaming.LocalName(ent.Linetype).Equals("ByBlock", StringComparison.OrdinalIgnoreCase)
            || (inheritLayer && LayerNaming.LocalName(ent.Linetype).Equals("ByLayer", StringComparison.OrdinalIgnoreCase));
        if (inheritLine && parent is not null) ltName = parent.Linetype;
        string ltSignature = inheritLine && parent is not null ? parent.LinetypeIdentity : LinetypeSignature(ltId, tr);
        var weight = ent.LineWeight == LineWeight.ByLayer ? (inheritLayer ? parent!.LineWeight : layer.LineWeight)
            : ent.LineWeight == LineWeight.ByBlock ? parent?.LineWeight ?? layer.LineWeight : ent.LineWeight;
        var transparency = ent.Transparency.IsByLayer ? (inheritLayer ? parent!.Transparency : TransparencyValue.From(layer.Transparency))
            : ent.Transparency.IsByBlock ? parent?.Transparency ?? TransparencyValue.From(layer.Transparency)
            : TransparencyValue.From(ent.Transparency);
        return new LayerDisplayStyle { Color = color, Linetype = ltName, LinetypeIdentity = ltSignature,
            LinetypeScale = ent.LinetypeScale, LineWeight = weight, Transparency = transparency };
    }

    internal static string LinetypeSignature(ObjectId id, Transaction tr)
    {
        var record = (LinetypeTableRecord)tr.GetObject(id, OpenMode.ForRead);
        if (record.NumDashes == 0) return "Continuous";
        var parts = new List<string> { NumberSignature(record.PatternLength), record.IsScaledToFit.ToString() };
        for (int i = 0; i < record.NumDashes; i++)
        {
            var shapeStyle = record.ShapeStyleAt(i);
            if (shapeStyle.IsNull)
            {
                parts.Add(NumberSignature(record.DashLengthAt(i)));
                continue;
            }
            bool isShape = ((TextStyleTableRecord)tr.GetObject(shapeStyle, OpenMode.ForRead)).IsShapeFile;
            var offset = record.ShapeOffsetAt(i);
            parts.Add(string.Join(":", NumberSignature(record.DashLengthAt(i)), isShape ? "" : record.TextAt(i),
                record.ShapeNumberAt(i), NumberSignature(record.ShapeScaleAt(i)), NumberSignature(record.ShapeRotationAt(i)),
                NumberSignature(offset.X), NumberSignature(offset.Y), record.ShapeIsUcsOrientedAt(i), record.ShapeIsUprightAt(i),
                TextStyleSignature(shapeStyle, record.Database, tr)));
        }
        return string.Join(";", parts);
    }

    private static string ColorSignature(Autodesk.AutoCAD.Colors.Color color) =>
        color.ColorMethod == Autodesk.AutoCAD.Colors.ColorMethod.ByAci && color.ColorIndex == 7
            ? "foreground" : color.ColorValue.ToArgb().ToString(System.Globalization.CultureInfo.InvariantCulture);

    private static string StyleSignature(LayerDisplayStyle style) => string.Join("|",
        ColorSignature(style.Color), style.LinetypeIdentity, NumberSignature(style.LinetypeIdentity == "Continuous" ? 1 : style.LinetypeScale),
        style.LineWeight, style.Transparency.Signature);

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
                // Oppervlak meten in het bronvlak en daarna met de vlaktransformatie schalen.
                area = ReadHatchArea(h) * AreaScale(transform, h.Normal);
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

    private static double ReadHatchArea(Hatch hatch)
    {
        try { return Math.Abs(hatch.Area); }
        catch (Autodesk.AutoCAD.Runtime.Exception ex) when (ex.ErrorStatus == Autodesk.AutoCAD.Runtime.ErrorStatus.NotApplicable
            && hatch.NumberOfLoops == 1)
        {
            // Oudere tekeningen kunnen één gesloten contour als Default opslaan. Bepaal het
            // oppervlak met dezelfde contour als buitenrand in een tijdelijk, los hatch-object.
            var loop = hatch.GetLoopAt(0);
            using var measure = new Hatch();
            measure.SetHatchPattern(HatchPatternType.PreDefined, "SOLID");
            if (loop.IsPolyline)
            {
                var points = new Point2dCollection();
                var bulges = new DoubleCollection();
                foreach (BulgeVertex vertex in loop.Polyline) { points.Add(vertex.Vertex); bulges.Add(vertex.Bulge); }
                measure.AppendLoop(HatchLoopTypes.External | HatchLoopTypes.Polyline, points, bulges);
            }
            else
            {
                var types = new IntegerCollection();
                foreach (Curve2d curve in loop.Curves)
                    types.Add(curve switch { LineSegment2d => 1, CircularArc2d => 2, EllipticalArc2d => 3, NurbCurve2d => 4,
                        _ => throw new InvalidOperationException("Onbekende arceringsgrens; oppervlak niet betrouwbaar meetbaar.") });
                measure.AppendLoop(HatchLoopTypes.External, loop.Curves, types);
            }
            measure.EvaluateHatch(true);
            return Math.Abs(measure.Area);
        }
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

    private sealed record LayerSample(NlcsLayerName Parsed, string Signature, LayerMetric Metric,
        HatchSample? Hatch, SymbolSample? Symbol, LayerDisplayStyle Style, string? Description,
        Dictionary<string, int>? Attributes);

    private static void CoalesceNlcsSources(Collector c)
    {
        var samples = c.NlcsRawKeys.Select(key => new LayerSample(c.Parsed[key],
            StyleSignature(c.Styles[key]) + "|" + HatchSignature(c.Hatches.GetValueOrDefault(key))
                + "|" + SymbolSignature(c.SymbolSamples.GetValueOrDefault(key)),
            c.Metrics.GetValueOrDefault(key), c.Hatches.GetValueOrDefault(key), c.SymbolSamples.GetValueOrDefault(key),
            c.Styles[key], c.CustomDesc.GetValueOrDefault(key), c.AttrOms.GetValueOrDefault(key))).ToList();
        c.Parsed.Clear(); c.Metrics.Clear(); c.Hatches.Clear(); c.SymbolSamples.Clear();
        c.Styles.Clear(); c.CustomDesc.Clear(); c.AttrOms.Clear(); c.NlcsRawKeys.Clear();

        foreach (var logical in samples.GroupBy(x => x.Parsed.GroupKey))
        {
            var bundles = new List<(List<LayerSample> Layers, string Signature)>();
            foreach (var source in logical.GroupBy(x => x.Parsed.SourceIdentity, StringComparer.OrdinalIgnoreCase))
            {
                var layers = source.ToList();
                var byType = layers.GroupBy(x => x.Parsed.DrawType).ToList();
                if (byType.Any(type => type.Select(x => x.Signature).Distinct(StringComparer.Ordinal).Count() > 1))
                {
                    foreach (var type in byType)
                        foreach (var variant in type.GroupBy(x => x.Signature, StringComparer.Ordinal))
                            bundles.Add((variant.ToList(), $"{(int)type.Key}:{variant.Key}"));
                }
                else
                    bundles.Add((layers, string.Join(";", byType.OrderBy(x => x.Key)
                        .Select(x => $"{(int)x.Key}:{x.First().Signature}"))));
            }
            int partition = 0;
            foreach (var renderClass in bundles.GroupBy(x => x.Signature, StringComparer.Ordinal)
                .OrderByDescending(g => g.Any(x => x.Layers.Any(l => !l.Parsed.IsXref)))
                .ThenByDescending(g => g.Count()).ThenBy(g => g.Key, StringComparer.Ordinal))
            {
                foreach (var type in renderClass.SelectMany(x => x.Layers).GroupBy(x => x.Parsed.DrawType))
                {
                    var members = type.OrderBy(x => x.Parsed.Scale ?? 0).ThenBy(x => x.Parsed.Raw, StringComparer.OrdinalIgnoreCase).ToList();
                    var rep = members[0];
                    string clean = LayerNaming.LocalName(rep.Parsed.LocalName);
                    string key = partition == 0 ? clean : $"{clean} #{partition + 1}";
                    c.Parsed[key] = new NlcsLayerName
                    {
                        Raw = rep.Parsed.Raw, LocalName = clean, IsXref = rep.Parsed.IsXref, XrefName = rep.Parsed.XrefName,
                        SourceLayers = members.Select(x => x.Parsed.Source).Distinct().ToArray(),
                        StatusCode = rep.Parsed.StatusCode, Status = rep.Parsed.Status, Discipline = rep.Parsed.Discipline,
                        Hoofdgroep = rep.Parsed.Hoofdgroep, Element = rep.Parsed.Element, TypeSuffix = rep.Parsed.TypeSuffix,
                        DrawType = rep.Parsed.DrawType, Scale = rep.Parsed.Scale, RenderLayerName = key, RenderIdentity = renderClass.Key
                    };
                    c.Metrics[key] = members.Aggregate(LayerMetric.Empty, (total, x) => total + x.Metric);
                    c.Styles[key] = rep.Style;
                    if (rep.Hatch is not null) c.Hatches[key] = rep.Hatch;
                    if (rep.Symbol is not null) c.SymbolSamples[key] = rep.Symbol;
                    if (rep.Description is not null) c.CustomDesc[key] = rep.Description;
                    var attributes = new Dictionary<string, int>(StringComparer.Ordinal);
                    foreach (var member in members)
                        if (member.Attributes is not null)
                            foreach (var item in member.Attributes)
                                attributes[item.Key] = attributes.GetValueOrDefault(item.Key) + item.Value;
                    if (attributes.Count > 0) c.AttrOms[key] = attributes;
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
            hatch.Color is null ? string.Empty : ColorSignature(hatch.Color),
            hatch.Transparency.Signature);
    }

    private static string SymbolSignature(SymbolSample? sample) =>
        sample?.RenderIdentity ?? string.Empty;

    private static string SymbolDefinitionIdentity(ObjectId definitionId, Database host, Transaction tr, LayerDisplayStyle parent)
    {
        string content = BlockContentSignature(definitionId, host, tr, new HashSet<ObjectId>(), 0, parent);
        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(content)));
    }

    private static string BlockContentSignature(
        ObjectId definitionId, Database host, Transaction tr, HashSet<ObjectId> path, int depth, LayerDisplayStyle parent)
    {
        if (depth > MaxDepth || !path.Add(definitionId))
            return "cycle";

        try
        {
            var definition = (BlockTableRecord)tr.GetObject(definitionId, OpenMode.ForRead);
            var entities = new List<string>();
            foreach (ObjectId id in definition)
                if (tr.GetObject(id, OpenMode.ForRead) is Entity entity)
                    entities.Add(SymbolEntitySignature(entity, host, tr, path, depth, parent));
            entities.Sort(StringComparer.Ordinal);
            return string.Join("\n", entities);
        }
        finally
        {
            path.Remove(definitionId);
        }
    }

    private static string SymbolEntitySignature(
        Entity entity, Database host, Transaction tr, HashSet<ObjectId> path, int depth, LayerDisplayStyle parent)
    {
        var style = EntityStyle(entity, tr, parent);
        var layer = (LayerTableRecord)tr.GetObject(entity.LayerId, OpenMode.ForRead);
        var parts = new List<string>
        {
            entity.GetType().FullName ?? entity.GetType().Name,
            StyleSignature(style), entity.Visible.ToString(), layer.IsOff.ToString(), layer.IsFrozen.ToString()
        };

        switch (entity)
        {
            case Line line:
                parts.Add($"L:{PointSignature(line.StartPoint)}:{PointSignature(line.EndPoint)}");
                break;
            case Circle circle:
                parts.Add($"C:{PointSignature(circle.Center)}:{NumberSignature(circle.Radius)}:{circle.Normal}");
                break;
            case Arc arc:
                parts.Add($"A:{PointSignature(arc.Center)}:{NumberSignature(arc.Radius)}:"
                    + $"{NumberSignature(arc.StartAngle)}:{NumberSignature(arc.EndAngle)}:{arc.Normal}");
                break;
            case Polyline polyline:
                parts.Add($"P:{polyline.Closed}:{NumberSignature(polyline.Elevation)}:{polyline.Normal}");
                for (int i = 0; i < polyline.NumberOfVertices; i++)
                    parts.Add($"{PointSignature(polyline.GetPoint2dAt(i))}:"
                        + $"{NumberSignature(polyline.GetBulgeAt(i))}:"
                        + $"{NumberSignature(polyline.GetStartWidthAt(i))}:"
                        + $"{NumberSignature(polyline.GetEndWidthAt(i))}");
                break;
            case DBText text:
                parts.Add($"T:{PointSignature(text.Position)}:{PointSignature(text.AlignmentPoint)}:"
                    + $"{text.TextString}:{NumberSignature(text.Height)}:{NumberSignature(text.Rotation)}:"
                    + $"{NumberSignature(text.WidthFactor)}:{NumberSignature(text.Oblique)}:{text.HorizontalMode}:{text.VerticalMode}:{text.IsMirroredInX}:{text.IsMirroredInY}:"
                    + TextStyleSignature(text.TextStyleId, host, tr));
                break;
            case MText text:
                parts.Add($"MT:{PointSignature(text.Location)}:{text.Contents}:{NumberSignature(text.TextHeight)}:"
                    + $"{NumberSignature(text.Width)}:{NumberSignature(text.Rotation)}:{text.Attachment}:{text.Normal}:"
                    + TextStyleSignature(text.TextStyleId, host, tr));
                break;
            case Hatch hatch:
                parts.Add($"H:{hatch.PatternType}:{hatch.PatternName}:{NumberSignature(hatch.PatternScale)}:"
                    + $"{NumberSignature(hatch.PatternAngle)}:{hatch.NumberOfLoops}:{hatch.Normal}:{NumberSignature(hatch.Elevation)}");
                for (int i = 0; i < hatch.NumberOfLoops; i++)
                {
                    var loop = hatch.GetLoopAt(i);
                    parts.Add(loop.LoopType.ToString());
                    if (loop.IsPolyline)
                        foreach (BulgeVertex vertex in loop.Polyline)
                            parts.Add($"{PointSignature(vertex.Vertex)}:{NumberSignature(vertex.Bulge)}");
                    else
                        foreach (Curve2d edge in loop.Curves)
                            parts.Add(edge.GetType().Name + ":" + string.Join(";", edge.GetSamplePoints(65).Select(PointSignature)));
                }
                break;
            case BlockReference nested:
                parts.Add($"B:{PointSignature(nested.Position)}:"
                    + $"{SymbolTransform.From(nested.BlockTransform).Signature}:"
                    + BlockContentSignature(nested.BlockTableRecord, host, tr, path, depth + 1, style));
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
                ? TextStyleContent(style)
                : string.Empty;
        using var sourceTransaction = source.TransactionManager.StartTransaction();
        var sourceStyle = sourceTransaction.GetObject(styleId, OpenMode.ForRead) as TextStyleTableRecord
            ?? throw new InvalidOperationException("Een symbooldefinitie bevat een ongeldige tekststijl.");
        string name = TextStyleContent(sourceStyle);
        sourceTransaction.Commit();
        return name;
    }

    private static string TextStyleContent(TextStyleTableRecord style) => string.Join(":",
        style.FileName.ToUpperInvariant(), style.BigFontFileName.ToUpperInvariant(),
        NumberSignature(style.TextSize), NumberSignature(style.XScale), NumberSignature(style.ObliquingAngle),
        style.IsVertical, style.IsShapeFile, style.Font.TypeFace, style.Font.Bold, style.Font.Italic);

    private static string LocalName(string name) => LayerNaming.LocalName(name).Trim();

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
        catch (Autodesk.AutoCAD.Runtime.Exception ex)
        {
            throw new InvalidOperationException($"Lengte/oppervlak van '{curve.Layer}' kan onder deze transformatie niet betrouwbaar worden bepaald.", ex);
        }
    }

    // Oppervlak schaalt in het vlak van de arcering, onafhankelijk van de Z-schaal.
    private static double AreaScale(Matrix3d transform, Vector3d normal)
    {
        var x = normal.GetPerpendicularVector().GetNormal();
        var y = normal.CrossProduct(x).GetNormal();
        return x.TransformBy(transform).CrossProduct(y.TransformBy(transform)).Length;
    }

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

    private static Dictionary<string, string> ReadLayerDescriptions(
        Database db, Transaction tr, IEnumerable<NlcsLayerName> layers)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
        foreach (var layer in layers)
        {
            if (result.ContainsKey(layer.EffectiveRenderLayerName) || !lt.Has(layer.Raw))
                continue;
            if (tr.GetObject(lt[layer.Raw], OpenMode.ForRead) is LayerTableRecord ltr
                && !string.IsNullOrWhiteSpace(ltr.Description))
            {
                result[layer.EffectiveRenderLayerName] = ltr.Description.Trim();
            }
        }
        return result;
    }
}
