using System.IO;
using Color = Autodesk.AutoCAD.Colors.Color;
using Autodesk.AutoCAD.Colors;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using NlcsLegenda.Core;
using AcAp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace NlcsLegenda.Plugin;

public partial class Commands
{
    [CommandMethod("NLCSLEGENDAAUDITVERSION", CommandFlags.Modal)]
    public void NlcsLegendaAuditVersion()
    {
        var assembly = typeof(Commands).Assembly;
        var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(assembly.Location)));
        AcAp.DocumentManager.MdiActiveDocument.Editor.WriteMessage($"\nAUDITVERSION {assembly.GetName().Version} SHA256={hash} PATH={assembly.Location}");
    }

    private static string AuditResourceCounts(Database db)
    {
        using var tr = db.TransactionManager.StartTransaction();
        int Count(ObjectId tableId) => ((SymbolTable)tr.GetObject(tableId, OpenMode.ForRead)).Cast<ObjectId>().Count(id => !id.IsErased);
        string counts = $"{Count(db.LayerTableId)}/{Count(db.LinetypeTableId)}/{Count(db.TextStyleTableId)}/"
            + ((DBDictionary)tr.GetObject(db.MaterialDictionaryId, OpenMode.ForRead)).Count;
        tr.Commit();
        return counts;
    }

    [CommandMethod("NLCSLEGENDAENTITYSTYLETEST", CommandFlags.Modal)]
    public void NlcsLegendaEntityStyleTest()
    {
        var doc = AcAp.DocumentManager.MdiActiveDocument;
        if (doc is null) return;
        var db = doc.Database;
        var ed = doc.Editor;
        const string layerName = "N-WE-VH-ENTITYSTYLE-G";
        DiagnosticRun.Execute(() =>
        {
            string path = Path.Join(Path.GetDirectoryName(db.Filename)!, "entity-style-source.dwg");
            using (var source = CreateXrefLineDatabase(path, layerName, 7, 7, out var lineId))
            {
                using (var tr = source.TransactionManager.StartTransaction())
                {
                    var line = (Line)tr.GetObject(lineId, OpenMode.ForWrite);
                    line.ColorIndex = 1;
                    var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(source), OpenMode.ForWrite);
                    using var green = new Line(new Point3d(0, 1, 0), new Point3d(3, 1, 0));
                    green.SetDatabaseDefaults(source);
                    green.LayerId = line.LayerId;
                    green.ColorIndex = 3;
                    ms.AppendEntity(green);
                    tr.AddNewlyCreatedDBObject(green, true);
                    tr.Commit();
                }
                source.SaveAs(path, DwgVersion.Current);
            }
            AttachAndInsert(db, path, "ENTITYSTYLE");
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var layers = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForWrite);
                var layer = new LayerTableRecord { Name = layerName, Color = Color.FromColorIndex(ColorMethod.ByAci, 7) };
                var layerId = layers.Add(layer);
                tr.AddNewlyCreatedDBObject(layer, true);
                var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForWrite);
                using var red = new Line(Point3d.Origin, new Point3d(5, 0, 0)) { LayerId = layerId, ColorIndex = 1 };
                ms.AppendEntity(red);
                tr.AddNewlyCreatedDBObject(red, true);
                tr.Commit();
            }
            var settings = new LegendSettings { IncludeXrefLayers = true, IncludeQuantities = true, ExplodeOnPlace = false };
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var result = DrawingAnalyzer.Analyze(db, tr, settings);
                var rows = result.Entries.Where(e => e.GeometryLayer is not null).ToList();
                bool redOk = rows.Count(e => EntryAci(result, e) == "1" && Math.Abs(e.Metric.Length - 12) < 1e-8) == 1;
                bool greenOk = rows.Count(e => EntryAci(result, e) == "3" && Math.Abs(e.Metric.Length - 3) < 1e-8) == 1;
                ed.WriteMessage($"\nENTITYSTYLE rows={rows.Count} red12={redOk} green3={greenOk} -> {(rows.Count == 2 && redOk && greenOk ? "OK" : "FAIL")}");
                tr.Commit();
            }
            int built = BuildPlaceXrefLegend(db, settings, out string error, out var group, out var block);
            ed.WriteMessage($"\nENTITYSTYLE placement rows={built} error={error} -> {(built == 2 && error == "geen" ? "OK" : "FAIL")}");
            if (built != 2 || error != "geen") throw new InvalidOperationException(error);
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var definition = (BlockTableRecord)tr.GetObject(block, OpenMode.ForRead);
                var swatches = definition.Cast<ObjectId>().Select(id => tr.GetObject(id, OpenMode.ForRead))
                    .OfType<Polyline>().Where(e => e.Layer.Contains("ENTITYSTYLE")).ToList();
                if (swatches.Count != 2 || !swatches.Select(e => e.ColorIndex).OrderBy(x => x).SequenceEqual(new[] { 1, 3 }))
                    throw new InvalidOperationException("Echte swatchkleuren zijn onjuist.");
                foreach (var id in swatches.Select(e => e.LayerId).Distinct())
                    ((LayerTableRecord)tr.GetObject(id, OpenMode.ForWrite)).Color = Color.FromColorIndex(ColorMethod.ByAci, 5);
                if (!swatches.Select(e => e.ColorIndex).OrderBy(x => x).SequenceEqual(new[] { 1, 3 }) || swatches.Any(e => e.Color.IsByLayer))
                    throw new InvalidOperationException("Gedeelde laagwijziging verandert de swatch.");
                tr.Commit();
            }
            ed.WriteMessage("\nENTITYSTYLE echte rode/groene swatches blijven gelijk na laagwijziging -> OK");
        }, message => ed.WriteMessage(message), "NlcsLegendaEntityStyleTest");
    }

    [CommandMethod("NLCSLEGENDAAUDITFIXTURE", CommandFlags.Modal)]
    public void NlcsLegendaAuditFixture()
    {
        var db = AcAp.DocumentManager.MdiActiveDocument.Database;
        using var tr = db.TransactionManager.StartTransaction();
        EnsureMTextFixtureLayers(db, tr);
        var layers = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForWrite);
        var layer = new LayerTableRecord { Name = "N-WE-VH-AUDITSYM-S" };
        var layerId = layers.Add(layer); tr.AddNewlyCreatedDBObject(layer, true);
        var blocks = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForWrite);
        var definition = new BlockTableRecord { Name = "AUDIT_SYMBOL" };
        var defId = blocks.Add(definition); tr.AddNewlyCreatedDBObject(definition, true);
        using var circle = new Circle(Point3d.Origin, Vector3d.ZAxis, 1);
        definition.AppendEntity(circle); tr.AddNewlyCreatedDBObject(circle, true);
        var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForWrite);
        using var reference = new BlockReference(new Point3d(80, 0, 0), defId) { LayerId = layerId };
        ms.AppendEntity(reference); tr.AddNewlyCreatedDBObject(reference, true);
        tr.Commit();
        AcAp.DocumentManager.MdiActiveDocument.Editor.WriteMessage("\nAUDITFIXTURE 3 lijnen en 1 symbool -> OK");
    }

    [CommandMethod("NLCSLEGENDADEPENDENCYTEST", CommandFlags.Modal)]
    public void NlcsLegendaDependencyTest()
    {
        var doc = AcAp.DocumentManager.MdiActiveDocument;
        if (doc is null) return;
        var db = doc.Database;
        var ed = doc.Editor;
        const string lineLayer = "N-WE-VH-DEPENDENCY-G";
        const string symbolLayer = "N-WE-VH-DEPENDENCY-S";
        DiagnosticRun.Execute(() =>
        {
            string path = Path.Join(Path.GetDirectoryName(db.Filename)!, "dependency-source.dwg");
            using (var source = CreateXrefLineDatabase(path, lineLayer, 1, 5, out _, symbolLayerName: symbolLayer, symbolBlockName: "DEPENDENCY_SYMBOL"))
            {
                using (var tr = source.TransactionManager.StartTransaction())
                {
                    var linetypes = (LinetypeTable)tr.GetObject(source.LinetypeTableId, OpenMode.ForWrite);
                    var lt = new LinetypeTableRecord { Name = "AUDIT_DASH", PatternLength = 1, NumDashes = 2 };
                    lt.SetDashLengthAt(0, 0.6);
                    lt.SetDashLengthAt(1, -0.4);
                    var ltId = linetypes.Add(lt);
                    tr.AddNewlyCreatedDBObject(lt, true);
                    var sourceLayers = (LayerTable)tr.GetObject(source.LayerTableId, OpenMode.ForWrite);
                    var detailLayer = new LayerTableRecord { Name = "AUDIT_DETAIL", Color = Color.FromColorIndex(ColorMethod.ByAci, 3) };
                    var detailLayerId = sourceLayers.Add(detailLayer);
                    tr.AddNewlyCreatedDBObject(detailLayer, true);
                    var materials = (DBDictionary)tr.GetObject(source.MaterialDictionaryId, OpenMode.ForWrite);
                    using var material = new Material { Name = "AUDIT_MATERIAL" };
                    var materialId = materials.SetAt(material.Name, material);
                    tr.AddNewlyCreatedDBObject(material, true);
                    var styles = (TextStyleTable)tr.GetObject(source.TextStyleTableId, OpenMode.ForWrite);
                    var textStyle = new TextStyleTableRecord { Name = "AUDIT_TEXT", FileName = "txt.shx", XScale = 0.75 };
                    var textStyleId = styles.Add(textStyle);
                    tr.AddNewlyCreatedDBObject(textStyle, true);
                    var blocks = (BlockTable)tr.GetObject(source.BlockTableId, OpenMode.ForRead);
                    var block = (BlockTableRecord)tr.GetObject(blocks["DEPENDENCY_SYMBOL"], OpenMode.ForRead);
                    foreach (ObjectId id in block)
                    {
                        var entity = (Entity)tr.GetObject(id, OpenMode.ForWrite);
                        entity.LinetypeId = ltId;
                        entity.MaterialId = materialId;
                        if (entity is Circle) { entity.LayerId = detailLayerId; entity.ColorIndex = 256; }
                        if (entity is DBText text) text.TextStyleId = textStyleId;
                        entity.CreateExtensionDictionary();
                        var dictionary = (DBDictionary)tr.GetObject(entity.ExtensionDictionary, OpenMode.ForWrite);
                        using var record = new Xrecord();
                        dictionary.SetAt("AUDIT", record);
                        tr.AddNewlyCreatedDBObject(record, true);
                        using var data = new ResultBuffer(new TypedValue((int)DxfCode.Text, "dependency fixture"));
                        record.Data = data;
                    }
                    tr.Commit();
                }
                source.SaveAs(path, DwgVersion.Current);
            }
            AttachAndInsert(db, path, "DEPENDENCY");
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var layers = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
                ((LayerTableRecord)tr.GetObject(layers["DEPENDENCY|AUDIT_DETAIL"], OpenMode.ForWrite)).Color = Color.FromColorIndex(ColorMethod.ByAci, 5);
                tr.Commit();
            }
            var settings = new LegendSettings { IncludeXrefLayers = true, ExplodeOnPlace = false };
            int built = BuildPlaceXrefLegend(db, settings, out string error, out var group, out var blockId);
            if (error != "geen") throw new InvalidOperationException(error);
            using (var tr = db.TransactionManager.StartTransaction())
            {
                AssertLegendHostOwnership(db, tr, group, blockId);
                var block = (BlockTableRecord)tr.GetObject(blockId, OpenMode.ForRead);
                int count = 0;
                foreach (ObjectId id in block)
                {
                    var entity = (Entity)tr.GetObject(id, OpenMode.ForRead);
                    foreach (var dependency in new[] { entity.MaterialId, entity.ExtensionDictionary }.Where(id => !id.IsNull))
                        RequireHostObjectId(db, dependency, entity.GetType().Name + " dependency");
                    foreach (ObjectId reactor in entity.GetPersistentReactorIds())
                        RequireHostObjectId(db, reactor, "reactor");
                    if (entity is Circle && entity.ColorIndex != 5)
                        throw new InvalidOperationException("Hostoverride van interne symboollaag verloren.");
                    count++;
                }
                ed.WriteMessage($"\nDEPENDENCY rows={built} entities={count} -> OK");
                tr.Commit();
            }
            DiagnosticRun.Execute(() =>
            {
                db.SaveAs(Path.Join(Path.GetDirectoryName(db.Filename)!, "programmatic-save.dwg"), DwgVersion.Current);
                ed.WriteMessage("\nDEPENDENCY SaveAs -> OK");
            }, message => ed.WriteMessage(message), "DEPENDENCY SaveAs");
        }, message => ed.WriteMessage(message), "NlcsLegendaDependencyTest");
    }
    [CommandMethod("NLCSLEGENDASTABILITYTEST", CommandFlags.Modal)]
    public void NlcsLegendaStabilityTest()
    {
        var doc = AcAp.DocumentManager.MdiActiveDocument;
        if (doc is null) return;
        var db = doc.Database;
        var ed = doc.Editor;
        DiagnosticRun.Execute(() =>
        {
            string Snapshot()
            {
                using var tr = db.TransactionManager.StartTransaction();
                var registry = LegendStore.Load(db, tr);
                if (registry.Legends.Count == 0) throw new InvalidOperationException("Geen testlegenda aanwezig.");
                var excluded = LegendManagement.CollectManagedIds(db, tr, registry);
                var parts = new List<string>();
                foreach (var definition in registry.Legends)
                {
                    var selection = definition.Scope == LegendScope.Selection
                        ? LegendManagement.ResolveHandles(db, definition.SourceHandles, out _) : null;
                    var analysis = DrawingAnalyzer.Analyze(db, tr, definition.Settings, selection,
                        DescriptionCatalog.Default(), excluded);
                    parts.Add(definition.Id + "|" + definition.GroupName + "|" + definition.Settings.ToJson());
                    foreach (var row in analysis.Entries)
                        parts.Add($"{row.Element}|{row.RenderIdentity}|{row.Description}|{row.Metric.Count}|{row.Metric.Length:R}|{row.Metric.Area:R}");
                    var groups = (DBDictionary)tr.GetObject(db.GroupDictionaryId, OpenMode.ForRead);
                    var group = (Group)tr.GetObject(groups.GetAt(definition.GroupName), OpenMode.ForRead);
                    foreach (var reference in group.GetAllEntityIds().Select(id => tr.GetObject(id, OpenMode.ForRead)).OfType<BlockReference>())
                    {
                        var ownership = AssertLegendHostOwnership(db, tr, definition.GroupName, reference.BlockTableRecord);
                        if (ownership.DbTexts != 0) throw new InvalidOperationException("DBText in legenda.");
                    }
                }
                tr.Commit();
                return string.Join("\n", parts);
            }
            string baseline = Snapshot();
            string? resourceBaseline = null;
            for (int i = 1; i <= 50; i++)
            {
                using (var tr = db.TransactionManager.StartTransaction())
                {
                    var registry = LegendStore.Load(db, tr);
                    foreach (var definition in registry.Legends)
                    {
                        var result = BuildManagedLegend(db, tr, registry, definition, out _, out var note);
                        if (result != UpdateResult.Updated || note.Length > 0)
                            throw new InvalidOperationException($"Bijwerken mislukt: {result}; {note}");
                    }
                    LegendStore.Save(db, tr, registry);
                    tr.Commit();
                }
                PurgePending(db);
                string resources = AuditResourceCounts(db);
                if (resourceBaseline is not null && resourceBaseline != resources)
                    throw new InvalidOperationException($"Resourcegroei bij update {i}: {resourceBaseline} -> {resources}.");
                resourceBaseline ??= resources;
                if (Snapshot() != baseline) throw new InvalidOperationException($"Brongegevens gewijzigd bij update {i}.");
                if (i == 10 || i == 50)
                {
                    ed.Command("_.QSAVE");
                    ed.WriteMessage($"\nSTABILITY updates={i} quantities/rows/settings/identity/ownership/resources={resources} -> OK");
                }
            }
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var blocks = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                int orphan = blocks.Cast<ObjectId>().Select(id => tr.GetObject(id, OpenMode.ForRead))
                    .OfType<BlockTableRecord>().Count(block => !block.IsLayout && !block.IsDependent
                        && (block.Name.StartsWith("NLCS_LEGENDA_", StringComparison.Ordinal)
                            || block.Name.StartsWith("NLCS_SRC_", StringComparison.Ordinal))
                        && block.GetBlockReferenceIds(true, false).Count == 0);
                if (orphan != 0) throw new InvalidOperationException($"{orphan} verweesde legenda-/symboolblokken.");
                ed.WriteMessage("\nSTABILITY orphan blocks=0 -> OK");
                tr.Commit();
            }
        }, message => ed.WriteMessage(message), "NlcsLegendaStabilityTest");
    }
}
