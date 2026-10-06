using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using NlcsLegenda.Core;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Core.Application;

namespace NlcsLegenda.Plugin;

public partial class Commands
{
    // Telt DBText en MText binnen een beheerde legenda-groep: de groep bevat óf een behouden
    // blokreferentie (we dalen af in de blokdefinitie) óf losse geëxplodeerde entiteiten.
    private static (int dbText, int mText) CountTextInGroup(Database db, Transaction tr, string groupName)
    {
        int dbt = 0, mt = 0;
        var gd = (DBDictionary)tr.GetObject(db.GroupDictionaryId, OpenMode.ForRead);
        if (!gd.Contains(groupName) || tr.GetObject(gd.GetAt(groupName), OpenMode.ForRead) is not Group g)
            return (0, 0);

        void Count(ObjectId id, int depth)
        {
            if (depth > 8 || tr.GetObject(id, OpenMode.ForRead) is not Entity e || e.IsErased)
                return;
            if (e is DBText) dbt++;
            else if (e is MText) mt++;
            else if (e is BlockReference br && !br.BlockTableRecord.IsNull
                     && tr.GetObject(br.BlockTableRecord, OpenMode.ForRead) is BlockTableRecord r)
                foreach (ObjectId cid in r) Count(cid, depth + 1);
        }

        foreach (ObjectId id in g.GetAllEntityIds()) Count(id, 0);
        return (dbt, mt);
    }

    // Telt DBText in alle legenda-blokdefinities (NLCS_LEGENDA_*), ongeacht of ze nog gebruikt
    // worden. Zo zien we of een update geen oude DBText-blokdefinitie laat rondslingeren.
    private static int CountDbTextInLegendBlocks(Database db, Transaction tr)
    {
        int dbt = 0;
        var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
        foreach (ObjectId btrId in bt)
        {
            if (tr.GetObject(btrId, OpenMode.ForRead) is not BlockTableRecord btr) continue;
            if (!btr.Name.StartsWith("NLCS_LEGENDA_", StringComparison.OrdinalIgnoreCase)) continue;
            foreach (ObjectId id in btr)
                if (tr.GetObject(id, OpenMode.ForRead) is DBText) dbt++;
        }
        return dbt;
    }

    // Maakt NLCS-bronlagen met geometrie zodat een legenda alle tekstcategorieën bevat: twee
    // statussen (statuskop) en twee hoofdgroepen (subkop), met een lange omschrijving (wrapping).
    private static void EnsureMTextFixtureLayers(Database db, Transaction tr)
    {
        var lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForWrite);
        var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForWrite);
        string[] layers =
        {
            "N-WE-VH-MTEKSTVERHARDINGMETLANGEOMSCHRIJVINGVOORWRAP-G",
            "N-WE-RI-MTEKSTRIOOL-G",
            "B-WE-VH-MTEKSTBESTAAND-G"
        };
        double x = 0;
        foreach (var n in layers)
        {
            ObjectId layerId;
            if (lt.Has(n)) layerId = lt[n];
            else { var r = new LayerTableRecord { Name = n }; layerId = lt.Add(r); tr.AddNewlyCreatedDBObject(r, true); }
            var line = new Line(new Point3d(x, 0, 0), new Point3d(x + 10, 0, 0)) { LayerId = layerId };
            ms.AppendEntity(line); tr.AddNewlyCreatedDBObject(line, true);
            x += 20;
        }
    }

    private static LegendSettings MTextFixtureSettings()
    {
        var s = LoadGlobalDefaults();
        s.IncludeTitle = true;
        if (string.IsNullOrWhiteSpace(s.Title)) s.Title = "Legenda";
        s.IncludeGroupHeaders = true;
        s.IncludeHoofdgroepHeaders = true;
        s.IncludeText = true;
        s.IncludeQuantities = true;
        s.IncludeFooter = true;
        s.IncludeDate = true;
        s.IncludeScaleBar = true;
        s.IncludeRemarks = true;
        if (string.IsNullOrWhiteSpace(s.RemarksTitle)) s.RemarksTitle = "Opmerkingen";
        s.RemarksText = "Eerste opmerking met wat tekst.\nTweede regel.";
        s.BlankEntries.Add(new BlankEntry());
        return s;
    }

    // Zero-DBText op een echte host: bouwt een representatieve legenda met alle tekstcategorieën
    // (titel, statuskop, hoofdgroepkop, omschrijving, wrapping, hoeveelheid, footer/datum,
    // schaalbalk, opmerkingen, blanco) en controleert dat de gebouwde geometrie geen DBText bevat.
    // Doet dit zowel voor een behouden blok als voor een geëxplodeerde legenda.
    [CommandMethod("NLCSLEGENDAMTEXTTEST", CommandFlags.Modal)]
    public void NlcsLegendaMTextTest()
    {
        var doc = AcApp.DocumentManager.MdiActiveDocument;
        if (doc is null) return;
        var ed = doc.Editor;
        var db = doc.Database;
        try
        {
            using (var tr = db.TransactionManager.StartTransaction())
            {
                EnsureMTextFixtureLayers(db, tr);
                tr.Commit();
            }

            (int dbt, int mt, int rows) Build(bool explode)
            {
                var s = MTextFixtureSettings();
                s.ExplodeOnPlace = explode;
                using var tr = db.TransactionManager.StartTransaction();
                var reg = LegendStore.Load(db, tr);
                var def = IsoDef(reg, s);
                reg.Add(def);
                BuildManagedLegend(db, tr, reg, def, out int rows, out _);
                LegendStore.Save(db, tr, reg);
                var (dbt, mt) = CountTextInGroup(db, tr, def.GroupName);
                tr.Commit();
                PurgePending(db);
                return (dbt, mt, rows);
            }

            var retained = Build(explode: false);
            bool okR = retained.dbt == 0 && retained.mt > 0;
            ed.WriteMessage($"\nMTEXT: behouden regels={retained.rows} DBText={retained.dbt} MText={retained.mt} -> {(okR ? "OK" : "FAIL")}");

            var exploded = Build(explode: true);
            bool okE = exploded.dbt == 0 && exploded.mt > 0;
            ed.WriteMessage($"\nMTEXT: geexplodeerd regels={exploded.rows} DBText={exploded.dbt} MText={exploded.mt} -> {(okE ? "OK" : "FAIL")}");

            int orphan;
            using (var tr = db.TransactionManager.StartTransaction())
            {
                orphan = CountDbTextInLegendBlocks(db, tr);
                tr.Commit();
            }
            ed.WriteMessage($"\nMTEXT: DBText in alle legenda-blokken={orphan} -> {(orphan == 0 ? "OK" : "FAIL")}");
            ed.WriteMessage($"\nMTEXT: totaal -> {(okR && okE && orphan == 0 ? "OK" : "FAIL")}");
        }
        catch (System.Exception ex)
        {
            ed.WriteMessage($"\nMTEXT error: {ex.Message}");
        }
    }

    // Zero-DBText met een bronsymbool dat intern DBText bevat. Bouwt een symboolblok met DBText,
    // MText en een lijn, plaatst het op een NLCS-symboollaag en controleert dat de gebouwde legenda
    // geen DBText bevat en dat de symbooltekst als MText is overgenomen (behouden én geëxplodeerd).
    [CommandMethod("NLCSLEGENDASYMBOOLTEKSTTEST", CommandFlags.Modal)]
    public void NlcsLegendaSymboolTekstTest()
    {
        var doc = AcApp.DocumentManager.MdiActiveDocument;
        if (doc is null) return;
        var ed = doc.Editor;
        var db = doc.Database;
        const string dbTextMark = "SYMDBTEKST";
        const string mTextMark = "SYMMTEKST";
        try
        {
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForWrite);
                if (!bt.Has("SYMTEKSTBLOK"))
                {
                    var sbtr = new BlockTableRecord { Name = "SYMTEKSTBLOK", Origin = Point3d.Origin };
                    bt.Add(sbtr); tr.AddNewlyCreatedDBObject(sbtr, true);
                    var ln = new Line(new Point3d(0, 0, 0), new Point3d(1, 1, 0));
                    sbtr.AppendEntity(ln); tr.AddNewlyCreatedDBObject(ln, true);
                    var t = new DBText { TextString = dbTextMark, Height = 0.3, Position = new Point3d(0, 0, 0) };
                    t.SetDatabaseDefaults();
                    sbtr.AppendEntity(t); tr.AddNewlyCreatedDBObject(t, true);
                    var inner = new MText { Contents = mTextMark, TextHeight = 0.3, Location = new Point3d(0.5, 0.5, 0) };
                    inner.SetDatabaseDefaults();
                    sbtr.AppendEntity(inner); tr.AddNewlyCreatedDBObject(inner, true);
                }
                var lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForWrite);
                const string symLayer = "N-WE-RI-SYMTEKST-S";
                if (!lt.Has(symLayer)) { var lr = new LayerTableRecord { Name = symLayer }; lt.Add(lr); tr.AddNewlyCreatedDBObject(lr, true); }
                var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForWrite);
                var bref = new BlockReference(new Point3d(0, 0, 0), bt["SYMTEKSTBLOK"]) { Layer = symLayer };
                ms.AppendEntity(bref); tr.AddNewlyCreatedDBObject(bref, true);
                tr.Commit();
            }

            (int dbt, int mt, bool db2mt, bool keptMt) Build(bool explode)
            {
                var s = LoadGlobalDefaults();
                s.InsertSymbolBlocks = true;
                s.ExplodeOnPlace = explode;
                using var tr = db.TransactionManager.StartTransaction();
                var reg = LegendStore.Load(db, tr);
                var def = IsoDef(reg, s);
                reg.Add(def);
                BuildManagedLegend(db, tr, reg, def, out _, out _);
                LegendStore.Save(db, tr, reg);
                var (dbt, mt) = CountTextInGroup(db, tr, def.GroupName);
                var texts = CollectMTextContents(db, tr, def.GroupName);
                tr.Commit();
                PurgePending(db);
                return (dbt, mt, texts.Any(x => x.Contains(dbTextMark)), texts.Any(x => x.Contains(mTextMark)));
            }

            var r = Build(explode: false);
            ed.WriteMessage($"\nSYMTEKST: behouden DBText={r.dbt} MText={r.mt} bron-DBText->MText={r.db2mt} bron-MText-behouden={r.keptMt} -> {(r.dbt == 0 && r.db2mt && r.keptMt ? "OK" : "FAIL")}");
            var e = Build(explode: true);
            ed.WriteMessage($"\nSYMTEKST: geexplodeerd DBText={e.dbt} MText={e.mt} bron-DBText->MText={e.db2mt} -> {(e.dbt == 0 && e.db2mt ? "OK" : "FAIL")}");
            ed.WriteMessage($"\nSYMTEKST: totaal -> {(r.dbt == 0 && r.db2mt && r.keptMt && e.dbt == 0 && e.db2mt ? "OK" : "FAIL")}");
        }
        catch (System.Exception ex)
        {
            ed.WriteMessage($"\nSYMTEKST error: {ex.Message}");
        }
    }

    // Verzamelt de MText-inhoud (contents) binnen een beheerde legenda-groep, blokken recursief.
    private static List<string> CollectMTextContents(Database db, Transaction tr, string groupName)
    {
        var result = new List<string>();
        var gd = (DBDictionary)tr.GetObject(db.GroupDictionaryId, OpenMode.ForRead);
        if (!gd.Contains(groupName) || tr.GetObject(gd.GetAt(groupName), OpenMode.ForRead) is not Group g)
            return result;
        void Walk(ObjectId id, int depth)
        {
            if (depth > 8 || tr.GetObject(id, OpenMode.ForRead) is not Entity e) return;
            if (e is MText m) result.Add(m.Contents);
            else if (e is BlockReference br && !br.BlockTableRecord.IsNull
                     && tr.GetObject(br.BlockTableRecord, OpenMode.ForRead) is BlockTableRecord r)
                foreach (ObjectId cid in r) Walk(cid, depth + 1);
        }
        foreach (ObjectId id in g.GetAllEntityIds()) Walk(id, 0);
        return result;
    }

    // Bouwt een blok dat een v1.27.x-legenda nabootst: tekst als DBText. Registreert het als
    // beheerde legenda (behouden blok) zodat we het bijwerkpad vanaf oude output kunnen testen.
    private static (string id, string group, Point3d topLeft) SeedOldDbTextLegend(
        Database db, Transaction tr, LegendSettings s,
        LegendScope scope = LegendScope.WholeDrawing, List<string>? handles = null)
    {
        // De lagen die de oude output gebruikte moeten bestaan voordat we er entiteiten op zetten.
        var lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForWrite);
        foreach (var n in new[] { s.HeaderTextLayer, s.TextLayer, s.FrameLayer })
            if (!string.IsNullOrEmpty(n) && !lt.Has(n))
            {
                var r = new LayerTableRecord { Name = n };
                lt.Add(r); tr.AddNewlyCreatedDBObject(r, true);
            }

        var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForWrite);
        var btr = new BlockTableRecord { Name = "NLCS_LEGENDA_" + System.Guid.NewGuid().ToString("N"), Origin = Point3d.Origin };
        var btrId = bt.Add(btr);
        tr.AddNewlyCreatedDBObject(btr, true);

        void OldText(string text, double x, double y, double h)
        {
            var t = new DBText();
            t.SetDatabaseDefaults();
            t.TextString = text;
            t.Height = h;
            t.Position = new Point3d(x, y, 0);
            t.Layer = s.HeaderTextLayer;
            btr.AppendEntity(t); tr.AddNewlyCreatedDBObject(t, true);
        }
        OldText("Oude legenda", 0, 0, s.ToModel(s.TitleTextHeightMm));
        OldText("Oude regel", 0, -5, s.ToModel(s.TextHeightMm));
        var pl = new Polyline();
        pl.AddVertexAt(0, new Point2d(0, -5), 0, 0, 0);
        pl.AddVertexAt(1, new Point2d(5, -5), 0, 0, 0);
        pl.Layer = s.FrameLayer;
        btr.AppendEntity(pl); tr.AddNewlyCreatedDBObject(pl, true);

        var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForWrite);
        var insert = ComputeInsertPoint(db, s);
        var br = new BlockReference(insert, btrId) { Layer = s.FrameLayer };
        ms.AppendEntity(br); tr.AddNewlyCreatedDBObject(br, true);

        var reg = LegendStore.Load(db, tr);
        var def = new LegendDefinition
        {
            Name = reg.NextDefaultName(),
            Scope = scope,
            SourceHandles = handles ?? new(),
            GroupName = LegendRegistry.NewGroupName(),
            Settings = s.Clone(),
            CreatedWithVersion = "1.27.1"
        };
        LegendManagement.AddToGroup(db, tr, def.GroupName, new[] { br.ObjectId });
        reg.Add(def);
        LegendStore.Save(db, tr, reg);
        LegendManagement.TryGetGroupExtents(db, tr, def.GroupName, out var ext);
        return (def.Id, def.GroupName, new Point3d(ext.MinPoint.X, ext.MaxPoint.Y, 0));
    }

    // Breed bijwerkbewijs voor oude DBText-legenda's: dekt WholeDrawing, Selection (met echte
    // bronhandles), een eigen bronlaag, blanco regels en xref-instellingen. Elke oude legenda
    // wordt met de normale updateflow bijgewerkt; daarna moet id/groep/scope/bron/instellingen en
    // plaats behouden zijn, de oude DBText weg, de nieuwe output MText-only en geen orphan-blok.
    [CommandMethod("NLCSLEGENDAOLDUPDATEBROADTEST", CommandFlags.Modal)]
    public void NlcsLegendaOldUpdateBroadTest()
    {
        var doc = AcApp.DocumentManager.MdiActiveDocument;
        if (doc is null) return;
        var ed = doc.Editor;
        var db = doc.Database;
        try
        {
            // NLCS-bronlagen + een eigen (niet-NLCS) laag met geometrie; bronhandles voor selectie.
            var handles = new List<string>();
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForWrite);
                ObjectId L(string n) { if (lt.Has(n)) return lt[n]; var r = new LayerTableRecord { Name = n }; var id2 = lt.Add(r); tr.AddNewlyCreatedDBObject(r, true); return id2; }
                var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForWrite);
                foreach (var n in new[] { "N-WE-VH-BREEDA-G", "N-WE-RI-BREEDB-G", "B-WE-VH-BREEDC-G" })
                {
                    var ln = new Line(new Point3d(0, 0, 0), new Point3d(5, 0, 0)) { LayerId = L(n) };
                    ms.AppendEntity(ln); tr.AddNewlyCreatedDBObject(ln, true);
                    handles.Add(ln.Handle.Value.ToString("X"));
                }
                var eigen = new Line(new Point3d(0, 0, 0), new Point3d(7, 0, 0)) { LayerId = L("Eigen breed") };
                ms.AppendEntity(eigen); tr.AddNewlyCreatedDBObject(eigen, true);
                tr.Commit();
            }
            var selHandles = handles.Take(2).ToList();

            // (naam, settings bouwen, scope, handles, extra-check op de bijgewerkte def)
            var scenarios = new (string Name, Func<LegendSettings> Make, LegendScope Scope, List<string>? Handles, Func<LegendDefinition, bool> Extra, string ExtraDesc)[]
            {
                ("WholeDrawing", () => LoadGlobalDefaults(), LegendScope.WholeDrawing, null, _ => true, "n.v.t."),
                ("Selection", () => LoadGlobalDefaults(), LegendScope.Selection, selHandles,
                    d => d.SourceHandles.Count == 2 && d.SourceHandles.SequenceEqual(selHandles), "handles behouden"),
                ("EigenLaag", () => { var s = LoadGlobalDefaults(); s.CustomLayerRules.Add(new CustomLayerRule { Layer = "Eigen breed", Element = "EIGENBREED", Type = NlcsDrawType.Geometrie }); return s; },
                    LegendScope.WholeDrawing, null, d => d.Settings.CustomLayerRules.Count == 1 && d.Settings.CustomLayerRules[0].Layer == "Eigen breed", "eigen-laagregel behouden"),
                ("Blanco", () => { var s = LoadGlobalDefaults(); s.BlankEntries.Add(new BlankEntry()); s.BlankEntries.Add(new BlankEntry()); return s; },
                    LegendScope.WholeDrawing, null, d => d.Settings.BlankEntries.Count == 2, "2 blanco behouden"),
                ("Xref", () => { var s = LoadGlobalDefaults(); s.XrefInclusion["breedxref"] = true; return s; },
                    LegendScope.WholeDrawing, null, d => d.Settings.XrefInclusion.TryGetValue("breedxref", out var v) && v, "xref-config behouden"),
            };

            var ids = new List<string>();
            bool all = true;
            foreach (var sc in scenarios)
            {
                string id, group; Point3d before; int dbtBefore;
                using (var tr = db.TransactionManager.StartTransaction())
                {
                    (id, group, before) = SeedOldDbTextLegend(db, tr, sc.Make(), sc.Scope, sc.Handles);
                    (dbtBefore, _) = CountTextInGroup(db, tr, group);
                    tr.Commit();
                }
                ids.Add(id);

                int dbtAfter, mtAfter; Point3d after; bool sameId, sameGroup, sameScope, extraOk;
                using (var tr = db.TransactionManager.StartTransaction())
                {
                    var reg = LegendStore.Load(db, tr);
                    var def = reg.FindById(id);
                    sameId = def is not null;
                    sameGroup = def is not null && def.GroupName == group;
                    sameScope = def is not null && def.Scope == sc.Scope;
                    if (def is not null) BuildManagedLegend(db, tr, reg, def, out _, out _);
                    LegendStore.Save(db, tr, reg);
                    (dbtAfter, mtAfter) = CountTextInGroup(db, tr, group);
                    LegendManagement.TryGetGroupExtents(db, tr, group, out var ext);
                    after = new Point3d(ext.MinPoint.X, ext.MaxPoint.Y, 0);
                    extraOk = def is not null && sc.Extra(reg.FindById(id)!);
                    tr.Commit();
                }
                PurgePending(db);

                bool posOk = before.DistanceTo(after) < 0.5;
                bool ok = dbtBefore > 0 && dbtAfter == 0 && mtAfter > 0 && sameId && sameGroup && sameScope && posOk && extraOk;
                all &= ok;
                ed.WriteMessage($"\nBREED {sc.Name}: DBText {dbtBefore}->{dbtAfter} MText={mtAfter} id/groep/scope={sameId}/{sameGroup}/{sameScope} pos={posOk} {sc.ExtraDesc}={extraOk} -> {(ok ? "OK" : "FAIL")}");
            }

            // Geen orphan DBText-blokken, en alle legenda's bestaan nog naast elkaar (isolatie).
            int orphan, legendsLeft;
            using (var tr = db.TransactionManager.StartTransaction())
            {
                orphan = CountDbTextInLegendBlocks(db, tr);
                var reg = LegendStore.Load(db, tr);
                legendsLeft = ids.Count(i => reg.FindById(i) is not null);
                tr.Commit();
            }
            ed.WriteMessage($"\nBREED: orphan-DBText-blokken={orphan} legenda's-naast-elkaar={legendsLeft}/{ids.Count} -> {(orphan == 0 && legendsLeft == ids.Count ? "OK" : "FAIL")}");
            ed.WriteMessage($"\nBREED: totaal -> {(all && orphan == 0 && legendsLeft == ids.Count ? "OK" : "FAIL")}");
        }
        catch (System.Exception ex)
        {
            ed.WriteMessage($"\nBREED error: {ex.Message}");
        }
    }

    // Meet de opmaak van een door de plugin gebouwde legenda en vergelijkt met de instellingen:
    // de MText-teksthoogtes moeten exact de ingestelde hoogtes zijn (geen schaalfactor) en de
    // swatchbreedte moet kloppen. Zo is bewezen dat de DBText->MText-overstap de maat niet wijzigt.
    [CommandMethod("NLCSLEGENDAOPMAAKMEETTEST", CommandFlags.Modal)]
    public void NlcsLegendaOpmaakMeetTest()
    {
        var doc = AcApp.DocumentManager.MdiActiveDocument;
        if (doc is null) return;
        var ed = doc.Editor;
        var db = doc.Database;
        try
        {
            var s = MTextFixtureSettings();
            s.DrawSwatchFrame = true;
            using (var tr = db.TransactionManager.StartTransaction()) { EnsureMTextFixtureLayers(db, tr); tr.Commit(); }

            double bodyH = s.ToModel(s.TextHeightMm), headH = s.ToModel(s.HeaderTextHeightMm);
            double titleH = s.ToModel(s.TitleTextHeightMm), swW = s.ToModel(s.SwatchWidthMm);
            var heights = new List<double>();
            double frameW = 0; bool wrapSeen = false;
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var reg = LegendStore.Load(db, tr);
                var def = IsoDef(reg, s); reg.Add(def);
                BuildManagedLegend(db, tr, reg, def, out _, out _);
                LegendStore.Save(db, tr, reg);
                var gd = (DBDictionary)tr.GetObject(db.GroupDictionaryId, OpenMode.ForRead);
                var g = (Group)tr.GetObject(gd.GetAt(def.GroupName), OpenMode.ForRead);
                void Walk(ObjectId id, int depth)
                {
                    if (depth > 8 || tr.GetObject(id, OpenMode.ForRead) is not Entity e) return;
                    if (e is MText m) { heights.Add(m.TextHeight); if (m.Width > 0) wrapSeen = true; }
                    else if (e is Polyline p && string.Equals(p.Layer, s.FrameLayer, StringComparison.OrdinalIgnoreCase) && p.NumberOfVertices == 4)
                    {
                        var ext = p.GeometricExtents;
                        double w = ext.MaxPoint.X - ext.MinPoint.X, h = ext.MaxPoint.Y - ext.MinPoint.Y;
                        if (Math.Abs(w - swW) < 0.05 && h < swW) frameW = w; // swatchkader, niet het buitenkader
                    }
                    else if (e is BlockReference br && !br.BlockTableRecord.IsNull && tr.GetObject(br.BlockTableRecord, OpenMode.ForRead) is BlockTableRecord r)
                        foreach (ObjectId cid in r) Walk(cid, depth + 1);
                }
                foreach (ObjectId id in g.GetAllEntityIds()) Walk(id, 0);
                tr.Commit();
            }
            PurgePending(db);

            bool Near(double a, double b) => Math.Abs(a - b) < 1e-6;
            bool bodyOk = heights.Any(h => Near(h, bodyH));
            bool headOk = heights.Any(h => Near(h, headH));
            bool titleOk = heights.Any(h => Near(h, titleH));
            bool swOk = Near(frameW, swW);
            ed.WriteMessage($"\nOPMAAK: teksthoogtes body={bodyH:0.###}({bodyOk}) kop={headH:0.###}({headOk}) titel={titleH:0.###}({titleOk}) -> {(bodyOk && headOk && titleOk ? "OK" : "FAIL")}");
            ed.WriteMessage($"\nOPMAAK: swatchbreedte verwacht={swW:0.###} gemeten={frameW:0.###} -> {(swOk ? "OK" : "FAIL")}");
            ed.WriteMessage($"\nOPMAAK: body-MText wrapt (vaste breedte)={wrapSeen} -> {(wrapSeen ? "OK" : "FAIL")}");
            ed.WriteMessage($"\nOPMAAK: totaal -> {(bodyOk && headOk && titleOk && swOk && wrapSeen ? "OK" : "FAIL")}");
        }
        catch (System.Exception ex) { ed.WriteMessage($"\nOPMAAK error: {ex.Message}"); }
    }

    // Save/reopen van een oude DBText-legenda. SETUP maakt NLCS-content en een oude DBText-
    // Selection-legenda; na QSAVE + heropenen werkt VERIFY die bij en controleert dat de oude
    // tekst weg is, de output MText-only, de bronhandles exact behouden en de plaats gelijk.
    [CommandMethod("NLCSLEGENDAOLDREOPENSETUP", CommandFlags.Modal)]
    public void NlcsLegendaOldReopenSetup()
    {
        var doc = AcApp.DocumentManager.MdiActiveDocument;
        if (doc is null) return;
        var ed = doc.Editor;
        var db = doc.Database;
        try
        {
            using var tr = db.TransactionManager.StartTransaction();
            var lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForWrite);
            ObjectId L(string n) { if (lt.Has(n)) return lt[n]; var r = new LayerTableRecord { Name = n }; var id2 = lt.Add(r); tr.AddNewlyCreatedDBObject(r, true); return id2; }
            var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForWrite);
            var handles = new List<string>();
            foreach (var n in new[] { "N-WE-VH-HEROPENA-G", "N-WE-RI-HEROPENB-G" })
            {
                var ln = new Line(new Point3d(0, 0, 0), new Point3d(5, 0, 0)) { LayerId = L(n) };
                ms.AppendEntity(ln); tr.AddNewlyCreatedDBObject(ln, true);
                handles.Add(ln.Handle.Value.ToString("X"));
            }
            var (id, group, _) = SeedOldDbTextLegend(db, tr, MTextFixtureSettings(), LegendScope.Selection, handles);
            var (dbt, _) = CountTextInGroup(db, tr, group);
            tr.Commit();
            ed.WriteMessage($"\nHEROPEN: setup klaar, oude DBText={dbt} handles={handles.Count}. QSAVE, heropenen, dan NLCSLEGENDAOLDREOPENVERIFY.");
        }
        catch (System.Exception ex) { ed.WriteMessage($"\nHEROPEN setup error: {ex.Message}"); }
    }

    [CommandMethod("NLCSLEGENDAOLDREOPENVERIFY", CommandFlags.Modal)]
    public void NlcsLegendaOldReopenVerify()
    {
        var doc = AcApp.DocumentManager.MdiActiveDocument;
        if (doc is null) return;
        var ed = doc.Editor;
        var db = doc.Database;
        try
        {
            string id, group, scope; int dbtBefore, dbtAfter, mtAfter, handlesAfter, orphan; Point3d before, after;
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var reg = LegendStore.Load(db, tr);
                var def = reg.Legends.FirstOrDefault();
                if (def is null) { ed.WriteMessage("\nHEROPEN: geen legenda gevonden -> FAIL"); tr.Commit(); return; }
                id = def.Id; group = def.GroupName; scope = def.Scope.ToString();
                (dbtBefore, _) = CountTextInGroup(db, tr, group);
                LegendManagement.TryGetGroupExtents(db, tr, group, out var e0);
                before = new Point3d(e0.MinPoint.X, e0.MaxPoint.Y, 0);
                tr.Commit();
            }
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var reg = LegendStore.Load(db, tr);
                var def = reg.FindById(id)!;
                BuildManagedLegend(db, tr, reg, def, out _, out _);
                LegendStore.Save(db, tr, reg);
                (dbtAfter, mtAfter) = CountTextInGroup(db, tr, group);
                handlesAfter = reg.FindById(id)!.SourceHandles.Count;
                LegendManagement.TryGetGroupExtents(db, tr, group, out var e1);
                after = new Point3d(e1.MinPoint.X, e1.MaxPoint.Y, 0);
                tr.Commit();
            }
            PurgePending(db);
            using (var tr = db.TransactionManager.StartTransaction()) { orphan = CountDbTextInLegendBlocks(db, tr); tr.Commit(); }

            bool posOk = before.DistanceTo(after) < 0.5;
            bool ok = dbtBefore > 0 && dbtAfter == 0 && mtAfter > 0 && scope == "Selection" && handlesAfter == 2 && posOk && orphan == 0;
            ed.WriteMessage($"\nHEROPEN: na reopen+update DBText {dbtBefore}->{dbtAfter} MText={mtAfter} scope={scope} handles={handlesAfter} pos={posOk} orphan={orphan} -> {(ok ? "OK" : "FAIL")}");
        }
        catch (System.Exception ex) { ed.WriteMessage($"\nHEROPEN verify error: {ex.Message}"); }
    }

    // Bijwerken van een oude DBText-legenda zonder apart migratiecommando: NLCSLEGENDAUPDATE moet
    // de oude tekst wissen, MText-only terugbouwen en identiteit/bron/instellingen/positie houden.
    [CommandMethod("NLCSLEGENDAOLDUPDATETEST", CommandFlags.Modal)]
    public void NlcsLegendaOldUpdateTest()
    {
        var doc = AcApp.DocumentManager.MdiActiveDocument;
        if (doc is null) return;
        var ed = doc.Editor;
        var db = doc.Database;
        try
        {
            using (var tr = db.TransactionManager.StartTransaction())
            {
                EnsureMTextFixtureLayers(db, tr);
                tr.Commit();
            }

            string id, group;
            Point3d before;
            int dbtBefore, mtBefore;
            using (var tr = db.TransactionManager.StartTransaction())
            {
                (id, group, before) = SeedOldDbTextLegend(db, tr, MTextFixtureSettings());
                (dbtBefore, mtBefore) = CountTextInGroup(db, tr, group);
                tr.Commit();
            }
            ed.WriteMessage($"\nOUD: voor update DBText={dbtBefore} MText={mtBefore} -> {(dbtBefore > 0 ? "OK (oude output)" : "FAIL")}");

            int dbtAfter, mtAfter, orphan;
            Point3d after;
            bool sameId, sameGroup;
            string scope;
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var reg = LegendStore.Load(db, tr);
                var def = reg.FindById(id);
                sameId = def is not null;
                sameGroup = def is not null && def.GroupName == group;
                scope = def?.Scope.ToString() ?? "?";
                if (def is not null)
                    BuildManagedLegend(db, tr, reg, def, out _, out _);
                LegendStore.Save(db, tr, reg);
                (dbtAfter, mtAfter) = CountTextInGroup(db, tr, group);
                LegendManagement.TryGetGroupExtents(db, tr, group, out var ext);
                after = new Point3d(ext.MinPoint.X, ext.MaxPoint.Y, 0);
                tr.Commit();
            }
            PurgePending(db);

            // Orphan-telling ná PurgePending: een normale NLCSLEGENDAUPDATE ruimt verweesde
            // blokdefinities op dezelfde manier op.
            using (var tr = db.TransactionManager.StartTransaction())
            {
                orphan = CountDbTextInLegendBlocks(db, tr);
                tr.Commit();
            }

            bool posOk = before.DistanceTo(after) < 0.5;
            ed.WriteMessage($"\nOUD: na update DBText={dbtAfter} MText={mtAfter} -> {(dbtAfter == 0 && mtAfter > 0 ? "OK" : "FAIL")}");
            ed.WriteMessage($"\nOUD: identiteit id-gelijk={sameId} groep-gelijk={sameGroup} scope={scope} -> {(sameId && sameGroup ? "OK" : "FAIL")}");
            ed.WriteMessage($"\nOUD: positie voor={before.X:0.0},{before.Y:0.0} na={after.X:0.0},{after.Y:0.0} -> {(posOk ? "OK" : "FAIL")}");
            ed.WriteMessage($"\nOUD: geen orphan DBText in legenda-blokken={orphan} -> {(orphan == 0 ? "OK" : "FAIL")}");
            bool all = dbtBefore > 0 && dbtAfter == 0 && mtAfter > 0 && sameId && sameGroup && posOk && orphan == 0;
            ed.WriteMessage($"\nOUD: totaal -> {(all ? "OK" : "FAIL")}");
        }
        catch (System.Exception ex)
        {
            ed.WriteMessage($"\nOUD error: {ex.Message}");
        }
    }
}
