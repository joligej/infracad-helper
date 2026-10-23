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

    // wissen en opbouwen binnen dezelfde transactie
    private static UpdateResult BuildManagedLegend(
        Database db, Transaction tr, LegendRegistry registry, LegendDefinition def,
        out int rows, out string note)
    {
        rows = 0;
        note = string.Empty;

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

        // eigen tekstkopie gebruiken, niet de gewijzigde globale standaard
        var analysis = DrawingAnalyzer.Analyze(db, tr, def.Settings, selection, DescriptionCatalog.Default(), excluded);
        if (analysis.Entries.Count == 0)
            note = AppendNote(note, "geen regels opgenomen; samenstelling behouden");
        int unknownAreas = analysis.Entries.Count(e => e.Metric.HasUnknownArea);
        if (unknownAreas > 0) note = AppendNote(note, $"{unknownAreas} regel(s) met onbekend oppervlak (? m²); zie de bronmeldingen in Overzicht");

        bool hadGeometry = LegendManagement.TryEraseGroup(db, tr, def.GroupName, out var topLeft, keepGroup: true);

        var btrId = LegendBuilder.BuildBlock(db, tr, analysis, def.Settings, out rows, out var issues);
        if (issues.Count > 0)
            throw new InvalidOperationException($"Legenda niet gewijzigd: {issues[0].Reason}");
        var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForWrite);
        using var br = new BlockReference(hadGeometry ? topLeft : ComputeInsertPoint(db, def.Settings), btrId);
        LegendBuilder.SetLegendDefaults(br, db);
        LegendBuilder.AppendEntity(ms, br);
        tr.AddNewlyCreatedDBObject(br, true);

        if (hadGeometry && LegendManagement.TryGetEntityExtents(tr, br, out var ext))
        {
            var shift = new Vector3d(topLeft.X - ext.MinPoint.X, topLeft.Y - ext.MaxPoint.Y, 0);
            if (!shift.IsZeroLength())
                br.Position += shift;
        }

        FinalizePlacement(tr, db, br, def.Settings, def.GroupName);
        if (!LegendViewport.Sync(db, tr, def))
            note = AppendNote(note, "een gekoppeld legenda-venster is te klein; vergroot het kader of maak een automatisch venster");
        return UpdateResult.Updated;
    }

    [CommandMethod("NLCSLEGENDARESET", CommandFlags.Modal)]
    public void NlcsLegendaReset()
    {
        var doc = AcApp.DocumentManager.MdiActiveDocument;
        if (doc is null || !RequireInteractive(doc.Editor, "NLCSLEGENDARESET")) return;
        try
        {
            if (!ResolveMutationTarget(doc.Editor, doc.Database, out var target, pickVerb: "resetten")) return;
            var choice = new PromptKeywordOptions("\nEigen instellingen en samenstelling vervangen door de globale standaard? [Resetten/Annuleren]", "Resetten Annuleren") { AllowNone = true };
            choice.Keywords.Default = "Annuleren";
            var answer = doc.Editor.GetKeywords(choice);
            if (answer.Status != PromptStatus.OK || answer.StringResult != "Resetten") return;
            ResetLegend(doc.Editor, doc.Database, target);
        }
        catch (Exception ex) when (!ExceptionSafety.IsFatal(ex))
        {
            doc.Editor.WriteMessage($"\nResetten mislukt: {ex.Message}");
        }
    }

    private string? ResetLegend(Editor editor, Database database, MutationTarget target)
    {
        var settings = LoadGlobalDefaults();
        settings.KnownEntryKeys.Clear();
        return ApplyTargetSettings(editor, database, target, settings, "Resetten");
    }

    private static string AppendNote(string note, string extra) =>
        string.IsNullOrEmpty(note) ? extra : $"{note}; {extra}";

    private static void PurgePending(Database db)
    {
        PurgeOrphanLegendBlocks(db);
    }

    [CommandMethod("NLCSLEGENDABEHEER", CommandFlags.Modal)]
    public void NlcsLegendaBeheer()
    {
        var doc = AcApp.DocumentManager.MdiActiveDocument;
        if (doc is null)
            return;
        var ed = doc.Editor;
        var db = doc.Database;
        if (!RequireInteractive(ed, "NLCSLEGENDABEHEER")) return;

        try
        {
            while (true)
            {
                LegendRegistry registry;
                using (var tr = db.TransactionManager.StartTransaction())
                {
                    registry = LegendManagement.ActiveRegistry(db, tr, LegendStore.Load(db, tr));
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
                pko.Keywords.Add("Resetten");
                pko.Keywords.Add("Instellingen");
                pko.Keywords.Add("Overnemen");
                pko.Keywords.Add("Standaardmaken");
                pko.Keywords.Add("Dupliceren");
                pko.Keywords.Add("Herkomst");
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
                    case "Resetten": NlcsLegendaReset(); break;
                    case "Instellingen": ManagerEditSettings(ed, db, registry); break;
                    case "Overnemen": ManagerCopySettings(ed, db, registry); break;
                    case "Standaardmaken": ManagerMakeGlobalDefault(ed, db, registry); break;
                    case "Dupliceren": ManagerDuplicate(ed, db, registry); break;
                    case "Herkomst": ManagerEditSource(ed, db, registry); break;
                    case "Zoom": ManagerZoom(ed, db, registry); break;
                    case "Naam": ManagerRename(ed, db, registry); break;
                    case "Verwijderen": ManagerDelete(ed, db, registry); break;
                }
            }
        }
        catch (Exception ex) when (!ExceptionSafety.IsFatal(ex))
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
            ids = LegendManagement.ActiveRegistry(db, tr, LegendStore.Load(db, tr)).Legends.Select(l => l.Id).ToList();
            tr.Commit();
        }
        // afzonderlijke transacties per legenda
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
            catch (Exception ex) when (!ExceptionSafety.IsFatal(ex))
            {
                failed++;
                ed.WriteMessage($"\nLegenda {id} niet bijgewerkt: {ex.Message}");
            }
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
            using var view = doc.Editor.GetCurrentView();
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

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private void ManagerEditSettings(Editor ed, Database db, LegendRegistry registry)
    {
        var def = PickLegendFromList(ed, registry, "de instellingen aanpassen van");
        if (def is null) return;
        string id = def.Id;
        var working = def.Settings.Clone();
        using var dialog = new SettingsDialog(working, $"Legenda: {def.Name}", xrefNames: CollectXrefNames(db),
            compositionSource: current => BuildCompositionItems(db, current, id), layerChoices: BuildLayerChoices(db, working));
        dialog.SaveChanges = () => ApplySettingsAndRebuild(ed, db, id, working);
        AcWindows.ShowModalDialog(dialog);
    }

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

    private void ManagerMakeGlobalDefault(Editor ed, Database db, LegendRegistry registry)
    {
        var def = PickLegendFromList(ed, registry, "als globale standaard gebruiken");
        if (def is null) return;
        if (!AskYesNo(ed, $"Instellingen van \"{def.Name}\" als globale standaard voor nieuwe legenda's gebruiken?", true))
            return;
        var where = SaveGlobalDefaults(def.Settings);
        ed.WriteMessage($"\nGlobale standaard bijgewerkt op basis van \"{def.Name}\" ({where}). Bestaande legenda's blijven ongewijzigd.");
    }

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
            // lege legenda: registratie terugdraaien
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

    private void ManagerEditSource(Editor ed, Database db, LegendRegistry registry)
    {
        var def = PickLegendFromList(ed, registry, "de bron aanpassen van");
        if (def is null) return;
        if (def.Scope != LegendScope.Selection)
        {
            ed.WriteMessage("\nAlleen selectie-legenda's hebben een aanpasbare bron; deze omvat de hele tekening.");
            return;
        }

        var pko = new PromptKeywordOptions("\nBron")
        {
            AllowNone = true
        };
        pko.Keywords.Add("Vervangen");
        pko.Keywords.Add("Toevoegen");
        pko.Keywords.Add("Weghalen");
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
                    "Weghalen" => target.SourceHandles.Except(pickedHandles, StringComparer.OrdinalIgnoreCase),
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
                ? (note.Length > 0
                    ? $"\nBron aangepast en legenda bijgewerkt. Let op: {note}."
                    : "\nBron aangepast en legenda bijgewerkt.")
                : $"\nGeen inhoud om te tekenen ({note}); bron en legenda blijven ongewijzigd.");
        }
        catch (Exception ex) when (!ExceptionSafety.IsFatal(ex))
        {
            ed.WriteMessage($"\nBron aanpassen mislukt: {ex.Message}");
        }
    }

    private string? ApplySettingsAndRebuild(Editor ed, Database db, string id, LegendSettings newSettings)
    {
        try
        {
            UpdateResult r; string note;
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var reg = LegendStore.Load(db, tr);
                var def = reg.FindById(id);
                if (def is null) { ed.WriteMessage("\nLegenda niet meer gevonden."); return "Legenda niet meer gevonden."; }
                def.Settings.CopyFrom(newSettings);
                r = BuildManagedLegend(db, tr, reg, def, out _, out note);
                if (r == UpdateResult.Updated)
                    LegendStore.Save(db, tr, reg);
                tr.Commit();
            }
            PurgePending(db);
            if (r != UpdateResult.Updated)
            {
                string error = r == UpdateResult.NoEntries
                    ? "Geen inhoud om te tekenen met deze instellingen" + (note.Length == 0 ? "" : $" ({note})") + ". De vorige legenda en instellingen zijn behouden."
                    : "Bijwerken mislukt. De vorige legenda en instellingen zijn behouden.";
                ed.WriteMessage("\n" + error);
                return error;
            }
            ed.Regen();
            AcApp.UpdateScreen();
            ed.WriteMessage(note.Length > 0
                ? $"\nInstellingen toegepast en legenda bijgewerkt. Let op: {note}."
                : "\nInstellingen toegepast en legenda bijgewerkt.");
            return null;
        }
        catch (Exception ex) when (!ExceptionSafety.IsFatal(ex))
        {
            ed.WriteMessage($"\nToepassen mislukt: {ex.Message}");
            return "Toepassen mislukt: " + ex.Message;
        }
    }

    [CommandMethod("NLCSLEGENDAELEMENT", CommandFlags.Modal)]
    [CommandMethod("NLCSLEGENDAWAAROM", CommandFlags.Modal)]
    public void NlcsLegendaElement()
    {
        var doc = AcApp.DocumentManager.MdiActiveDocument;
        if (doc is null)
            return;
        var ed = doc.Editor;
        var db = doc.Database;
        if (!RequireInteractive(ed, "NLCSLEGENDAELEMENT")) return;

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
                registry = LegendManagement.ActiveRegistry(db, tr0, LegendStore.Load(db, tr0));
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
            var name = target?.Name ?? "globale standaard";

            using var tr = db.TransactionManager.StartTransaction();
            bool managed = LegendManagement.CollectManagedIds(db, tr, registry).Contains(per.ObjectId);
            string layer = tr.GetObject(per.ObjectId, OpenMode.ForRead) is Entity ent ? ent.Layer : "?";
            string localName = string.Empty;
            string? reason = managed
                ? "hoort bij een beheerde legenda, telt niet mee als bron"
                : DiagnoseObject(db, tr, layer, settings, target, per.ObjectId, out localName);
            tr.Commit();

            ed.WriteMessage($"\nElement: {layer}"
                + (string.IsNullOrEmpty(localName) ? "" : $" ({localName})"));
            ed.WriteMessage($"\nLegenda: {name}");
            ed.WriteMessage($"\nResultaat: {(reason is null ? "opgenomen" : "niet opgenomen")}");
            if (reason is not null)
                ed.WriteMessage($"\nReden: {reason}");
        }
        catch (Exception ex) when (!ExceptionSafety.IsFatal(ex))
        {
            ed.WriteMessage($"\nNLCSLEGENDAELEMENT fout: {ex.Message}");
        }
    }

    private static string? DiagnoseObject(
        Database db, Transaction tr, string layerName, LegendSettings settings,
        LegendDefinition? target, ObjectId entId, out string localName)
    {
        localName = string.Empty;
        var ent = tr.GetObject(entId, OpenMode.ForRead) as Entity;

        var lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
        if (lt.Has(layerName) && tr.GetObject(lt[layerName], OpenMode.ForRead) is LayerTableRecord ltr
            && !settings.IncludeInvisibleLayers && (ltr.IsOff || ltr.IsFrozen))
            return "de laag staat uit of is bevroren (zet 'Onzichtbare lagen meenemen' aan om hem toch mee te nemen)";

        var rule = settings.CustomLayerRules.FirstOrDefault(r =>
            ent is not null && r.Matches(layerName, string.Empty)
            && DrawingAnalyzer.EntityMatchesType(ent, r.Type)
            && r.MatchtBlok(ent is BlockReference br ? DrawingAnalyzer.BlockName(br, tr) : string.Empty));
        if (rule is not null)
        {
            localName = rule.Element;
            if (!settings.IsDrawTypeIncluded(rule.Type))
                return $"elementsoort '{rule.Type.DisplayName()}' staat voor deze legenda uit";
            if (target is { Scope: LegendScope.Selection })
            {
                var h = entId.Handle.Value.ToString("X");
                if (!target.SourceHandles.Contains(h, StringComparer.OrdinalIgnoreCase))
                    return "dit object hoort niet bij de bronselectie van deze legenda";
            }
            return null;
        }

        if (!NlcsLayerParser.TryParse(layerName, out var parsed))
            return "deze laag is niet NLCS en heeft geen eigen koppeling";
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

    internal const string TargetEnvVar = "NLCSLEGENDA_TARGET";

    private static LegendDefinition? ResolveTargetLegend(
        Editor ed, Database db, LegendRegistry registry, string actie)
    {
        using (var tr = db.TransactionManager.StartTransaction())
        {
            registry = LegendManagement.ActiveRegistry(db, tr, registry);
            tr.Commit();
        }
        var token = HostEnvironment.IsCoreConsole ? Environment.GetEnvironmentVariable(TargetEnvVar) : null;
        if (!string.IsNullOrWhiteSpace(token))
        {
            if (LegendTargeting.TryResolveByToken(registry, token, out var byToken, out var err))
                return byToken;
            ed.WriteMessage($"\n{TargetEnvVar}=\"{token}\": {err}. Geen wijziging doorgevoerd.");
            return null;
        }

        switch (LegendTargeting.ResolveAuto(registry, out var target))
        {
            case TargetResolution.None:
                return null;
            case TargetResolution.Single:
                return target;
        }

        if (HostEnvironment.IsCoreConsole)
        {
            ed.WriteMessage(
                $"\nMeerdere legenda's gevonden. Kies er headless één via de omgevingsvariabele " +
                $"{TargetEnvVar} (legenda-id of unieke naam). Geen wijziging doorgevoerd.");
            return null;
        }

        var peo = new PromptEntityOptions($"\nKlik een onderdeel van de legenda om te {actie} (of Enter voor een lijst)")
        {
            AllowNone = true
        };
        var per = ed.GetEntity(peo);
        if (per.Status != PromptStatus.OK && per.Status != PromptStatus.None)
            return null;
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
