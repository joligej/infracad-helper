using System.IO;
using System.Reflection;
using System.Text;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using NlcsLegenda.Core;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Core.Application;
using AcWindows = Autodesk.AutoCAD.ApplicationServices.Application;
using Exception = System.Exception;
using WinForms = System.Windows.Forms;

[assembly: CommandClass(typeof(NlcsLegenda.Plugin.Commands))]

namespace NlcsLegenda.Plugin;

public partial class Commands
{
    private const string LegendLayerPrefix = "X-XX-AL-LEGENDA";

    private static string ConfigDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "NlcsLegenda");

    private static string ConfigPath => Path.Combine(ConfigDir, "settings.json");

    private static string DescriptionsPath => Path.Combine(ConfigDir, "omschrijvingen.json");

    private static string PresetsDir => Path.Combine(ConfigDir, "presets");

    internal static string PluginVersion
    {
        get
        {
            var asm = typeof(Commands).Assembly;
            var info = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
            if (!string.IsNullOrEmpty(info))
            {
                var plus = info.IndexOf('+');
                return plus > 0 ? info[..plus] : info;
            }
            return asm.GetName().Version?.ToString(3) ?? "onbekend";
        }
    }

    private static LegendSettings LoadSettings(Database db)
    {
        if (DrawingStore.ReadSettings(db) is { } json)
            return LegendSettings.FromJson(json);
        return LegendSettings.Load(ConfigPath);
    }

    // De globale standaardinstellingen: het startpunt voor een NIEUWE legenda. Bestaande
    // legenda's houden hun eigen snapshot en veranderen niet mee.
    private static LegendSettings LoadGlobalDefaults() => LegendSettings.Load(ConfigPath);

    private static DescriptionCatalog LoadCatalog(Database db)
    {
        var catalog = DescriptionCatalog.Default();
        if (File.Exists(DescriptionsPath))
            catalog.MergeFrom(DescriptionCatalog.Load(DescriptionsPath));
        if (DrawingStore.ReadDescriptions(db) is { } json)
            catalog.MergeFrom(DescriptionCatalog.FromJson(json));
        return catalog;
    }

    [CommandMethod("NLCSLEGENDA", CommandFlags.Modal)]
    public void NlcsLegenda()
    {
        var doc = AcApp.DocumentManager.MdiActiveDocument;
        if (doc is null)
            return;
        var ed = doc.Editor;
        var db = doc.Database;

        try
        {
            // Nieuwe legenda begint bij de globale standaard; wordt daarna een eigen snapshot.
            var settings = LoadGlobalDefaults();
            if (!PromptOptions(ed, settings, out var selection))
            {
                ed.WriteMessage("\nGeannuleerd.");
                return;
            }

            ObjectId btrId;
            bool cancelled = false;

            using (var tr = db.TransactionManager.StartTransaction())
            {
                var registry = LegendStore.Load(db, tr);
                var excluded = LegendManagement.CollectManagedIds(db, tr, registry);
                var analysis = DrawingAnalyzer.Analyze(db, tr, settings, selection, LoadCatalog(db), excluded);
                if (analysis.Entries.Count == 0)
                {
                    ed.WriteMessage("\nGeen NLCS-lagen gevonden om een legenda van te maken.");
                    tr.Commit();
                    return;
                }
                ReportAnalysis(ed, analysis);

                btrId = LegendBuilder.BuildBlock(db, tr, analysis, settings, out _);

                var ms = (BlockTableRecord)tr.GetObject(
                    SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForWrite);
                var br = new BlockReference(Point3d.Origin, btrId);
                ms.AppendEntity(br);
                tr.AddNewlyCreatedDBObject(br, true);

                var jig = new LegendJig(br);
                var result = ed.Drag(jig);
                if (result.Status != PromptStatus.OK)
                {
                    br.Erase();
                    cancelled = true;
                }
                else
                {
                    br.Position = jig.Position;
                    var def = new LegendDefinition
                    {
                        Name = registry.NextDefaultName(),
                        Scope = selection is { Length: > 0 } ? LegendScope.Selection : LegendScope.WholeDrawing,
                        SourceHandles = selection is { Length: > 0 } ? LegendManagement.ToHandles(selection) : new(),
                        GroupName = LegendRegistry.NewGroupName(),
                        Settings = settings.Clone(),
                        CreatedWithVersion = PluginVersion
                    };
                    FinalizePlacement(tr, db, br, settings, def.GroupName);
                    registry.Add(def);
                    LegendStore.Save(db, tr, registry);
                    ed.WriteMessage($"\n\"{def.Name}\" geplaatst ({def.Scope.ToDisplay()}).");
                }
                tr.Commit();
            }

            PurgeTempBlock(db, btrId);
            if (cancelled)
                ed.WriteMessage("\nGeannuleerd.");
        }
        catch (Exception ex)
        {
            ed.WriteMessage($"\nFout bij genereren legenda: {ex.Message}");
        }
    }

    [CommandMethod("NLCSLEGENDAINFO", CommandFlags.Modal)]
    public void NlcsLegendaInfo()
    {
        var doc = AcApp.DocumentManager.MdiActiveDocument;
        if (doc is null)
            return;
        var ed = doc.Editor;
        var db = doc.Database;

        try
        {
            using var tr = db.TransactionManager.StartTransaction();
            var registry = LegendStore.Load(db, tr);

            if (registry.Legends.Count > 0)
            {
                var sb = new StringBuilder();
                sb.Append($"\nNLCS Legenda {PluginVersion} — {registry.Legends.Count} legenda('s) in deze tekening:");
                foreach (var l in registry.Legends)
                {
                    var scope = l.Scope == LegendScope.Selection
                        ? $"selectie ({l.SourceHandles.Count} object(en))"
                        : "hele tekening";
                    sb.Append($"\n  - {l.Name} — {scope}");
                }
                sb.Append("\nGebruik NLCSLEGENDABEHEER om ze te bekijken en bij te werken.");
                ed.WriteMessage(sb.ToString());
                tr.Commit();
                return;
            }

            // Nog geen beheerde legenda: toon wat een hele-tekeninglegenda zou bevatten.
            var settings = LoadGlobalDefaults();
            var excluded = LegendManagement.CollectManagedIds(db, tr, registry);
            var analysis = DrawingAnalyzer.Analyze(db, tr, settings, catalog: LoadCatalog(db), excludedIds: excluded);
            ed.WriteMessage(BuildInfoReport(analysis, settings, "alle tekeningen (globaal)"));
            tr.Commit();
        }
        catch (Exception ex)
        {
            ed.WriteMessage($"\nNLCSLEGENDAINFO fout: {ex.Message}");
        }
    }

    [CommandMethod("NLCSLEGENDAEXPORT", CommandFlags.Modal)]
    public void NlcsLegendaExport()
    {
        var doc = AcApp.DocumentManager.MdiActiveDocument;
        if (doc is null)
            return;
        var ed = doc.Editor;
        var db = doc.Database;

        try
        {
            var settings = LoadGlobalDefaults();
            IReadOnlyList<LegendEntry> entries;
            string suffix = "NLCS-legenda";
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var registry = LegendStore.Load(db, tr);
                var excluded = LegendManagement.CollectManagedIds(db, tr, registry);
                if (registry.Legends.Count > 0)
                {
                    // Bij beheerde legenda's exporteert dit de gekozen legenda met exact
                    // zijn eigen scope en instellingen.
                    var target = ResolveTargetLegend(ed, db, registry, "exporteren");
                    if (target is null)
                    {
                        ed.WriteMessage("\nGeannuleerd.");
                        tr.Commit();
                        return;
                    }
                    ObjectId[]? selection = target.Scope == LegendScope.Selection
                        ? LegendManagement.ResolveHandles(db, target.SourceHandles, out _)
                            .Where(id => !excluded.Contains(id)).ToArray()
                        : null;
                    settings = target.Settings;
                    entries = DrawingAnalyzer.Analyze(db, tr, settings, selection, LoadCatalog(db), excluded).Entries;
                    suffix = "Legenda-" + LegendPresets.SafeFileName(target.Name);
                }
                else
                {
                    entries = DrawingAnalyzer.Analyze(db, tr, settings, catalog: LoadCatalog(db), excludedIds: excluded).Entries;
                }
                tr.Commit();
            }

            if (entries.Count == 0)
            {
                ed.WriteMessage("\nGeen NLCS-lagen gevonden om te exporteren.");
                return;
            }

            var baseName = string.IsNullOrEmpty(db.Filename)
                ? Path.Combine(Path.GetTempPath(), suffix)
                : Path.Combine(Path.GetDirectoryName(db.Filename)!,
                    Path.GetFileNameWithoutExtension(db.Filename) + "_" + suffix);

            var csvPath = baseName + ".csv";
            var jsonPath = baseName + ".json";
            File.WriteAllText(csvPath,
                LegendExport.ToCsv(entries, settings.QuantityDecimals, settings.UnitArea, settings.UnitLength, settings.UnitCount),
                new UTF8Encoding(true));
            File.WriteAllText(jsonPath,
                LegendExport.ToJson(entries, settings.QuantityDecimals, settings.UnitArea, settings.UnitLength, settings.UnitCount));

            ed.WriteMessage($"\n{entries.Count} regel(s) geëxporteerd naar:\n  {csvPath}\n  {jsonPath}");
        }
        catch (Exception ex)
        {
            ed.WriteMessage($"\nNLCSLEGENDAEXPORT fout: {ex.Message}");
        }
    }

    [CommandMethod("NLCSLEGENDABATCH", CommandFlags.Modal)]
    public void NlcsLegendaBatch()
    {
        var doc = AcApp.DocumentManager.MdiActiveDocument;
        if (doc is null)
            return;
        var ed = doc.Editor;

        try
        {
            using var dialog = new WinForms.FolderBrowserDialog
            {
                Description = "Kies de map met DWG-bestanden voor de gecombineerde uittrekstaat",
                UseDescriptionForTitle = true,
                ShowNewFolderButton = false
            };
            if (dialog.ShowDialog() != WinForms.DialogResult.OK || string.IsNullOrWhiteSpace(dialog.SelectedPath))
            {
                ed.WriteMessage("\nGeannuleerd.");
                return;
            }
            RunBatch(ed, doc.Database, dialog.SelectedPath);
        }
        catch (Exception ex)
        {
            ed.WriteMessage($"\nNLCSLEGENDABATCH fout: {ex.Message}");
        }
    }

    // Scriptbare batchvariant voor headless tests.
    [CommandMethod("NLCSLEGENDABATCHTEST", CommandFlags.Modal)]
    public void NlcsLegendaBatchTest()
    {
        var doc = AcApp.DocumentManager.MdiActiveDocument;
        if (doc is null)
            return;
        var ed = doc.Editor;
        try
        {
            var sidecar = Path.Combine(Path.GetTempPath(), "nlcs_batch_test.txt");
            if (!File.Exists(sidecar))
            {
                ed.WriteMessage($"\nNLCSBATCHTEST: {sidecar} ontbreekt.");
                return;
            }
            RunBatch(ed, doc.Database, File.ReadAllText(sidecar).Trim());
        }
        catch (Exception ex)
        {
            ed.WriteMessage($"\nNLCSBATCHTEST error: {ex.Message}");
        }
    }

    private void RunBatch(Editor ed, Database db, string folder)
    {
        if (!Directory.Exists(folder))
        {
            ed.WriteMessage($"\nMap niet gevonden: {folder}");
            return;
        }

        var settings = LoadSettings(db);
        var catalog = LoadCatalog(db);
        var results = BatchExport.AnalyzeFolder(folder, settings, catalog);
        if (results.Count == 0)
        {
            ed.WriteMessage($"\nGeen DWG-bestanden in: {folder}");
            return;
        }

        var withEntries = results
            .Where(r => r.Error is null && r.Entries.Count > 0)
            .Select(r => (r.Drawing, r.Entries))
            .ToList();

        var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        var csvPath = Path.Combine(folder, $"NLCS-uittrekstaat_{stamp}.csv");
        var jsonPath = Path.Combine(folder, $"NLCS-uittrekstaat_{stamp}.json");
        File.WriteAllText(csvPath,
            LegendExport.ToCombinedCsv(withEntries, settings.QuantityDecimals, settings.UnitArea, settings.UnitLength, settings.UnitCount),
            new UTF8Encoding(true));
        File.WriteAllText(jsonPath,
            LegendExport.ToCombinedJson(withEntries, settings.QuantityDecimals, settings.UnitArea, settings.UnitLength, settings.UnitCount));

        int totalRows = withEntries.Sum(w => w.Entries.Count);
        var failed = results.Where(r => r.Error is not null).ToList();
        var empty = results.Count(r => r.Error is null && r.Entries.Count == 0);

        ed.WriteMessage($"\nNLCSBATCH: {results.Count} tekening(en), {withEntries.Count} met regels, " +
                        $"{totalRows} regel(s) totaal, {empty} zonder NLCS, {failed.Count} fout.");
        foreach (var f in failed)
            ed.WriteMessage($"\n  fout in {f.Drawing}: {f.Error}");
        ed.WriteMessage($"\nGeschreven:\n  {csvPath}\n  {jsonPath}");
    }

    [CommandMethod("NLCSLEGENDAVIEWPORT", CommandFlags.Modal)]
    public void NlcsLegendaViewport()
    {
        var doc = AcApp.DocumentManager.MdiActiveDocument;
        if (doc is null)
            return;
        var ed = doc.Editor;
        var db = doc.Database;

        try
        {
            if (db.TileMode)
            {
                ed.WriteMessage("\nOpen eerst een layout (paper space-tabblad) en start het commando daar opnieuw.");
                return;
            }

            var settings = LoadGlobalDefaults();

            Point3d min, max;
            using (var trExt = db.TransactionManager.StartTransaction())
            {
                var registry = LegendStore.Load(db, trExt);
                bool found;
                if (registry.Legends.Count > 0)
                {
                    var target = ResolveTargetLegend(ed, db, registry, "de viewport om te maken");
                    if (target is null)
                    {
                        ed.WriteMessage("\nGeannuleerd.");
                        trExt.Commit();
                        return;
                    }
                    found = LegendManagement.TryGetGroupExtents(db, trExt, target.GroupName, out var ext);
                    min = found ? ext.MinPoint : Point3d.Origin;
                    max = found ? ext.MaxPoint : Point3d.Origin;
                    settings = target.Settings;
                }
                else
                {
                    found = TryGetLegendExtents(db, trExt, out min, out max);
                }
                trExt.Commit();
                if (!found)
                {
                    ed.WriteMessage("\nGeen legenda gevonden om een viewport omheen te maken.");
                    return;
                }
            }

            double mw = max.X - min.X;
            double mh = max.Y - min.Y;
            double paperPerModel = 1000.0 / settings.Scale; // papier-mm per modeleenheid
            double marginPaper = settings.ViewportMarginMm;

            double vpW, vpH;
            Point3d vpCenter;
            Point2d viewCenter;

            if (settings.ViewportManual)
            {
                var p1 = ed.GetPoint("\nEerste hoek van de viewport:");
                if (p1.Status != PromptStatus.OK)
                {
                    ed.WriteMessage("\nGeannuleerd.");
                    return;
                }
                var pco = new PromptCornerOptions("\nTegenoverliggende hoek:", p1.Value);
                var p2 = ed.GetCorner(pco);
                if (p2.Status != PromptStatus.OK)
                {
                    ed.WriteMessage("\nGeannuleerd.");
                    return;
                }

                double left = Math.Min(p1.Value.X, p2.Value.X);
                double right = Math.Max(p1.Value.X, p2.Value.X);
                double bottom = Math.Min(p1.Value.Y, p2.Value.Y);
                double topY = Math.Max(p1.Value.Y, p2.Value.Y);
                vpW = right - left;
                vpH = topY - bottom;
                if (vpW < 1e-6 || vpH < 1e-6)
                {
                    ed.WriteMessage("\nDe getekende rechthoek is te klein.");
                    return;
                }
                vpCenter = new Point3d((left + right) / 2.0, (bottom + topY) / 2.0, 0);
                // Legenda met de linkerbovenhoek in de linkerbovenhoek van de viewport.
                viewCenter = new Point2d(
                    min.X + (vpW / 2.0) / paperPerModel,
                    max.Y - (vpH / 2.0) / paperPerModel);

                double needW = mw * paperPerModel;
                double needH = mh * paperPerModel;
                if (needW > vpW + 1e-6 || needH > vpH + 1e-6)
                    ed.WriteMessage(
                        $"\nLet op: de legenda ({needW:0} x {needH:0} mm op 1:{settings.Scale:0}) is groter dan " +
                        $"het getekende kader ({vpW:0} x {vpH:0} mm); een deel valt buiten de viewport.");
            }
            else
            {
                vpW = mw * paperPerModel + 2 * marginPaper;
                vpH = mh * paperPerModel + 2 * marginPaper;
                viewCenter = new Point2d((min.X + max.X) / 2.0, (min.Y + max.Y) / 2.0);

                var pp = ed.GetPoint("\nMiddelpunt van de viewport op de sheet:");
                if (pp.Status != PromptStatus.OK)
                {
                    ed.WriteMessage("\nGeannuleerd.");
                    return;
                }
                vpCenter = pp.Value;
            }

            using (var tr = db.TransactionManager.StartTransaction())
            {
                // De paper space van de huidige layout (robuust als je in MSPACE staat).
                var lm = LayoutManager.Current;
                var layout = (Layout)tr.GetObject(lm.GetLayoutId(lm.CurrentLayout), OpenMode.ForRead);
                var ps = (BlockTableRecord)tr.GetObject(layout.BlockTableRecordId, OpenMode.ForWrite);
                var vp = new Viewport();
                ps.AppendEntity(vp);
                tr.AddNewlyCreatedDBObject(vp, true);

                vp.CenterPoint = vpCenter;
                vp.Width = vpW;
                vp.Height = vpH;
                vp.ViewCenter = viewCenter;
                vp.ViewHeight = vpH / paperPerModel; // exacte schaal 1:Scale
                try { vp.On = true; } catch { /* maximaal aantal actieve viewports bereikt */ }
                vp.Locked = true;

                tr.Commit();
            }

            ed.WriteMessage($"\nViewport (1:{settings.Scale:0}) geplaatst in de huidige layout.");
        }
        catch (Exception ex)
        {
            ed.WriteMessage($"\nNLCSLEGENDAVIEWPORT fout: {ex.Message}");
        }
    }

    [CommandMethod("NLCSLEGENDAUPDATE", CommandFlags.Modal)]
    public void NlcsLegendaUpdate()
    {
        var doc = AcApp.DocumentManager.MdiActiveDocument;
        if (doc is null)
            return;
        var ed = doc.Editor;
        var db = doc.Database;

        try
        {
            LegendDefinition? target;
            using (var tr0 = db.TransactionManager.StartTransaction())
            {
                var reg0 = LegendStore.Load(db, tr0);
                MaybeMigrate(db, tr0, reg0);
                tr0.Commit();
            }
            using (var trPick = db.TransactionManager.StartTransaction())
            {
                var reg = LegendStore.Load(db, trPick);
                trPick.Commit();
                if (reg.Legends.Count == 0)
                {
                    ed.WriteMessage("\nGeen legenda om bij te werken. Plaats er eerst een met NLCSLEGENDA.");
                    return;
                }
                target = ResolveTargetLegend(ed, db, reg, "bijwerken");
            }
            if (target is null)
            {
                ed.WriteMessage("\nGeannuleerd.");
                return;
            }

            UpdateResult result;
            int rows = 0;
            string note = string.Empty;
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var registry = LegendStore.Load(db, tr);
                var def = registry.FindById(target.Id) ?? target;
                result = BuildManagedLegend(db, tr, registry, def, out rows, out note);
                if (result == UpdateResult.Updated)
                    LegendStore.Save(db, tr, registry);
                tr.Commit();
            }
            PurgePending(db);

            switch (result)
            {
                case UpdateResult.Updated:
                    ed.WriteMessage($"\n\"{target.Name}\" bijgewerkt ({rows} regel(s))"
                        + (note.Length > 0 ? $"; {note}." : "."));
                    break;
                case UpdateResult.NoEntries:
                    ed.WriteMessage($"\n\"{target.Name}\" levert geen legenda-regels op"
                        + (note.Length > 0 ? $" ({note})" : "")
                        + "; de bestaande legenda is niet gewijzigd. Verwijder hem eventueel via NLCSLEGENDABEHEER.");
                    break;
                default:
                    ed.WriteMessage($"\n\"{target.Name}\" kon niet worden bijgewerkt; de bestaande legenda staat er nog.");
                    break;
            }
        }
        catch (Exception ex)
        {
            ed.WriteMessage($"\nNLCSLEGENDAUPDATE fout: {ex.Message}");
        }
    }

    [CommandMethod("NLCSLEGENDACONFIG", CommandFlags.Modal)]
    public void NlcsLegendaConfig()
    {
        var doc = AcApp.DocumentManager.MdiActiveDocument;
        var ed = doc?.Editor;
        if (ed is null)
            return;

        try
        {
            Directory.CreateDirectory(ConfigDir);
            var created = new List<string>();
            if (!File.Exists(ConfigPath))
            {
                new LegendSettings().Save(ConfigPath);
                created.Add("instellingen");
            }
            if (!File.Exists(DescriptionsPath))
            {
                DescriptionCatalog.Default().Save(DescriptionsPath);
                created.Add("omschrijvingen");
            }

            var maakStatus = created.Count > 0 ? $" ({string.Join(" + ", created)} aangemaakt)" : "";
            var db = doc!.Database;
            var drawingSettings = DrawingStore.HasSettings(db);
            var drawingDesc = DrawingStore.HasDescriptions(db);

            ed.WriteMessage(
                $"\nGlobale bestanden{maakStatus} (voor alle tekeningen):" +
                $"\n  {ConfigPath}" +
                $"\n  {DescriptionsPath}" +
                "\nTekeningspecifieke instellingen bewaren we in de tekening zelf; er komen dus" +
                " geen losse bestanden naast je .dwg te staan." +
                $"\n  Instellingen in deze tekening: {(drawingSettings ? "ja" : "nee")}" +
                $"\n  Omschrijvingen in deze tekening: {(drawingDesc ? "ja" : "nee")}");

            if ((drawingSettings || drawingDesc) &&
                AskYesNo(ed, "Tekeningspecifieke NLCS-configuratie uit deze tekening wissen?", false))
            {
                DrawingStore.Clear(db);
                ed.WriteMessage("\nTekeningspecifieke configuratie gewist; voortaan gelden de globale instellingen.");
            }
        }
        catch (Exception ex)
        {
            ed.WriteMessage($"\nNLCSLEGENDACONFIG fout: {ex.Message}");
        }
    }

    [CommandMethod("NLCSLEGENDAOPTIES", CommandFlags.Modal)]
    public void NlcsLegendaOpties()
    {
        var doc = AcApp.DocumentManager.MdiActiveDocument;
        if (doc is null)
            return;
        var ed = doc.Editor;
        var db = doc.Database;

        try
        {
            var initial = DrawingStore.HasSettings(db) ? ConfigScope.Drawing : ConfigScope.Global;
            using var dialog = new SettingsDialog(initial, scope => LoadSettingsForScope(db, scope));
            dialog.ApplyRequested += (_, _) =>
                ed.WriteMessage($"\nInstellingen opgeslagen ({SaveSettingsToScope(db, dialog.Settings, dialog.Scope)}).");
            if (AcWindows.ShowModalDialog(dialog) != WinForms.DialogResult.OK)
            {
                ed.WriteMessage("\nGesloten.");
                return;
            }
            var where = SaveSettingsToScope(db, dialog.Settings, dialog.Scope);
            ed.WriteMessage($"\nInstellingen opgeslagen ({where}).");
        }
        catch (Exception ex)
        {
            ed.WriteMessage($"\nNLCSLEGENDAOPTIES fout: {ex.Message}");
        }
    }

    [CommandMethod("NLCSLEGENDAOMSCHRIJVINGEN", CommandFlags.Modal)]
    public void NlcsLegendaOmschrijvingen()
    {
        var doc = AcApp.DocumentManager.MdiActiveDocument;
        if (doc is null)
            return;
        var ed = doc.Editor;
        var db = doc.Database;

        try
        {
            var initial = DrawingStore.HasDescriptions(db) ? ConfigScope.Drawing : ConfigScope.Global;
            using var dialog = new DescriptionsDialog(initial, scope => LoadCatalogForScope(db, scope));
            dialog.ApplyRequested += (_, _) =>
                ed.WriteMessage($"\nOmschrijvingen opgeslagen ({SaveCatalogToScope(db, dialog.ToCatalog().Diff(DescriptionCatalog.Default()), dialog.Scope)}).");
            if (AcWindows.ShowModalDialog(dialog) != WinForms.DialogResult.OK)
            {
                ed.WriteMessage("\nGesloten.");
                return;
            }
            var where = SaveCatalogToScope(db, dialog.ToCatalog().Diff(DescriptionCatalog.Default()), dialog.Scope);
            ed.WriteMessage($"\nOmschrijvingen opgeslagen ({where}).");
        }
        catch (Exception ex)
        {
            ed.WriteMessage($"\nNLCSLEGENDAOMSCHRIJVINGEN fout: {ex.Message}");
        }
    }

    private static LegendSettings LoadSettingsForScope(Database db, ConfigScope scope)
    {
        if (scope == ConfigScope.Drawing && DrawingStore.ReadSettings(db) is { } json)
            return LegendSettings.FromJson(json);
        return LegendSettings.Load(ConfigPath);
    }

    private static DescriptionCatalog LoadCatalogForScope(Database db, ConfigScope scope)
    {
        var catalog = DescriptionCatalog.Default();
        if (File.Exists(DescriptionsPath))
            catalog.MergeFrom(DescriptionCatalog.Load(DescriptionsPath));
        if (scope == ConfigScope.Drawing && DrawingStore.ReadDescriptions(db) is { } json)
            catalog.MergeFrom(DescriptionCatalog.FromJson(json));
        return catalog;
    }

    private static ConfigScope PromptScope(Editor ed, ConfigScope preferred)
    {
        // Sinds meerdere legenda's is er geen "voor deze tekening"-scope meer voor
        // instellingen: deze commando's bewerken de globale standaard voor nieuwe legenda's.
        // Bestaande legenda's pas je aan via NLCSLEGENDABEHEER.
        return ConfigScope.Global;
    }

    [CommandMethod("NLCSLEGENDAUITVINKEN", CommandFlags.Modal)]
    public void NlcsLegendaUitvinken()
    {
        var doc = AcApp.DocumentManager.MdiActiveDocument;
        if (doc is null)
            return;
        var ed = doc.Editor;
        var db = doc.Database;

        try
        {
            var peo = new PromptEntityOptions("\nSelecteer een NLCS-object om uit/aan te vinken");
            peo.SetRejectMessage("\nGeen geldig object.");
            var per = ed.GetEntity(peo);
            if (per.Status != PromptStatus.OK)
            {
                ed.WriteMessage("\nGeannuleerd.");
                return;
            }

            string layerName;
            using (var tr = db.TransactionManager.StartTransaction())
            {
                layerName = ((Entity)tr.GetObject(per.ObjectId, OpenMode.ForRead)).Layer;
                tr.Commit();
            }

            if (!NlcsLayerParser.TryParse(layerName, out var layer))
            {
                ed.WriteMessage($"\n\"{layerName}\" is geen NLCS-laag.");
                return;
            }

            var key = LegendSettings.EntryKey(layer!);
            var scope = PromptScope(ed, DrawingStore.HasSettings(db) ? ConfigScope.Drawing : ConfigScope.Global);
            var settings = LoadSettingsForScope(db, scope);

            bool nowExcluded;
            if (settings.ExcludedEntries.Contains(key))
            {
                settings.ExcludedEntries.Remove(key);
                nowExcluded = false;
            }
            else
            {
                settings.ExcludedEntries.Add(key);
                nowExcluded = true;
            }
            var where = SaveSettingsToScope(db, settings, scope);
            ed.WriteMessage($"\n{key} {(nowExcluded ? "uitgevinkt" : "weer opgenomen")} ({where}).");

            if (LegendGroupExists(db) && AskYesNo(ed, "Legenda nu bijwerken?", true))
                NlcsLegendaUpdate();
        }
        catch (Exception ex)
        {
            ed.WriteMessage($"\nNLCSLEGENDAUITVINKEN fout: {ex.Message}");
        }
    }

    [CommandMethod("NLCSLEGENDATOEVOEGEN", CommandFlags.Modal)]
    public void NlcsLegendaToevoegen()
    {
        var doc = AcApp.DocumentManager.MdiActiveDocument;
        if (doc is null)
            return;
        var ed = doc.Editor;
        var db = doc.Database;

        try
        {
            var peo = new PromptEntityOptions(
                "\nSelecteer een object van de gewenste laag [Typen]", "Typen");
            peo.AllowNone = false;
            var per = ed.GetEntity(peo);
            string layerName;
            if (per.Status == PromptStatus.Keyword && per.StringResult == "Typen")
            {
                var pso = new PromptStringOptions("\nLaagnaam:") { AllowSpaces = true };
                var sr = ed.GetString(pso);
                if (sr.Status != PromptStatus.OK || string.IsNullOrWhiteSpace(sr.StringResult))
                {
                    ed.WriteMessage("\nGeannuleerd.");
                    return;
                }
                layerName = sr.StringResult.Trim();
            }
            else if (per.Status == PromptStatus.OK)
            {
                using var tr = db.TransactionManager.StartTransaction();
                layerName = ((Entity)tr.GetObject(per.ObjectId, OpenMode.ForRead)).Layer;
                tr.Commit();
            }
            else
            {
                ed.WriteMessage("\nGeannuleerd.");
                return;
            }

            if (!LayerNaming.IsValid(layerName))
            {
                ed.WriteMessage($"\n\"{layerName}\" is geen geldige laagnaam.");
                return;
            }

            var type = PromptDrawType(ed);
            if (type is null)
                return;

            var pdesc = new PromptStringOptions("\nOmschrijving:") { AllowSpaces = true };
            var dres = ed.GetString(pdesc);
            if (dres.Status != PromptStatus.OK || string.IsNullOrWhiteSpace(dres.StringResult))
            {
                ed.WriteMessage("\nGeannuleerd.");
                return;
            }

            var status = PromptEntryStatus(ed);
            var manual = new ManualEntry
            {
                Layer = layerName,
                Type = type.Value,
                Description = dres.StringResult.Trim(),
                Status = status
            };

            if (type == NlcsDrawType.Arcering)
            {
                var pp = new PromptStringOptions("\nArceerpatroon <SOLID>:")
                { AllowSpaces = false, DefaultValue = "SOLID", UseDefaultValue = true };
                var pr = ed.GetString(pp);
                if (pr.Status == PromptStatus.OK && !string.IsNullOrWhiteSpace(pr.StringResult))
                    manual.HatchPattern = pr.StringResult.Trim();
            }

            var scope = PromptScope(ed, DrawingStore.HasSettings(db) ? ConfigScope.Drawing : ConfigScope.Global);
            var settings = LoadSettingsForScope(db, scope);
            settings.ManualEntries.Add(manual);
            var where = SaveSettingsToScope(db, settings, scope);
            ed.WriteMessage($"\nRegel \"{manual.Description}\" toegevoegd ({where}).");

            if (LegendGroupExists(db) && AskYesNo(ed, "Legenda nu bijwerken?", true))
                NlcsLegendaUpdate();
        }
        catch (Exception ex)
        {
            ed.WriteMessage($"\nNLCSLEGENDATOEVOEGEN fout: {ex.Message}");
        }
    }

    private static NlcsDrawType? PromptDrawType(Editor ed)
    {
        var pko = new PromptKeywordOptions("\nType");
        pko.Keywords.Add("Lijn");
        pko.Keywords.Add("Vlak");
        pko.Keywords.Add("Arcering");
        pko.Keywords.Add("Vulling");
        pko.Keywords.Add("Symbool");
        pko.Keywords.Default = "Lijn";
        pko.AllowNone = true;
        var res = ed.GetKeywords(pko);
        if (res.Status != PromptStatus.OK)
            return null;
        return res.StringResult switch
        {
            "Vlak" => NlcsDrawType.Vlak,
            "Arcering" => NlcsDrawType.Arcering,
            "Vulling" => NlcsDrawType.Vlakvulling,
            "Symbool" => NlcsDrawType.Symbool,
            _ => NlcsDrawType.Geometrie
        };
    }

    private static NlcsStatus PromptEntryStatus(Editor ed)
    {
        var pko = new PromptKeywordOptions("\nStatus");
        pko.Keywords.Add("Nieuw");
        pko.Keywords.Add("Bestaand");
        pko.Keywords.Add("Vervallen");
        pko.Keywords.Add("Tijdelijk");
        pko.Keywords.Add("Revisie");
        pko.Keywords.Default = "Nieuw";
        pko.AllowNone = true;
        var res = ed.GetKeywords(pko);
        if (res.Status != PromptStatus.OK)
            return NlcsStatus.Nieuw;
        return res.StringResult switch
        {
            "Bestaand" => NlcsStatus.Bestaand,
            "Vervallen" => NlcsStatus.Vervallen,
            "Tijdelijk" => NlcsStatus.Tijdelijk,
            "Revisie" => NlcsStatus.Revisie,
            _ => NlcsStatus.Nieuw
        };
    }

    [CommandMethod("NLCSLEGENDASAMENSTELLEN", CommandFlags.Modal)]
    public void NlcsLegendaSamenstellen()
    {
        var doc = AcApp.DocumentManager.MdiActiveDocument;
        if (doc is null)
            return;
        var ed = doc.Editor;
        var db = doc.Database;

        try
        {
            var initial = ConfigScope.Global;

            var probe = LoadSettingsForScope(db, initial);
            probe.ExcludedEntries = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            probe.ManualEntries = new List<ManualEntry>();
            probe.IncludedStatuses = new HashSet<NlcsStatus>
            {
                NlcsStatus.Nieuw, NlcsStatus.Bestaand, NlcsStatus.Vervallen,
                NlcsStatus.Tijdelijk, NlcsStatus.Revisie, NlcsStatus.Overig
            };

            List<EntryCheckItem> items;
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var analysis = DrawingAnalyzer.Analyze(db, tr, probe, catalog: LoadCatalog(db));
                items = analysis.Entries
                    .Select(e => new EntryCheckItem(LegendSettings.EntryKey(e),
                        $"[{e.Status.DisplayName()}] {e.Description}"))
                    .GroupBy(x => x.Key)
                    .Select(g => g.First())
                    .OrderBy(x => x.Label, StringComparer.CurrentCultureIgnoreCase)
                    .ToList();
                tr.Commit();
            }

            using var dialog = new LegendManageDialog(items, initial, scope => LoadSettingsForScope(db, scope));
            dialog.ApplyRequested += (_, _) =>
                ed.WriteMessage($"\nSamenstelling opgeslagen ({SaveComposition(db, dialog.Scope, dialog.ExcludedKeys, dialog.ManualEntries)}).");
            if (AcWindows.ShowModalDialog(dialog) != WinForms.DialogResult.OK)
            {
                ed.WriteMessage("\nGesloten.");
                return;
            }
            var where = SaveComposition(db, dialog.Scope, dialog.ExcludedKeys, dialog.ManualEntries);
            ed.WriteMessage($"\nSamenstelling opgeslagen ({where}).");

            if (LegendGroupExists(db) && AskYesNo(ed, "Legenda nu bijwerken?", true))
                NlcsLegendaUpdate();
        }
        catch (Exception ex)
        {
            ed.WriteMessage($"\nNLCSLEGENDASAMENSTELLEN fout: {ex.Message}");
        }
    }

    private static string SaveComposition(
        Database db, ConfigScope scope, HashSet<string> excluded, List<ManualEntry> manual)
    {
        var settings = LoadSettingsForScope(db, scope);
        settings.ExcludedEntries = excluded;
        settings.ManualEntries = manual;
        return SaveSettingsToScope(db, settings, scope);
    }

    [CommandMethod("NLCSLEGENDAXREFS", CommandFlags.Modal)]
    public void NlcsLegendaXrefs()
    {
        var doc = AcApp.DocumentManager.MdiActiveDocument;
        if (doc is null)
            return;
        var ed = doc.Editor;
        var db = doc.Database;

        try
        {
            var xrefs = new List<string>();
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                foreach (ObjectId id in bt)
                {
                    if (tr.GetObject(id, OpenMode.ForRead) is BlockTableRecord btr
                        && btr.IsFromExternalReference && !btr.IsDependent)
                        xrefs.Add(btr.Name);
                }
                tr.Commit();
            }
            xrefs.Sort(StringComparer.CurrentCultureIgnoreCase);

            if (xrefs.Count == 0)
            {
                ed.WriteMessage("\nGeen gekoppelde xrefs in deze tekening.");
                return;
            }

            // De keuze staat altijd in de tekening zelf.
            var settings = LoadSettingsForScope(db, ConfigScope.Drawing);

            while (true)
            {
                ed.WriteMessage("\nXrefs meenemen (tekeningspecifiek):");
                for (int i = 0; i < xrefs.Count; i++)
                    ed.WriteMessage($"\n  {i + 1}. {xrefs[i]}: {(settings.IsXrefIncluded(xrefs[i]) ? "aan" : "uit")}");

                var pko = new PromptKeywordOptions("\nKies een nummer om te wisselen");
                for (int i = 0; i < xrefs.Count; i++)
                    pko.Keywords.Add((i + 1).ToString());
                pko.Keywords.Add("Alle");
                pko.Keywords.Add("Geen");
                pko.Keywords.Add("Klaar");
                pko.Keywords.Default = "Klaar";
                pko.AllowNone = true;

                var res = ed.GetKeywords(pko);
                if (res.Status != PromptStatus.OK || res.StringResult == "Klaar")
                    break;

                if (res.StringResult == "Alle")
                    foreach (var x in xrefs) settings.XrefInclusion[x] = true;
                else if (res.StringResult == "Geen")
                    foreach (var x in xrefs) settings.XrefInclusion[x] = false;
                else if (int.TryParse(res.StringResult, out var n) && n >= 1 && n <= xrefs.Count)
                {
                    var x = xrefs[n - 1];
                    settings.XrefInclusion[x] = !settings.IsXrefIncluded(x);
                }
            }

            SaveSettingsToScope(db, settings, ConfigScope.Drawing);
            ed.WriteMessage("\n  \u2192 xref-keuze opgeslagen in deze tekening.");

            if (LegendGroupExists(db) && AskYesNo(ed, "Legenda nu bijwerken?", true))
                NlcsLegendaUpdate();
        }
        catch (Exception ex)
        {
            ed.WriteMessage($"\nNLCSLEGENDAXREFS fout: {ex.Message}");
        }
    }

    [CommandMethod("NLCSLEGENDASTATUS", CommandFlags.Modal)]
    public void NlcsLegendaStatus()
    {
        var doc = AcApp.DocumentManager.MdiActiveDocument;
        if (doc is null)
            return;
        var ed = doc.Editor;
        var db = doc.Database;

        try
        {
            var scope = PromptScope(ed, DrawingStore.HasSettings(db) ? ConfigScope.Drawing : ConfigScope.Global);
            var settings = LoadSettingsForScope(db, scope);
            bool changed = false;

            while (true)
            {
                ed.WriteMessage("\nEigen statussen:");
                if (settings.CustomStatuses.Count == 0)
                    ed.WriteMessage("\n  (nog geen)");
                for (int i = 0; i < settings.CustomStatuses.Count; i++)
                    ed.WriteMessage($"\n  {i + 1}. {settings.CustomStatuses[i].Name}: " +
                        $"{settings.CustomStatuses[i].Members.Count} regel(s)");

                var pko = new PromptKeywordOptions("\nKies een status om te bewerken");
                for (int i = 0; i < settings.CustomStatuses.Count; i++)
                    pko.Keywords.Add((i + 1).ToString());
                pko.Keywords.Add("Nieuw");
                if (settings.CustomStatuses.Count > 0)
                    pko.Keywords.Add("Verwijderen");
                pko.Keywords.Add("Klaar");
                pko.Keywords.Default = "Klaar";
                pko.AllowNone = true;

                var res = ed.GetKeywords(pko);
                if (res.Status != PromptStatus.OK || res.StringResult == "Klaar")
                    break;

                if (res.StringResult == "Nieuw")
                {
                    var name = AskStatusName(ed);
                    if (name is not null)
                    {
                        settings.CustomStatuses.Add(new CustomStatus { Name = name });
                        changed = true;
                    }
                }
                else if (res.StringResult == "Verwijderen")
                {
                    var idx = AskStatusIndex(ed, settings.CustomStatuses);
                    if (idx >= 0)
                    {
                        ed.WriteMessage($"\n  \u2192 \"{settings.CustomStatuses[idx].Name}\" verwijderd.");
                        settings.CustomStatuses.RemoveAt(idx);
                        changed = true;
                    }
                }
                else if (int.TryParse(res.StringResult, out var n) && n >= 1 && n <= settings.CustomStatuses.Count)
                {
                    changed |= EditCustomStatus(ed, db, settings, settings.CustomStatuses[n - 1]);
                }
            }

            if (!changed)
            {
                ed.WriteMessage("\nNiets gewijzigd.");
                return;
            }

            var where = SaveSettingsToScope(db, settings, scope);
            ed.WriteMessage($"\n  \u2192 eigen statussen opgeslagen ({where}).");

            if (LegendGroupExists(db) && AskYesNo(ed, "Legenda nu bijwerken?", true))
                NlcsLegendaUpdate();
        }
        catch (Exception ex)
        {
            ed.WriteMessage($"\nNLCSLEGENDASTATUS fout: {ex.Message}");
        }
    }

    [CommandMethod("NLCSLEGENDAPRESET", CommandFlags.Modal)]
    public void NlcsLegendaPreset()
    {
        var doc = AcApp.DocumentManager.MdiActiveDocument;
        if (doc is null)
            return;
        var ed = doc.Editor;
        var db = doc.Database;

        try
        {
            var pko = new PromptKeywordOptions("\nProfielen");
            pko.Keywords.Add("Laden");
            pko.Keywords.Add("Opslaan");
            pko.Keywords.Add("Lijst");
            pko.Keywords.Add("Verwijderen");
            pko.Keywords.Add("Importeren");
            pko.Keywords.Add("Exporteren");
            pko.Keywords.Default = "Laden";
            pko.AllowNone = true;

            var res = ed.GetKeywords(pko);
            if (res.Status != PromptStatus.OK)
            {
                ed.WriteMessage("\nGeannuleerd.");
                return;
            }

            switch (res.StringResult)
            {
                case "Opslaan": PresetSave(ed, db); break;
                case "Laden": PresetLoad(ed, db); break;
                case "Lijst": PresetList(ed); break;
                case "Verwijderen": PresetDelete(ed); break;
                case "Importeren": PresetImport(ed); break;
                case "Exporteren": PresetExport(ed); break;
            }
        }
        catch (Exception ex)
        {
            ed.WriteMessage($"\nNLCSLEGENDAPRESET fout: {ex.Message}");
        }
    }

    private void PresetSave(Editor ed, Database db)
    {
        var name = AskPresetName(ed);
        if (name is null)
            return;
        if (LegendPresets.Exists(PresetsDir, name)
            && !AskYesNo(ed, $"Profiel \"{name}\" bestaat al. Overschrijven?", true))
        {
            ed.WriteMessage("\nGeannuleerd.");
            return;
        }
        LegendPresets.Save(PresetsDir, name, LoadSettings(db));
        ed.WriteMessage($"\nProfiel \"{name}\" opgeslagen.");
    }

    private void PresetLoad(Editor ed, Database db)
    {
        var names = LegendPresets.List(PresetsDir);
        if (names.Count == 0)
        {
            ed.WriteMessage("\nNog geen profielen opgeslagen. Gebruik eerst de optie Opslaan.");
            return;
        }
        int idx = AskPresetIndex(ed, names, "Welk profiel laden");
        if (idx < 0)
            return;
        var settings = LegendPresets.Load(PresetsDir, names[idx]);
        if (settings is null)
        {
            ed.WriteMessage("\nProfiel niet gevonden.");
            return;
        }
        var scope = PromptScope(ed, ConfigScope.Drawing);
        var where = SaveSettingsToScope(db, settings, scope);
        ed.WriteMessage($"\nProfiel \"{names[idx]}\" geladen ({where}).");

        if (LegendGroupExists(db) && AskYesNo(ed, "Legenda nu bijwerken?", true))
            NlcsLegendaUpdate();
    }

    private static void PresetList(Editor ed)
    {
        var names = LegendPresets.List(PresetsDir);
        if (names.Count == 0)
        {
            ed.WriteMessage("\nNog geen profielen opgeslagen.");
            return;
        }
        ed.WriteMessage($"\n{names.Count} profiel(en):");
        foreach (var n in names)
            ed.WriteMessage($"\n  - {n}");
    }

    private static void PresetDelete(Editor ed)
    {
        var names = LegendPresets.List(PresetsDir);
        if (names.Count == 0)
        {
            ed.WriteMessage("\nNog geen profielen opgeslagen.");
            return;
        }
        int idx = AskPresetIndex(ed, names, "Welk profiel verwijderen");
        if (idx < 0)
            return;
        if (!AskYesNo(ed, $"Profiel \"{names[idx]}\" verwijderen?", false))
        {
            ed.WriteMessage("\nGeannuleerd.");
            return;
        }
        LegendPresets.Delete(PresetsDir, names[idx]);
        ed.WriteMessage($"\nProfiel \"{names[idx]}\" verwijderd.");
    }

    private void PresetImport(Editor ed)
    {
        var opts = new PromptOpenFileOptions("\nProfiel importeren")
        {
            Filter = "NLCS-profiel (*.json)|*.json|Alle bestanden (*.*)|*.*"
        };
        var fr = ed.GetFileNameForOpen(opts);
        if (fr.Status != PromptStatus.OK)
        {
            ed.WriteMessage("\nGeannuleerd.");
            return;
        }

        var suggested = Path.GetFileNameWithoutExtension(fr.StringResult);
        var name = AskPresetName(ed, suggested);
        if (name is null)
            return;
        if (LegendPresets.Exists(PresetsDir, name)
            && !AskYesNo(ed, $"Profiel \"{name}\" bestaat al. Overschrijven?", true))
        {
            ed.WriteMessage("\nGeannuleerd.");
            return;
        }

        if (LegendPresets.TryImport(PresetsDir, fr.StringResult, name, out var error))
            ed.WriteMessage($"\nProfiel \"{name}\" geïmporteerd.");
        else
            ed.WriteMessage($"\nImporteren mislukt: {error} De bestaande profielen zijn ongewijzigd.");
    }

    private void PresetExport(Editor ed)
    {
        var names = LegendPresets.List(PresetsDir);
        if (names.Count == 0)
        {
            ed.WriteMessage("\nNog geen profielen om te exporteren. Gebruik eerst de optie Opslaan.");
            return;
        }
        int idx = AskPresetIndex(ed, names, "Welk profiel exporteren");
        if (idx < 0)
            return;

        var opts = new PromptSaveFileOptions("\nProfiel opslaan als")
        {
            Filter = "NLCS-profiel (*.json)|*.json",
            InitialFileName = LegendPresets.SafeFileName(names[idx])
        };
        var fr = ed.GetFileNameForSave(opts);
        if (fr.Status != PromptStatus.OK)
        {
            ed.WriteMessage("\nGeannuleerd.");
            return;
        }

        var path = LegendPresets.Export(PresetsDir, names[idx], fr.StringResult);
        ed.WriteMessage($"\nProfiel \"{names[idx]}\" geëxporteerd naar:\n  {path}");
    }

    private static string? AskPresetName(Editor ed, string? initial = null)
    {
        var pso = new PromptStringOptions("\nNaam voor het profiel:") { AllowSpaces = true };
        if (!string.IsNullOrWhiteSpace(initial))
        {
            pso.DefaultValue = initial;
            pso.UseDefaultValue = true;
        }
        var sr = ed.GetString(pso);
        if (sr.Status != PromptStatus.OK || string.IsNullOrWhiteSpace(sr.StringResult))
        {
            ed.WriteMessage("\nGeannuleerd.");
            return null;
        }
        return sr.StringResult.Trim();
    }

    private static int AskPresetIndex(Editor ed, IReadOnlyList<string> names, string prompt)
    {
        ed.WriteMessage($"\n{prompt}:");
        for (int i = 0; i < names.Count; i++)
            ed.WriteMessage($"\n  {i + 1}. {names[i]}");

        var pio = new PromptIntegerOptions($"\n{prompt} (1-{names.Count}, 0 = annuleren)")
        {
            LowerLimit = 0,
            UpperLimit = names.Count,
            DefaultValue = 0,
            AllowNone = true
        };
        var r = ed.GetInteger(pio);
        if (r.Status != PromptStatus.OK || r.Value < 1)
        {
            ed.WriteMessage("\nGeannuleerd.");
            return -1;
        }
        return r.Value - 1;
    }

    private static bool EditCustomStatus(Editor ed, Database db, LegendSettings settings, CustomStatus status)
    {
        bool changed = false;
        while (true)
        {
            ed.WriteMessage($"\n\"{status.Name}\" \u2014 {status.Members.Count} regel(s) toegewezen.");

            var pko = new PromptKeywordOptions("\nActie");
            pko.Keywords.Add("Toewijzen");
            pko.Keywords.Add("Losmaken");
            pko.Keywords.Add("Handmatig");
            pko.Keywords.Add("Hernoemen");
            pko.Keywords.Add("Terug");
            pko.Keywords.Default = "Terug";
            pko.AllowNone = true;

            var res = ed.GetKeywords(pko);
            if (res.Status != PromptStatus.OK || res.StringResult == "Terug")
                break;

            switch (res.StringResult)
            {
                case "Toewijzen":
                    changed |= AssignByPick(ed, db, status, add: true);
                    break;
                case "Losmaken":
                    changed |= AssignByPick(ed, db, status, add: false);
                    break;
                case "Handmatig":
                    changed |= AssignManualEntry(ed, settings, status);
                    break;
                case "Hernoemen":
                    var name = AskStatusName(ed);
                    if (name is not null) { status.Name = name; changed = true; }
                    break;
            }
        }
        return changed;
    }

    private static bool AssignByPick(Editor ed, Database db, CustomStatus status, bool add)
    {
        bool changed = false;
        while (true)
        {
            var peo = new PromptEntityOptions(add
                ? "\nWijs een NLCS-object aan om toe te voegen (Enter = stoppen)"
                : "\nWijs een NLCS-object aan om los te maken (Enter = stoppen)");
            peo.SetRejectMessage("\nGeen geldig object.");
            peo.AllowNone = true;
            var per = ed.GetEntity(peo);
            if (per.Status != PromptStatus.OK)
                break;

            string layerName;
            using (var tr = db.TransactionManager.StartTransaction())
            {
                layerName = ((Entity)tr.GetObject(per.ObjectId, OpenMode.ForRead)).Layer;
                tr.Commit();
            }

            if (!NlcsLayerParser.TryParse(layerName, out var layer))
            {
                ed.WriteMessage($"\n  \"{layerName}\" is geen NLCS-laag.");
                continue;
            }

            var key = LegendSettings.EntryKey(layer!);
            if (add)
            {
                if (status.Members.Contains(key, StringComparer.OrdinalIgnoreCase))
                    ed.WriteMessage($"\n  (al toegewezen) {key}");
                else
                {
                    status.Members.Add(key);
                    changed = true;
                    ed.WriteMessage($"\n  + {key}");
                }
            }
            else
            {
                if (status.Members.RemoveAll(m => string.Equals(m, key, StringComparison.OrdinalIgnoreCase)) > 0)
                {
                    changed = true;
                    ed.WriteMessage($"\n  \u2212 {key}");
                }
                else
                    ed.WriteMessage($"\n  (niet toegewezen) {key}");
            }
        }
        return changed;
    }

    private static bool AssignManualEntry(Editor ed, LegendSettings settings, CustomStatus status)
    {
        var manuals = settings.ManualEntries.Where(m => m.IsValid).ToList();
        if (manuals.Count == 0)
        {
            ed.WriteMessage("\nEr zijn nog geen handmatige regels (zie NLCSLEGENDATOEVOEGEN).");
            return false;
        }

        bool changed = false;
        while (true)
        {
            ed.WriteMessage("\nHandmatige regels:");
            for (int i = 0; i < manuals.Count; i++)
            {
                var key = LegendSettings.EntryKey(manuals[i].ToLegendEntry());
                var mark = status.Members.Contains(key, StringComparer.OrdinalIgnoreCase) ? "toegewezen" : "-";
                ed.WriteMessage($"\n  {i + 1}. {manuals[i].Description}: {mark}");
            }

            var pko = new PromptKeywordOptions("\nKies een nummer om te wisselen");
            for (int i = 0; i < manuals.Count; i++)
                pko.Keywords.Add((i + 1).ToString());
            pko.Keywords.Add("Terug");
            pko.Keywords.Default = "Terug";
            pko.AllowNone = true;

            var res = ed.GetKeywords(pko);
            if (res.Status != PromptStatus.OK || res.StringResult == "Terug")
                break;

            if (int.TryParse(res.StringResult, out var n) && n >= 1 && n <= manuals.Count)
            {
                var key = LegendSettings.EntryKey(manuals[n - 1].ToLegendEntry());
                if (status.Members.RemoveAll(m => string.Equals(m, key, StringComparison.OrdinalIgnoreCase)) == 0)
                    status.Members.Add(key);
                changed = true;
            }
        }
        return changed;
    }

    private static string? AskStatusName(Editor ed)
    {
        var pso = new PromptStringOptions("\nNaam van de status:") { AllowSpaces = true };
        var sr = ed.GetString(pso);
        if (sr.Status != PromptStatus.OK || string.IsNullOrWhiteSpace(sr.StringResult))
        {
            ed.WriteMessage("\nGeannuleerd.");
            return null;
        }
        return sr.StringResult.Trim();
    }

    private static int AskStatusIndex(Editor ed, List<CustomStatus> statuses)
    {
        var pko = new PromptKeywordOptions("\nWelke status verwijderen?");
        for (int i = 0; i < statuses.Count; i++)
            pko.Keywords.Add((i + 1).ToString());
        pko.Keywords.Add("Annuleren");
        pko.Keywords.Default = "Annuleren";
        pko.AllowNone = true;

        var res = ed.GetKeywords(pko);
        if (res.Status != PromptStatus.OK || res.StringResult == "Annuleren")
            return -1;
        return int.TryParse(res.StringResult, out var n) && n >= 1 && n <= statuses.Count ? n - 1 : -1;
    }

    [CommandMethod("NLCSLEGENDATEKST", CommandFlags.Modal)]
    public void NlcsLegendaTekst()
    {
        var doc = AcApp.DocumentManager.MdiActiveDocument;
        if (doc is null)
            return;
        var ed = doc.Editor;
        var db = doc.Database;

        try
        {
            var peo = new PromptEntityOptions(
                "\nSelecteer een NLCS-object of legenda-onderdeel om de tekst aan te passen");
            peo.SetRejectMessage("\nGeen geldig object.");
            var per = ed.GetEntity(peo);
            if (per.Status != PromptStatus.OK)
            {
                ed.WriteMessage("\nGeannuleerd.");
                return;
            }

            string layerName;
            using (var tr = db.TransactionManager.StartTransaction())
            {
                layerName = ((Entity)tr.GetObject(per.ObjectId, OpenMode.ForRead)).Layer;
                tr.Commit();
            }

            if (!NlcsLayerParser.TryParse(layerName, out var layer))
            {
                ed.WriteMessage($"\n\"{layerName}\" is geen NLCS-laag.");
                return;
            }

            var key = $"{layer.Hoofdgroep}|{layer.Element.ToUpperInvariant()}";

            var def = DescriptionCatalog.Default();
            def.Elementen.TryGetValue(key, out var defaultEntry);
            var fallback = new DescriptionEntry
            {
                Algemeen = defaultEntry?.Algemeen ?? StandardTexts.HoofdgroepName(layer.Hoofdgroep),
                Specifiek = defaultEntry?.Specifiek ?? StandardTexts.Humanize(layer.Element)
            };

            DescriptionEntry LoadEntry(ConfigScope scope)
            {
                var catalog = LoadCatalogForScope(db, scope);
                if (catalog.Elementen.TryGetValue(key, out var e))
                    return new DescriptionEntry { Algemeen = e.Algemeen, Specifiek = e.Specifiek };
                return new DescriptionEntry { Algemeen = fallback.Algemeen, Specifiek = fallback.Specifiek };
            }

            var initial = DrawingStore.HasDescriptions(db) ? ConfigScope.Drawing : ConfigScope.Global;
            using var dialog = new TextEditDialog(key, initial, LoadEntry, fallback);
            dialog.ApplyRequested += (_, _) =>
            {
                var applied = new DescriptionEntry
                {
                    Algemeen = string.IsNullOrWhiteSpace(dialog.Algemeen) ? null : dialog.Algemeen,
                    Specifiek = dialog.Specifiek
                };
                ed.WriteMessage($"\nTekst voor {key} opgeslagen ({SaveSingleDescription(db, key, applied, dialog.Scope)}).");
            };
            if (AcWindows.ShowModalDialog(dialog) != WinForms.DialogResult.OK)
            {
                ed.WriteMessage("\nGesloten.");
                return;
            }

            var entry = new DescriptionEntry
            {
                Algemeen = string.IsNullOrWhiteSpace(dialog.Algemeen) ? null : dialog.Algemeen,
                Specifiek = dialog.Specifiek
            };
            var where = SaveSingleDescription(db, key, entry, dialog.Scope);
            ed.WriteMessage($"\nTekst voor {key} opgeslagen ({where}).");

            if (LegendGroupExists(db) && AskYesNo(ed, "Legenda nu bijwerken?", true))
                NlcsLegendaUpdate();
        }
        catch (Exception ex)
        {
            ed.WriteMessage($"\nNLCSLEGENDATEKST fout: {ex.Message}");
        }
    }

    private static string SaveSingleDescription(Database db, string key, DescriptionEntry entry, ConfigScope scope)
    {
        if (scope == ConfigScope.Drawing)
        {
            var catalog = DrawingStore.ReadDescriptions(db) is { } j
                ? DescriptionCatalog.FromJson(j) : new DescriptionCatalog();
            catalog.Elementen[key] = entry;
            DrawingStore.WriteDescriptions(db, catalog.Diff(DescriptionCatalog.Default()).ToJson());
            return "in deze tekening";
        }

        var global = File.Exists(DescriptionsPath) ? DescriptionCatalog.Load(DescriptionsPath) : new DescriptionCatalog();
        global.Elementen[key] = entry;
        Directory.CreateDirectory(ConfigDir);
        global.Save(DescriptionsPath);
        return "voor alle tekeningen";
    }

    private static bool AskYesNo(Editor ed, string question, bool defaultYes)
    {
        var pko = new PromptKeywordOptions($"\n{question}");
        pko.Keywords.Add("Ja");
        pko.Keywords.Add("Nee");
        pko.Keywords.Default = defaultYes ? "Ja" : "Nee";
        pko.AllowNone = true;
        var res = ed.GetKeywords(pko);
        if (res.Status != PromptStatus.OK)
            return false;
        return res.StringResult == "Ja";
    }

    private static bool LegendGroupExists(Database db)
    {
        using var tr = db.TransactionManager.StartTransaction();
        var reg = LegendStore.Load(db, tr);
        var gd = (DBDictionary)tr.GetObject(db.GroupDictionaryId, OpenMode.ForRead);
        var exists = reg.Legends.Count > 0 || gd.Contains(LegendManagement.LegacyGroupName);
        tr.Commit();
        return exists;
    }

    private static string SaveSettingsToScope(Database db, LegendSettings settings, ConfigScope scope)
    {
        // Instellingen worden altijd als globale standaard bewaard (voor nieuwe legenda's);
        // de oude tekening-scope bestaat niet meer.
        Directory.CreateDirectory(ConfigDir);
        settings.Save(ConfigPath);
        return "als globale standaard";
    }

    private static string SaveCatalogToScope(Database db, DescriptionCatalog catalog, ConfigScope scope)
    {
        if (scope == ConfigScope.Drawing)
        {
            DrawingStore.WriteDescriptions(db, catalog.ToJson());
            return "in deze tekening";
        }
        Directory.CreateDirectory(ConfigDir);
        catalog.Save(DescriptionsPath);
        return "voor alle tekeningen";
    }

    // Headless smoke-test via AutoCAD Core Console: plaatst een beheerde hele-tekeninglegenda.
    [CommandMethod("NLCSLEGENDATEST", CommandFlags.Modal)]
    public void NlcsLegendaTest()
    {
        var doc = AcApp.DocumentManager.MdiActiveDocument;
        if (doc is null)
            return;
        var ed = doc.Editor;
        var db = doc.Database;

        try
        {
            var settings = LoadGlobalDefaults();
            int rows = 0;

            using (var tr = db.TransactionManager.StartTransaction())
            {
                var registry = LegendStore.Load(db, tr);
                var excluded = LegendManagement.CollectManagedIds(db, tr, registry);
                var analysis = DrawingAnalyzer.Analyze(db, tr, settings, catalog: LoadCatalog(db), excludedIds: excluded);
                ed.WriteMessage(
                    $"\nNLCSTEST entries={analysis.Entries.Count} usedLayers={analysis.UsedNlcsLayerCount} " +
                    $"describedLayers={analysis.DescribedLayerCount} legends={registry.Legends.Count}");
                if (analysis.Entries.Count == 0)
                {
                    tr.Commit();
                    return;
                }

                var btrId = LegendBuilder.BuildBlock(db, tr, analysis, settings, out rows);
                var insert = ComputeInsertPoint(db, settings);
                var ms = (BlockTableRecord)tr.GetObject(
                    SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForWrite);
                var br = new BlockReference(insert, btrId);
                ms.AppendEntity(br);
                tr.AddNewlyCreatedDBObject(br, true);

                var def = new LegendDefinition
                {
                    Name = registry.NextDefaultName(),
                    Scope = LegendScope.WholeDrawing,
                    GroupName = LegendRegistry.NewGroupName(),
                    Settings = settings.Clone(),
                    CreatedWithVersion = PluginVersion
                };
                FinalizePlacement(tr, db, br, settings, def.GroupName);
                registry.Add(def);
                LegendStore.Save(db, tr, registry);
                _pendingPurge.Add(btrId);
                ed.WriteMessage($"\nNLCSTEST placed rows={rows} at {insert.X:0.0},{insert.Y:0.0} name={def.Name}");
                tr.Commit();
            }
            PurgePending(db);
        }
        catch (Exception ex)
        {
            ed.WriteMessage($"\nNLCSTEST error: {ex.Message}");
        }
    }

    private static void ReportAnalysis(Editor ed, AnalysisResult analysis)
    {
        ed.WriteMessage(
            $"\n{analysis.Entries.Count} legenda-regel(s) uit {analysis.UsedNlcsLayerCount} " +
            $"gebruikte NLCS-laag/-lagen ({analysis.DescribedLayerCount} met laagbeschrijving).");
        if (analysis.ExcludedNlcsLayerCount > 0)
            ed.WriteMessage(
                $"\n{analysis.ExcludedNlcsLayerCount} NLCS-laag/-lagen vielen buiten de legenda door de " +
                "filters (status, discipline, hoofdgroep, xref of uitgevinkt). Tekst- en overige lagen " +
                "krijgen geen eigen regel.");
    }

    private static string BuildInfoReport(AnalysisResult analysis, LegendSettings settings, string configSource)
    {
        var sb = new StringBuilder();
        sb.Append($"\nNLCS Legenda {PluginVersion} — instellingen uit: {configSource}.");
        sb.Append($"\nOverzicht: {analysis.Entries.Count} regel(s) uit " +
                  $"{analysis.UsedNlcsLayerCount} gebruikte laag/-lagen.");
        if (analysis.ExcludedNlcsLayerCount > 0)
            sb.Append($"\n({analysis.ExcludedNlcsLayerCount} NLCS-laag/-lagen buiten de legenda door filters; " +
                      "tekst-/overige lagen krijgen geen eigen regel.)");

        var disabledTypes = settings.DisabledDrawTypes();
        if (disabledTypes.Count > 0)
            sb.Append($"\nElementsoorten uitgeschakeld: {string.Join(", ", disabledTypes.Select(t => t.DisplayName()))}.");

        var disabledStatuses = new[]
        {
            NlcsStatus.Nieuw, NlcsStatus.Bestaand, NlcsStatus.Vervallen,
            NlcsStatus.Tijdelijk, NlcsStatus.Revisie
        }.Where(s => !settings.IncludedStatuses.Contains(s)).ToList();
        if (disabledStatuses.Count > 0)
            sb.Append($"\nStatussen uitgeschakeld: {string.Join(", ", disabledStatuses.Select(s => s.DisplayName()))}.");

        foreach (var statusGroup in analysis.Entries
                     .GroupBy(e => e.Status)
                     .OrderBy(g => g.Key.SortOrder()))
        {
            sb.Append($"\n  {statusGroup.Key.DisplayName()}: {statusGroup.Count()} regel(s)");
            foreach (var hg in statusGroup
                         .GroupBy(e => e.Hoofdgroep)
                         .OrderBy(g => g.Key))
            {
                sb.Append($"\n    - {StandardTexts.HoofdgroepName(hg.Key)} ({hg.Key}): {hg.Count()}");
            }
        }

        double totalLength = analysis.Entries.Sum(e => e.Metric.Length);
        double totalArea = analysis.Entries.Sum(e => e.Metric.Area);
        int totalCount = analysis.Entries.Sum(e => e.Metric.Count);
        sb.Append($"\n  Totalen: {totalCount} object(en), {totalLength:0} m lengte, {totalArea:0} m\u00B2 oppervlak.");

        sb.Append("\n  Hoeveelheden per hoofdgroep:");
        foreach (var g in LegendTotals.ByHoofdgroep(analysis.Entries))
        {
            var parts = new List<string>();
            if (g.Count > 0) parts.Add($"{g.Count} st");
            if (g.Length > 0) parts.Add($"{g.Length:0} m");
            if (g.Area > 0) parts.Add($"{g.Area:0} m\u00B2");
            var q = parts.Count > 0 ? " = " + string.Join(", ", parts) : string.Empty;
            sb.Append($"\n    - {StandardTexts.HoofdgroepName(g.Hoofdgroep)} ({g.Hoofdgroep}): {g.EntryCount} regel(s){q}");
        }

        var zonder = analysis.Entries
            .Where(e => e.DescriptionSource == DescriptionSource.Laagnaam)
            .Select(e => e.Element)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        if (zonder.Count > 0)
        {
            sb.Append($"\n  {zonder.Count} regel(s) zonder eigen omschrijving (laagnaam als tekst):");
            foreach (var el in zonder.Take(15))
                sb.Append($"\n    - {el}");
            if (zonder.Count > 15)
                sb.Append($"\n    - (+{zonder.Count - 15} meer)");
            sb.Append("\n  Vul deze aan via NLCSLEGENDAOMSCHRIJVINGEN of NLCSLEGENDATEKST.");
        }
        return sb.ToString();
    }

    private static string OptionsSummary(LegendSettings s)
    {
        var parts = new List<string>
        {
            $"schaal 1:{s.Scale:0}",
            $"tekst {OnOff(s.IncludeText)}",
            $"koppen {OnOff(s.IncludeGroupHeaders)}",
            $"titel {OnOff(s.IncludeTitle)}",
            $"kader {OnOff(s.DrawBorder)}",
            $"hoeveelheden {OnOff(s.IncludeQuantities)}",
            s.Columns > 0 ? $"{s.Columns} kolommen" : "kolommen auto",
            $"sortering {s.SortMode.ToString().ToLowerInvariant()}"
        };
        return string.Join(" | ", parts);
    }

    private static bool PromptOptions(Editor ed, LegendSettings s, out ObjectId[]? selection)
    {
        selection = null;
        while (true)
        {
            ed.WriteMessage($"\nHuidig: {OptionsSummary(s)}");
            var pko = new PromptKeywordOptions("\nOpties");
            pko.Keywords.Add("Schaal");
            pko.Keywords.Add("Tekst");
            pko.Keywords.Add("Algemeen");
            pko.Keywords.Add("Groepskoppen");
            pko.Keywords.Add("Hoofdgroepen");
            pko.Keywords.Add("Titel");
            pko.Keywords.Add("Kader");
            pko.Keywords.Add("Datum");
            pko.Keywords.Add("Hoeveelheden");
            pko.Keywords.Add("Kolommen");
            pko.Keywords.Add("Ordenen");
            pko.Keywords.Add("Filteren");
            pko.Keywords.Add("Elementsoorten");
            pko.Keywords.Add("Selecteren");
            pko.Keywords.Add("Instellingen");
            pko.Keywords.Add("Beschrijvingen");
            pko.Keywords.Add("Opslaan");
            pko.Keywords.Add("Plaatsen");
            pko.Keywords.Default = "Plaatsen";
            pko.AllowNone = true;

            var res = ed.GetKeywords(pko);
            if (res.Status == PromptStatus.Cancel)
                return false;
            if (res.Status != PromptStatus.OK)
                return true;

            switch (res.StringResult)
            {
                case "Schaal":
                    var pdo = new PromptDoubleOptions("\nSchaal 1:")
                    {
                        DefaultValue = s.Scale, UseDefaultValue = true,
                        AllowNegative = false, AllowZero = false
                    };
                    var pd = ed.GetDouble(pdo);
                    if (pd.Status == PromptStatus.OK) { s.Scale = pd.Value; Changed(ed, $"schaal 1:{s.Scale:0}"); }
                    break;
                case "Tekst": s.IncludeText = !s.IncludeText; Changed(ed, $"tekst {OnOff(s.IncludeText)}"); break;
                case "Algemeen": s.IncludeGeneralDescription = !s.IncludeGeneralDescription; Changed(ed, $"algemeen deel {OnOff(s.IncludeGeneralDescription)}"); break;
                case "Groepskoppen": s.IncludeGroupHeaders = !s.IncludeGroupHeaders; Changed(ed, $"groepskoppen {OnOff(s.IncludeGroupHeaders)}"); break;
                case "Hoofdgroepen": s.IncludeHoofdgroepHeaders = !s.IncludeHoofdgroepHeaders; Changed(ed, $"hoofdgroepkoppen {OnOff(s.IncludeHoofdgroepHeaders)}"); break;
                case "Titel": s.IncludeTitle = !s.IncludeTitle; Changed(ed, $"titel {OnOff(s.IncludeTitle)}"); break;
                case "Kader": s.DrawBorder = !s.DrawBorder; Changed(ed, $"kader {OnOff(s.DrawBorder)}"); break;
                case "Datum": s.IncludeDate = !s.IncludeDate; Changed(ed, $"datum {OnOff(s.IncludeDate)}"); break;
                case "Hoeveelheden": s.IncludeQuantities = !s.IncludeQuantities; Changed(ed, $"hoeveelheden {OnOff(s.IncludeQuantities)}"); break;
                case "Kolommen": PromptColumns(ed, s); break;
                case "Ordenen": PromptSortMode(ed, s); break;
                case "Filteren": PromptStatusFilter(ed, s); break;
                case "Elementsoorten": PromptDrawTypeFilter(ed, s); break;
                case "Selecteren": selection = PromptSelection(ed); break;
                case "Instellingen": EditSettingsDialog(ed, s); break;
                case "Beschrijvingen": EditDescriptionsDialog(ed); break;
                case "Opslaan": SaveSettings(ed, s); break;
                case "Plaatsen": return true;
            }
        }
    }

    private static string OnOff(bool value) => value ? "aan" : "uit";

    private static void Changed(Editor ed, string what) => ed.WriteMessage($"\n  \u2192 {what} gewijzigd.");

    private static void PromptColumns(Editor ed, LegendSettings s)
    {
        var pio = new PromptIntegerOptions("\nAantal kolommen (0 = automatisch):")
        {
            DefaultValue = s.Columns, UseDefaultValue = true, LowerLimit = 0
        };
        var res = ed.GetInteger(pio);
        if (res.Status == PromptStatus.OK)
            s.Columns = res.Value;
    }

    private static void PromptStatusFilter(Editor ed, LegendSettings s)
    {
        var all = new[]
        {
            NlcsStatus.Nieuw, NlcsStatus.Bestaand, NlcsStatus.Vervallen,
            NlcsStatus.Tijdelijk, NlcsStatus.Revisie
        };

        while (true)
        {
            var state = string.Join(" ",
                all.Select(st => $"{st.DisplayName()}:{(s.IncludedStatuses.Contains(st) ? "aan" : "uit")}"));
            var pko = new PromptKeywordOptions($"\nStatussen [{state}] (kies om te wisselen)");
            pko.Keywords.Add("Nieuw");
            pko.Keywords.Add("Bestaand");
            pko.Keywords.Add("Vervallen");
            pko.Keywords.Add("Tijdelijk");
            pko.Keywords.Add("Revisie");
            pko.Keywords.Add("Klaar");
            pko.Keywords.Default = "Klaar";
            pko.AllowNone = true;

            var res = ed.GetKeywords(pko);
            if (res.Status != PromptStatus.OK || res.StringResult == "Klaar")
                return;

            var status = res.StringResult switch
            {
                "Nieuw" => NlcsStatus.Nieuw,
                "Bestaand" => NlcsStatus.Bestaand,
                "Vervallen" => NlcsStatus.Vervallen,
                "Tijdelijk" => NlcsStatus.Tijdelijk,
                _ => NlcsStatus.Revisie
            };
            if (!s.IncludedStatuses.Add(status))
                s.IncludedStatuses.Remove(status);
        }
    }

    private static void PromptDrawTypeFilter(Editor ed, LegendSettings s)
    {
        var all = new[]
        {
            NlcsDrawType.Geometrie, NlcsDrawType.Vlak, NlcsDrawType.Arcering,
            NlcsDrawType.Vlakvulling, NlcsDrawType.Symbool
        };

        while (true)
        {
            var state = string.Join(" ",
                all.Select(t => $"{t.DisplayName()}:{(s.IsDrawTypeIncluded(t) ? "aan" : "uit")}"));
            var pko = new PromptKeywordOptions($"\nElementsoorten [{state}] (kies om te wisselen)");
            pko.Keywords.Add("Geometrie");
            pko.Keywords.Add("Vlakken");
            pko.Keywords.Add("Arceringen");
            pko.Keywords.Add("Vlakvullingen");
            pko.Keywords.Add("Symbolen");
            pko.Keywords.Add("Klaar");
            pko.Keywords.Default = "Klaar";
            pko.AllowNone = true;

            var res = ed.GetKeywords(pko);
            if (res.Status != PromptStatus.OK || res.StringResult == "Klaar")
                return;

            var type = res.StringResult switch
            {
                "Geometrie" => NlcsDrawType.Geometrie,
                "Vlakken" => NlcsDrawType.Vlak,
                "Arceringen" => NlcsDrawType.Arcering,
                "Vlakvullingen" => NlcsDrawType.Vlakvulling,
                _ => NlcsDrawType.Symbool
            };
            if (!s.IncludedDrawTypes.Add(type))
                s.IncludedDrawTypes.Remove(type);
        }
    }

    private static void PromptSortMode(Editor ed, LegendSettings s)
    {
        var pko = new PromptKeywordOptions($"\nSorteren op [Status/Naam/Hoeveelheid] <{s.SortMode}>");
        pko.Keywords.Add("Status");
        pko.Keywords.Add("Naam");
        pko.Keywords.Add("Hoeveelheid");
        pko.Keywords.Default = s.SortMode.ToString();
        pko.AllowNone = true;
        var res = ed.GetKeywords(pko);
        if (res.Status == PromptStatus.OK)
            s.SortMode = res.StringResult switch
            {
                "Naam" => LegendSortMode.Naam,
                "Hoeveelheid" => LegendSortMode.Hoeveelheid,
                _ => LegendSortMode.Status
            };
    }

    private static ObjectId[]? PromptSelection(Editor ed)
    {
        var res = ed.GetSelection();
        return res.Status == PromptStatus.OK ? res.Value.GetObjectIds() : null;
    }

    private static void SaveSettings(Editor ed, LegendSettings s)
    {
        try
        {
            // Pre-plaatsingsinstellingen horen altijd bij deze tekening, nooit globaal.
            var db = AcApp.DocumentManager.MdiActiveDocument?.Database;
            if (db is null)
                return;
            DrawingStore.WriteSettings(db, s.ToJson());
            ed.WriteMessage("\n  \u2192 instellingen opgeslagen in deze tekening.");
        }
        catch (Exception ex)
        {
            ed.WriteMessage($"\nKon instellingen niet opslaan: {ex.Message}");
        }
    }

    private static void EditSettingsDialog(Editor ed, LegendSettings s)
    {
        try
        {
            var db = AcApp.DocumentManager.MdiActiveDocument?.Database;
            if (db is null)
                return;
            using var dialog = new SettingsDialog(ConfigScope.Global, scope =>
                scope == ConfigScope.Drawing && DrawingStore.ReadSettings(db) is { } j
                    ? LegendSettings.FromJson(j)
                    : LegendSettings.FromJson(s.ToJson()));
            dialog.ApplyRequested += (_, _) =>
            {
                s.CopyFrom(dialog.Settings);
                ed.WriteMessage($"\nInstellingen opgeslagen ({SaveSettingsToScope(db, dialog.Settings, dialog.Scope)}).");
            };
            if (AcWindows.ShowModalDialog(dialog) == WinForms.DialogResult.OK)
            {
                s.CopyFrom(dialog.Settings);
                var where = SaveSettingsToScope(db, dialog.Settings, dialog.Scope);
                ed.WriteMessage($"\nInstellingen opgeslagen ({where}).");
            }
        }
        catch (Exception ex)
        {
            ed.WriteMessage($"\nInstellingen bewerken mislukt: {ex.Message}");
        }
    }

    private static void EditDescriptionsDialog(Editor ed)
    {
        try
        {
            var db = AcApp.DocumentManager.MdiActiveDocument?.Database;
            if (db is null)
                return;
            var initial = DrawingStore.HasDescriptions(db) ? ConfigScope.Drawing : ConfigScope.Global;
            using var dialog = new DescriptionsDialog(initial, scope => LoadCatalogForScope(db, scope));
            dialog.ApplyRequested += (_, _) =>
                ed.WriteMessage($"\nOmschrijvingen opgeslagen ({SaveCatalogToScope(db, dialog.ToCatalog().Diff(DescriptionCatalog.Default()), dialog.Scope)}).");
            if (AcWindows.ShowModalDialog(dialog) == WinForms.DialogResult.OK)
            {
                var where = SaveCatalogToScope(db, dialog.ToCatalog().Diff(DescriptionCatalog.Default()), dialog.Scope);
                ed.WriteMessage($"\nOmschrijvingen opgeslagen ({where}).");
            }
        }
        catch (Exception ex)
        {
            ed.WriteMessage($"\nOmschrijvingen bewerken mislukt: {ex.Message}");
        }
    }

    private static bool TryGetLegendExtents(Database db, Transaction tr, out Point3d min, out Point3d max)
    {
        double minX = double.MaxValue, minY = double.MaxValue;
        double maxX = double.MinValue, maxY = double.MinValue;
        bool found = false;

        var ms = (BlockTableRecord)tr.GetObject(
            SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForRead);
        foreach (ObjectId id in ms)
        {
            if (tr.GetObject(id, OpenMode.ForRead) is not Entity ent)
                continue;
            if (!ent.Layer.StartsWith(LegendLayerPrefix, StringComparison.OrdinalIgnoreCase))
                continue;
            var ext = ent.Bounds;
            if (!ext.HasValue)
                continue;
            minX = Math.Min(minX, ext.Value.MinPoint.X);
            minY = Math.Min(minY, ext.Value.MinPoint.Y);
            maxX = Math.Max(maxX, ext.Value.MaxPoint.X);
            maxY = Math.Max(maxY, ext.Value.MaxPoint.Y);
            found = true;
        }

        min = new Point3d(minX, minY, 0);
        max = new Point3d(maxX, maxY, 0);
        return found;
    }

}
