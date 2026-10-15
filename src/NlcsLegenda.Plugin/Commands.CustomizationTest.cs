using System.IO;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using NlcsLegenda.Core;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Core.Application;

namespace NlcsLegenda.Plugin;

public partial class Commands
{
    [CommandMethod("NLCSLEGENDACUSTOMIZATIONTEST", CommandFlags.Modal)]
    public void NlcsLegendaCustomizationTest()
    {
        var doc = AcApp.DocumentManager.MdiActiveDocument;
        if (doc is null) return;
        try
        {
            var db = doc.Database;
            using (var tr = db.TransactionManager.StartTransaction())
            {
                const string hatchLayer = "N-WE-VH-CUSTOMIZATION-A";
                const string symbolLayer = "N-WE-IE-CUSTOMIZATION-S";
                var model = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForWrite);
                ReviewLayer(db, tr, hatchLayer);
                ReviewLayer(db, tr, symbolLayer);
                using var hatch = new Hatch { Layer = hatchLayer };
                model.AppendEntity(hatch); tr.AddNewlyCreatedDBObject(hatch, true);
                hatch.SetHatchPattern(HatchPatternType.PreDefined, "SOLID");
                var points = new Point2dCollection(new[] { new Point2d(0, 0), new Point2d(4, 0), new Point2d(4, 2), new Point2d(0, 2) });
                hatch.AppendLoop(HatchLoopTypes.External | HatchLoopTypes.Polyline, points, new DoubleCollection(new[] { 0.0, 0.0, 0.0, 0.0 }));
                hatch.EvaluateHatch(true);
                var blocks = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForWrite);
                ObjectId sourceBlock = ObjectId.Null;
                foreach (var (name, radius) in new[] { ("CUSTOMIZATION_SOURCE", 2.0), ("CUSTOMIZATION_MANUAL", 5.0) })
                {
                    using var block = new BlockTableRecord { Name = name };
                    var id = blocks.Add(block); tr.AddNewlyCreatedDBObject(block, true);
                    using var circle = new Circle(Point3d.Origin, Vector3d.ZAxis, radius);
                    block.AppendEntity(circle); tr.AddNewlyCreatedDBObject(circle, true);
                    if (name == "CUSTOMIZATION_SOURCE") sourceBlock = id;
                }
                using var insert = new BlockReference(new Point3d(20, 0, 0), sourceBlock) { Layer = symbolLayer, ScaleFactors = new Scale3d(3) };
                model.AppendEntity(insert); tr.AddNewlyCreatedDBObject(insert, true);
                var settings = new LegendSettings { Scale = 200, IncludeRemarks = false, IncludeScaleBar = false,
                    ManualEntries = new() {
                        new() { Layer = "EIGEN-ARCERING", Type = NlcsDrawType.Arcering, Description = "Eigen vulling", HatchPattern = "SOLID" },
                        new() { Layer = "EIGEN-ARCERING", Type = NlcsDrawType.Arcering, Description = "Eigen arcering", HatchPattern = "ANSI31" },
                        new() { Layer = hatchLayer, Type = NlcsDrawType.Arcering, Description = "Eigen bronlaag", HatchPattern = "ANSI31" },
                        new() { Layer = symbolLayer, Type = NlcsDrawType.Symbool, Description = "Eigen symbool", SymbolBlock = "CUSTOMIZATION_MANUAL" }
                    } };
                var registry = LegendStore.Load(db, tr);
                var definition = IsoDef(registry, settings); definition.Name = "Eigen voorbeelden"; registry.Add(definition);
                if (BuildManagedLegend(db, tr, registry, definition, out var rows, out _) != UpdateResult.Updated || rows != 6)
                    throw new InvalidOperationException("Zes bron- en eigen regels ontbreken.");
                LegendStore.Save(db, tr, registry);
                tr.Commit();
            }
            VerifyCustomization(doc.Database, false);
            doc.Editor.WriteMessage("\nCUSTOMIZATION eigen arceringen en symbolen onafhankelijk van dezelfde bronlaag -> OK");
        }
        catch (System.Exception ex) when (ex is Autodesk.AutoCAD.Runtime.Exception or InvalidOperationException or ArgumentException)
        { doc.Editor.WriteMessage($"\nCUSTOMIZATION FAIL: {ex}"); }
    }

    [CommandMethod("NLCSLEGENDACUSTOMIZATIONVERIFY", CommandFlags.Modal)]
    public void NlcsLegendaCustomizationVerify()
    {
        var doc = AcApp.DocumentManager.MdiActiveDocument;
        if (doc is null) return;
        try
        {
            VerifyCustomization(doc.Database, true);
            doc.Editor.WriteMessage("\nCUSTOMIZATION na heropenen en bijwerken: eigen voorbeelden behouden -> OK");
        }
        catch (System.Exception ex) when (ex is Autodesk.AutoCAD.Runtime.Exception or InvalidOperationException or ArgumentException)
        { doc.Editor.WriteMessage($"\nCUSTOMIZATION VERIFY FAIL: {ex}"); }
    }

    private static void VerifyCustomization(Database db, bool update)
    {
        using var tr = db.TransactionManager.StartTransaction();
        var registry = LegendStore.Load(db, tr);
        var definition = registry.Legends.Single(l => l.Name == "Eigen voorbeelden");
        if (update && BuildManagedLegend(db, tr, registry, definition, out _, out _) != UpdateResult.Updated)
            throw new InvalidOperationException("Bijwerken eigen voorbeelden mislukt.");
        var dictionary = (DBDictionary)tr.GetObject(db.GroupDictionaryId, OpenMode.ForRead);
        var group = (Group)tr.GetObject(dictionary.GetAt(definition.GroupName), OpenMode.ForRead);
        var members = group.GetAllEntityIds().Where(id => !id.IsErased).Select(id => (Entity)tr.GetObject(id, OpenMode.ForRead));
        var entities = members.SelectMany(entity => entity is BlockReference reference
            ? ((BlockTableRecord)tr.GetObject(reference.BlockTableRecord, OpenMode.ForRead)).Cast<ObjectId>().Select(id => (Entity)tr.GetObject(id, OpenMode.ForRead))
            : new[] { entity }).Where(e => !e.IsErased).ToArray();
        var hatches = entities.OfType<Hatch>().ToArray();
        var radii = entities.OfType<Circle>().Select(c => c.Radius).Order().ToArray();
        var errors = new List<string>();
        if (hatches.Length != 4 || hatches.Count(h => h.PatternName == "ANSI31") != 2 || hatches.Count(h => h.PatternName == "SOLID") != 2)
            errors.Add("Eigen arceringspatronen: " + string.Join(", ", hatches.Select(h => h.PatternName)));
        if (radii.Length != 2 || Math.Abs(radii[0] - 1) > 1e-8 || Math.Abs(radii[1] - 6) > 1e-8)
            errors.Add("Eigen symboolmaat: " + string.Join(", ", radii));
        var tagged = entities.Where(e => LegendManagement.ReadRow(e) is not null).ToArray();
        if (tagged.OfType<MText>().Count() != 2 || tagged.Length < 4)
            errors.Add("Tekstkeuze verliest de koppeling met de bronregel.");
        foreach (var row in tagged.Select(e => LegendManagement.ReadRow(e)!.Value))
        {
            if (row.Element != "CUSTOMIZATION" || row.Hoofdgroep is not ("VH" or "IE"))
                errors.Add("Tekstkeuze verwijst naar een verkeerde bronregel.");
        }
        if (update && !definition.Settings.ExplodeOnPlace)
        {
            definition.Settings.ExplodeOnPlace = true;
            if (BuildManagedLegend(db, tr, registry, definition, out _, out _) != UpdateResult.Updated)
                errors.Add("Opbouw met losse onderdelen mislukt.");
            var updatedGroup = (Group)tr.GetObject(dictionary.GetAt(definition.GroupName), OpenMode.ForRead);
            var updated = updatedGroup.GetAllEntityIds().Where(id => !id.IsErased).Select(id => (Entity)tr.GetObject(id, OpenMode.ForRead)).ToArray();
            if (updated.Any(e => e is BlockReference) || updated.Count(e => LegendManagement.ReadRow(e) is not null) != tagged.Length)
                errors.Add("Tekstkoppeling na exploderen verloren.");
        }
        if (errors.Count > 0) throw new InvalidOperationException(string.Join("; ", errors));
        LegendStore.Save(db, tr, registry);
        tr.Commit();
    }
}
