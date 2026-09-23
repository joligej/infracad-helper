using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using NlcsLegenda.Core;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Core.Application;
using AcWindows = Autodesk.AutoCAD.ApplicationServices.Application;
using Exception = System.Exception;
using WinForms = System.Windows.Forms;

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
                pko.Keywords.Add("Instellingen");
                pko.Keywords.Add("Overnemen");
                pko.Keywords.Add("Standaardmaken");
                pko.Keywords.Add("Dupliceren");
                pko.Keywords.Add("Bron");
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
                    case "Instellingen": ManagerEditSettings(ed, db, registry); break;
                    case "Overnemen": ManagerCopySettings(ed, db, registry); break;
                    case "Standaardmaken": ManagerMakeGlobalDefault(ed, db, registry); break;
                    case "Dupliceren": ManagerDuplicate(ed, db, registry); break;
                    case "Bron": ManagerEditSource(ed, db, registry); break;
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

    // Bewerkt de instellingen van precies één legenda (op LegendId) en bouwt die legenda
    // atomair opnieuw op. Annuleren wijzigt niets; andere legenda's blijven ongemoeid.
    private void ManagerEditSettings(Editor ed, Database db, LegendRegistry registry)
    {
        var def = PickLegendFromList(ed, registry, "de instellingen aanpassen van");
        if (def is null) return;
        string id = def.Id;
        var working = def.Settings.Clone();
        using var dialog = new SettingsDialog(working, $"Legenda: {def.Name}");
        dialog.ApplyRequested += (_, _) => ApplySettingsAndRebuild(ed, db, id, working);
        if (AcWindows.ShowModalDialog(dialog) == WinForms.DialogResult.OK)
            ApplySettingsAndRebuild(ed, db, id, working);
    }

    // Neemt alle per-legenda-instellingen over van een andere legenda (U2). Identiteit, naam,
    // bron en positie van de doel-legenda blijven; de instellingen worden diep gekopieerd.
    private void ManagerCopySettings(Editor ed, Database db, LegendRegistry registry)
    {
        if (registry.Legends.Count < 2)
        {
            ed.WriteMessage("\nEr is maar één legenda; er is niets om instellingen van over te nemen.");
            return;
        }
        var target = PickLegendFromList(ed, registry, "de instellingen wijzigen van");
        if (target is null) return;
        var sources = registry.Legends.Where(l => l.Id != target.Id).ToList();
        var source = PickFromList(ed, sources, "instellingen overnemen van");
        if (source is null) return;
        if (!AskYesNo(ed, $"Instellingen van \"{source.Name}\" overnemen in \"{target.Name}\"?", true))
            return;
        ApplySettingsAndRebuild(ed, db, target.Id, source.Settings);
    }

    // Zet de instellingen van een legenda als globale standaard voor nieuwe legenda's (U6).
    // Bestaande legenda's veranderen niet.
    private void ManagerMakeGlobalDefault(Editor ed, Database db, LegendRegistry registry)
    {
        var def = PickLegendFromList(ed, registry, "als globale standaard gebruiken");
        if (def is null) return;
        if (!AskYesNo(ed, $"Instellingen van \"{def.Name}\" als globale standaard voor nieuwe legenda's gebruiken?", true))
            return;
        var where = SaveSettingsToScope(db, def.Settings, ConfigScope.Global);
        ed.WriteMessage($"\nGlobale standaard bijgewerkt op basis van \"{def.Name}\" ({where}). Bestaande legenda's blijven ongewijzigd.");
    }

    // Dupliceert een legenda: nieuwe identiteit, dezelfde instellingen/scope/bron, nieuwe
    // plaatsing (U3). Geen gedeelde technische identiteit met het origineel.
    private void ManagerDuplicate(Editor ed, Database db, LegendRegistry registry)
    {
        var def = PickLegendFromList(ed, registry, "dupliceren");
        if (def is null) return;

        string note = string.Empty;
        bool ok = false;
        string newName = string.Empty;
        using (var tr = db.TransactionManager.StartTransaction())
        {
            var reg = LegendStore.Load(db, tr);
            var src = reg.FindById(def.Id);
            if (src is null) { tr.Commit(); ed.WriteMessage("\nLegenda niet meer gevonden."); return; }

            var copy = new LegendDefinition
            {
                Name = reg.NextDefaultName(),
                Scope = src.Scope,
                SourceHandles = new List<string>(src.SourceHandles),
                GroupName = LegendRegistry.NewGroupName(),
                Settings = src.Settings.Clone(),
                CreatedWithVersion = PluginVersion
            };
            reg.Add(copy);
            newName = copy.Name;
            var r = BuildManagedLegend(db, tr, reg, copy, out _, out note);
            if (r == UpdateResult.Updated)
            {
                LegendStore.Save(db, tr, reg);
                tr.Commit();
                ok = true;
            }
            // Bij geen inhoud committen we niet: de toegevoegde registry-entry rolt terug.
        }
        if (ok)
        {
            PurgePending(db);
            ed.WriteMessage($"\nGedupliceerd naar \"{newName}\".");
        }
        else
        {
            ed.WriteMessage($"\nDupliceren leverde geen inhoud op ({note}).");
        }
    }

    // Past de bronselectie van een selectie-legenda aan: vervangen, toevoegen of verwijderen
    // (U1). Legenda-eigen geometrie wordt geweigerd als bron; lege selectie wijzigt niets.
    private void ManagerEditSource(Editor ed, Database db, LegendRegistry registry)
    {
        var def = PickLegendFromList(ed, registry, "de bron aanpassen van");
        if (def is null) return;
        if (def.Scope != LegendScope.Selection)
        {
            ed.WriteMessage("\nAlleen selectie-legenda's hebben een aanpasbare bron; deze omvat de hele tekening.");
            return;
        }

        var pko = new PromptKeywordOptions("\nBron [Vervangen/Toevoegen/Verwijderen/Annuleren]")
        {
            AllowNone = true
        };
        pko.Keywords.Add("Vervangen");
        pko.Keywords.Add("Toevoegen");
        pko.Keywords.Add("Verwijderen");
        pko.Keywords.Add("Annuleren");
        pko.Keywords.Default = "Vervangen";
        var mode = ed.GetKeywords(pko);
        if (mode.Status != PromptStatus.OK || mode.StringResult == "Annuleren")
            return;

        var sel = ed.GetSelection();
        if (sel.Status != PromptStatus.OK)
        {
            ed.WriteMessage("\nGeen selectie; bron ongewijzigd.");
            return;
        }

        try
        {
            UpdateResult result; string note;
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var reg = LegendStore.Load(db, tr);
                var target = reg.FindById(def.Id);
                if (target is null) { tr.Commit(); ed.WriteMessage("\nLegenda niet meer gevonden."); return; }

                var excluded = LegendManagement.CollectManagedIds(db, tr, reg);
                var picked = sel.Value.GetObjectIds().Where(id => !excluded.Contains(id)).ToArray();
                var pickedHandles = LegendManagement.ToHandles(picked);

                var newHandles = mode.StringResult switch
                {
                    "Toevoegen" => target.SourceHandles.Concat(pickedHandles),
                    "Verwijderen" => target.SourceHandles.Except(pickedHandles, StringComparer.OrdinalIgnoreCase),
                    _ => pickedHandles
                };
                var deduped = newHandles.Distinct(StringComparer.OrdinalIgnoreCase).ToList();

                if (deduped.Count == 0)
                {
                    tr.Commit();
                    ed.WriteMessage("\nDe nieuwe bron zou leeg zijn; bron ongewijzigd.");
                    return;
                }

                target.SourceHandles = deduped;
                result = BuildManagedLegend(db, tr, reg, target, out _, out note);
                if (result == UpdateResult.Updated)
                    LegendStore.Save(db, tr, reg);
                tr.Commit();
            }
            PurgePending(db);
            ed.WriteMessage(result == UpdateResult.Updated
                ? "\nBron aangepast en legenda bijgewerkt."
                : $"\nBron aangepast, maar geen inhoud om te tekenen ({note}).");
        }
        catch (Exception ex)
        {
            ed.WriteMessage($"\nBron aanpassen mislukt: {ex.Message}");
        }
    }

    // Past nieuwe instellingen toe op één legenda en bouwt die atomair opnieuw op. Bij een
    // fout of lege inhoud blijft de oude legenda volledig bruikbaar.
    private void ApplySettingsAndRebuild(Editor ed, Database db, string id, LegendSettings newSettings)
    {
        try
        {
            UpdateResult r; string note;
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var reg = LegendStore.Load(db, tr);
                var def = reg.FindById(id);
                if (def is null) { tr.Commit(); ed.WriteMessage("\nLegenda niet meer gevonden."); return; }
                def.Settings.CopyFrom(newSettings);
                r = BuildManagedLegend(db, tr, reg, def, out _, out note);
                if (r == UpdateResult.Updated)
                    LegendStore.Save(db, tr, reg);
                tr.Commit();
            }
            PurgePending(db);
            ed.WriteMessage(r switch
            {
                UpdateResult.Updated => "\nInstellingen toegepast en legenda bijgewerkt.",
                UpdateResult.NoEntries => $"\nGeen inhoud om te tekenen met deze instellingen ({note}); oude legenda blijft staan.",
                _ => "\nBijwerken mislukt; de oude legenda blijft staan."
            });
        }
        catch (Exception ex)
        {
            ed.WriteMessage($"\nToepassen mislukt: {ex.Message}");
        }
    }

    // "Waarom ontbreekt dit?" (U5): diagnosticeert voor een gekozen legenda waarom een
    // aangeklikt object wel of niet wordt opgenomen. Gebruikt exact dezelfde filterlogica
    // als de analyse (ExplainExclusion + elementsoort- en zichtbaarheidsfilter).
    [CommandMethod("NLCSLEGENDAWAAROM", CommandFlags.Modal)]
    public void NlcsLegendaWaarom()
    {
        var doc = AcApp.DocumentManager.MdiActiveDocument;
        if (doc is null)
            return;
        var ed = doc.Editor;
        var db = doc.Database;

        try
        {
            var peo = new PromptEntityOptions("\nSelecteer een object om te controleren");
            peo.SetRejectMessage("\nKies een enkel object.");
            var per = ed.GetEntity(peo);
            if (per.Status != PromptStatus.OK)
            {
                ed.WriteMessage("\nGeannuleerd.");
                return;
            }

            LegendRegistry registry;
            using (var tr0 = db.TransactionManager.StartTransaction())
            {
                registry = LegendStore.Load(db, tr0);
                tr0.Commit();
            }

            LegendDefinition? target = registry.Legends.Count switch
            {
                0 => null,
                1 => registry.Legends[0],
                _ => PickFromList(ed, registry.Legends, "controleren voor")
            };
            if (registry.Legends.Count > 1 && target is null)
            {
                ed.WriteMessage("\nGeannuleerd.");
                return;
            }
            var settings = target?.Settings ?? LoadGlobalDefaults();
            var name = target?.Name ?? "de globale standaard";

            using var tr = db.TransactionManager.StartTransaction();
            if (LegendManagement.CollectManagedIds(db, tr, registry).Contains(per.ObjectId))
            {
                ed.WriteMessage("\nDit object hoort bij een beheerde legenda en telt nooit mee als bron.");
                tr.Commit();
                return;
            }

            if (tr.GetObject(per.ObjectId, OpenMode.ForRead) is not Entity ent)
            {
                tr.Commit();
                return;
            }
            var reason = DiagnoseObject(db, tr, ent.Layer, settings, target, per.ObjectId, out var localName);
            tr.Commit();

            var head = $"\nObject op laag '{ent.Layer}'"
                + (string.IsNullOrEmpty(localName) ? "" : $" ({localName})") + $" — legenda {name}:";
            ed.WriteMessage(head);
            ed.WriteMessage(reason is null
                ? "\n  \u2192 wordt opgenomen."
                : $"\n  \u2192 niet opgenomen: {reason}.");
        }
        catch (Exception ex)
        {
            ed.WriteMessage($"\nNLCSLEGENDAWAAROM fout: {ex.Message}");
        }
    }

    private static string? DiagnoseObject(
        Database db, Transaction tr, string layerName, LegendSettings settings,
        LegendDefinition? target, ObjectId entId, out string localName)
    {
        localName = string.Empty;

        var lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
        if (lt.Has(layerName) && tr.GetObject(lt[layerName], OpenMode.ForRead) is LayerTableRecord ltr
            && !settings.IncludeInvisibleLayers && (ltr.IsOff || ltr.IsFrozen))
            return "de laag staat uit of is bevroren (zet 'Onzichtbare lagen meenemen' aan om hem toch mee te nemen)";

        if (!NlcsLayerParser.TryParse(layerName, out var parsed))
            return "dit is geen NLCS-laag (niet herkend aan de InfraCAD-laagnaamopbouw)";
        localName = parsed.LocalName;

        if (target is { Scope: LegendScope.Selection })
        {
            var handle = entId.Handle.Value.ToString("X");
            if (!target.SourceHandles.Contains(handle, StringComparer.OrdinalIgnoreCase))
                return "dit object hoort niet bij de bronselectie van deze legenda";
        }

        if (!settings.IsDrawTypeIncluded(parsed.DrawType))
            return $"elementsoort '{parsed.DrawType.DisplayName()}' staat voor deze legenda uit";

        return settings.ExplainExclusion(parsed);
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
        => PickFromList(ed, registry.Legends, actie);

    private static LegendDefinition? PickFromList(
        Editor ed, IReadOnlyList<LegendDefinition> legends, string actie)
    {
        if (legends.Count == 0)
            return null;
        if (legends.Count == 1)
            return legends[0];

        ed.WriteMessage($"\nWelke legenda {actie}:");
        for (int i = 0; i < legends.Count; i++)
        {
            var l = legends[i];
            ed.WriteMessage($"\n  {i + 1}. {l.Name} ({l.Scope.ToDisplay()})");
        }
        var pio = new PromptIntegerOptions($"\nNummer (1-{legends.Count}, 0 = annuleren)")
        {
            LowerLimit = 0,
            UpperLimit = legends.Count,
            DefaultValue = 0,
            AllowNone = true
        };
        var r = ed.GetInteger(pio);
        if (r.Status != PromptStatus.OK || r.Value < 1)
            return null;
        return legends[r.Value - 1];
    }
}
