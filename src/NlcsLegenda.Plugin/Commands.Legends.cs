using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using NlcsLegenda.Core;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Core.Application;
using Exception = System.Exception;

namespace NlcsLegenda.Plugin;

public partial class Commands
{
    private enum UpdateResult { Updated, NoEntries, Failed }

    // Bouwt één beheerde legenda opnieuw op basis van zijn scope en instellingen. Erase en
    // opbouw gebeuren in dezelfde transactie: mislukt de opbouw, dan blijft de oude staan.
    private static UpdateResult BuildManagedLegend(
        Database db, Transaction tr, LegendRegistry registry, LegendDefinition def,
        out int rows, out string note)
    {
        rows = 0;
        note = string.Empty;

        // Bron bepalen op basis van scope; eigen legenda-geometrie uitsluiten.
        var excluded = LegendManagement.CollectManagedIds(db, tr, registry);
        ObjectId[]? selection = null;
        if (def.Scope == LegendScope.Selection)
        {
            selection = LegendManagement.ResolveHandles(db, def.SourceHandles, out var missing)
                .Where(id => !excluded.Contains(id)).ToArray();
            if (selection.Length == 0)
            {
                note = missing > 0
                    ? $"alle {missing} bronobjecten ontbreken"
                    : "geen bronobjecten meer";
                return UpdateResult.NoEntries;
            }
            if (missing > 0)
                note = $"{missing} bronobject(en) ontbreken";
        }

        var analysis = DrawingAnalyzer.Analyze(db, tr, def.Settings, selection, LoadCatalog(db), excluded);
        if (analysis.Entries.Count == 0)
            return UpdateResult.NoEntries;

        // Positie van de bestaande legenda vasthouden.
        bool hadGeometry = LegendManagement.TryEraseGroup(db, tr, def.GroupName, out var topLeft);

        var btrId = LegendBuilder.BuildBlock(db, tr, analysis, def.Settings, out rows);
        var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForWrite);
        var br = new BlockReference(hadGeometry ? topLeft : ComputeInsertPoint(db, def.Settings), btrId);
        ms.AppendEntity(br);
        tr.AddNewlyCreatedDBObject(br, true);

        if (hadGeometry)
        {
            var ext = br.Bounds;
            if (ext.HasValue)
            {
                var shift = new Vector3d(topLeft.X - ext.Value.MinPoint.X, topLeft.Y - ext.Value.MaxPoint.Y, 0);
                if (!shift.IsZeroLength())
                    br.Position += shift;
            }
        }

        FinalizePlacement(tr, db, br, def.Settings, def.GroupName);
        _pendingPurge.Add(btrId);
        return UpdateResult.Updated;
    }

    private static readonly List<ObjectId> _pendingPurge = new();

    private static void PurgePending(Database db)
    {
        foreach (var id in _pendingPurge)
            PurgeTempBlock(db, id);
        _pendingPurge.Clear();
    }

    // Voert de eenmalige legacy-migratie uit binnen een bestaande transactie.
    private static void MaybeMigrate(Database db, Transaction tr, LegendRegistry registry)
    {
        try
        {
            LegendManagement.MigrateLegacyIfNeeded(db, tr, registry, LoadGlobalDefaults(), PluginVersion, out _);
        }
        catch
        {
            // Migratie faalt zacht; de tekening blijft bruikbaar.
        }
    }

    [CommandMethod("NLCSLEGENDABEHEER", CommandFlags.Modal)]
    public void NlcsLegendaBeheer()
    {
        var doc = AcApp.DocumentManager.MdiActiveDocument;
        if (doc is null)
            return;
        var ed = doc.Editor;
        var db = doc.Database;

        try
        {
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var reg = LegendStore.Load(db, tr);
                MaybeMigrate(db, tr, reg);
                tr.Commit();
            }

            while (true)
            {
                LegendRegistry registry;
                using (var tr = db.TransactionManager.StartTransaction())
                {
                    registry = LegendStore.Load(db, tr);
                    tr.Commit();
                }

                if (registry.Legends.Count == 0)
                {
                    ed.WriteMessage("\nGeen beheerde legenda's in deze tekening. Plaats er een met NLCSLEGENDA.");
                    return;
                }

                ed.WriteMessage($"\n{registry.Legends.Count} legenda('s):");
                for (int i = 0; i < registry.Legends.Count; i++)
                {
                    var l = registry.Legends[i];
                    var src = l.Scope == LegendScope.Selection ? $"selectie ({l.SourceHandles.Count})" : "hele tekening";
                    ed.WriteMessage($"\n  {i + 1}. {l.Name} — {src}");
                }

                var pko = new PromptKeywordOptions("\nActie");
                pko.Keywords.Add("Bijwerken");
                pko.Keywords.Add("Alles");
                pko.Keywords.Add("Zoom");
                pko.Keywords.Add("Naam");
                pko.Keywords.Add("Verwijderen");
                pko.Keywords.Add("Klaar");
                pko.Keywords.Default = "Klaar";
                pko.AllowNone = true;
                var res = ed.GetKeywords(pko);
                if (res.Status != PromptStatus.OK || res.StringResult == "Klaar")
                    return;

                switch (res.StringResult)
                {
                    case "Bijwerken": NlcsLegendaUpdate(); break;
                    case "Alles": UpdateAllLegends(ed, db); break;
                    case "Zoom": ManagerZoom(ed, db, registry); break;
                    case "Naam": ManagerRename(ed, db, registry); break;
                    case "Verwijderen": ManagerDelete(ed, db, registry); break;
                }
            }
        }
        catch (Exception ex)
        {
            ed.WriteMessage($"\nNLCSLEGENDABEHEER fout: {ex.Message}");
        }
    }

    private void UpdateAllLegends(Editor ed, Database db)
    {
        int ok = 0, skipped = 0, failed = 0;
        List<string> ids;
        using (var tr = db.TransactionManager.StartTransaction())
        {
            ids = LegendStore.Load(db, tr).Legends.Select(l => l.Id).ToList();
            tr.Commit();
        }
        // Iedere legenda in een eigen transactie: één fout blokkeert de rest niet.
        foreach (var id in ids)
        {
            try
            {
                using var tr = db.TransactionManager.StartTransaction();
                var registry = LegendStore.Load(db, tr);
                var def = registry.FindById(id);
                if (def is null) { tr.Commit(); continue; }
                var r = BuildManagedLegend(db, tr, registry, def, out _, out _);
                if (r == UpdateResult.Updated) { LegendStore.Save(db, tr, registry); ok++; }
                else skipped++;
                tr.Commit();
            }
            catch { failed++; }
        }
        PurgePending(db);
        ed.WriteMessage($"\nAlles bijwerken: {ok} bijgewerkt, {skipped} overgeslagen, {failed} fout.");
    }

    private void ManagerZoom(Editor ed, Database db, LegendRegistry registry)
    {
        var def = PickLegendFromList(ed, registry, "zoomen naar");
        if (def is null) return;
        using var tr = db.TransactionManager.StartTransaction();
        if (LegendManagement.TryGetGroupExtents(db, tr, def.GroupName, out var ext))
        {
            var doc = AcApp.DocumentManager.MdiActiveDocument;
            var view = doc.Editor.GetCurrentView();
            view.CenterPoint = new Point2d((ext.MinPoint.X + ext.MaxPoint.X) / 2, (ext.MinPoint.Y + ext.MaxPoint.Y) / 2);
            view.Width = (ext.MaxPoint.X - ext.MinPoint.X) * 1.2;
            view.Height = (ext.MaxPoint.Y - ext.MinPoint.Y) * 1.2;
            doc.Editor.SetCurrentView(view);
            ed.WriteMessage($"\nGezoomd naar \"{def.Name}\".");
        }
        else
        {
            ed.WriteMessage($"\n\"{def.Name}\" heeft geen zichtbare geometrie.");
        }
        tr.Commit();
    }

    private void ManagerRename(Editor ed, Database db, LegendRegistry registry)
    {
        var def = PickLegendFromList(ed, registry, "hernoemen");
        if (def is null) return;
        var pso = new PromptStringOptions($"\nNieuwe naam voor \"{def.Name}\":") { AllowSpaces = true };
        var sr = ed.GetString(pso);
        if (sr.Status != PromptStatus.OK || string.IsNullOrWhiteSpace(sr.StringResult))
            return;
        var name = sr.StringResult.Trim();
        using var tr = db.TransactionManager.StartTransaction();
        var reg = LegendStore.Load(db, tr);
        var target = reg.FindById(def.Id);
        if (target is not null)
        {
            target.Name = name;
            LegendStore.Save(db, tr, reg);
            ed.WriteMessage($"\nHernoemd naar \"{name}\".");
        }
        tr.Commit();
    }

    private void ManagerDelete(Editor ed, Database db, LegendRegistry registry)
    {
        var def = PickLegendFromList(ed, registry, "verwijderen");
        if (def is null) return;
        if (!AskYesNo(ed, $"\"{def.Name}\" verwijderen?", false))
            return;
        using var tr = db.TransactionManager.StartTransaction();
        var reg = LegendStore.Load(db, tr);
        var target = reg.FindById(def.Id);
        if (target is not null)
        {
            LegendManagement.TryEraseGroup(db, tr, target.GroupName, out _);
            reg.Remove(target.Id);
            LegendStore.Save(db, tr, reg);
            ed.WriteMessage($"\n\"{target.Name}\" verwijderd.");
        }
        tr.Commit();
    }

    // Kiest de doel-legenda voor update/viewport/export: 0 = null, 1 = die ene, >1 = de
    // gebruiker klikt een legenda-object aan (of kiest uit een lijst als klikken faalt).
    private static LegendDefinition? ResolveTargetLegend(
        Editor ed, Database db, LegendRegistry registry, string actie)
    {
        if (registry.Legends.Count == 0)
            return null;
        if (registry.Legends.Count == 1)
            return registry.Legends[0];

        var peo = new PromptEntityOptions($"\nKlik een onderdeel van de legenda om te {actie} (of Enter voor een lijst)")
        {
            AllowNone = true
        };
        var per = ed.GetEntity(peo);
        if (per.Status == PromptStatus.OK)
        {
            using var tr = db.TransactionManager.StartTransaction();
            var def = LegendManagement.FindLegendForEntity(db, tr, registry, per.ObjectId);
            tr.Commit();
            if (def is not null)
                return def;
            ed.WriteMessage("\nDat object hoort niet bij een beheerde legenda.");
        }

        return PickLegendFromList(ed, registry, actie);
    }

    private static LegendDefinition? PickLegendFromList(Editor ed, LegendRegistry registry, string actie)
    {
        ed.WriteMessage($"\nWelke legenda {actie}:");
        for (int i = 0; i < registry.Legends.Count; i++)
        {
            var l = registry.Legends[i];
            ed.WriteMessage($"\n  {i + 1}. {l.Name} ({l.Scope.ToDisplay()})");
        }
        var pio = new PromptIntegerOptions($"\nNummer (1-{registry.Legends.Count}, 0 = annuleren)")
        {
            LowerLimit = 0,
            UpperLimit = registry.Legends.Count,
            DefaultValue = 0,
            AllowNone = true
        };
        var r = ed.GetInteger(pio);
        if (r.Status != PromptStatus.OK || r.Value < 1)
            return null;
        return registry.Legends[r.Value - 1];
    }
}
