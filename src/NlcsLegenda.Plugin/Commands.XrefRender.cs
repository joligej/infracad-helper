using System.Collections.Generic;
using System.IO;
using System.Linq;
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
    // Vaste laagnamen voor de synthetische xref-inhoud: een rode G-lijn en een groene A-arcering.
    private const string XrefGLayer = "N-WE-VH-KANTVERHARDING-G";          // rood (ACI 1)
    private const string XrefALayer = "N-WE-VH-VERHARDING_ASFALT_ZWART-A"; // groen (ACI 3)

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
                var gLayer = LayerC(XrefGLayer, 1);          // rood
                var aLayer = LayerC(XrefALayer, 3); // groen
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

    // Maakt een geneste xref-keten: een binnenbestand met de NLCS-inhoud en een buitenbestand dat
    // het binnenbestand als xref bevat. Een host die het buitenbestand koppelt ziet de NLCS-lagen
    // met een geneste prefix (OUTER|INNER|...), precies het scenario dat we moeten kunnen renderen.
    private static void CreateNestedXrefDwgs(string innerPath, string outerPath)
    {
        CreateXrefRenderDwg(innerPath);
        var prev = HostApplicationServices.WorkingDatabase;
        using var odb = new Database(true, true);
        HostApplicationServices.WorkingDatabase = odb;
        try
        {
            ObjectId iid = odb.AttachXref(innerPath, "INNER");
            using (var tr = odb.TransactionManager.StartTransaction())
            {
                var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(odb), OpenMode.ForWrite);
                var br = new BlockReference(Point3d.Origin, iid);
                ms.AppendEntity(br); tr.AddNewlyCreatedDBObject(br, true);
                tr.Commit();
            }
            odb.SaveAs(outerPath, DwgVersion.Current);
        }
        finally { HostApplicationServices.WorkingDatabase = prev; }
    }

    private static void AttachAndInsert(Database db, string path, string name)
    {
        ObjectId xid = db.AttachXref(path, name);
        using var tr = db.TransactionManager.StartTransaction();
        var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForWrite);
        var br = new BlockReference(Point3d.Origin, xid);
        ms.AppendEntity(br); tr.AddNewlyCreatedDBObject(br, true);
        tr.Commit();
    }

    // Exact dezelfde bouw-/plaatsflow als een normale gebruiker (analyse -> block -> insert ->
    // group -> registry -> commit). Vangt databasefouten op zodat eWrongDatabase zichtbaar wordt.
    private static int BuildPlaceXrefLegend(Database db, LegendSettings settings, out string err)
    {
        int entryCount = 0; err = "geen";
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
        return entryCount;
    }

    private static string ReadAci(Database db, string layerName)
    {
        using var tr = db.TransactionManager.StartTransaction();
        var lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
        string res = lt.Has(layerName) && tr.GetObject(lt[layerName], OpenMode.ForRead) is LayerTableRecord r
            ? r.Color.ColorIndex.ToString() : "geen";
        tr.Commit();
        return res;
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
            AttachAndInsert(db, xpath, "KADER");

            var settings = LoadGlobalDefaults();
            settings.IncludeXrefLayers = true;
            int entryCount = BuildPlaceXrefLegend(db, settings, out string err);
            string gColor = ReadAci(db, XrefGLayer), aColor = ReadAci(db, XrefALayer);

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

    // Geneste xref: een host die alleen een buiten-xref koppelt die zelf een binnen-xref met de
    // NLCS-inhoud bevat. Controleert de werkelijke geneste laagnaam (meerdere | -segmenten), dat de
    // NLCS-parsing door de nesting heen werkt, dat er geen eWrongDatabase optreedt en dat de
    // swatchkleuren uit de diepste bron correct worden overgenomen.
    [CommandMethod("NLCSLEGENDAXREFNESTEDTEST", CommandFlags.Modal)]
    public void NlcsLegendaXrefNestedTest()
    {
        var doc = AcAp.DocumentManager.MdiActiveDocument;
        if (doc is null) return;
        var db = doc.Database; var ed = doc.Editor;
        string dir = Path.GetDirectoryName(db.Filename) ?? Path.GetTempPath();
        string inner = Path.Combine(dir, "xref_inner.dwg");
        string outer = Path.Combine(dir, "xref_outer.dwg");
        try
        {
            CreateNestedXrefDwgs(inner, outer);
            AttachAndInsert(db, outer, "OUTER");

            string rawNested = "geen";
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
                foreach (ObjectId lid in lt)
                    if (tr.GetObject(lid, OpenMode.ForRead) is LayerTableRecord r
                        && r.Name.Contains('|')
                        && r.Name.EndsWith(XrefGLayer, System.StringComparison.OrdinalIgnoreCase))
                        rawNested = r.Name;
                tr.Commit();
            }

            var settings = LoadGlobalDefaults();
            settings.IncludeXrefLayers = true;
            int entryCount = BuildPlaceXrefLegend(db, settings, out string err);
            string gColor = ReadAci(db, XrefGLayer), aColor = ReadAci(db, XrefALayer);

            int segments = rawNested.Split('|').Length;
            // AutoCAD representeert de geneste NLCS-laag met een xref-namespace-prefix; de parser
            // neemt robuust het deel na de laatste '|'. De waargenomen diepte kan 2 of meer zijn.
            bool nestedNameOk = segments >= 2;
            bool parseOk = entryCount >= 2;
            bool noDbErr = err == "geen";
            bool colorOk = gColor == "1" && aColor == "3";
            ed.WriteMessage($"\nXREFNESTED naam: {rawNested} (segmenten={segments}) -> {(nestedNameOk ? "OK" : "FAIL")}");
            ed.WriteMessage($"\nXREFNESTED parse: entries={entryCount} -> {(parseOk ? "OK" : "FAIL")}");
            ed.WriteMessage($"\nXREFNESTED database: fout={err} -> {(noDbErr ? "OK" : "FAIL")}");
            ed.WriteMessage($"\nXREFNESTED kleur: G={gColor}(rood=1) A={aColor}(groen=3) -> {(colorOk ? "OK" : "FAIL")}");
            ed.WriteMessage($"\nXREFNESTED: totaal -> {(nestedNameOk && parseOk && noDbErr && colorOk ? "OK" : "FAIL")}");
        }
        catch (System.Exception ex) { ed.WriteMessage($"\nXREFNESTED fatale fout: {ex.GetType().Name}: {ex.Message}"); }
    }

    // Save/reopen/update met een xref-only kader. SETUP koppelt de xref, bouwt en plaatst de
    // legenda via het echte pad; na QSAVE + heropenen (xref vanaf schijf opnieuw geresolved)
    // werkt VERIFY elke legenda bij met de normale updateflow en controleert dat er geen
    // eWrongDatabase optreedt en dat de swatchkleuren behouden blijven. Dit dekt het scenario
    // waarin heropende xref-ObjectId's anders zouden kunnen lekken.
    [CommandMethod("NLCSLEGENDAXREFSAVESETUP", CommandFlags.Modal)]
    public void NlcsLegendaXrefSaveSetup()
    {
        var doc = AcAp.DocumentManager.MdiActiveDocument;
        if (doc is null) return;
        var db = doc.Database; var ed = doc.Editor;
        string xpath = Path.Combine(Path.GetDirectoryName(db.Filename) ?? Path.GetTempPath(), "xrefrender_src.dwg");
        try
        {
            CreateXrefRenderDwg(xpath);
            AttachAndInsert(db, xpath, "KADER");
            var settings = LoadGlobalDefaults();
            settings.IncludeXrefLayers = true;
            int entryCount = BuildPlaceXrefLegend(db, settings, out string err);
            ed.WriteMessage($"\nXREFSAVE setup: entries={entryCount} fout={err}. QSAVE, heropenen, dan NLCSLEGENDAXREFSAVEVERIFY.");
        }
        catch (System.Exception ex) { ed.WriteMessage($"\nXREFSAVE setup error: {ex.GetType().Name}: {ex.Message}"); }
    }

    [CommandMethod("NLCSLEGENDAXREFSAVEVERIFY", CommandFlags.Modal)]
    public void NlcsLegendaXrefSaveVerify()
    {
        var doc = AcAp.DocumentManager.MdiActiveDocument;
        if (doc is null) return;
        var db = doc.Database; var ed = doc.Editor;
        string err = "geen"; int updated = 0;
        try
        {
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var reg = LegendStore.Load(db, tr);
                foreach (var def in reg.Legends.ToList())
                    if (BuildManagedLegend(db, tr, reg, def, out _, out _) == UpdateResult.Updated)
                        updated++;
                LegendStore.Save(db, tr, reg);
                tr.Commit();
            }
            PurgePending(db);
        }
        catch (System.Exception ex) { err = ex.GetType().Name + ": " + ex.Message; }

        string gColor = ReadAci(db, XrefGLayer), aColor = ReadAci(db, XrefALayer);
        bool noDbErr = err == "geen";
        bool updOk = updated >= 1;
        bool colorOk = gColor == "1" && aColor == "3";
        ed.WriteMessage($"\nXREFSAVE verify: bijgewerkt={updated} fout={err} -> {(noDbErr && updOk ? "OK" : "FAIL")}");
        ed.WriteMessage($"\nXREFSAVE kleur na heropenen: G={gColor}(rood=1) A={aColor}(groen=3) -> {(colorOk ? "OK" : "FAIL")}");
        ed.WriteMessage($"\nXREFSAVE: totaal -> {(noDbErr && updOk && colorOk ? "OK" : "FAIL")}");
    }

    // Eén logische NLCS-laag uit meerdere bronnen: gelijke weergave hoort samen te vallen (één
    // regel, opgetelde hoeveelheid), verschillende weergave hoort te splitsen (aparte regels, eigen
    // hoeveelheid). Sectie 11: A rood 5 m + B rood 7 m + C groen 3 m -> rood 12 m, groen 3 m, 2 regels.
    private static void CreateXrefLineDwg(string path, string layerName, short colorIndex, double lengthM,
        LineWeight lineWeight = LineWeight.ByLineWeightDefault)
    {
        var prev = HostApplicationServices.WorkingDatabase;
        using var xdb = new Database(true, true);
        HostApplicationServices.WorkingDatabase = xdb;
        try
        {
            using (var tr = xdb.TransactionManager.StartTransaction())
            {
                var lt = (LayerTable)tr.GetObject(xdb.LayerTableId, OpenMode.ForWrite);
                var r = new LayerTableRecord { Name = layerName, Color = AcColor.Color.FromColorIndex(AcColor.ColorMethod.ByAci, colorIndex), LineWeight = lineWeight };
                var lid = lt.Add(r); tr.AddNewlyCreatedDBObject(r, true);
                var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(xdb), OpenMode.ForWrite);
                var line = new Line(new Point3d(0, 0, 0), new Point3d(lengthM, 0, 0)) { LayerId = lid };
                ms.AppendEntity(line); tr.AddNewlyCreatedDBObject(line, true);
                tr.Commit();
            }
            xdb.SaveAs(path, DwgVersion.Current);
        }
        finally { HostApplicationServices.WorkingDatabase = prev; }
    }

    private static string EntryAci(AnalysisResult a, LegendEntry e) =>
        e.GeometryLayer is { } g && a.LayerStyles.TryGetValue(g, out var s) ? s.Color.ColorIndex.ToString() : "?";

    [CommandMethod("NLCSLEGENDAXREFSPLITTEST", CommandFlags.Modal)]
    public void NlcsLegendaXrefSplitTest()
    {
        var doc = AcAp.DocumentManager.MdiActiveDocument;
        if (doc is null) return;
        var db = doc.Database; var ed = doc.Editor;
        string dir = Path.GetDirectoryName(db.Filename) ?? Path.GetTempPath();
        const string layer = "N-WE-VH-WEG-G";
        try
        {
            var a = Path.Combine(dir, "split_a.dwg");
            var b = Path.Combine(dir, "split_b.dwg");
            var cpath = Path.Combine(dir, "split_c.dwg");
            CreateXrefLineDwg(a, layer, 1, 5.0);    // rood 5 m
            CreateXrefLineDwg(b, layer, 1, 7.0);    // rood 7 m
            CreateXrefLineDwg(cpath, layer, 3, 3.0); // groen 3 m
            AttachAndInsert(db, a, "SPLITA");
            AttachAndInsert(db, b, "SPLITB");
            AttachAndInsert(db, cpath, "SPLITC");

            var settings = LoadGlobalDefaults();
            settings.IncludeXrefLayers = true;
            settings.IncludeQuantities = true;
            using var tr = db.TransactionManager.StartTransaction();
            var analysis = DrawingAnalyzer.Analyze(db, tr, settings, catalog: LoadCatalog(db));
            var rows = analysis.Entries.Where(e => e.GeometryLayer is not null).ToList();
            ed.WriteMessage($"\nSPLIT: regels={rows.Count}");
            foreach (var e in rows)
                ed.WriteMessage($"\nSPLIT: element='{e.Element}' lengte={e.Metric.Length:0.#} kleur={EntryAci(analysis, e)} laag={e.GeometryLayer}");
            double red = rows.Where(e => EntryAci(analysis, e) == "1").Sum(e => e.Metric.Length);
            double green = rows.Where(e => EntryAci(analysis, e) == "3").Sum(e => e.Metric.Length);
            bool rowsOk = rows.Count == 2, redOk = System.Math.Abs(red - 12.0) < 0.01, greenOk = System.Math.Abs(green - 3.0) < 0.01;
            ed.WriteMessage($"\nSPLIT: rood={red:0.#}(verw 12) groen={green:0.#}(verw 3) regels={rows.Count}(verw 2) -> {(rowsOk && redOk && greenOk ? "OK" : "FAIL")}");
            tr.Commit();

            // De gebouwde legenda moet twee swatchlagen met de juiste kleur opleveren.
            int built = BuildPlaceXrefLegend(db, settings, out string err);
            string c1 = ReadAci(db, "N-WE-VH-WEG-G"), c2 = ReadAci(db, "N-WE-VH-WEG-G #2");
            bool noErr = err == "geen";
            bool swatchOk = (c1 == "1" && c2 == "3") || (c1 == "3" && c2 == "1");
            ed.WriteMessage($"\nSPLIT: gebouwd regels={built} fout={err} swatchkleuren={c1}/{c2} -> {(noErr && swatchOk ? "OK" : "FAIL")}");
        }
        catch (System.Exception ex) { ed.WriteMessage($"\nSPLIT fatale fout: {ex.GetType().Name}: {ex.Message}"); }
    }

    // Splitsen moet ook werken op een ander stijlverschil dan kleur (sectie 5). Twee bronnen met
    // dezelfde logische laag en dezelfde kleur maar verschillende lijndikte horen apart te blijven;
    // twee bronnen met een identieke lijndikte horen samen te vallen met opgetelde hoeveelheid.
    [CommandMethod("NLCSLEGENDAXREFSTYLETEST", CommandFlags.Modal)]
    public void NlcsLegendaXrefStyleTest()
    {
        var doc = AcAp.DocumentManager.MdiActiveDocument;
        if (doc is null) return;
        var db = doc.Database; var ed = doc.Editor;
        string dir = Path.GetDirectoryName(db.Filename) ?? Path.GetTempPath();
        const string layer = "N-WE-VH-WEG-G";
        try
        {
            var thin1 = Path.Combine(dir, "style_thin1.dwg");
            var thin2 = Path.Combine(dir, "style_thin2.dwg");
            var thick = Path.Combine(dir, "style_thick.dwg");
            CreateXrefLineDwg(thin1, layer, 1, 4.0, LineWeight.LineWeight025); // dun, 4 m
            CreateXrefLineDwg(thin2, layer, 1, 6.0, LineWeight.LineWeight025); // dun, 6 m (gelijk uiterlijk)
            CreateXrefLineDwg(thick, layer, 1, 2.0, LineWeight.LineWeight100); // dik, 2 m
            AttachAndInsert(db, thin1, "STYLETHIN1");
            AttachAndInsert(db, thin2, "STYLETHIN2");
            AttachAndInsert(db, thick, "STYLETHICK");

            var settings = LoadGlobalDefaults();
            settings.IncludeXrefLayers = true;
            settings.IncludeQuantities = true;
            using var tr = db.TransactionManager.StartTransaction();
            var analysis = DrawingAnalyzer.Analyze(db, tr, settings, catalog: LoadCatalog(db));
            var rows = analysis.Entries.Where(e => e.GeometryLayer is not null).ToList();
            ed.WriteMessage($"\nSTYLE: regels={rows.Count}");
            foreach (var e in rows)
                ed.WriteMessage($"\nSTYLE: lengte={e.Metric.Length:0.#} laag={e.GeometryLayer}");
            // Dunne bronnen (4+6=10 m) vallen samen; dikke (2 m) blijft apart -> 2 regels.
            bool rowsOk = rows.Count == 2;
            bool thinOk = rows.Any(e => System.Math.Abs(e.Metric.Length - 10.0) < 0.01);
            bool thickOk = rows.Any(e => System.Math.Abs(e.Metric.Length - 2.0) < 0.01);
            ed.WriteMessage($"\nSTYLE: dun-samen(10m)={thinOk} dik-apart(2m)={thickOk} regels={rows.Count}(verw 2) -> {(rowsOk && thinOk && thickOk ? "OK" : "FAIL")}");
            tr.Commit();
        }
        catch (System.Exception ex) { ed.WriteMessage($"\nSTYLE fatale fout: {ex.GetType().Name}: {ex.Message}"); }
    }
}
