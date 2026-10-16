using System.IO;
using System.Security.Cryptography;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using NlcsLegenda.Core;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Core.Application;

namespace NlcsLegenda.Plugin;

public partial class Commands
{
    [CommandMethod("NLCSLEGENDAAPPEARANCETEST", CommandFlags.Modal)]
    public void AppearanceTest()
    {
        var doc = AcApp.DocumentManager.MdiActiveDocument;
        try
        {
            string path = Environment.GetEnvironmentVariable("NLCS_TEMPLATE_REFERENCE") ?? string.Empty;
            if (!File.Exists(path)) throw new InvalidOperationException("Referentietemplate ontbreekt.");
            string before = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
            string fixture = Path.Join(Path.GetDirectoryName(doc.Database.Filename)!, "appearance-source.dwg");
            using (var source = new Database(false, true))
            using (var copy = new Database(true, true))
            {
                source.ReadDwgFile(path, FileOpenMode.OpenForReadAndAllShare, true, string.Empty);
                source.CloseInput(true);
                using var sourceTr = source.TransactionManager.StartTransaction();
                var model = (BlockTableRecord)sourceTr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(source), OpenMode.ForRead);
                var objects = model.Cast<ObjectId>().Select(id => sourceTr.GetObject(id, OpenMode.ForRead)).OfType<Entity>().ToArray();
                var pattern = objects.OfType<Hatch>().First(h => !h.IsSolidFill && h.NumberOfPatternDefinitions > 1);
                var solid = objects.OfType<Hatch>().First(h => h.IsSolidFill && h.PatternName.StartsWith('_'));
                var label = objects.OfType<BlockReference>().First(b => b.AttributeCollection.Cast<ObjectId>()
                    .Any(id => sourceTr.GetObject(id, OpenMode.ForRead) is AttributeReference { TextString: "A01" }));
                using var ids = new ObjectIdCollection { pattern.ObjectId, solid.ObjectId, label.ObjectId };
                using var mapping = new IdMapping();
                source.WblockCloneObjects(ids, SymbolUtilityServices.GetBlockModelSpaceId(copy), mapping, DuplicateRecordCloning.Ignore, false);
                using (var tr = copy.TransactionManager.StartTransaction())
                {
                    foreach (var (id, name) in new[] { (pattern.ObjectId, "N-WE-VH-APPEARANCE_PATTERN-A"),
                        (solid.ObjectId, "N-WE-VH-APPEARANCE_SOLID-A"), (label.ObjectId, "N-WE-VH-APPEARANCE_LABEL-S") })
                    {
                        var entity = (Entity)tr.GetObject(mapping[id].Value, OpenMode.ForWrite);
                        var old = (LayerTableRecord)tr.GetObject(entity.LayerId, OpenMode.ForRead);
                        var layer = ReviewLayer(copy, tr, name);
                        var target = (LayerTableRecord)tr.GetObject(layer, OpenMode.ForWrite);
                        target.Color = old.Color; target.LineWeight = old.LineWeight;
                        entity.LayerId = layer;
                    }
                    using var labelIds = new ObjectIdCollection { mapping[label.ObjectId].Value };
                    using var duplicate = new IdMapping();
                    copy.DeepCloneObjects(labelIds, SymbolUtilityServices.GetBlockModelSpaceId(copy), duplicate, false);
                    var second = (BlockReference)tr.GetObject(duplicate[labelIds[0]].Value, OpenMode.ForWrite);
                    foreach (ObjectId id in second.AttributeCollection)
                    {
                        var attribute = (AttributeReference)tr.GetObject(id, OpenMode.ForWrite);
                        if (attribute.TextString == "A01") { attribute.TextString = "A02"; attribute.AdjustAlignment(copy); }
                    }
                    tr.Commit();
                }
                copy.SaveAs(fixture, DwgVersion.Current);
                using var all = new ObjectIdCollection();
                using (var tr = copy.TransactionManager.StartTransaction())
                    foreach (ObjectId id in (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(copy), OpenMode.ForRead)) all.Add(id);
                using var local = new IdMapping();
                copy.WblockCloneObjects(all, SymbolUtilityServices.GetBlockModelSpaceId(doc.Database), local, DuplicateRecordCloning.Ignore, false);
            }
            AddKlicXref(doc.Database, fixture, "APPEARANCE", 2);
            CheckAppearance(doc.Database);
            var settings = new LegendSettings { Scale = 200, IncludeXrefLayers = true };
            if (BuildPlaceXrefLegend(doc.Database, settings, out var error, out _, out _) < 4 || error != "geen")
                throw new InvalidOperationException("Plaatsing mislukt: " + error);
            if (before != Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))))
                throw new InvalidOperationException("Referentie gewijzigd.");
            doc.Editor.WriteMessage("\nAPPEARANCE ingesloten patronen, gesloten vulling, A01/A02 en tekenvolgorde lokaal/xref -> OK");
        }
        catch (InvalidOperationException ex) { doc.Editor.WriteMessage("\nAPPEARANCE FAIL: " + ex); }
        catch (IOException ex) { doc.Editor.WriteMessage("\nAPPEARANCE FAIL: " + ex); }
        catch (Autodesk.AutoCAD.Runtime.Exception ex) { doc.Editor.WriteMessage("\nAPPEARANCE FAIL: " + ex); }
    }

    [CommandMethod("NLCSLEGENDAAPPEARANCEVERIFY", CommandFlags.Modal)]
    public void AppearanceVerify()
    {
        var doc = AcApp.DocumentManager.MdiActiveDocument;
        try { CheckAppearance(doc.Database); doc.Editor.WriteMessage("\nAPPEARANCE na heropenen -> OK"); }
        catch (InvalidOperationException ex) { doc.Editor.WriteMessage("\nAPPEARANCE FAIL: " + ex); }
        catch (IOException ex) { doc.Editor.WriteMessage("\nAPPEARANCE FAIL: " + ex); }
        catch (Autodesk.AutoCAD.Runtime.Exception ex) { doc.Editor.WriteMessage("\nAPPEARANCE FAIL: " + ex); }
    }

    private static void CheckAppearance(Database db)
    {
        using var tr = db.TransactionManager.StartTransaction();
        var settings = new LegendSettings { Scale = 200, IncludeXrefLayers = true };
        var excluded = LegendManagement.CollectManagedIds(db, tr, LegendStore.Load(db, tr));
        var analysis = DrawingAnalyzer.Analyze(db, tr, settings, excludedIds: excluded);
        if (analysis.SymbolSamples.Values.Select(s => s.AttributeIdentity).Distinct().Count() != 2
            || analysis.Entries.Count(e => e.SymbolLayer is not null) != 4)
            throw new InvalidOperationException($"Attribuutwaarden of xref-schaal niet onderscheiden: {analysis.SymbolSamples.Count} voorbeelden, {analysis.SymbolSamples.Values.Select(s => s.AttributeIdentity).Distinct().Count()} waarden, {analysis.Entries.Count(e => e.SymbolLayer is not null)} regels.");
        var blockId = LegendBuilder.BuildBlock(db, tr, analysis, settings, out _, out var issues);
        if (issues.Count != 0) throw new InvalidOperationException(issues[0].Reason);
        var block = (BlockTableRecord)tr.GetObject(blockId, OpenMode.ForRead);
        var entities = block.Cast<ObjectId>().Select(id => tr.GetObject(id, OpenMode.ForRead)).OfType<Entity>().ToArray();
        var labels = entities.OfType<MText>().Where(t => t.Text is "A01" or "A02").ToArray();
        if (labels.Count(t => t.Text == "A01") != 2 || labels.Count(t => t.Text == "A02") != 2
            || entities.OfType<MText>().Any(t => t.Text == "V0X"))
            throw new InvalidOperationException("Ingevulde bloklabels ontbreken.");
        var expected = analysis.Entries.Where(e => e.HatchLayer is not null).Select(e => analysis.HatchSamples[e.HatchLayer!])
            .Where(h => !h.IsSolid).Select(h => h.DefinitionSignature(h.ScaleForLegend(settings.Scale))).Order().ToArray();
        var hatches = entities.OfType<Hatch>().Where(h => !h.IsSolidFill).ToArray();
        var actual = hatches.Select(h => HatchSample.From(h).DefinitionSignature(1)).Order().ToArray();
        if (!actual.SequenceEqual(expected) || actual.Length != 2) throw new InvalidOperationException("Ingesloten patroon gewijzigd.");
        var loops = hatches.Concat(entities.OfType<Hatch>().Where(h => h.Layer.Contains("APPEARANCE_SOLID", StringComparison.Ordinal)))
            .Select(h => h.GetLoopAt(0));
        foreach (var loop in loops)
        {
            if (!loop.IsPolyline || loop.Polyline.Count != 5 || (loop.Polyline[0].Vertex - loop.Polyline[4].Vertex).Length > 1e-9)
                throw new InvalidOperationException("Arceringsrand niet gesloten.");
        }
        var order = ((DrawOrderTable)tr.GetObject(block.DrawOrderTableId, OpenMode.ForRead)).GetFullDrawOrder(0).Cast<ObjectId>().ToArray();
        foreach (var label in labels)
        {
            var mask = entities.OfType<Hatch>().Where(h => h.IsSolidFill && h.Color.ColorValue.R == 255 && h.Color.ColorValue.G == 255)
                .OrderBy(h => (h.GeometricExtents.MinPoint + (h.GeometricExtents.MaxPoint - h.GeometricExtents.MinPoint) / 2).DistanceTo(label.Location)).First();
            if (Array.IndexOf(order, label.ObjectId) <= Array.IndexOf(order, mask.ObjectId))
                throw new InvalidOperationException("Vulling bedekt het bloklabel.");
        }
        if (entities.OfType<Polyline>().Any(p => p.Color.ColorIndex == 40 && ((LayerTableRecord)tr.GetObject(p.LayerId, OpenMode.ForRead)).IsPlottable))
            throw new InvalidOperationException("Hulplijn is plotbaar geworden.");
        tr.Abort();
    }
}
