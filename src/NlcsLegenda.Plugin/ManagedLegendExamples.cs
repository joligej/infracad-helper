using Autodesk.AutoCAD.DatabaseServices;
using NlcsLegenda.Core;

namespace NlcsLegenda.Plugin;

internal static class ManagedLegendExamples
{
    public static ExampleDisplay Display(LayerDisplayStyle style) => new(DrawingAnalyzer.ColorSignature(style.Color),
        style.LinetypeIdentity, (int)style.LineWeight,
        style.Transparency.Method == Autodesk.AutoCAD.Colors.TransparencyMethod.ByAlpha ? 255 - style.Transparency.Alpha : 0);

    public static IReadOnlyList<ExampleDisplay> Read(Database db, Transaction tr, IEnumerable<ObjectId> ids, LayerDisplayStyle? rootStyle = null, LayerDisplayStyle? rootLayer = null, ObjectId viewport = default, bool includeText = false)
    {
        var values = new HashSet<ExampleDisplay>(); var stack = new HashSet<ObjectId>();
        void Visit(ObjectId id, LayerDisplayStyle? parent, LayerDisplayStyle? parentLayer, int depth)
        {
            if (depth > 30 || id.IsNull || id.IsErased || !stack.Add(id)) return;
            try
            {
                if (tr.GetObject(id, OpenMode.ForRead) is not Entity e || !e.Visible) return;
                var layer = depth == 0 && rootLayer is not null ? rootLayer : DrawingAnalyzer.EntityLayerStyle(e, tr, parentLayer, viewport);
                var record = (LayerTableRecord)tr.GetObject(e.LayerId, OpenMode.ForRead);
                if (!record.IsPlottable || record.IsOff || record.IsFrozen) return;
                if (!viewport.IsNull && ((Viewport)tr.GetObject(viewport, OpenMode.ForRead)).GetFrozenLayers().Cast<ObjectId>().Contains(e.LayerId)) return;
                var style = depth == 0 && rootStyle is not null ? rootStyle : DrawingAnalyzer.EntityStyle(e, tr, parent, layer);
                if (e is BlockReference reference)
                {
                    var block = (BlockTableRecord)tr.GetObject(reference.BlockTableRecord, OpenMode.ForRead);
                    foreach (ObjectId child in block) Visit(child, style, layer, depth + 1);
                }
                else if (e is not AttributeDefinition && (includeText || e is not DBText && e is not MText)) values.Add(Display(style));
            }
            finally { stack.Remove(id); }
        }
        foreach (var id in ids) Visit(id, null, null, 0);
        return values.OrderBy(v => v.Color).ThenBy(v => v.Linetype).ThenBy(v => v.LineWeight).ThenBy(v => v.Transparency).ToArray();
    }

    public static IReadOnlyList<ExampleDisplay> ReadBlock(Database db, Transaction tr, string name)
    {
        var blocks = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
        return blocks.Has(name) ? Read(db, tr, ((BlockTableRecord)tr.GetObject(blocks[name], OpenMode.ForRead)).Cast<ObjectId>(), includeText: true) : Array.Empty<ExampleDisplay>();
    }

    public static IReadOnlyList<SavedLegendRow> CurrentRows(Database db, Transaction tr, LegendDefinition definition)
    {
        var saved = definition.Rows.Select(r => r.Clone()).ToArray();
        var groups = (DBDictionary)tr.GetObject(db.GroupDictionaryId, OpenMode.ForRead);
        if (!groups.Contains(definition.GroupName)) return saved;
        var group = (Group)tr.GetObject(groups.GetAt(definition.GroupName), OpenMode.ForRead);
        var entities = new List<Entity>();
        foreach (var id in group.GetAllEntityIds().Where(id => !id.IsNull && id.IsValid && !id.IsErased))
        {
            if (tr.GetObject(id, OpenMode.ForRead) is BlockReference root)
                entities.AddRange(((BlockTableRecord)tr.GetObject(root.BlockTableRecord, OpenMode.ForRead)).Cast<ObjectId>().Select(i => tr.GetObject(i, OpenMode.ForRead)).OfType<Entity>());
            else if (tr.GetObject(id, OpenMode.ForRead) is Entity entity) entities.Add(entity);
        }
        foreach (var row in saved)
        {
            var members = entities.Where(e => LegendManagement.ReadRowKeys(e, tr).Intersect(row.ExclusionKeys, StringComparer.OrdinalIgnoreCase).Any()).ToArray();
            if (members.Length == 0) continue;
            var description = members.OfType<MText>().Where(t => t.Layer == definition.Settings.TextLayer).OrderBy(t => t.Location.X).FirstOrDefault();
            if (description is not null) row.Description = description.Text;
            row.ExampleDisplay = Read(db, tr, members.Where(e => e.Layer != definition.Settings.SwatchFrameLayer
                && e.Layer != definition.Settings.TextLayer).Select(e => e.ObjectId), includeText: row.Manual).ToList();
        }
        return saved;
    }

    public static AnalysisResult Prepare(Database db, Transaction tr, LegendDefinition definition, AnalysisResult analysis)
    {
        foreach (var entry in analysis.Entries)
        {
            var manual = definition.Settings.ManualEntries.FirstOrDefault(m => m.ToLegendEntry().Element == entry.Element);
            if (manual?.Link is { } link)
            {
                try
                {
                    var source = LinkedLegendExamples.Resolve(db, tr, link);
                    entry.SourceDisplay = source.Display;
                    entry.SourceAppearance = System.Text.Json.JsonSerializer.Serialize(source.Display);
                    entry.VariantDetails += " Gekoppeld: " + link.Label + (link.Component.Length > 0 ? " · " + link.Component : "") + ".";
                }
                catch (System.Exception ex) when (!ExceptionSafety.IsFatal(ex)) { entry.SourceNotice += " Verwachte bronkoppeling niet beschikbaar: " + ex.GetBaseException().Message; }
            }
            if (!entry.CanFollowSource) entry.VariantDetails += " Eigen voorbeeld zonder bronkoppeling.";
            var matches = definition.Rows.Where(r => LegendComposition.Matches(r, entry)).Take(2).ToArray();
            var previous = matches.Length == 1 ? matches[0] : null;
            if (previous is not null) entry.ApprovedRowId = previous.Id;
            entry.CapturedSampleBlock = previous?.SampleBlock ?? entry.SampleBlock;
            entry.CapturedSampleScale = previous?.Scale ?? entry.SampleScale;
            string sample = entry.SampleBlock;
            if (definition.Settings.ExampleOverrides.TryGetValue(LegendOrder.Key(entry), out var chosen)) sample = chosen;
            if (sample.Length > 0)
            {
                entry.ExampleDisplay = ReadBlock(db, tr, sample);
                var difference = ExampleDisplay.Difference(entry.SourceDisplay, entry.ExampleDisplay);
                if (entry.SourceDisplay.Count > 0 && difference.Length > 0)
                    entry.SourceNotice += " " + (entry.ExampleMode == ExampleMode.Fixed ? "Vast voorbeeld wijkt af van de bron: " : "Bronvolgend voorbeeld wijkt af: ") + difference + ".";
                else if (entry.SourceDisplay.Count == 0 && entry.Sources.Count > 0)
                    entry.SourceNotice += " De gekoppelde bronweergave kon niet worden gecontroleerd.";
            }
            else entry.ExampleDisplay = entry.SourceDisplay;
        }
        return analysis;
    }
}
