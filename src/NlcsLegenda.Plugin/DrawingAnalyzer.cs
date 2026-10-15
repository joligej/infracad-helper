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
        public readonly HashSet<string> NlcsSourceLayers = new(StringComparer.OrdinalIgnoreCase);
        public readonly HashSet<string> CustomSourceLayers = new(StringComparer.OrdinalIgnoreCase);
        public HashSet<ObjectId> Excluded = new();
        public LayerTable? Layers;
        public required KlicSourceMetadata Klic;
        public readonly Dictionary<string, List<KlicObjectData>> KlicObjects = new(StringComparer.OrdinalIgnoreCase);
    }

    // Veiligheidslimiet tegen ontspoorde recursie; echte cycli worden met een pad-set
    // afgevangen, dus deze grens hoeft niet laag te zijn.
    private const int MaxDepth = 16;

    public static AnalysisResult Analyze(
        Database db, Transaction tr, LegendSettings settings,
        IReadOnlyCollection<ObjectId>? selection = null,
        DescriptionCatalog? catalog = null,
        IReadOnlyCollection<ObjectId>? excludedIds = null,
        Action<double, double>? reportTimings = null)
    {
        var timer = reportTimings is null ? null : System.Diagnostics.Stopwatch.StartNew();
        using var klic = new KlicSourceMetadata(settings.Klic, db, tr);
        var c = new Collector { Klic = klic };
        c.Layers = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
        if (excludedIds is { Count: > 0 })
            c.Excluded = new HashSet<ObjectId>(excludedIds);
        // Pad-set voor cyclusdetectie op blokdefinities: een blok dat meerdere keren is
        // ingevoegd, wordt per instance geteld, maar een echte cyclus wordt gestopt.
        var path = new HashSet<ObjectId>();

        if (selection is not null)
        {
            foreach (var id in selection)
                if (tr.GetObject(id, OpenMode.ForRead) is Entity ent)
                    Process(ent, tr, c, settings, path, 0, Matrix3d.Identity, sourceXref: string.Empty);
        }
        else
        {
            var msId = SymbolUtilityServices.GetBlockModelSpaceId(db);
            CollectFromBlock(msId, tr, c, settings, path, 0, Matrix3d.Identity, sourceXref: string.Empty);
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
            if (c.SymbolSamples.ContainsKey(name)) return string.Empty;
            renderIds.TryGetValue(name, out var baseId);
            var hatch = c.Hatches.TryGetValue(name, out var h) ? HatchSignature(h) : string.Empty;
            return (baseId ?? string.Empty) + "|" + hatch;
        }

        // DescriptionOverrides worden in LegendGrouping.Build toegepast (één plek), dus hier
        // alleen de basiscatalogus doorgeven.
        double analysisMilliseconds = timer?.Elapsed.TotalMilliseconds ?? 0;
        timer?.Restart();
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
            name => c.SymbolSamples.TryGetValue(name, out var sample) ? sample.RenderIdentity : null,
            name => c.KlicObjects.TryGetValue(name, out var objects) ? objects : Array.Empty<KlicObjectData>());

        if (timer is not null)
        {
            timer.Stop();
            reportTimings!(analysisMilliseconds, timer.Elapsed.TotalMilliseconds);
        }
        return new AnalysisResult
        {
            Entries = entries,
            HatchSamples = c.Hatches,
            SymbolSamples = c.SymbolSamples,
            LayerStyles = c.Styles,
            UsedNlcsLayerCount = c.NlcsSourceLayers.Count,
            UsedCustomLayerCount = c.CustomSourceLayers.Count,
            DescribedLayerCount = c.NlcsSourceLayers.Count(name => c.Layers.Has(name)
                && !string.IsNullOrWhiteSpace(((LayerTableRecord)tr.GetObject(c.Layers[name], OpenMode.ForRead)).Description)),
            AttrDescribedLayerCount = attrDesc.Count,
            ExcludedNlcsLayerCount = c.ExcludedNlcs.Count
        };
    }

    private static void CollectFromBlock(
        ObjectId btrId, Transaction tr, Collector c, LegendSettings settings,
        HashSet<ObjectId> path, int depth, Matrix3d transform, string sourceXref, LayerDisplayStyle? inheritedStyle = null, LayerDisplayStyle? inheritedLayerStyle = null)
    {
        if (depth > MaxDepth) throw new InvalidOperationException("Te diep geneste blokken; analyse afgebroken om onvolledige hoeveelheden te voorkomen.");
        if (!path.Add(btrId)) return;

        var btr = (BlockTableRecord)tr.GetObject(btrId, OpenMode.ForRead);
        foreach (ObjectId id in btr)
            if (tr.GetObject(id, OpenMode.ForRead) is Entity ent)
                Process(ent, tr, c, settings, path, depth, transform, sourceXref, inheritedStyle, inheritedLayerStyle);

        path.Remove(btrId);
    }

    private static void Process(
        Entity ent, Transaction tr, Collector c, LegendSettings settings,
        HashSet<ObjectId> path, int depth, Matrix3d transform, string sourceXref, LayerDisplayStyle? inheritedStyle = null, LayerDisplayStyle? inheritedLayerStyle = null)
    {
        // Eigen legenda-geometrie telt nooit als bron.
        if (c.Excluded.Contains(ent.ObjectId))
            return;

        if (!settings.IncludeInvisibleLayers)
        {
            if (!ent.Visible) return;
            bool inheritsVisibleLayer = inheritedLayerStyle is not null && LayerNaming.LocalName(ent.Layer) == "0";
            if (!inheritsVisibleLayer && !IsVisible(ent.Layer, tr, c)) return;
        }
        var layerStyle = EntityLayerStyle(ent, tr, inheritedLayerStyle);
        var effectiveStyle = EntityStyle(ent, tr, inheritedStyle, layerStyle);
        var block = ent as BlockReference;
        var definition = block is not null && !block.BlockTableRecord.IsNull
            ? tr.GetObject(block.BlockTableRecord, OpenMode.ForRead) as BlockTableRecord : null;
        // De xref-insert is een broncontainer, geen lokaal legendasymbool. De laag
        // waarop hij is ingevoegd mag een uitgeschakelde xref niet alsnog opnemen.
        if (definition is not { IsFromExternalReference: true })
            Record(ent, tr, c, settings, transform, sourceXref, effectiveStyle, layerStyle);

        if (ent is BlockReference br && !br.BlockTableRecord.IsNull)
        {
            var def = tr.GetObject(br.BlockTableRecord, OpenMode.ForRead) as BlockTableRecord;
            if (def is { IsLayout: false })
            {
                var nested = transform * br.BlockTransform;
                if (def.IsFromExternalReference)
                {
                    string childSource = string.IsNullOrEmpty(sourceXref) ? def.Name : sourceXref + "|" + LayerNaming.LocalName(def.Name);
                    if (settings.IsXrefIncluded(childSource))
                        CollectFromBlock(br.BlockTableRecord, tr, c, settings, path, depth + 1, nested,
                            sourceXref: childSource, inheritedStyle: effectiveStyle, inheritedLayerStyle: layerStyle);
                }
                else
                {
                    CollectFromBlock(br.BlockTableRecord, tr, c, settings, path, depth + 1, nested, sourceXref, effectiveStyle, layerStyle);
                }
            }
        }
    }

    private static void Record(Entity ent, Transaction tr, Collector c, LegendSettings settings,
        Matrix3d transform, string sourceXref, LayerDisplayStyle style, LayerDisplayStyle layerStyle)
    {
        var layerName = ent.Layer;
        KlicObjectData? metadata = null;
        CustomLayerRule? rule = null;
        NlcsLayerName? parsed;
        if (TryMatchCustomRule(ent, tr, settings, layerName, sourceXref, out rule, out parsed))
        {
            metadata = c.Klic.Read(ent, sourceXref);
            parsed = CopyLayer(parsed!, sourceXref, ElementProperties.IsKlic(parsed!.Discipline, parsed.Hoofdgroep) ? metadata.Provider : string.Empty,
                metadata.GroupKey(settings.Klic.GroupFields));
            if (!settings.IsIncluded(parsed)) return;
        }
        else
        {
            if (!NlcsLayerParser.TryParse(layerName, out parsed)) return;
            metadata = c.Klic.Read(ent, sourceXref);
            parsed = CopyLayer(parsed, sourceXref, ElementProperties.IsKlic(parsed.Discipline, parsed.Hoofdgroep) ? metadata.Provider : string.Empty,
                metadata.GroupKey(settings.Klic.GroupFields));
            if (!settings.IsIncluded(parsed))
            {
                c.ExcludedNlcs.Add(parsed.LocalName);
                return;
            }
        }
        if (!settings.IsDrawTypeIncluded(parsed!.DrawType) || !EntityMatchesType(ent, parsed.DrawType)) return;

        HatchSample? hatch = ent is Hatch h ? HatchSample.From(h, transform) : null;
        if (hatch is not null)
        {
            hatch.SourceScale = parsed.Scale;
            hatch.TargetScale = settings.Scale;
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
            var cacheKey = (id, StyleSignature(style) + "|" + StyleSignature(layerStyle));
            if (!c.SymbolDefinitionIdentities.TryGetValue(cacheKey, out var identity))
            {
                identity = SymbolDefinitionIdentity(id, ent.Database, tr, style, layerStyle, settings.IncludeInvisibleLayers);
                c.SymbolDefinitionIdentities[cacheKey] = identity;
            }
            symbol = new SymbolSample(blockName, identity, SymbolTransform.From(transform * br.BlockTransform))
            { InsertionStyle = style, InsertionLayerStyle = layerStyle, SourceScale = parsed.Scale, TargetScale = settings.Scale };
        }
        metadata ??= c.Klic.Read(ent, sourceXref);
        string appearance = SampleSignature(style, hatch, symbol);
        string key = parsed.GroupKey + "\u0001" + parsed.Netbeheerder.ToUpperInvariant() + "\u0001" + parsed.KlicGroupKey + "\u0001" + sourceXref + "\u0001" + layerName + "\u0001" + appearance;
        (rule is null ? c.NlcsSourceLayers : c.CustomSourceLayers).Add(layerName);
        c.Parsed[key] = parsed;
        if (metadata.HasData || metadata.ReadIssues.Count > 0)
        {
            if (!c.KlicObjects.TryGetValue(key, out var objects)) c.KlicObjects.Add(key, objects = new());
            objects.Add(metadata);
        }
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

    private static bool IsBlockPartVisible(Entity entity, LayerTableRecord layer)
    {
        if (!entity.Visible) return false;
        if (LayerNaming.LocalName(layer.Name) == "0") return true;
        return !layer.IsOff && !layer.IsFrozen;
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
                    if (!includeInvisible && !IsBlockPartVisible(entity, layer)) continue;
                    if (entity is not BlockReference nested || HasSymbolGeometry(nested.BlockTableRecord, tr, includeInvisible, path)) return true;
                }
            return false;
        }
        finally { path.Remove(id); }
    }

    private static NlcsLayerName CopyLayer(NlcsLayerName p, string source, string provider, string klicGroup = "") => new()
    {
        Raw = p.Raw, LocalName = p.LocalName, IsXref = source.Length > 0,
        XrefName = source, Netbeheerder = provider, KlicGroupKey = klicGroup, StatusCode = p.StatusCode, Status = p.Status,
        Discipline = p.Discipline, Hoofdgroep = p.Hoofdgroep, Element = p.Element,
        TypeSuffix = p.TypeSuffix, DrawType = p.DrawType, Scale = p.Scale
    };

    internal static LayerDisplayStyle EntityLayerStyle(Entity entity, Transaction tr, LayerDisplayStyle? parentLayer = null)
    {
        var layer = (LayerTableRecord)tr.GetObject(entity.LayerId, OpenMode.ForRead);
        if (LayerNaming.LocalName(layer.Name) == "0" && parentLayer is not null) return parentLayer;
        var linetype = (LinetypeTableRecord)tr.GetObject(layer.LinetypeObjectId, OpenMode.ForRead);
        return new LayerDisplayStyle
        {
            Color = layer.Color, Linetype = linetype.Name,
            LinetypeIdentity = LinetypeSignature(layer.LinetypeObjectId, tr),
            LineWeight = layer.LineWeight, Transparency = TransparencyValue.From(layer.Transparency)
        };
    }

    internal static LayerDisplayStyle EntityStyle(Entity entity, Transaction tr,
        LayerDisplayStyle? parent, LayerDisplayStyle layer)
    {
        var color = entity.Color.IsByLayer ? layer.Color
            : entity.Color.IsByBlock ? parent?.Color ?? layer.Color : entity.Color;
        if (color.IsByLayer || color.IsByBlock)
            color = Autodesk.AutoCAD.Colors.Color.FromColorIndex(Autodesk.AutoCAD.Colors.ColorMethod.ByAci, 7);
        string linetype = LayerNaming.LocalName(entity.Linetype);
        var inheritedLine = linetype.Equals("ByLayer", StringComparison.OrdinalIgnoreCase) ? layer
            : linetype.Equals("ByBlock", StringComparison.OrdinalIgnoreCase) ? parent ?? layer : null;
        return new LayerDisplayStyle
        {
            Color = color,
            Linetype = inheritedLine?.Linetype ?? entity.Linetype,
            LinetypeIdentity = inheritedLine?.LinetypeIdentity ?? LinetypeSignature(entity.LinetypeId, tr),
            LinetypeScale = entity.LinetypeScale,
            LineWeight = entity.LineWeight == LineWeight.ByLayer ? layer.LineWeight
                : entity.LineWeight == LineWeight.ByBlock ? parent?.LineWeight ?? layer.LineWeight : entity.LineWeight,
            Transparency = entity.Transparency.IsByLayer ? layer.Transparency
                : entity.Transparency.IsByBlock ? parent?.Transparency ?? layer.Transparency
                : TransparencyValue.From(entity.Transparency)
        };
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

    private static void AddMetric(Entity ent, string localLayer, Collector c, Matrix3d transform, bool automatic = true)
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

        if (automatic)
        {
            // Een G-contour levert lengte; zijn oppervlak mag een A-arcering niet verdubbelen.
            var type = c.Parsed[localLayer].DrawType;
            if (type == NlcsDrawType.Geometrie) area = 0;
            else if (type is NlcsDrawType.Vlak or NlcsDrawType.Arcering or NlcsDrawType.Vlakvulling) length = 0;
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
        Dictionary<string, int>? Attributes, IReadOnlyList<KlicObjectData> KlicObjects);

    private static void CoalesceNlcsSources(Collector c)
    {
        var samples = c.NlcsRawKeys.Select(key => new LayerSample(c.Parsed[key],
            SampleSignature(c.Styles[key], c.Hatches.GetValueOrDefault(key), c.SymbolSamples.GetValueOrDefault(key)),
            c.Metrics.GetValueOrDefault(key), c.Hatches.GetValueOrDefault(key), c.SymbolSamples.GetValueOrDefault(key),
            c.Styles[key], c.CustomDesc.GetValueOrDefault(key), c.AttrOms.GetValueOrDefault(key),
            c.KlicObjects.TryGetValue(key, out var objects) ? objects : Array.Empty<KlicObjectData>())).ToList();
        c.Parsed.Clear(); c.Metrics.Clear(); c.Hatches.Clear(); c.SymbolSamples.Clear();
        c.Styles.Clear(); c.CustomDesc.Clear(); c.AttrOms.Clear(); c.NlcsRawKeys.Clear(); c.KlicObjects.Clear();

        foreach (var logical in samples.GroupBy(x => x.Parsed.GroupKey + "\u0001" + x.Parsed.Netbeheerder.ToUpperInvariant() + "\u0001" + x.Parsed.KlicGroupKey).OrderBy(x => x.Key, StringComparer.Ordinal))
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
                    // Eén eigen bronlaag kan meerdere elementsoorten of blokfilters bevatten.
                    if (c.Parsed.ContainsKey(key))
                    {
                        string identity = logical.Key + "|" + type.Key + "|" + renderClass.Key;
                        string suffix = " #" + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
                            System.Text.Encoding.UTF8.GetBytes(identity)))[..16];
                        key = clean[..Math.Min(clean.Length, 255 - suffix.Length)] + suffix;
                    }
                    c.Parsed.Add(key, new NlcsLayerName
                    {
                        Raw = rep.Parsed.Raw, LocalName = clean, IsXref = rep.Parsed.IsXref, XrefName = rep.Parsed.XrefName,
                        SourceLayers = members.Select(x => x.Parsed.Source).Distinct().ToArray(),
                        Netbeheerder = rep.Parsed.Netbeheerder, KlicGroupKey = rep.Parsed.KlicGroupKey,
                        StatusCode = rep.Parsed.StatusCode, Status = rep.Parsed.Status, Discipline = rep.Parsed.Discipline,
                        Hoofdgroep = rep.Parsed.Hoofdgroep, Element = rep.Parsed.Element, TypeSuffix = rep.Parsed.TypeSuffix,
                        DrawType = rep.Parsed.DrawType, Scale = rep.Parsed.Scale, RenderLayerName = key, RenderIdentity = renderClass.Key
                    });
                    c.KlicObjects[key] = KlicSummary.Combine(members.SelectMany(x => x.KlicObjects)).ToList();
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
            NumberSignature(hatch.PatternScale * hatch.ScaleForLegend(hatch.TargetScale)),
            NumberSignature(hatch.PatternAngle),
            NumberSignature(hatch.PatternSpace * hatch.ScaleForLegend(hatch.TargetScale)), hatch.PatternDouble,
            hatch.IsSolid,
            hatch.Color is null ? string.Empty : ColorSignature(hatch.Color),
            hatch.Transparency.Signature);
    }

    private static string SampleSignature(LayerDisplayStyle style, HatchSample? hatch, SymbolSample? symbol) =>
        symbol is not null ? symbol.RenderIdentity : StyleSignature(style) + "|" + HatchSignature(hatch);

    private static string SymbolDefinitionIdentity(ObjectId definitionId, Database host, Transaction tr, LayerDisplayStyle parent, LayerDisplayStyle parentLayer, bool includeInvisible)
    {
        string content = BlockContentSignature(definitionId, host, tr, new HashSet<ObjectId>(), 0, parent, parentLayer, includeInvisible);
        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(content)));
    }

    private static string BlockContentSignature(
        ObjectId definitionId, Database host, Transaction tr, HashSet<ObjectId> path, int depth, LayerDisplayStyle parent, LayerDisplayStyle parentLayer, bool includeInvisible)
    {
        if (depth > MaxDepth) throw new InvalidOperationException("Symbool is te diep genest.");
        if (!path.Add(definitionId)) return string.Empty;

        try
        {
            var definition = (BlockTableRecord)tr.GetObject(definitionId, OpenMode.ForRead);
            var entities = new List<string>();
            foreach (ObjectId id in definition)
                if (tr.GetObject(id, OpenMode.ForRead) is Entity entity)
                {
                    var layer = (LayerTableRecord)tr.GetObject(entity.LayerId, OpenMode.ForRead);
                    if (!includeInvisible && !IsBlockPartVisible(entity, layer)) continue;
                    entities.Add(SymbolEntitySignature(entity, host, tr, path, depth, parent, parentLayer, includeInvisible));
                }
            entities.Sort(StringComparer.Ordinal);
            return string.Join("\n", entities);
        }
        finally
        {
            path.Remove(definitionId);
        }
    }

    private static string SymbolEntitySignature(
        Entity entity, Database host, Transaction tr, HashSet<ObjectId> path, int depth, LayerDisplayStyle parent, LayerDisplayStyle parentLayer, bool includeInvisible)
    {
        var layerStyle = EntityLayerStyle(entity, tr, parentLayer);
        var style = EntityStyle(entity, tr, parent, layerStyle);
        var parts = new List<string>
        {
            entity.GetType().FullName ?? entity.GetType().Name,
            StyleSignature(style)
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
            case Polyline2d polyline:
                parts.Add($"P2:{polyline.Closed}:{polyline.PolyType}:{PointSignature(polyline.Normal)}:{NumberSignature(polyline.Thickness)}");
                foreach (ObjectId vertexId in polyline)
                {
                    var vertex = (Vertex2d)tr.GetObject(vertexId, OpenMode.ForRead);
                    parts.Add($"{PointSignature(vertex.Position)}:{NumberSignature(vertex.Bulge)}:"
                        + $"{NumberSignature(vertex.StartWidth)}:{NumberSignature(vertex.EndWidth)}:{vertex.VertexType}");
                }
                break;
            case Polyline3d polyline:
                parts.Add($"P3:{polyline.Closed}:{polyline.PolyType}");
                foreach (ObjectId vertexId in polyline)
                    parts.Add(PointSignature(((PolylineVertex3d)tr.GetObject(vertexId, OpenMode.ForRead)).Position));
                break;
            case Ellipse ellipse:
                parts.Add($"E:{PointSignature(ellipse.Center)}:{PointSignature(ellipse.Normal)}:{PointSignature(ellipse.MajorAxis)}:"
                    + $"{NumberSignature(ellipse.RadiusRatio)}:{NumberSignature(ellipse.StartAngle)}:{NumberSignature(ellipse.EndAngle)}");
                break;
            case Spline spline:
                var nurbs = spline.NurbsData;
                parts.Add($"N:{nurbs.Degree}:{nurbs.Rational}:{nurbs.Closed}:{nurbs.Periodic}");
                parts.Add(string.Join(";", nurbs.GetControlPoints().Cast<Point3d>().Select(PointSignature)));
                parts.Add(string.Join(";", nurbs.GetKnots().Cast<double>().Select(NumberSignature)));
                parts.Add(string.Join(";", nurbs.GetWeights().Cast<double>().Select(NumberSignature)));
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
                    + $"{NumberSignature(hatch.PatternAngle)}:{NumberSignature(hatch.PatternSpace)}:{hatch.PatternDouble}:"
                    + $"{hatch.NumberOfLoops}:{PointSignature(hatch.Normal)}:{NumberSignature(hatch.Elevation)}");
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
                    + BlockContentSignature(nested.BlockTableRecord, host, tr, path, depth + 1, style, layerStyle, includeInvisible));
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

    private static string PointSignature(Vector3d vector) =>
        $"{NumberSignature(vector.X)},{NumberSignature(vector.Y)},{NumberSignature(vector.Z)}";

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
        AddMetric(ent, key, c, transform, automatic: mode == CustomQuantityMode.Auto);
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
            if (result.ContainsKey(layer.EffectiveRenderLayerName)) continue;
            var sources = new[] { layer.Raw }.Concat(layer.SourceLayers.Select(x => x.SourceLayerName)
                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)).Distinct(StringComparer.OrdinalIgnoreCase);
            foreach (var source in sources)
            {
                if (!lt.Has(source)) continue;
                var record = (LayerTableRecord)tr.GetObject(lt[source], OpenMode.ForRead);
                if (string.IsNullOrWhiteSpace(record.Description)) continue;
                var description = LayerDescription.Clean(record.Description, layer);
                if (description is null) continue;
                result[layer.EffectiveRenderLayerName] = description;
                break;
            }
        }
        return result;
    }
}
