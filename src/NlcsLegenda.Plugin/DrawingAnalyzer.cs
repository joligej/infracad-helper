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
        public readonly HashSet<string> ExcludedNlcs = new(StringComparer.OrdinalIgnoreCase);
        public readonly Dictionary<string, bool> Visible = new(StringComparer.OrdinalIgnoreCase);
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
                    Process(ent, tr, c, settings, path, 0, Matrix3d.Identity, insideIncludedXref: false);
        }
        else
        {
            var msId = SymbolUtilityServices.GetBlockModelSpaceId(db);
            CollectFromBlock(msId, tr, c, settings, path, 0, Matrix3d.Identity, insideIncludedXref: false);
        }

        var descriptions = ReadLayerDescriptions(db, tr, c.Parsed.Values);

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

        var entries = LegendGrouping.Build(
            c.Parsed.Values, settings,
            name => descriptions.TryGetValue(name, out var d) ? d : null,
            c.Metrics,
            name => c.SymbolBlocks.TryGetValue(name, out var b) ? b : null,
            catalog);

        return new AnalysisResult
        {
            Entries = entries,
            HatchSamples = c.Hatches,
            UsedNlcsLayerCount = c.Parsed.Count,
            DescribedLayerCount = descriptions.Count,
            ExcludedNlcsLayerCount = c.ExcludedNlcs.Count
        };
    }

    private static void CollectFromBlock(
        ObjectId btrId, Transaction tr, Collector c, LegendSettings settings,
        HashSet<ObjectId> path, int depth, Matrix3d transform, bool insideIncludedXref)
    {
        if (depth > MaxDepth || !path.Add(btrId))
            return;

        var btr = (BlockTableRecord)tr.GetObject(btrId, OpenMode.ForRead);
        foreach (ObjectId id in btr)
            if (tr.GetObject(id, OpenMode.ForRead) is Entity ent)
                Process(ent, tr, c, settings, path, depth, transform, insideIncludedXref);

        path.Remove(btrId);
    }

    private static void Process(
        Entity ent, Transaction tr, Collector c, LegendSettings settings,
        HashSet<ObjectId> path, int depth, Matrix3d transform, bool insideIncludedXref)
    {
        // Eigen legenda-geometrie telt nooit als bron.
        if (c.Excluded.Contains(ent.ObjectId))
            return;

        Record(ent, tr, c, settings, transform);

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
                        CollectFromBlock(br.BlockTableRecord, tr, c, settings, path, depth + 1, nested, insideIncludedXref: true);
                }
                else
                {
                    CollectFromBlock(br.BlockTableRecord, tr, c, settings, path, depth + 1, nested, insideIncludedXref);
                }
            }
        }
    }

    private static void Record(Entity ent, Transaction tr, Collector c, LegendSettings settings, Matrix3d transform)
    {
        var layerName = ent.Layer;
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

        if (ent is BlockReference symbolRef && nlcs.DrawType == NlcsDrawType.Symbool
            && !c.SymbolBlocks.ContainsKey(nlcs.LocalName))
        {
            var name = BlockName(symbolRef, tr);
            if (!string.IsNullOrEmpty(name) && !name.StartsWith('*'))
                c.SymbolBlocks[nlcs.LocalName] = name;
        }
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
