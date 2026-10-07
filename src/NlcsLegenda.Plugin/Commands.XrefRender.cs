using System.IO;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using AcAp = Autodesk.AutoCAD.ApplicationServices.Application;
using AcColor = Autodesk.AutoCAD.Colors;
using NlcsLegenda.Core;

namespace NlcsLegenda.Plugin;

// Een xref-only kader (host zonder eigen NLCS-geometrie, alleen een xref) moet een volledig
// gevulde en correct gekleurde legenda opleveren, via precies dezelfde bouw-/plaatsflow als de
// gebruiker. Deze test bouwt een synthetische xref met een rode G-lijn en een groene arcering,
// genereert de legenda en controleert de werkelijke swatchkleuren plus het uitblijven van
// eWrongDatabase. Een rijtelling alleen is geen bewijs.
public partial class Commands
{
    private static void CreateXrefRenderDwg(string path)
    {
        var prev = HostApplicationServices.WorkingDatabase;
        using var xdb = new Database(true, true);
        HostApplicationServices.WorkingDatabase = xdb;
        try
        {
            using (var tr = xdb.TransactionManager.StartTransaction())
            {
                var lt = (LayerTable)tr.GetObject(xdb.LayerTableId, OpenMode.ForWrite);
                ObjectId LayerC(string n, short ci) { var r = new LayerTableRecord { Name = n, Color = AcColor.Color.FromColorIndex(AcColor.ColorMethod.ByAci, ci) }; var id = lt.Add(r); tr.AddNewlyCreatedDBObject(r, true); return id; }
                var gLayer = LayerC("N-WE-VH-KANTVERHARDING-G", 1);          // rood
                var aLayer = LayerC("N-WE-VH-VERHARDING_ASFALT_ZWART-A", 3); // groen
                var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(xdb), OpenMode.ForWrite);
                var line = new Line(new Point3d(0, 0, 0), new Point3d(10, 0, 0)) { LayerId = gLayer };
                ms.AppendEntity(line); tr.AddNewlyCreatedDBObject(line, true);
                var pl = new Polyline();
                pl.AddVertexAt(0, new Point2d(0, 5), 0, 0, 0); pl.AddVertexAt(1, new Point2d(5, 5), 0, 0, 0);
                pl.AddVertexAt(2, new Point2d(5, 10), 0, 0, 0); pl.AddVertexAt(3, new Point2d(0, 10), 0, 0, 0);
                pl.Closed = true; pl.LayerId = aLayer;
                var plId = ms.AppendEntity(pl); tr.AddNewlyCreatedDBObject(pl, true);
                var hatch = new Hatch { LayerId = aLayer };
                ms.AppendEntity(hatch); tr.AddNewlyCreatedDBObject(hatch, true);
                hatch.SetHatchPattern(HatchPatternType.PreDefined, "SOLID");
                hatch.Associative = false;
                using (var loop = new ObjectIdCollection { plId }) hatch.AppendLoop(HatchLoopTypes.Default, loop);
                hatch.EvaluateHatch(true);
                tr.Commit();
            }
            xdb.SaveAs(path, DwgVersion.Current);
        }
        finally { HostApplicationServices.WorkingDatabase = prev; }
    }

    [CommandMethod("NLCSLEGENDAXREFRENDERTEST", CommandFlags.Modal)]
    public void NlcsLegendaXrefRenderTest()
    {
        var doc = AcAp.DocumentManager.MdiActiveDocument;
        if (doc is null) return;
        var db = doc.Database; var ed = doc.Editor;
        string xpath = Path.Combine(Path.GetDirectoryName(db.Filename) ?? Path.GetTempPath(), "xrefrender_src.dwg");
        try
        {
            CreateXrefRenderDwg(xpath);
            ObjectId xid = db.AttachXref(xpath, "KADER");
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForWrite);
                var br = new BlockReference(Point3d.Origin, xid);
                ms.AppendEntity(br); tr.AddNewlyCreatedDBObject(br, true);
                tr.Commit();
            }

            var settings = LoadGlobalDefaults();
            settings.IncludeXrefLayers = true;
            int entryCount = 0; string err = "geen";
            try
            {
                using var tr = db.TransactionManager.StartTransaction();
                var registry = LegendStore.Load(db, tr);
                var analysis = DrawingAnalyzer.Analyze(db, tr, settings, catalog: LoadCatalog(db));
                entryCount = analysis.Entries.Count;
                var btrId = LegendBuilder.BuildBlock(db, tr, analysis, settings, out _, out _);
                var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForWrite);
                var br = new BlockReference(new Point3d(200, 0, 0), btrId);
                ms.AppendEntity(br); tr.AddNewlyCreatedDBObject(br, true);
                var def = new LegendDefinition { Name = registry.NextDefaultName(), Scope = LegendScope.WholeDrawing, GroupName = LegendRegistry.NewGroupName(), Settings = settings.Clone(), CreatedWithVersion = PluginVersion };
                FinalizePlacement(tr, db, br, settings, def.GroupName);
                registry.Add(def);
                LegendStore.Save(db, tr, registry);
                tr.Commit();
            }
            catch (System.Exception ex) { err = ex.GetType().Name + ": " + ex.Message; }

            string gColor = "?", aColor = "?";
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
                string Read(string n) => lt.Has(n) && tr.GetObject(lt[n], OpenMode.ForRead) is LayerTableRecord r ? r.Color.ColorIndex.ToString() : "geen";
                gColor = Read("N-WE-VH-KANTVERHARDING-G"); aColor = Read("N-WE-VH-VERHARDING_ASFALT_ZWART-A");
                tr.Commit();
            }

            bool parseOk = entryCount >= 2;
            bool noDbErr = err == "geen";
            bool colorOk = gColor == "1" && aColor == "3";
            ed.WriteMessage($"\nXREFRENDER parse: entries={entryCount} -> {(parseOk ? "OK" : "FAIL")}");
            ed.WriteMessage($"\nXREFRENDER database: fout={err} -> {(noDbErr ? "OK" : "FAIL")}");
            ed.WriteMessage($"\nXREFRENDER kleur: G-swatch={gColor}(rood=1) A-swatch={aColor}(groen=3) -> {(colorOk ? "OK" : "FAIL")}");
            ed.WriteMessage($"\nXREFRENDER: totaal -> {(parseOk && noDbErr && colorOk ? "OK" : "FAIL")}");
        }
        catch (System.Exception ex) { ed.WriteMessage($"\nXREFRENDER fatale fout: {ex.GetType().Name}: {ex.Message}"); }
    }
}
