using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using NlcsLegenda.Core;

namespace NlcsLegenda.Plugin;

internal static class LegendRowSamples
{
    public static void Seed(Database db, Transaction tr, LegendDefinition legend, AnalysisResult analysis)
    {
        if (legend.Rows.Count > 0) return;
        var groups = (DBDictionary)tr.GetObject(db.GroupDictionaryId, OpenMode.ForRead);
        if (!groups.Contains(legend.GroupName)) return;
        var group = (Group)tr.GetObject(groups.GetAt(legend.GroupName), OpenMode.ForRead);
        var entities = new List<Entity>();
        foreach (ObjectId id in group.GetAllEntityIds())
        {
            if (tr.GetObject(id, OpenMode.ForRead) is BlockReference root)
                entities.AddRange(((BlockTableRecord)tr.GetObject(root.BlockTableRecord, OpenMode.ForRead)).Cast<ObjectId>().Select(i => tr.GetObject(i, OpenMode.ForRead)).OfType<Entity>());
            else if (tr.GetObject(id, OpenMode.ForRead) is Entity entity) entities.Add(entity);
        }
        string PlainKey(string key) => key.Split("|STYLE:", 2, StringSplitOptions.None)[0];
        foreach (var row in entities.Where(e => LegendManagement.RowToken(e) is not null).GroupBy(e => LegendManagement.RowToken(e)!))
        {
            var keys = LegendManagement.ReadRowKeys(row.First(), tr);
            var candidates = analysis.Entries.Where(e => e.ExclusionKeys.Intersect(keys, StringComparer.OrdinalIgnoreCase).Any()).ToArray();
            if (candidates.Length == 0) candidates = analysis.Entries.Where(e => e.ExclusionKeys.Select(PlainKey).Intersect(keys.Select(PlainKey), StringComparer.OrdinalIgnoreCase).Any()).ToArray();
            var match = candidates.Length == 1 ? candidates[0] : null;
            var texts = row.OfType<MText>().Where(t => t.Layer == legend.Settings.TextLayer).ToArray();
            var description = texts.OrderBy(t => t.Location.X).FirstOrDefault()?.Text;
            if (string.IsNullOrWhiteSpace(description)) continue;
            var sample = row.Where(e => e.Layer != legend.Settings.SwatchFrameLayer && !texts.Any(t => t.ObjectId == e.ObjectId)).Select(e => e.ObjectId).ToArray();
            if (sample.Length == 0) continue;
            var saved = new SavedLegendRow { Id = row.Key, Description = description, Status = match?.Status ?? NlcsStatusExtensions.FromCode(keys.FirstOrDefault()?.Split('|')[0]),
                CustomStatusName = match?.CustomStatusName, SampleBlock = Capture(db, tr, sample), Scale = legend.Settings.Scale,
                OrderKey = match is null ? row.Key : LegendOrder.Key(match), ExclusionKeys = keys.ToList(),
                Sources = match?.Sources.ToList() ?? new(), Appearance = match is null ? "" : LegendUpdatePlan.Appearance(match), Manual = match?.IsManual == true };
            legend.Rows.Add(saved);
            if (match is null) continue;
            if (description != match.DisplayDescription && match.DescriptionSource != DescriptionSource.Bloknaam
                && !match.IsManual && match.Sources.Count > 0)
            {
                LegendContinuity.SetChoice(legend.Settings, match, ChoiceScope.Variant, description);
                match.Description = description;
            }
        }
    }

    public static string Capture(Database db, Transaction tr, IReadOnlyList<ObjectId> ids)
    {
        var table = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForWrite);
        using var block = new BlockTableRecord { Name = "NLCS_ROW_" + Guid.NewGuid().ToString("N") };
        table.Add(block); tr.AddNewlyCreatedDBObject(block, true);
        if (ids.Count == 0) return block.Name;
        var ordinary = new List<ObjectId>();
        using var imports = new LegendBuilder.SymbolImportSession(db, tr);
        foreach (var id in ids)
        {
            if (tr.GetObject(id, OpenMode.ForRead) is not BlockReference reference) { ordinary.Add(id); continue; }
            var source = (BlockTableRecord)tr.GetObject(reference.BlockTableRecord, OpenMode.ForRead);
            if (source.IsFromExternalReference) throw new InvalidOperationException("Selecteer een voorbeeld, niet een volledige xref.");
            var layerStyle = DrawingAnalyzer.EntityLayerStyle(reference, tr);
            var style = DrawingAnalyzer.EntityStyle(reference, tr, null, layerStyle);
            var sample = new SymbolSample(source.Name, "", SymbolTransform.From(reference.BlockTransform)) {
                ReferenceId = id, InsertionStyle = style, InsertionLayerStyle = layerStyle };
            if (!LegendBuilder.TryInsertSymbol(block, tr, db, imports, source.Name, reference.Layer, sample.Transform,
                sample, reference.Position.X, reference.Position.Y, 1, true, out _, out var error)) throw new InvalidOperationException(error);
        }
        if (ordinary.Count > 0)
        {
            using var objects = new ObjectIdCollection(ordinary.ToArray());
            using var mapping = new IdMapping();
            db.DeepCloneObjects(objects, block.ObjectId, mapping, false);
        }
        foreach (ObjectId id in block.Cast<ObjectId>().ToArray())
            if (tr.GetObject(id, OpenMode.ForWrite) is DBText text) LegendBuilder.SymbolTextToMText(block, tr, text);
        var bounds = new Extents3d();
        bool hasBounds = false;
        foreach (ObjectId id in block)
        {
            var entity = (Entity)tr.GetObject(id, OpenMode.ForWrite);
            entity.XData = null;
            if (entity is Hatch hatch) hatch.Associative = false;
            if (!LegendManagement.TryGetEntityExtents(tr, entity, out var extent)) continue;
            bounds.AddExtents(extent); hasBounds = true;
        }
        if (hasBounds)
        {
            var center = bounds.MinPoint + (bounds.MaxPoint - bounds.MinPoint) / 2;
            foreach (ObjectId id in block)
                ((Entity)tr.GetObject(id, OpenMode.ForWrite)).TransformBy(Matrix3d.Displacement(-center.GetAsVector()));
        }
        return block.Name;
    }

    public static SavedLegendRow Save(Database db, Transaction tr, LegendEntry entry, IReadOnlyList<ObjectId> sample, double scale) => new()
    {
        Description = entry.DisplayDescription, Status = entry.Status, CustomStatusName = entry.CustomStatusName,
        SampleBlock = Capture(db, tr, sample), Scale = scale, OrderKey = LegendOrder.Key(entry),
        ExclusionKeys = entry.ExclusionKeys.ToList(), Sources = entry.Sources.ToList(), Appearance = LegendUpdatePlan.Appearance(entry),
        Manual = entry.IsManual
    };

    public static SavedLegendRow SaveRetained(LegendDefinition legend, LegendEntry entry)
    {
        var saved = legend.Rows.First(r => "BEWAARD_" + r.Id == entry.Element).Clone();
        saved.Description = entry.Description;
        return saved;
    }

    public static AnalysisResult Retain(AnalysisResult analysis, LegendDefinition legend)
    {
        if (!legend.Settings.RetainMissingRows || legend.Rows.Count == 0) return analysis;
        var entries = analysis.Entries.ToList();
        foreach (var row in legend.Rows.Where(r => !r.Manual && r.SampleBlock.Length > 0))
        {
            if (entries.Any(e => LegendContinuity.Related(row.Sources, e.Sources) || row.OrderKey == LegendOrder.Key(e))) continue;
            var retained = row.RetainedEntry();
            LegendContinuity.ApplyChoices(legend.Settings, new() { retained });
            if (retained.SourceExcluded ?? row.ExclusionKeys.Any(legend.Settings.IsEntryExcluded)) continue;
            if (legend.Settings.RowChoices.Any(c => c.PreviousRowId == row.Id && entries.Any(c.Matches))) continue;
            if (!legend.Settings.IncludedStatuses.Contains(row.Status)) continue;
            entries.Add(retained);
            int index = legend.Settings.ManualEntryOrder.IndexOf(row.OrderKey);
            if (index >= 0) legend.Settings.ManualEntryOrder[index] = LegendOrder.Key(retained);
        }
        return analysis.WithEntries(LegendGrouping.Sort(entries, legend.Settings));
    }

    public static void Prune(Database db)
    {
        using var tr = db.TransactionManager.StartTransaction();
        var registry = LegendStore.Load(db, tr);
        var keep = registry.Legends.SelectMany(d => d.Rows.Select(r => r.SampleBlock)
            .Concat(d.Settings.ManualEntries.Select(m => m.SampleBlock))
            .Concat(d.Settings.RowChoices.Select(c => c.Example ?? ""))
            .Concat(d.Settings.ExampleOverrides.Values)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var blocks = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
        using var unused = new ObjectIdCollection();
        foreach (ObjectId id in blocks)
        {
            var block = (BlockTableRecord)tr.GetObject(id, OpenMode.ForRead);
            if (block.Name.StartsWith("NLCS_ROW_", StringComparison.Ordinal) && !keep.Contains(block.Name)) unused.Add(id);
        }
        db.Purge(unused);
        foreach (ObjectId id in unused) tr.GetObject(id, OpenMode.ForWrite).Erase();
        tr.Commit();
    }
}
