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
    private static (string id, string group, Point3d topLeft) SeedOldDbTextLegend(Database db, Transaction tr, LegendSettings s)
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
            Scope = LegendScope.WholeDrawing,
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
