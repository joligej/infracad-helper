using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
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
    private const string XrefSymbolLayer = "N-WE-RI-VWA_RIOOLPUT-S";

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
                hatch.SetHatchPattern(HatchPatternType.PreDefined, "ANSI31");
                hatch.PatternScale = 1.5;
                hatch.PatternAngle = Math.PI / 4.0;
                hatch.Associative = false;
                using (var loop = new ObjectIdCollection { plId }) hatch.AppendLoop(HatchLoopTypes.Default, loop);
                hatch.EvaluateHatch(true);

                var symbolLayer = LayerC(XrefSymbolLayer, 2);
                var blocks = (BlockTable)tr.GetObject(xdb.BlockTableId, OpenMode.ForWrite);
                var symbol = new BlockTableRecord { Name = "XREF_RIOOLPUT" };
                var symbolId = blocks.Add(symbol);
                tr.AddNewlyCreatedDBObject(symbol, true);
                var zeroLayer = lt["0"];
                var circle = new Circle(Point3d.Origin, Vector3d.ZAxis, 2.0) { LayerId = zeroLayer };
                symbol.AppendEntity(circle); tr.AddNewlyCreatedDBObject(circle, true);
                var label = new DBText
                {
                    Position = new Point3d(-1.0, -3.0, 0),
                    Height = 1.0,
                    TextString = "XREF-SYMBOL",
                    LayerId = zeroLayer
                };
                label.SetDatabaseDefaults();
                symbol.AppendEntity(label); tr.AddNewlyCreatedDBObject(label, true);
                var symbolReference = new BlockReference(new Point3d(20, 0, 0), symbolId)
                {
                    LayerId = symbolLayer
                };
                ms.AppendEntity(symbolReference); tr.AddNewlyCreatedDBObject(symbolReference, true);
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

    private static ObjectId AttachAndInsert(Database db, string path, string name)
    {
        ObjectId xid = db.AttachXref(path, name);
        using var tr = db.TransactionManager.StartTransaction();
        var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForWrite);
        var br = new BlockReference(Point3d.Origin, xid);
        ms.AppendEntity(br); tr.AddNewlyCreatedDBObject(br, true);
        tr.Commit();
        return br.ObjectId;
    }

    // Exact dezelfde bouw-/plaatsflow als een normale gebruiker (analyse -> block -> insert ->
    // group -> registry -> commit). Vangt databasefouten op zodat eWrongDatabase zichtbaar wordt.
    private static int BuildPlaceXrefLegend(
        Database db, LegendSettings settings, out string err, out string groupName, out ObjectId blockRecordId)
    {
        int entryCount = 0;
        err = "geen";
        groupName = string.Empty;
        blockRecordId = ObjectId.Null;
        try
        {
            using var tr = db.TransactionManager.StartTransaction();
            var registry = LegendStore.Load(db, tr);
            var analysis = DrawingAnalyzer.Analyze(db, tr, settings, catalog: LoadCatalog(db));
            entryCount = analysis.Entries.Count;
            var btrId = LegendBuilder.BuildBlock(db, tr, analysis, settings, out _, out var issues);
            if (issues.Count > 0)
                throw new InvalidOperationException("Tekenfouten in legenda: "
                    + string.Join("; ", issues.Select(issue =>
                        $"{issue.Entry} [{issue.SourceLayer}; {issue.Kind}]: {issue.Reason}")));
            var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForWrite);
            var br = new BlockReference(Point3d.Origin, btrId);
            ms.AppendEntity(br); tr.AddNewlyCreatedDBObject(br, true);
            var definition = PlaceLegendAtPoint(tr, db, registry, settings, null, btrId, br, new Point3d(200, 0, 0));
            groupName = definition.GroupName;
            blockRecordId = btrId;
            tr.Commit();
        }
        catch (System.Exception ex) { err = ex.GetType().Name + ": " + ex.Message; }
        return entryCount;
    }

    private static (int Members, int Entities, int Hatches, int MTexts, int DbTexts) AssertLegendHostOwnership(
        Database db, Transaction tr, string groupName, ObjectId blockRecordId)
    {
        RequireHostObjectId(db, blockRecordId, "legend block record");
        var groupDictionary = (DBDictionary)tr.GetObject(db.GroupDictionaryId, OpenMode.ForRead);
        RequireHostObjectId(db, groupDictionary.GetAt(groupName), "legend group");
        var group = (Group)tr.GetObject(groupDictionary.GetAt(groupName), OpenMode.ForRead);
        var visitedBlocks = new HashSet<ObjectId>();
        int members = 0, entities = 0, hatches = 0, mtexts = 0, dbtexts = 0;

        void VisitEntity(ObjectId id)
        {
            RequireHostObjectId(db, id, "legend entity");
            if (tr.GetObject(id, OpenMode.ForRead) is not Entity entity)
                return;
            entities++;
            if (!entity.LayerId.IsNull)
            {
                RequireHostObjectId(db, entity.LayerId, "entity layer");
                var layer = (LayerTableRecord)tr.GetObject(entity.LayerId, OpenMode.ForRead);
                RequireHostObjectId(db, layer.LinetypeObjectId, "layer linetype");
            }
            if (entity is MText mt)
            {
                mtexts++;
                RequireHostObjectId(db, mt.TextStyleId, "MText style");
            }
            if (entity is DBText)
                dbtexts++;
            if (entity is Hatch hatch)
            {
                hatches++;
                for (int i = 0; i < hatch.NumberOfLoops; i++)
                    foreach (ObjectId boundary in hatch.GetAssociatedObjectIdsAt(i))
                        RequireHostObjectId(db, boundary, "hatch boundary");
            }
            if (entity is BlockReference blockReference)
            {
                RequireHostObjectId(db, blockReference.BlockTableRecord, "referenced block record");
                VisitBlock(blockReference.BlockTableRecord);
            }
        }

        void VisitBlock(ObjectId id)
        {
            RequireHostObjectId(db, id, "block record");
            if (!visitedBlocks.Add(id))
                return;
            var btr = (BlockTableRecord)tr.GetObject(id, OpenMode.ForRead);
            foreach (ObjectId child in btr)
                VisitEntity(child);
        }

        var groupMembers = group.GetAllEntityIds();
        foreach (ObjectId member in groupMembers)
        {
            members++;
            VisitEntity(member);
        }
        VisitBlock(blockRecordId);
        return (members, entities, hatches, mtexts, dbtexts);
    }

    private sealed record PlacementFingerprint(
        int ModelspaceEntities, int Groups, int LegendBlocks, int RegistryEntries, int Layers, int TextStyles);

    private static PlacementFingerprint ReadPlacementFingerprint(Database db)
    {
        using var tr = db.TransactionManager.StartTransaction();
        var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForRead);
        int modelspaceEntities = ms.Cast<ObjectId>().Count();
        var groups = (DBDictionary)tr.GetObject(db.GroupDictionaryId, OpenMode.ForRead);
        int groupCount = 0;
        foreach (var _ in groups)
            groupCount++;
        var blocks = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
        int legendBlocks = 0;
        foreach (ObjectId id in blocks)
            if (tr.GetObject(id, OpenMode.ForRead) is BlockTableRecord btr
                && btr.Name.StartsWith("NLCS_LEGENDA_", System.StringComparison.OrdinalIgnoreCase))
                legendBlocks++;
        var layers = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
        int layerCount = layers.Cast<ObjectId>().Count();
        var textStyles = (TextStyleTable)tr.GetObject(db.TextStyleTableId, OpenMode.ForRead);
        int textStyleCount = textStyles.Cast<ObjectId>().Count();
        int registryCount = LegendStore.Load(db, tr).Legends.Count;
        tr.Commit();
        return new PlacementFingerprint(modelspaceEntities, groupCount, legendBlocks, registryCount, layerCount, textStyleCount);
    }

    private static string[] ReadLegendBlockTexts(Transaction tr, ObjectId blockRecordId)
    {
        var btr = (BlockTableRecord)tr.GetObject(blockRecordId, OpenMode.ForRead);
        return btr.Cast<ObjectId>()
            .Select(id => tr.GetObject(id, OpenMode.ForRead))
            .OfType<MText>()
            .Select(text => text.Contents)
            .ToArray();
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

    private static LineWeight ReadLineWeight(Database db, string layerName)
    {
        using var tr = db.TransactionManager.StartTransaction();
        var layers = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
        var result = layers.Has(layerName)
            && tr.GetObject(layers[layerName], OpenMode.ForRead) is LayerTableRecord layer
                ? layer.LineWeight
                : LineWeight.ByLineWeightDefault;
        tr.Commit();
        return result;
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
            settings.HatchScaleFactor = 1.0;
            int entryCount = BuildPlaceXrefLegend(db, settings, out string err, out string groupName, out ObjectId blockRecordId);
            string gColor = ReadAci(db, XrefGLayer), aColor = ReadAci(db, XrefALayer);

            bool parseOk = entryCount >= 2;
            bool noDbErr = err == "geen";
            bool colorOk = gColor == "1" && aColor == "3";
            (int Members, int Entities, int Hatches, int MTexts, int DbTexts) ownership = default;
            int symbolPrimitives = 0, symbolText = 0, hatchLoops = 0;
            bool hatchStyleOk = false;
            if (noDbErr)
            {
                using var tr = db.TransactionManager.StartTransaction();
                ownership = AssertLegendHostOwnership(db, tr, groupName, blockRecordId);
                var block = (BlockTableRecord)tr.GetObject(blockRecordId, OpenMode.ForRead);
                foreach (ObjectId id in block)
                {
                    var entity = tr.GetObject(id, OpenMode.ForRead);
                    if (entity is Circle)
                        symbolPrimitives++;
                    else if (entity is MText text
                        && text.Contents.Contains("XREF-SYMBOL", System.StringComparison.Ordinal))
                        symbolText++;
                    else if (entity is Hatch hatch)
                    {
                        hatchLoops += hatch.NumberOfLoops;
                        if (hatch.Layer.StartsWith(XrefALayer, System.StringComparison.OrdinalIgnoreCase)
                            && hatch.PatternName.Equals("ANSI31", System.StringComparison.OrdinalIgnoreCase))
                            hatchStyleOk = Math.Abs(hatch.PatternScale - 1.5) < 1e-9
                                && Math.Abs(hatch.PatternAngle - Math.PI / 4.0) < 1e-9;
                    }
                }
                tr.Commit();
            }
            bool ownershipOk = ownership.Members > 0 && ownership.Entities > 0
                && ownership.Hatches > 0 && ownership.MTexts > 0 && ownership.DbTexts == 0;
            bool symbolOk = symbolPrimitives > 0 && symbolText > 0;
            ed.WriteMessage($"\nXREFRENDER parse: entries={entryCount} -> {(parseOk ? "OK" : "FAIL")}");
            ed.WriteMessage($"\nXREFRENDER database: fout={err} -> {(noDbErr ? "OK" : "FAIL")}");
            ed.WriteMessage($"\nXREFRENDER kleur: G-swatch={gColor}(rood=1) A-swatch={aColor}(groen=3) -> {(colorOk ? "OK" : "FAIL")}");
            ed.WriteMessage($"\nXREFRENDER symbol: primitives={symbolPrimitives} bron-MText={symbolText} -> {(symbolOk ? "OK" : "FAIL")}");
            ed.WriteMessage($"\nXREFRENDER hatch: ANSI31 schaal=1.5 hoek=45° -> {(hatchStyleOk ? "OK" : "FAIL")}");
            ed.WriteMessage($"\nXREFRENDER ownership: entities={ownership.Entities} hatches={ownership.Hatches} hatchloops={hatchLoops} MText={ownership.MTexts} DBText={ownership.DbTexts} -> {(ownershipOk && hatchLoops > 0 ? "OK" : "FAIL")}");
            ed.WriteMessage($"\nXREFRENDER: totaal -> {(parseOk && noDbErr && colorOk && ownershipOk && hatchLoops > 0 && symbolOk && hatchStyleOk ? "OK" : "FAIL")}");
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
            int entryCount = BuildPlaceXrefLegend(db, settings, out string err, out _, out _);
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
            int entryCount = BuildPlaceXrefLegend(db, settings, out string err, out _, out _);
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
    private static Database CreateXrefLineDatabase(string path, string layerName, short colorIndex, double lengthM,
        out ObjectId sourceLineId, LineWeight lineWeight = LineWeight.ByLineWeightDefault,
        string? symbolLayerName = null, string? symbolBlockName = null, string? symbolLabel = null,
        double symbolScale = 1.0, double symbolRotation = 0.0, bool symbolMirrorX = false)
    {
        var prev = HostApplicationServices.WorkingDatabase;
        var xdb = new Database(true, true);
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
                sourceLineId = ms.AppendEntity(line); tr.AddNewlyCreatedDBObject(line, true);
                if (symbolLayerName is not null || symbolBlockName is not null)
                {
                    if (string.IsNullOrWhiteSpace(symbolLayerName) || string.IsNullOrWhiteSpace(symbolBlockName))
                        throw new ArgumentException("Een symboolfixture vereist zowel een laagnaam als een bloknaam.");
                    var symbolLayer = new LayerTableRecord
                    {
                        Name = symbolLayerName,
                        Color = AcColor.Color.FromColorIndex(AcColor.ColorMethod.ByAci, colorIndex)
                    };
                    var symbolLayerId = lt.Add(symbolLayer);
                    tr.AddNewlyCreatedDBObject(symbolLayer, true);
                    var blocks = (BlockTable)tr.GetObject(xdb.BlockTableId, OpenMode.ForWrite);
                    var symbol = new BlockTableRecord { Name = symbolBlockName };
                    var symbolId = blocks.Add(symbol);
                    tr.AddNewlyCreatedDBObject(symbol, true);
                    var zeroLayer = lt["0"];
                    var circle = new Circle(Point3d.Origin, Vector3d.ZAxis, 2.0) { LayerId = zeroLayer };
                    symbol.AppendEntity(circle); tr.AddNewlyCreatedDBObject(circle, true);
                    var marker = new Line(new Point3d(0, 0, 0), new Point3d(2, 1, 0)) { LayerId = zeroLayer };
                    symbol.AppendEntity(marker); tr.AddNewlyCreatedDBObject(marker, true);
                    var label = new DBText
                    {
                        Position = new Point3d(-1.0, -3.0, 0),
                        Height = 1.0,
                        TextString = symbolLabel ?? "XREF-PLACEMENT-SYMBOL",
                        LayerId = zeroLayer
                    };
                    label.SetDatabaseDefaults();
                    symbol.AppendEntity(label); tr.AddNewlyCreatedDBObject(label, true);
                    var symbolReference = new BlockReference(new Point3d(20, 0, 0), symbolId)
                    {
                        LayerId = symbolLayerId,
                        ScaleFactors = new Scale3d(symbolMirrorX ? -symbolScale : symbolScale, symbolScale, symbolScale),
                        Rotation = symbolRotation
                    };
                    ms.AppendEntity(symbolReference); tr.AddNewlyCreatedDBObject(symbolReference, true);
                }
                tr.Commit();
            }
            xdb.SaveAs(path, DwgVersion.Current);
            return xdb;
        }
        catch
        {
            xdb.Dispose();
            throw;
        }
        finally { HostApplicationServices.WorkingDatabase = prev; }
    }

    private static void CreateXrefLineDwg(string path, string layerName, short colorIndex, double lengthM,
        LineWeight lineWeight = LineWeight.ByLineWeightDefault,
        string? symbolLayerName = null, string? symbolBlockName = null, string? symbolLabel = null,
        double symbolScale = 1.0, double symbolRotation = 0.0, bool symbolMirrorX = false)
    {
        using var xdb = CreateXrefLineDatabase(
            path, layerName, colorIndex, lengthM, out _, lineWeight, symbolLayerName, symbolBlockName, symbolLabel,
            symbolScale, symbolRotation, symbolMirrorX);
    }

    private static void CreateXrefHatchDwg(
        string path, string layerName, short colorIndex, string patternName, double patternScale, double patternAngle)
    {
        var previousWorkingDatabase = HostApplicationServices.WorkingDatabase;
        using var xdb = new Database(true, true);
        HostApplicationServices.WorkingDatabase = xdb;
        try
        {
            using (var tr = xdb.TransactionManager.StartTransaction())
            {
                var layers = (LayerTable)tr.GetObject(xdb.LayerTableId, OpenMode.ForWrite);
                var layer = new LayerTableRecord
                {
                    Name = layerName,
                    Color = AcColor.Color.FromColorIndex(AcColor.ColorMethod.ByAci, colorIndex)
                };
                var layerId = layers.Add(layer);
                tr.AddNewlyCreatedDBObject(layer, true);

                var modelSpace = (BlockTableRecord)tr.GetObject(
                    SymbolUtilityServices.GetBlockModelSpaceId(xdb), OpenMode.ForWrite);
                var boundary = new Polyline { LayerId = layerId };
                boundary.AddVertexAt(0, new Point2d(0, 0), 0, 0, 0);
                boundary.AddVertexAt(1, new Point2d(10, 0), 0, 0, 0);
                boundary.AddVertexAt(2, new Point2d(10, 10), 0, 0, 0);
                boundary.AddVertexAt(3, new Point2d(0, 10), 0, 0, 0);
                boundary.Closed = true;
                var boundaryId = modelSpace.AppendEntity(boundary);
                tr.AddNewlyCreatedDBObject(boundary, true);

                var hatch = new Hatch();
                hatch.SetDatabaseDefaults();
                hatch.LayerId = layerId;
                hatch.Associative = false;
                modelSpace.AppendEntity(hatch);
                tr.AddNewlyCreatedDBObject(hatch, true);
                hatch.SetHatchPattern(HatchPatternType.PreDefined, patternName);
                hatch.PatternScale = patternScale;
                hatch.PatternAngle = patternAngle;
                using var loop = new ObjectIdCollection { boundaryId };
                hatch.AppendLoop(HatchLoopTypes.Default, loop);
                hatch.EvaluateHatch(true);
                tr.Commit();
            }
            xdb.SaveAs(path, DwgVersion.Current);
        }
        finally
        {
            HostApplicationServices.WorkingDatabase = previousWorkingDatabase;
        }
    }

    private static void CreateConflictingHatchDwg(string path, string layerName)
    {
        var previousWorkingDatabase = HostApplicationServices.WorkingDatabase;
        using var xdb = new Database(true, true);
        HostApplicationServices.WorkingDatabase = xdb;
        try
        {
            using (var tr = xdb.TransactionManager.StartTransaction())
            {
                var layers = (LayerTable)tr.GetObject(xdb.LayerTableId, OpenMode.ForWrite);
                var layer = new LayerTableRecord
                {
                    Name = layerName,
                    Color = AcColor.Color.FromColorIndex(AcColor.ColorMethod.ByAci, 3)
                };
                var layerId = layers.Add(layer);
                tr.AddNewlyCreatedDBObject(layer, true);
                var modelSpace = (BlockTableRecord)tr.GetObject(
                    SymbolUtilityServices.GetBlockModelSpaceId(xdb), OpenMode.ForWrite);
                string[] patterns = { "SOLID", "ANSI31" };
                for (int i = 0; i < patterns.Length; i++)
                {
                    double x = i * 10.0;
                    var boundary = new Polyline();
                    boundary.AddVertexAt(0, new Point2d(x, 0), 0, 0, 0);
                    boundary.AddVertexAt(1, new Point2d(x + 5, 0), 0, 0, 0);
                    boundary.AddVertexAt(2, new Point2d(x + 5, 5), 0, 0, 0);
                    boundary.AddVertexAt(3, new Point2d(x, 5), 0, 0, 0);
                    boundary.Closed = true;
                    boundary.LayerId = layerId;
                    var boundaryId = modelSpace.AppendEntity(boundary);
                    tr.AddNewlyCreatedDBObject(boundary, true);

                    var hatch = new Hatch { LayerId = layerId, Associative = false };
                    modelSpace.AppendEntity(hatch);
                    tr.AddNewlyCreatedDBObject(hatch, true);
                    hatch.SetHatchPattern(HatchPatternType.PreDefined, patterns[i]);
                    hatch.PatternScale = 1.0;
                    using var loop = new ObjectIdCollection { boundaryId };
                    hatch.AppendLoop(HatchLoopTypes.Default, loop);
                    hatch.EvaluateHatch(true);
                }
                tr.Commit();
            }
            xdb.SaveAs(path, DwgVersion.Current);
        }
        finally
        {
            HostApplicationServices.WorkingDatabase = previousWorkingDatabase;
        }
    }

    private static void CreateConflictingSymbolDwg(string path, string layerName)
    {
        var previousWorkingDatabase = HostApplicationServices.WorkingDatabase;
        using var xdb = new Database(true, true);
        HostApplicationServices.WorkingDatabase = xdb;
        try
        {
            using (var tr = xdb.TransactionManager.StartTransaction())
            {
                var layers = (LayerTable)tr.GetObject(xdb.LayerTableId, OpenMode.ForWrite);
                var layer = new LayerTableRecord
                {
                    Name = layerName,
                    Color = AcColor.Color.FromColorIndex(AcColor.ColorMethod.ByAci, 3)
                };
                var layerId = layers.Add(layer);
                tr.AddNewlyCreatedDBObject(layer, true);
                var blocks = (BlockTable)tr.GetObject(xdb.BlockTableId, OpenMode.ForWrite);
                var modelSpace = (BlockTableRecord)tr.GetObject(
                    SymbolUtilityServices.GetBlockModelSpaceId(xdb), OpenMode.ForWrite);
                string[] names = { "SYMBOL_RENDER_A", "SYMBOL_RENDER_B" };
                for (int i = 0; i < names.Length; i++)
                {
                    var definition = new BlockTableRecord { Name = names[i] };
                    var definitionId = blocks.Add(definition);
                    tr.AddNewlyCreatedDBObject(definition, true);
                    var circle = new Circle(Point3d.Origin, Vector3d.ZAxis, 1.0 + i);
                    definition.AppendEntity(circle);
                    tr.AddNewlyCreatedDBObject(circle, true);
                    var reference = new BlockReference(new Point3d(i * 5.0, 0, 0), definitionId)
                    {
                        LayerId = layerId
                    };
                    modelSpace.AppendEntity(reference);
                    tr.AddNewlyCreatedDBObject(reference, true);
                }
                tr.Commit();
            }
            xdb.SaveAs(path, DwgVersion.Current);
        }
        finally
        {
            HostApplicationServices.WorkingDatabase = previousWorkingDatabase;
        }
    }

    private static bool RejectsAmbiguousSample(
        Database db, ObjectId sourceReference, LegendSettings settings, string errorFragment)
    {
        try
        {
            using var tr = db.TransactionManager.StartTransaction();
            DrawingAnalyzer.Analyze(
                db, tr, settings, new[] { sourceReference }, catalog: LoadCatalog(db));
            tr.Commit();
            return false;
        }
        catch (InvalidOperationException ex)
        {
            return ex.Message.Contains(errorFragment, System.StringComparison.Ordinal);
        }
    }

    private static void AddHostLine(Database db, string layerName, short colorIndex, double lengthM)
    {
        using var tr = db.TransactionManager.StartTransaction();
        var layers = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForWrite);
        ObjectId layerId;
        if (layers.Has(layerName))
            layerId = layers[layerName];
        else
        {
            var layer = new LayerTableRecord
            {
                Name = layerName,
                Color = AcColor.Color.FromColorIndex(AcColor.ColorMethod.ByAci, colorIndex)
            };
            layerId = layers.Add(layer);
            tr.AddNewlyCreatedDBObject(layer, true);
        }
        var modelSpace = (BlockTableRecord)tr.GetObject(
            SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForWrite);
        var line = new Line(new Point3d(0, 0, 0), new Point3d(lengthM, 0, 0)) { LayerId = layerId };
        modelSpace.AppendEntity(line);
        tr.AddNewlyCreatedDBObject(line, true);
        tr.Commit();
    }

    private static AnalysisResult AnalyzeHostAndXref(string xrefPath, short hostColor, double hostLength, string xrefName)
    {
        var previousWorkingDatabase = HostApplicationServices.WorkingDatabase;
        using var hostDb = new Database(true, true);
        HostApplicationServices.WorkingDatabase = hostDb;
        try
        {
            AddHostLine(hostDb, "N-WE-VH-WEG-G", hostColor, hostLength);
            AttachAndInsert(hostDb, xrefPath, xrefName);
            var settings = LoadGlobalDefaults();
            settings.IncludeXrefLayers = true;
            settings.IncludeQuantities = true;
            using var tr = hostDb.TransactionManager.StartTransaction();
            var result = DrawingAnalyzer.Analyze(hostDb, tr, settings, catalog: LoadCatalog(hostDb));
            tr.Commit();
            return result;
        }
        finally
        {
            HostApplicationServices.WorkingDatabase = previousWorkingDatabase;
        }
    }

    private static string EntryAci(AnalysisResult a, LegendEntry e) =>
        e.GeometryLayer is { } g && a.LayerStyles.TryGetValue(g, out var s) ? s.Color.ColorIndex.ToString() : "?";

    private static double[] ExportedLengths(IEnumerable<LegendEntry> entries)
    {
        using var json = System.Text.Json.JsonDocument.Parse(LegendExport.ToJson(entries));
        return json.RootElement.EnumerateArray()
            .Select(row => row.GetProperty("lengteM").GetDouble())
            .OrderBy(length => length)
            .ToArray();
    }

    private static int[] ExportedCounts(IEnumerable<LegendEntry> entries)
    {
        using var json = System.Text.Json.JsonDocument.Parse(LegendExport.ToJson(entries));
        return json.RootElement.EnumerateArray()
            .Select(row => row.GetProperty("aantal").GetInt32())
            .OrderBy(count => count)
            .ToArray();
    }

    private static bool InfoRowCountMatches(AnalysisResult analysis, LegendSettings settings) =>
        BuildInfoReport(analysis, settings, "acceptance")
            .Contains($"Overzicht: {analysis.Entries.Count} regel(s)", System.StringComparison.Ordinal);

    [CommandMethod("NLCSLEGENDAXREFHOSTTEST", CommandFlags.Modal)]
    public void NlcsLegendaXrefHostTest()
    {
        var doc = AcAp.DocumentManager.MdiActiveDocument;
        if (doc is null) return;
        var ed = doc.Editor;
        string dir = Path.GetDirectoryName(doc.Database.Filename) ?? Path.GetTempPath();
        const string layer = "N-WE-VH-WEG-G";
        try
        {
            string samePath = Path.Combine(dir, "hostxref_same.dwg");
            string diffPath = Path.Combine(dir, "hostxref_diff.dwg");
            CreateXrefLineDwg(samePath, layer, 1, 7.0);
            CreateXrefLineDwg(diffPath, layer, 3, 7.0);

            var same = AnalyzeHostAndXref(samePath, 1, 5.0, "HOSTSAME");
            var sameRows = same.Entries.Where(entry => entry.GeometryLayer is not null).ToList();
            var sameRow = sameRows.SingleOrDefault();
            bool sameOk = sameRow is not null
                && System.Math.Abs(sameRow.Metric.Length - 12.0) < 0.01
                && EntryAci(same, sameRow) == "1"
                && sameRow.SourceLayers.Count == 2;
            bool sameExportOk = sameOk
                && ExportedLengths(sameRows).SequenceEqual(new[] { 12.0 })
                && InfoRowCountMatches(same, LoadGlobalDefaults());

            var different = AnalyzeHostAndXref(diffPath, 1, 5.0, "HOSTDIFF");
            var differentRows = different.Entries.Where(entry => entry.GeometryLayer is not null).ToList();
            double red = differentRows.Where(entry => EntryAci(different, entry) == "1").Sum(entry => entry.Metric.Length);
            double green = differentRows.Where(entry => EntryAci(different, entry) == "3").Sum(entry => entry.Metric.Length);
            bool differentOk = differentRows.Count == 2
                && System.Math.Abs(red - 5.0) < 0.01
                && System.Math.Abs(green - 7.0) < 0.01;
            bool differentExportOk = differentOk
                && ExportedLengths(differentRows).SequenceEqual(new[] { 5.0, 7.0 })
                && InfoRowCountMatches(different, LoadGlobalDefaults());
            ed.WriteMessage($"\nHOSTXREF gelijk: regels={sameRows.Count} rood={sameRow?.Metric.Length:0.#} m kleur={(sameRow is null ? "geen" : EntryAci(same, sameRow))} -> {(sameOk ? "OK" : "FAIL")}");
            ed.WriteMessage($"\nHOSTXREF verschillend: regels={differentRows.Count} rood={red:0.#} m groen={green:0.#} m -> {(differentOk ? "OK" : "FAIL")}");
            ed.WriteMessage($"\nHOSTXREF export/info: gelijk={sameExportOk} verschillend={differentExportOk}");
            ed.WriteMessage($"\nHOSTXREF totaal: {(sameOk && differentOk && sameExportOk && differentExportOk ? "OK" : "FAIL")}");
        }
        catch (System.Exception ex)
        {
            ed.WriteMessage($"\nHOSTXREF fatale fout: {ex.GetType().Name}: {ex.Message}");
        }
    }

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
            bool exportOk = ExportedLengths(rows).SequenceEqual(new[] { 3.0, 12.0 });
            bool infoOk = InfoRowCountMatches(analysis, settings);
            ed.WriteMessage($"\nSPLIT: rood={red:0.#}(verw 12) groen={green:0.#}(verw 3) regels={rows.Count}(verw 2) export={exportOk} info={infoOk} -> {(rowsOk && redOk && greenOk && exportOk && infoOk ? "OK" : "FAIL")}");
            tr.Commit();

            // De gebouwde legenda moet twee swatchlagen met de juiste kleur opleveren.
            int built = BuildPlaceXrefLegend(db, settings, out string err, out _, out _);
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
            var thinRow = rows.SingleOrDefault(e => System.Math.Abs(e.Metric.Length - 10.0) < 0.01);
            var thickRow = rows.SingleOrDefault(e => System.Math.Abs(e.Metric.Length - 2.0) < 0.01);
            bool thinOk = thinRow is not null
                && analysis.LayerStyles.TryGetValue(thinRow.GeometryLayer!, out var thinStyle)
                && thinStyle.LineWeight == LineWeight.LineWeight025;
            bool thickOk = thickRow is not null
                && analysis.LayerStyles.TryGetValue(thickRow.GeometryLayer!, out var thickStyle)
                && thickStyle.LineWeight == LineWeight.LineWeight100;
            bool styleSplitOk = thinRow is not null && thickRow is not null
                && thinRow.RenderIdentity != thickRow.RenderIdentity;
            tr.Commit();

            int built = BuildPlaceXrefLegend(db, settings, out string err, out _, out _);
            string[] renderLayers = { "N-WE-VH-WEG-G", "N-WE-VH-WEG-G #2" };
            var renderedWeights = renderLayers.Select(name => ReadLineWeight(db, name)).ToArray();
            bool renderedStyleOk = built == 2 && err == "geen"
                && renderedWeights.Contains(LineWeight.LineWeight025)
                && renderedWeights.Contains(LineWeight.LineWeight100);
            ed.WriteMessage($"\nSTYLE: dun-samen(10m)={thinOk} dik-apart(2m)={thickOk} aparte-render-id={styleSplitOk} -> {(rowsOk && thinOk && thickOk && styleSplitOk ? "OK" : "FAIL")}");
            ed.WriteMessage($"\nSTYLE: echte swatches 0.25/1.00 mm={renderedStyleOk} fout={err} weights={string.Join("/", renderedWeights)} -> {(renderedStyleOk ? "OK" : "FAIL")}");
            ed.WriteMessage($"\nSTYLE: totaal -> {(rowsOk && thinOk && thickOk && styleSplitOk && renderedStyleOk ? "OK" : "FAIL")}");
        }
        catch (System.Exception ex) { ed.WriteMessage($"\nSTYLE fatale fout: {ex.GetType().Name}: {ex.Message}"); }
    }

    [CommandMethod("NLCSLEGENDAXREFSYMBOLTEST", CommandFlags.Modal)]
    public void NlcsLegendaXrefSymbolTest()
    {
        var doc = AcAp.DocumentManager.MdiActiveDocument;
        if (doc is null) return;
        var db = doc.Database;
        var ed = doc.Editor;
        string dir = Path.GetDirectoryName(db.Filename) ?? Path.GetTempPath();
        const string layer = "N-WE-VH-WEG-G";
        const string symbolLayer = "N-WE-VH-WEG-S";
        try
        {
            string pathA = Path.Combine(dir, "symbol_a.dwg");
            string pathB = Path.Combine(dir, "symbol_b.dwg");
            string pathC = Path.Combine(dir, "symbol_c.dwg");
            string pathD = Path.Combine(dir, "symbol_d.dwg");
            string pathE = Path.Combine(dir, "symbol_e.dwg");
            string pathF = Path.Combine(dir, "symbol_f.dwg");
            CreateXrefLineDwg(pathA, layer, 1, 5.0,
                symbolLayerName: symbolLayer, symbolBlockName: "SYMBOL_A", symbolLabel: "SHARED");
            CreateXrefLineDwg(pathB, layer, 1, 7.0,
                symbolLayerName: symbolLayer, symbolBlockName: "SYMBOL_B", symbolLabel: "SHARED");
            CreateXrefLineDwg(pathC, layer, 1, 3.0,
                symbolLayerName: symbolLayer, symbolBlockName: "SYMBOL_A", symbolLabel: "DIFFERENT");
            CreateXrefLineDwg(pathD, layer, 1, 2.0,
                symbolLayerName: symbolLayer, symbolBlockName: "SYMBOL_A", symbolLabel: "SHARED",
                symbolRotation: Math.PI / 2.0);
            CreateXrefLineDwg(pathE, layer, 1, 4.0,
                symbolLayerName: symbolLayer, symbolBlockName: "SYMBOL_A", symbolLabel: "SHARED",
                symbolScale: 0.5);
            CreateXrefLineDwg(pathF, layer, 1, 1.0,
                symbolLayerName: symbolLayer, symbolBlockName: "SYMBOL_A", symbolLabel: "SHARED",
                symbolMirrorX: true);
            AttachAndInsert(db, pathA, "SYMBOLA");
            AttachAndInsert(db, pathB, "SYMBOLB");
            AttachAndInsert(db, pathC, "SYMBOLC");
            AttachAndInsert(db, pathD, "SYMBOLD");
            AttachAndInsert(db, pathE, "SYMBOLE");
            AttachAndInsert(db, pathF, "SYMBOLF");

            var settings = LoadGlobalDefaults();
            settings.IncludeXrefLayers = true;
            settings.IncludeQuantities = true;
            List<LegendEntry> rows;
            bool symbolGroupingOk, exportOk, infoOk;
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var analysis = DrawingAnalyzer.Analyze(db, tr, settings, catalog: LoadCatalog(db));
                rows = analysis.Entries.Where(entry => entry.SymbolLayer is not null).ToList();
                foreach (var row in rows)
                {
                    var sources = string.Join(",", row.SourceLayers.Select(source => source.SourceIdentity)
                        .Distinct(System.StringComparer.OrdinalIgnoreCase).OrderBy(source => source, System.StringComparer.Ordinal));
                    ed.WriteMessage($"\nSYMBOL row: length={row.Metric.Length:0.###} sources={sources}");
                }
                var shared = rows.SingleOrDefault(entry => System.Math.Abs(entry.Metric.Length - 12.0) < 0.01);
                var different = rows.SingleOrDefault(entry => System.Math.Abs(entry.Metric.Length - 3.0) < 0.01);
                var rotated = rows.SingleOrDefault(entry => System.Math.Abs(entry.Metric.Length - 2.0) < 0.01);
                var scaled = rows.SingleOrDefault(entry => System.Math.Abs(entry.Metric.Length - 4.0) < 0.01);
                var mirrored = rows.SingleOrDefault(entry => System.Math.Abs(entry.Metric.Length - 1.0) < 0.01);
                var distinctIdentities = rows.Select(entry => entry.RenderIdentity)
                    .Distinct(StringComparer.Ordinal).Count();
                symbolGroupingOk = rows.Count == 5
                    && shared is not null && different is not null && rotated is not null
                    && scaled is not null && mirrored is not null
                    && rows.Select(entry => entry.Description).Distinct(StringComparer.Ordinal).Count() == 1
                    && distinctIdentities == 5
                    && shared.SymbolBlockName?.EndsWith("SYMBOL_A", System.StringComparison.OrdinalIgnoreCase) == true
                    && different.SymbolBlockName?.EndsWith("SYMBOL_A", System.StringComparison.OrdinalIgnoreCase) == true
                    && shared.Description == different.Description
                    && shared.SourceLayers.Select(source => source.SourceIdentity)
                        .Distinct(System.StringComparer.OrdinalIgnoreCase).OrderBy(source => source, System.StringComparer.Ordinal)
                        .SequenceEqual(new[] { "SYMBOLA", "SYMBOLB" })
                    && different.SourceLayers.Select(source => source.SourceIdentity)
                        .Distinct(System.StringComparer.OrdinalIgnoreCase).SequenceEqual(new[] { "SYMBOLC" })
                    && rotated.SourceLayers.Select(source => source.SourceIdentity)
                        .Distinct(System.StringComparer.OrdinalIgnoreCase).SequenceEqual(new[] { "SYMBOLD" })
                    && scaled.SourceLayers.Select(source => source.SourceIdentity)
                        .Distinct(System.StringComparer.OrdinalIgnoreCase).SequenceEqual(new[] { "SYMBOLE" })
                    && mirrored.SourceLayers.Select(source => source.SourceIdentity)
                        .Distinct(System.StringComparer.OrdinalIgnoreCase).SequenceEqual(new[] { "SYMBOLF" });
                exportOk = ExportedLengths(rows).SequenceEqual(new[] { 1.0, 2.0, 3.0, 4.0, 12.0 });
                infoOk = InfoRowCountMatches(analysis, settings);
                tr.Commit();
            }

            int built = BuildPlaceXrefLegend(db, settings, out string error, out string groupName, out ObjectId blockId);
            bool ownershipOk = false, primitivesOk = false, textOk = false, transformsOk = false;
            if (error == "geen")
            {
                using var tr = db.TransactionManager.StartTransaction();
                var ownership = AssertLegendHostOwnership(db, tr, groupName, blockId);
                var block = (BlockTableRecord)tr.GetObject(blockId, OpenMode.ForRead);
                int circles = block.Cast<ObjectId>()
                    .Select(id => tr.GetObject(id, OpenMode.ForRead))
                    .OfType<Circle>()
                    .Count();
                var markerLines = block.Cast<ObjectId>()
                    .Select(id => tr.GetObject(id, OpenMode.ForRead))
                    .OfType<Line>()
                    .Where(line => line.Layer.StartsWith(symbolLayer, System.StringComparison.OrdinalIgnoreCase))
                    .ToList();
                var texts = ReadLegendBlockTexts(tr, blockId);
                var symbolTexts = block.Cast<ObjectId>()
                    .Select(id => tr.GetObject(id, OpenMode.ForRead))
                    .OfType<MText>()
                    .Where(text => text.Contents.Contains("SHARED", System.StringComparison.Ordinal))
                    .Select(text => text.Rotation)
                    .ToList();
                ownershipOk = ownership.Entities > 0 && ownership.DbTexts == 0;
                primitivesOk = circles == 5 && markerLines.Count == 5;
                textOk = texts.Any(text => text.Contains("SHARED", System.StringComparison.Ordinal))
                    && texts.Any(text => text.Contains("DIFFERENT", System.StringComparison.Ordinal));
                var markerAngles = markerLines.Select(line =>
                {
                    var vector = line.EndPoint - line.StartPoint;
                    double angle = Math.Atan2(vector.Y, vector.X);
                    return angle < 0 ? angle + 2 * Math.PI : angle;
                }).ToList();
                bool hasNormal = markerAngles.Any(angle => Math.Abs(angle - Math.Atan2(1.0, 2.0)) < 0.05);
                bool hasRotated = markerAngles.Any(angle => Math.Abs(angle - (Math.Atan2(1.0, 2.0) + Math.PI / 2.0)) < 0.05);
                bool hasMirrored = markerAngles.Any(angle => Math.Abs(angle - (Math.PI - Math.Atan2(1.0, 2.0))) < 0.05);
                bool scaledSmaller = block.Cast<ObjectId>()
                    .Select(id => tr.GetObject(id, OpenMode.ForRead))
                    .OfType<Circle>()
                    .Select(circle => circle.Radius)
                    .Distinct()
                    .Count() > 1;
                transformsOk = symbolTexts.Any(angle => Math.Abs(angle - Math.PI / 2.0) < 0.05)
                    && hasNormal && hasRotated && hasMirrored && scaledSmaller;
                tr.Commit();
            }

            bool totalOk = symbolGroupingOk && exportOk && infoOk && built == 5
                && error == "geen" && ownershipOk && primitivesOk && textOk && transformsOk;
            ed.WriteMessage($"\nSYMBOL: gelijke geometrie over blocknamen 5+7=12 m; andere inhoud/rotatie/schaal/mirror splitst={symbolGroupingOk}");
            ed.WriteMessage($"\nSYMBOL: export/info behouden vijf correcte rijen={exportOk}/{infoOk}");
            ed.WriteMessage($"\nSYMBOL: legenda gebouwd={built} geometrie={primitivesOk} bronlabels={textOk} rotatie/schaal/mirror={transformsOk} host-owned/MText-only={ownershipOk}; fout={error}");
            ed.WriteMessage($"\nSYMBOL totaal: {(totalOk ? "OK" : "FAIL")}");
        }
        catch (System.Exception ex)
        {
            ed.WriteMessage($"\nSYMBOL fatale fout: {ex.GetType().Name}: {ex.Message}");
        }
    }

    [CommandMethod("NLCSLEGENDAXREFHATCHTEST", CommandFlags.Modal)]
    public void NlcsLegendaXrefHatchTest()
    {
        var doc = AcAp.DocumentManager.MdiActiveDocument;
        if (doc is null) return;
        var db = doc.Database;
        var ed = doc.Editor;
        string dir = Path.GetDirectoryName(db.Filename) ?? Path.GetTempPath();
        const string layer = "N-WE-VH-VERHARDING_ASFALT_ZWART-A";
        try
        {
            var fixtures = new[]
            {
                (Name: "HATCHA", File: "hatch_a.dwg", Pattern: "ANSI31", Scale: 1.25, Angle: 0.25),
                (Name: "HATCHB", File: "hatch_b.dwg", Pattern: "ANSI31", Scale: 1.25, Angle: 0.25),
                (Name: "HATCHC", File: "hatch_c.dwg", Pattern: "ANSI31", Scale: 2.0, Angle: 0.25),
                (Name: "HATCHD", File: "hatch_d.dwg", Pattern: "ANSI31", Scale: 1.25, Angle: 0.75),
                (Name: "HATCHE", File: "hatch_e.dwg", Pattern: "ANSI32", Scale: 1.25, Angle: 0.25)
            };
            foreach (var fixture in fixtures)
            {
                string path = Path.Combine(dir, fixture.File);
                CreateXrefHatchDwg(path, layer, 3, fixture.Pattern, fixture.Scale, fixture.Angle);
                AttachAndInsert(db, path, fixture.Name);
            }

            var settings = LoadGlobalDefaults();
            settings.IncludeXrefLayers = true;
            settings.IncludeQuantities = true;
            settings.HatchScaleFactor = 1.0;
            List<LegendEntry> rows;
            bool groupsOk, exportOk, infoOk;
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var analysis = DrawingAnalyzer.Analyze(db, tr, settings, catalog: LoadCatalog(db));
                rows = analysis.Entries.Where(entry => entry.HatchLayer is not null).ToList();
                var shared = rows.SingleOrDefault(entry =>
                    entry.SourceLayers.Select(source => source.SourceIdentity)
                        .Distinct(StringComparer.OrdinalIgnoreCase).ToHashSet(StringComparer.OrdinalIgnoreCase)
                        .SetEquals(new[] { "HATCHA", "HATCHB" }));
                string[] expectedSources = { "HATCHC", "HATCHD", "HATCHE" };
                var separate = expectedSources.Select(source => rows.SingleOrDefault(entry =>
                    entry.SourceLayers.Any(layerSource => layerSource.SourceIdentity.Equals(
                        source, StringComparison.OrdinalIgnoreCase)))).ToArray();

                bool SharedSampleMatches() =>
                    shared?.HatchLayer is { } key
                    && analysis.HatchSamples.TryGetValue(key, out var sample)
                    && sample.PatternName.Equals("ANSI31", StringComparison.OrdinalIgnoreCase)
                    && Math.Abs(sample.PatternScale - 1.25) < 1e-9
                    && Math.Abs(sample.PatternAngle - 0.25) < 1e-9;
                bool SeparateSamplesMatch()
                {
                    if (separate.Any(entry => entry?.HatchLayer is null))
                        return false;
                    var samples = separate
                        .Select(entry => analysis.HatchSamples[entry!.HatchLayer!])
                        .ToArray();
                    return samples[0].PatternName.Equals("ANSI31", StringComparison.OrdinalIgnoreCase)
                        && Math.Abs(samples[0].PatternScale - 2.0) < 1e-9
                        && Math.Abs(samples[0].PatternAngle - 0.25) < 1e-9
                        && samples[1].PatternName.Equals("ANSI31", StringComparison.OrdinalIgnoreCase)
                        && Math.Abs(samples[1].PatternScale - 1.25) < 1e-9
                        && Math.Abs(samples[1].PatternAngle - 0.75) < 1e-9
                        && samples[2].PatternName.Equals("ANSI32", StringComparison.OrdinalIgnoreCase)
                        && Math.Abs(samples[2].PatternScale - 1.25) < 1e-9
                        && Math.Abs(samples[2].PatternAngle - 0.25) < 1e-9;
                }

                groupsOk = rows.Count == 4 && shared?.Metric.Count == 4
                    && separate.All(entry => entry?.Metric.Count == 2)
                    && rows.Select(entry => entry.RenderIdentity).Distinct(StringComparer.Ordinal).Count() == 4
                    && SharedSampleMatches() && SeparateSamplesMatch();
                exportOk = ExportedCounts(rows).SequenceEqual(new[] { 2, 2, 2, 4 });
                infoOk = InfoRowCountMatches(analysis, settings);
                tr.Commit();
            }

            int built = BuildPlaceXrefLegend(db, settings, out string error, out string groupName, out ObjectId blockId);
            bool ownershipOk = false, renderedPatternsOk = false, renderedColorsOk = false;
            if (error == "geen")
            {
                using var tr = db.TransactionManager.StartTransaction();
                var ownership = AssertLegendHostOwnership(db, tr, groupName, blockId);
                var block = (BlockTableRecord)tr.GetObject(blockId, OpenMode.ForRead);
                var hatches = block.Cast<ObjectId>()
                    .Select(id => tr.GetObject(id, OpenMode.ForRead))
                    .OfType<Hatch>()
                    .Where(hatch => hatch.PatternName.StartsWith("ANSI", StringComparison.OrdinalIgnoreCase))
                    .ToList();
                var actual = hatches.Select(hatch =>
                    $"{hatch.PatternName.ToUpperInvariant()}|{Math.Round(hatch.PatternScale, 6)}|{Math.Round(hatch.PatternAngle, 6)}")
                    .OrderBy(signature => signature, StringComparer.Ordinal)
                    .ToArray();
                var expected = new[]
                {
                    "ANSI31|1.25|0.25",
                    "ANSI31|1.25|0.75",
                    "ANSI31|2|0.25",
                    "ANSI32|1.25|0.25"
                }.OrderBy(signature => signature, StringComparer.Ordinal).ToArray();
                renderedPatternsOk = actual.SequenceEqual(expected);
                var layers = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
                renderedColorsOk = hatches.Count == 4 && hatches.All(hatch =>
                    layers.Has(hatch.Layer)
                    && tr.GetObject(layers[hatch.Layer], OpenMode.ForRead) is LayerTableRecord layerRecord
                    && layerRecord.Color.ColorIndex == 3);
                ownershipOk = ownership.Entities > 0 && ownership.Hatches >= hatches.Count
                    && ownership.DbTexts == 0;
                tr.Commit();
            }

            bool totalOk = groupsOk && exportOk && infoOk && built == 4 && error == "geen"
                && ownershipOk && renderedPatternsOk && renderedColorsOk;
            ed.WriteMessage($"\nHATCH: zelfde patroon/hoek/schaal 1 regel, verschil in patroon/hoek/schaal 4 regels={groupsOk}");
            ed.WriteMessage($"\nHATCH: aantallen/export/info behouden {string.Join("/", ExportedCounts(rows))}={exportOk}/{infoOk}");
            ed.WriteMessage($"\nHATCH: gebouwd={built} werkelijke ANSI-patterns/hoeken/schalen={renderedPatternsOk} groen={renderedColorsOk} host-owned/MText-only={ownershipOk}; fout={error}");
            ed.WriteMessage($"\nHATCH totaal: {(totalOk ? "OK" : "FAIL")}");
        }
        catch (System.Exception ex)
        {
            ed.WriteMessage($"\nHATCH fatale fout: {ex.GetType().Name}: {ex.Message}");
        }
    }

    [CommandMethod("NLCSLEGENDAXREFSAMPLECONFLICTTEST", CommandFlags.Modal)]
    public void NlcsLegendaXrefSampleConflictTest()
    {
        var doc = AcAp.DocumentManager.MdiActiveDocument;
        if (doc is null) return;
        var db = doc.Database;
        var ed = doc.Editor;
        string dir = Path.GetDirectoryName(db.Filename) ?? Path.GetTempPath();
        const string hatchLayer = "N-WE-VH-VERHARDING_ASFALT_ZWART-A";
        const string symbolLayer = "N-WE-VH-OPENVERHARDING_BETONSTRAATSTEEN-S";
        try
        {
            string hatchPath = Path.Combine(dir, "sample_hatch_conflict.dwg");
            string symbolPath = Path.Combine(dir, "sample_symbol_conflict.dwg");
            CreateConflictingHatchDwg(hatchPath, hatchLayer);
            CreateConflictingSymbolDwg(symbolPath, symbolLayer);
            ObjectId hatchReference = AttachAndInsert(db, hatchPath, "HATCHCONFLICT");
            ObjectId symbolReference = AttachAndInsert(db, symbolPath, "SYMBOLCONFLICT");
            var settings = LoadGlobalDefaults();
            settings.IncludeXrefLayers = true;

            bool hatchRejected = RejectsAmbiguousSample(
                db, hatchReference, settings, "meerdere hatch-renderingen");
            bool symbolRejected = RejectsAmbiguousSample(
                db, symbolReference, settings, "meerdere symboolrenderingen");
            ed.WriteMessage($"\nSAMPLECONFLICT hatch first-wins geblokkeerd={hatchRejected}");
            ed.WriteMessage($"\nSAMPLECONFLICT symbool first-wins geblokkeerd={symbolRejected}");
            ed.WriteMessage($"\nSAMPLECONFLICT totaal: {(hatchRejected && symbolRejected ? "OK" : "FAIL")}");
        }
        catch (System.Exception ex)
        {
            ed.WriteMessage($"\nSAMPLECONFLICT fout: {ex.GetType().Name}: {ex.Message}");
        }
    }

    [CommandMethod("NLCSLEGENDAXREFRELOADSETUP", CommandFlags.Modal)]
    public void NlcsLegendaXrefReloadSetup()
    {
        var doc = AcAp.DocumentManager.MdiActiveDocument;
        if (doc is null) return;
        var db = doc.Database;
        var ed = doc.Editor;
        string dir = Path.GetDirectoryName(db.Filename) ?? Path.GetTempPath();
        string sourcePath = Path.Combine(dir, "reload_source.dwg");
        string replacementPath = Path.Combine(dir, "reload_replacement.dwg");
        const string layerName = "N-WE-VH-WEG-G";

        try
        {
            int previousVisretain = System.Convert.ToInt32(AcAp.GetSystemVariable("VISRETAIN"));
            try
            {
                CreateXrefLineDwg(sourcePath, layerName, 1, 5.0);
                CreateXrefLineDwg(replacementPath, layerName, 3, 5.0);
                AcAp.SetSystemVariable("VISRETAIN", 0);
                AttachAndInsert(db, sourcePath, "RELOADTEST");
                var settings = LoadGlobalDefaults();
                settings.IncludeXrefLayers = true;
                settings.IncludeText = true;
                settings.IncludeQuantities = true;
                settings.UnitLength = "m";
                settings.QuantityDecimals = 0;
                int rows = BuildPlaceXrefLegend(db, settings, out string error, out _, out _);
                bool ok = rows == 1 && error == "geen";
                ed.WriteMessage($"\nXREFRELOAD setup: eerst rood 5 m; plaatsing={rows} fout={error} -> {(ok ? "OK" : "FAIL")}");
                ed.WriteMessage($"\nXREFRELOAD vervang na sluiten: {sourcePath} <- {replacementPath}");
            }
            finally
            {
                AcAp.SetSystemVariable("VISRETAIN", previousVisretain);
            }
        }
        catch (System.Exception ex)
        {
            ed.WriteMessage($"\nXREFRELOAD setup fout: {ex.GetType().Name}: {ex.Message}");
        }
    }

    [CommandMethod("NLCSLEGENDAXREFRELOADVERIFY", CommandFlags.Modal)]
    public void NlcsLegendaXrefReloadVerify()
    {
        var doc = AcAp.DocumentManager.MdiActiveDocument;
        if (doc is null) return;
        var db = doc.Database;
        var ed = doc.Editor;
        string legendId;
        string groupName;
        Extents3d oldExtents;
        int analyzedRows;
        int updatedRows;
        double length;
        string swatchLayer;
        string renderedSwatchLayer = string.Empty;
        bool colorOk;
        bool exportOk;
        bool infoOk;
        string updateNote;

        try
        {
            int previousVisretain = System.Convert.ToInt32(AcAp.GetSystemVariable("VISRETAIN"));
            try
            {
                AcAp.SetSystemVariable("VISRETAIN", 0);
                ed.Command("_.-XREF", "_Reload", "RELOADTEST");
            }
            finally
            {
                AcAp.SetSystemVariable("VISRETAIN", previousVisretain);
            }

            using (var tr = db.TransactionManager.StartTransaction())
            {
                var registry = LegendStore.Load(db, tr);
                if (registry.Legends.Count != 1)
                    throw new InvalidOperationException($"Verwachtte één beheerde legenda, gevonden: {registry.Legends.Count}.");
                var definition = registry.Legends[0];
                legendId = definition.Id;
                groupName = definition.GroupName;
                if (!LegendManagement.TryGetGroupExtents(db, tr, groupName, out oldExtents))
                    throw new InvalidOperationException("De geplaatste xref-legenda heeft geen extents.");

                var excluded = LegendManagement.CollectManagedIds(db, tr, registry);
                var analysis = DrawingAnalyzer.Analyze(
                    db, tr, definition.Settings, catalog: DescriptionCatalog.Default(), excludedIds: excluded);
                var rows = analysis.Entries.Where(entry => entry.GeometryLayer is not null).ToList();
                analyzedRows = rows.Count;
                var row = rows.SingleOrDefault();
                length = row?.Metric.Length ?? 0;
                swatchLayer = row?.GeometryLayer ?? string.Empty;
                colorOk = row is not null && EntryAci(analysis, row) == "3";
                exportOk = ExportedLengths(rows).SequenceEqual(new[] { 5.0 });
                infoOk = InfoRowCountMatches(analysis, definition.Settings);
                var result = BuildManagedLegend(db, tr, registry, definition, out updatedRows, out updateNote);
                if (result != UpdateResult.Updated)
                    throw new InvalidOperationException($"Legenda-update mislukt: {result}; {updateNote}");
                LegendStore.Save(db, tr, registry);
                tr.Commit();
            }
            PurgePending(db);

            bool identityOk;
            bool positionOk;
            (int Members, int Entities, int Hatches, int MTexts, int DbTexts) ownership;
            bool textOk;
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var registry = LegendStore.Load(db, tr);
                var definition = registry.Legends.Single();
                identityOk = definition.Id == legendId && definition.GroupName == groupName;
                positionOk = LegendManagement.TryGetGroupExtents(db, tr, groupName, out var newExtents)
                    && System.Math.Abs(oldExtents.MinPoint.X - newExtents.MinPoint.X) < 1e-6
                    && System.Math.Abs(oldExtents.MaxPoint.Y - newExtents.MaxPoint.Y) < 1e-6;
                var groupDictionary = (DBDictionary)tr.GetObject(db.GroupDictionaryId, OpenMode.ForRead);
                var group = (Group)tr.GetObject(groupDictionary.GetAt(groupName), OpenMode.ForRead);
                var blockReference = group.GetAllEntityIds()
                    .Select(id => tr.GetObject(id, OpenMode.ForRead))
                    .OfType<BlockReference>()
                    .Single();
                var blockId = blockReference.BlockTableRecord;
                var block = (BlockTableRecord)tr.GetObject(blockId, OpenMode.ForRead);
                renderedSwatchLayer = block.Cast<ObjectId>()
                    .Select(id => tr.GetObject(id, OpenMode.ForRead))
                    .OfType<Polyline>()
                    .Select(polyline => polyline.Layer)
                    .Single(layer => layer.Equals(swatchLayer, System.StringComparison.OrdinalIgnoreCase)
                        || layer.StartsWith(swatchLayer + " #R", System.StringComparison.OrdinalIgnoreCase));
                ownership = AssertLegendHostOwnership(db, tr, groupName, blockId);
                textOk = ReadLegendBlockTexts(tr, blockId)
                    .Any(text => text.Contains("5 m", System.StringComparison.Ordinal));
                tr.Commit();
            }
            colorOk &= ReadAci(db, renderedSwatchLayer) == "3";
            bool ownershipOk = ownership.Members > 0 && ownership.Entities > 0
                && ownership.MTexts > 0 && ownership.DbTexts == 0;
            bool allOk = analyzedRows == 1 && updatedRows == 1 && System.Math.Abs(length - 5.0) < 0.01
                && colorOk && exportOk && infoOk && identityOk && positionOk && ownershipOk && textOk;
            ed.WriteMessage($"\nXREFRELOAD na heropenen: rijen={analyzedRows} hoeveelheid={length:0.#} m kleur={ReadAci(db, renderedSwatchLayer)} laag={renderedSwatchLayer} export={exportOk} info={infoOk} -> {(analyzedRows == 1 && length == 5.0 && colorOk && exportOk && infoOk ? "OK" : "FAIL")}");
            ed.WriteMessage($"\nXREFRELOAD update: rijen={updatedRows} id/groep={identityOk} positie={positionOk} -> {(updatedRows == 1 && identityOk && positionOk ? "OK" : "FAIL")}");
            ed.WriteMessage($"\nXREFRELOAD ownership: entities={ownership.Entities} MText={ownership.MTexts} DBText={ownership.DbTexts} tekst={textOk} -> {(ownershipOk && textOk ? "OK" : "FAIL")}");
            ed.WriteMessage($"\nXREFRELOAD totaal: {(allOk ? "OK" : "FAIL")}");
        }
        catch (System.Exception ex)
        {
            ed.WriteMessage($"\nXREFRELOAD verify fout: {ex.GetType().Name}: {ex.Message}");
        }
    }

    // Exerciset de Enter/default-instellingenflow en exact dezelfde PlaceLegendAtPoint-methode
    // als NLCSLEGENDA. De xref-bron blijft als afzonderlijke Database geladen; de ownership-walk
    // controleert daarna elk geplaatst object, inclusief blokken, lagen, tekststijlen en hatches.
    [CommandMethod("NLCSLEGENDAXREFPLACEMENTTEST", CommandFlags.Modal)]
    public void NlcsLegendaXrefPlacementTest()
    {
        var doc = AcAp.DocumentManager.MdiActiveDocument;
        if (doc is null) return;
        var activeDb = doc.Database;
        var db = activeDb;
        var ed = doc.Editor;
        const string layer = "N-WE-VH-WEG-G";
        const string symbolLayer = "N-WE-VH-WEG-S";
        const string symbolBlock = "PLACEMENT_SYMBOL";
        const string rollbackMarker = "NLCSLEGENDAXREFPLACEMENTTEST_ROLLBACK";
        string dir = Path.GetDirectoryName(activeDb.Filename) ?? Path.GetTempPath();
        string hostPath = Path.Combine(dir, "placement_host.dwg");
        var previousWorkingDatabase = HostApplicationServices.WorkingDatabase;

        var settings = LoadGlobalDefaults();
        settings.IncludeXrefLayers = true;
        settings.IncludeQuantities = true;
        settings.IncludeText = true;
        settings.IncludeGroupHeaders = true;
        settings.TextHeightMm = 3.0;
        settings.HeaderTextHeightMm = 4.0;
        settings.TitleTextHeightMm = 6.0;
        settings.ExplodeOnPlace = false;
        settings.IncludeTitle = true;
        settings.Title = "XREF-PLACEMENT-ACCEPTANCE";
        settings.UnitLength = "m";
        settings.QuantityDecimals = 0;

        if (!PromptOptions(ed, settings, out var selection))
        {
            ed.WriteMessage("\nPLACEMENT prompt: geannuleerd.");
            return;
        }
        bool enterDefault = selection is null;

        HostApplicationServices.WorkingDatabase = db;
        try
        {
            string a = Path.Combine(dir, "placement_a.dwg");
            string b = Path.Combine(dir, "placement_b.dwg");
            string c = Path.Combine(dir, "placement_c.dwg");
            using var sourceDb = CreateXrefLineDatabase(
                a, layer, 1, 5.0, out var sourceLineId,
                symbolLayerName: symbolLayer, symbolBlockName: symbolBlock);
            CreateXrefLineDwg(b, layer, 1, 7.0, symbolLayerName: symbolLayer, symbolBlockName: symbolBlock);
            CreateXrefLineDwg(c, layer, 3, 3.0, symbolLayerName: symbolLayer, symbolBlockName: symbolBlock);
            AttachAndInsert(db, a, "PLACEMENTA");
            AttachAndInsert(db, b, "PLACEMENTB");
            AttachAndInsert(db, c, "PLACEMENTC");

            bool sourceDatabaseOk = sourceLineId.Database == sourceDb && sourceDb != db;
            string error = "geen";
            string redLayer = string.Empty, greenLayer = string.Empty, groupName = string.Empty;
            int analysisRows = 0, renderedRows = 0;
            double redLength = 0, greenLength = 0;
            bool provenanceOk = false, actualTextOk = false, customHeightsOk = false, symbolOk = false;
            (int Members, int Entities, int Hatches, int MTexts, int DbTexts) ownership = default;
            (int Members, int Entities, int Hatches, int MTexts, int DbTexts) explodedOwnership = default;
            (int dbText, int mText) explodedText = default;
            bool explodedPlacementOk = false;
            string explodedError = "geen";

            try
            {
                using var tr = db.TransactionManager.StartTransaction();
                var registry = LegendStore.Load(db, tr);
                var excluded = LegendManagement.CollectManagedIds(db, tr, registry);
                var analysis = DrawingAnalyzer.Analyze(db, tr, settings, selection, LoadCatalog(db), excluded);
                var rows = analysis.Entries.Where(entry => entry.GeometryLayer is not null).ToList();
                analysisRows = rows.Count;
                var red = rows.SingleOrDefault(entry => EntryAci(analysis, entry) == "1");
                var green = rows.SingleOrDefault(entry => EntryAci(analysis, entry) == "3");
                if (red is null || green is null)
                    throw new InvalidOperationException("Verwachte rode en groene render-partitie ontbreekt.");

                redLength = red.Metric.Length;
                greenLength = green.Metric.Length;
                redLayer = red.GeometryLayer!;
                greenLayer = green.GeometryLayer!;
                var redSources = red.SourceLayers.Select(source => source.SourceIdentity)
                    .ToHashSet(System.StringComparer.OrdinalIgnoreCase);
                var greenSources = green.SourceLayers.Select(source => source.SourceIdentity)
                    .ToHashSet(System.StringComparer.OrdinalIgnoreCase);
                provenanceOk = redSources.SetEquals(new[] { "PLACEMENTA", "PLACEMENTB" })
                    && greenSources.SetEquals(new[] { "PLACEMENTC" })
                    && red.RenderIdentity != green.RenderIdentity;

                var blockId = LegendBuilder.BuildBlock(db, tr, analysis, settings, out renderedRows, out var issues);
                if (issues.Count > 0)
                    throw new InvalidOperationException("Tekenfouten in legenda: "
                        + string.Join("; ", issues.Select(issue =>
                            $"{issue.Entry} [{issue.SourceLayer}; {issue.Kind}]: {issue.Reason}")));
                var modelSpace = (BlockTableRecord)tr.GetObject(
                    SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForWrite);
                var preview = new BlockReference(Point3d.Origin, blockId);
                modelSpace.AppendEntity(preview);
                tr.AddNewlyCreatedDBObject(preview, true);
                var pointPrompt = ed.GetPoint("\nPlaats de xref-legenda (acceptance):");
                if (pointPrompt.Status != PromptStatus.OK
                    || pointPrompt.Value.DistanceTo(new Point3d(200, 0, 0)) > 1e-8)
                    throw new InvalidOperationException("Het gescripte plaatsingspunt ontbreekt of is onjuist.");
                var definition = PlaceLegendAtPoint(
                    tr, db, registry, settings, selection, blockId, preview, pointPrompt.Value);
                groupName = definition.GroupName;
                ownership = AssertLegendHostOwnership(db, tr, groupName, blockId);

                var legendBlock = (BlockTableRecord)tr.GetObject(blockId, OpenMode.ForRead);
                int symbolPrimitives = 0, symbolTexts = 0;
                foreach (ObjectId id in legendBlock)
                {
                    var entity = tr.GetObject(id, OpenMode.ForRead);
                    if (entity is Circle)
                        symbolPrimitives++;
                    else if (entity is MText text
                        && text.Contents.Contains("XREF-PLACEMENT-SYMBOL", System.StringComparison.Ordinal))
                        symbolTexts++;
                }
                symbolOk = symbolPrimitives > 0 && symbolTexts > 0;

                var blockTexts = ReadLegendBlockTexts(tr, blockId);
                actualTextOk = blockTexts.Any(text => text.Contains("12 m", System.StringComparison.Ordinal))
                    && blockTexts.Any(text => text.Contains("3 m", System.StringComparison.Ordinal));
                var block = (BlockTableRecord)tr.GetObject(blockId, OpenMode.ForRead);
                var mtexts = block.Cast<ObjectId>()
                    .Select(id => tr.GetObject(id, OpenMode.ForRead))
                    .OfType<MText>()
                    .ToList();
                bool hasBody = mtexts.Any(text => text.Contents.Contains(red.Description, System.StringComparison.OrdinalIgnoreCase)
                    && System.Math.Abs(text.TextHeight - settings.ToModel(3.0)) < 1e-8);
                bool hasHeader = mtexts.Any(text => text.Contents.Contains(settings.LabelNieuw, System.StringComparison.OrdinalIgnoreCase)
                    && System.Math.Abs(text.TextHeight - settings.ToModel(4.0)) < 1e-8);
                bool hasTitle = mtexts.Any(text => text.Contents.Contains(settings.Title, System.StringComparison.Ordinal)
                    && System.Math.Abs(text.TextHeight - settings.ToModel(6.0)) < 1e-8);
                customHeightsOk = hasBody && hasHeader && hasTitle;
                tr.Commit();
            }
            catch (System.Exception ex)
            {
                error = ex.GetType().Name + ": " + ex.Message;
            }

            try
            {
                var explodedSettings = settings.Clone();
                explodedSettings.ExplodeOnPlace = true;
                using var tr = db.TransactionManager.StartTransaction();
                var registry = LegendStore.Load(db, tr);
                var excluded = LegendManagement.CollectManagedIds(db, tr, registry);
                var analysis = DrawingAnalyzer.Analyze(db, tr, explodedSettings, selection, LoadCatalog(db), excluded);
                var blockId = LegendBuilder.BuildBlock(db, tr, analysis, explodedSettings, out int explodedRows, out var issues);
                if (issues.Count > 0)
                    throw new InvalidOperationException("Tekenfouten in geëxplodeerde legenda: "
                        + string.Join("; ", issues.Select(issue =>
                            $"{issue.Entry} [{issue.SourceLayer}; {issue.Kind}]: {issue.Reason}")));
                var modelSpace = (BlockTableRecord)tr.GetObject(
                    SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForWrite);
                var preview = new BlockReference(Point3d.Origin, blockId);
                modelSpace.AppendEntity(preview);
                tr.AddNewlyCreatedDBObject(preview, true);
                var definition = PlaceLegendAtPoint(
                    tr, db, registry, explodedSettings, selection, blockId, preview, new Point3d(400, 0, 0));
                explodedOwnership = AssertLegendHostOwnership(db, tr, definition.GroupName, blockId);
                explodedText = CountTextInGroup(db, tr, definition.GroupName);
                explodedPlacementOk = explodedRows == 2
                    && explodedOwnership.Members > 0 && explodedOwnership.Entities > 0
                    && explodedOwnership.MTexts > 0 && explodedOwnership.DbTexts == 0
                    && explodedText.dbText == 0 && explodedText.mText > 0;
                // No commit: verify real exploded output while leaving the save/reopen fixture unchanged.
            }
            catch (System.Exception ex)
            {
                explodedError = ex.GetType().Name + ": " + ex.Message;
            }

            string redAci = string.IsNullOrEmpty(redLayer) ? "geen" : ReadAci(db, redLayer);
            string greenAci = string.IsNullOrEmpty(greenLayer) ? "geen" : ReadAci(db, greenLayer);
            bool rowsOk = analysisRows == 2 && renderedRows == 2
                && System.Math.Abs(redLength - 12.0) < 0.01
                && System.Math.Abs(greenLength - 3.0) < 0.01;
            bool ownershipOk = ownership.Members > 0 && ownership.Entities > 0
                && ownership.MTexts > 0 && ownership.DbTexts == 0;
            bool colorsOk = redAci == "1" && greenAci == "3";
            bool placementOk = error == "geen" && explodedError == "geen" && sourceDatabaseOk && enterDefault
                && rowsOk && provenanceOk && actualTextOk && customHeightsOk && ownershipOk && colorsOk && symbolOk;

            ed.WriteMessage($"\nPLACEMENT Enter/default: selection={(selection is null ? "null" : selection.Length.ToString())} -> {(enterDefault ? "OK" : "FAIL")}");
            ed.WriteMessage($"\nPLACEMENT bronnen: bron-db-eigen={sourceDatabaseOk} A+B rood={redLength:0.#} m C groen={greenLength:0.#} m -> {(provenanceOk ? "OK" : "FAIL")}");
            ed.WriteMessage($"\nPLACEMENT rijen/hoeveelheden: analyse={analysisRows} gebouwd={renderedRows} rood={redAci}/{redLength:0.#} m groen={greenAci}/{greenLength:0.#} m tekst={actualTextOk} -> {(rowsOk && colorsOk && actualTextOk ? "OK" : "FAIL")}");
            ed.WriteMessage($"\nPLACEMENT ownership: group={ownership.Members} entities={ownership.Entities} hatches={ownership.Hatches} MText={ownership.MTexts} DBText={ownership.DbTexts} -> {(ownershipOk ? "OK" : "FAIL")}");
            ed.WriteMessage($"\nPLACEMENT xref-symbool: primitives/text={symbolOk} -> {(symbolOk ? "OK" : "FAIL")}");
            ed.WriteMessage($"\nPLACEMENT xref exploded: group={explodedOwnership.Members} entities={explodedOwnership.Entities} MText={explodedText.mText} DBText={explodedText.dbText} host-owned={explodedOwnership.DbTexts == 0} -> {(explodedPlacementOk ? "OK" : "FAIL")}; fout={explodedError}");
            ed.WriteMessage($"\nPLACEMENT custom text height 3/4/6 mm: {(customHeightsOk ? "OK" : "FAIL")}; fout={error}");

            bool rollbackAttempted = false;
            string rollbackError = string.Empty;
            var beforeRollback = ReadPlacementFingerprint(db);
            try
            {
                using var tr = db.TransactionManager.StartTransaction();
                var registry = LegendStore.Load(db, tr);
                var excluded = LegendManagement.CollectManagedIds(db, tr, registry);
                var analysis = DrawingAnalyzer.Analyze(db, tr, settings, selection, LoadCatalog(db), excluded);
                var blockId = LegendBuilder.BuildBlock(db, tr, analysis, settings, out _, out _);
                var modelSpace = (BlockTableRecord)tr.GetObject(
                    SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForWrite);
                var preview = new BlockReference(Point3d.Origin, blockId);
                modelSpace.AppendEntity(preview);
                tr.AddNewlyCreatedDBObject(preview, true);
                PlaceLegendAtPoint(tr, db, registry, settings, selection, blockId, preview, new Point3d(300, 0, 0));
                rollbackAttempted = true;
                throw new InvalidOperationException(rollbackMarker);
            }
            catch (System.Exception ex) when (ex.Message == rollbackMarker) { }
            catch (System.Exception ex) { rollbackError = ex.GetType().Name + ": " + ex.Message; }
            bool rollbackOk = rollbackAttempted && rollbackError.Length == 0
                && beforeRollback == ReadPlacementFingerprint(db);
            ed.WriteMessage($"\nPLACEMENT rollback: {(rollbackOk ? "atomic" : "FAIL")} fout={rollbackError}");
            ed.WriteMessage($"\nPLACEMENT totaal: {(placementOk && explodedPlacementOk && rollbackOk ? "OK" : "FAIL")}; group={groupName}");
            ed.WriteMessage($"\nPLACEMENT hostbestand na SAVEAS: {hostPath}");
        }
        catch (System.Exception ex)
        {
            ed.WriteMessage($"\nPLACEMENT fatale fout: {ex.GetType().Name}: {ex.Message}");
        }
        finally
        {
            HostApplicationServices.WorkingDatabase = previousWorkingDatabase;
        }
    }

    [CommandMethod("NLCSLEGENDAXREFPLACEMENTVERIFY", CommandFlags.Modal)]
    public void NlcsLegendaXrefPlacementVerify()
    {
        var doc = AcAp.DocumentManager.MdiActiveDocument;
        if (doc is null) return;
        var db = doc.Database;
        var ed = doc.Editor;
        string error = "geen";
        string redLayer = string.Empty, greenLayer = string.Empty;
        string originalId = string.Empty, originalGroup = string.Empty;
        int analyzedRows = 0, updatedRows = 0;
        double redLength = 0, greenLength = 0;
        bool rowsOk = false, stableIdentity = false, stablePosition = false;
        bool settingsOk = false, textOk = false, ownershipOk = false;
        (int Members, int Entities, int Hatches, int MTexts, int DbTexts) ownership = default;

        try
        {
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var registry = LegendStore.Load(db, tr);
                if (registry.Legends.Count != 1)
                    throw new InvalidOperationException($"Verwachtte één beheerde legenda, gevonden: {registry.Legends.Count}.");
                var definition = registry.Legends[0];
                originalId = definition.Id;
                originalGroup = definition.GroupName;
                if (!LegendManagement.TryGetGroupExtents(db, tr, originalGroup, out var oldExtents))
                    throw new InvalidOperationException("Bestaande legenda-extents ontbreken.");

                var excluded = LegendManagement.CollectManagedIds(db, tr, registry);
                var analysis = DrawingAnalyzer.Analyze(
                    db, tr, definition.Settings, catalog: DescriptionCatalog.Default(), excludedIds: excluded);
                var rows = analysis.Entries.Where(entry => entry.GeometryLayer is not null).ToList();
                analyzedRows = rows.Count;
                var red = rows.SingleOrDefault(entry => EntryAci(analysis, entry) == "1");
                var green = rows.SingleOrDefault(entry => EntryAci(analysis, entry) == "3");
                if (red is null || green is null)
                    throw new InvalidOperationException("Na heropenen ontbreken de rode/groene render-partities.");
                redLayer = red.GeometryLayer!;
                greenLayer = green.GeometryLayer!;
                redLength = red.Metric.Length;
                greenLength = green.Metric.Length;
                rowsOk = rows.Count == 2
                    && System.Math.Abs(redLength - 12.0) < 0.01
                    && System.Math.Abs(greenLength - 3.0) < 0.01;

                settingsOk = definition.Settings.IncludeXrefLayers
                    && definition.Settings.TextHeightMm == 3.0
                    && definition.Settings.HeaderTextHeightMm == 4.0
                    && definition.Settings.TitleTextHeightMm == 6.0
                    && !definition.Settings.ExplodeOnPlace;
                var oldSettings = definition.Settings.Clone();
                var result = BuildManagedLegend(db, tr, registry, definition, out updatedRows, out var note);
                if (result != UpdateResult.Updated)
                    throw new InvalidOperationException($"Update-resultaat {result}: {note}");
                LegendStore.Save(db, tr, registry);
                stableIdentity = definition.Id == originalId && definition.GroupName == originalGroup
                    && definition.Settings.TextHeightMm == oldSettings.TextHeightMm
                    && definition.Settings.HeaderTextHeightMm == oldSettings.HeaderTextHeightMm
                    && definition.Settings.TitleTextHeightMm == oldSettings.TitleTextHeightMm;
                stablePosition = LegendManagement.TryGetGroupExtents(db, tr, originalGroup, out var newExtents)
                    && System.Math.Abs(oldExtents.MinPoint.X - newExtents.MinPoint.X) < 1e-6
                    && System.Math.Abs(oldExtents.MaxPoint.Y - newExtents.MaxPoint.Y) < 1e-6;
                tr.Commit();
            }
            PurgePending(db);

            using (var tr = db.TransactionManager.StartTransaction())
            {
                var registry = LegendStore.Load(db, tr);
                var definition = registry.Legends.Single();
                var groupDictionary = (DBDictionary)tr.GetObject(db.GroupDictionaryId, OpenMode.ForRead);
                var group = (Group)tr.GetObject(groupDictionary.GetAt(definition.GroupName), OpenMode.ForRead);
                var blockReference = group.GetAllEntityIds()
                    .Cast<ObjectId>()
                    .Select(id => tr.GetObject(id, OpenMode.ForRead))
                    .OfType<BlockReference>()
                    .Single();
                var blockId = blockReference.BlockTableRecord;
                ownership = AssertLegendHostOwnership(db, tr, definition.GroupName, blockId);
                ownershipOk = ownership.Members > 0 && ownership.Entities > 0
                    && ownership.MTexts > 0 && ownership.DbTexts == 0;
                var texts = ReadLegendBlockTexts(tr, blockId);
                textOk = texts.Any(text => text.Contains("12 m", System.StringComparison.Ordinal))
                    && texts.Any(text => text.Contains("3 m", System.StringComparison.Ordinal));
                var textObjects = ((BlockTableRecord)tr.GetObject(blockId, OpenMode.ForRead))
                    .Cast<ObjectId>()
                    .Select(id => tr.GetObject(id, OpenMode.ForRead))
                    .OfType<MText>()
                    .ToList();
                textOk &= textObjects.Any(text => text.Contents.Contains(definition.Settings.Title, System.StringComparison.Ordinal)
                    && System.Math.Abs(text.TextHeight - definition.Settings.ToModel(6.0)) < 1e-8)
                    && textObjects.Any(text => text.Contents.Contains(definition.Settings.LabelNieuw, System.StringComparison.OrdinalIgnoreCase)
                    && System.Math.Abs(text.TextHeight - definition.Settings.ToModel(4.0)) < 1e-8);
                tr.Commit();
            }

            string redAci = ReadAci(db, redLayer);
            string greenAci = ReadAci(db, greenLayer);
            var fingerprint = ReadPlacementFingerprint(db);
            bool blocksOk = fingerprint.Groups == 1 && fingerprint.LegendBlocks == 1 && fingerprint.RegistryEntries == 1;
            bool allOk = error == "geen" && analyzedRows == 2 && updatedRows == 2 && rowsOk
                && stableIdentity && stablePosition && settingsOk && textOk && ownershipOk
                && redAci == "1" && greenAci == "3" && blocksOk;
            ed.WriteMessage($"\nPLACEMENTVERIFY heropenen: regels={analyzedRows} update={updatedRows} rood={redLength:0.#} m/{redAci} groen={greenLength:0.#} m/{greenAci} -> {(rowsOk && redAci == "1" && greenAci == "3" ? "OK" : "FAIL")}");
            ed.WriteMessage($"\nPLACEMENTVERIFY behoud: id/groep={stableIdentity} positie={stablePosition} instellingen 3/4/6={settingsOk} -> {(stableIdentity && stablePosition && settingsOk ? "OK" : "FAIL")}");
            ed.WriteMessage($"\nPLACEMENTVERIFY ownership: entities={ownership.Entities} MText={ownership.MTexts} DBText={ownership.DbTexts} groups={fingerprint.Groups} blocks={fingerprint.LegendBlocks} -> {(ownershipOk && blocksOk ? "OK" : "FAIL")}");
            ed.WriteMessage($"\nPLACEMENTVERIFY totaal: {(allOk ? "OK" : "FAIL")}; fout={error}");
        }
        catch (System.Exception ex)
        {
            error = ex.GetType().Name + ": " + ex.Message;
            ed.WriteMessage($"\nPLACEMENTVERIFY fout: {error}");
        }
    }
}
