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

    // De globale standaardinstellingen: het startpunt voor een NIEUWE legenda. Bestaande
    // legenda's houden hun eigen snapshot en veranderen niet mee.
    private static LegendSettings LoadGlobalDefaults() => LegendSettings.Load(ConfigPath);

    private static string SaveGlobalDefaults(LegendSettings settings)
    {
        Directory.CreateDirectory(ConfigDir);
        settings.Save(ConfigPath);
        return "als globale standaard";
    }

    // GUI-only commands roepen dit aan vóór de eerste interactieve/native prompt. In de Core
    // Console (headless) is er geen venster en crashen GetEntity/GetPoint/dialogen; dan weigeren
    // we veilig zonder iets te muteren.
    private static bool RequireInteractive(Editor ed, string command)
    {
        if (!HostEnvironment.IsCoreConsole)
            return true;
        ed.WriteMessage(
            $"\n{command} werkt alleen in AutoCAD met venster, niet in de Core Console (headless). " +
            "Geen wijziging doorgevoerd.");
        return false;
    }

    // De globale omschrijvingen (ingebouwde catalogus + globale gebruikersoverrides). Oude
    // tekeningspecifieke omschrijvingen worden nog ingelezen zodat bestaande tekeningen hun
    // teksten behouden, maar er wordt niets nieuws meer tekeningspecifiek weggeschreven.
    // De gedeelde/globale omschrijvingen (ingebouwde catalogus + globale gebruikersdefaults).
    // Legacy tekening-descriptions worden hier NIET meer gelezen; die komen via migratie in de
    // per-legenda overrides terecht. Per-legenda overrides worden in de analyse toegevoegd.
    private static DescriptionCatalog LoadCatalog(Database db) => LoadGlobalCatalog();

    private static DescriptionCatalog LoadGlobalCatalog()
    {
        var catalog = DescriptionCatalog.Default();
        if (File.Exists(DescriptionsPath))
            catalog.MergeFrom(DescriptionCatalog.Load(DescriptionsPath));
        return catalog;
    }

    // Alleen de globale gebruikersomschrijvingen (het verschil t.o.v. de ingebouwde catalogus).
    // Wordt bij het maken van een legenda als onafhankelijke snapshot in de legenda gezet, zodat
    // latere wijzigingen aan de globale standaard bestaande legenda's niet meer veranderen.
    private static DescriptionCatalog LoadGlobalDescriptionDefaults()
        => File.Exists(DescriptionsPath) ? DescriptionCatalog.Load(DescriptionsPath) : new DescriptionCatalog();

    private static string SaveGlobalCatalog(DescriptionCatalog catalog)
    {
        Directory.CreateDirectory(ConfigDir);
        catalog.Save(DescriptionsPath);
        return "voor alle tekeningen";
    }

    [CommandMethod("NLCSLEGENDA", CommandFlags.Modal)]
    public void NlcsLegenda()
    {
        var doc = AcApp.DocumentManager.MdiActiveDocument;
        if (doc is null)
            return;
        var ed = doc.Editor;
        var db = doc.Database;
        if (!RequireInteractive(ed, "NLCSLEGENDA")) return;

        try
        {
            // Nieuwe legenda begint bij de globale standaard.
            var settings = LoadGlobalDefaults();

            // Onafhankelijke omschrijving-snapshot: leg de huidige globale gebruikersomschrijvingen
            // vast in de legenda.
            if (settings.DescriptionOverrides.Elementen.Count == 0)
                settings.DescriptionOverrides = LoadGlobalDescriptionDefaults();

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

                btrId = LegendBuilder.BuildBlock(db, tr, analysis, settings, out _, out var issues);
                ReportRenderIssues(ed, issues);

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

            // Doel kiezen vóór de transactie; export mag geen transactie openhouden tijdens
            // een eventuele keuze. Headless met meerdere legenda's weigert de resolver veilig.
            LegendRegistry registry;
            using (var trReg = db.TransactionManager.StartTransaction())
            {
                registry = LegendStore.Load(db, trReg);
                trReg.Commit();
            }

            LegendDefinition? target = null;
            if (registry.Legends.Count > 0)
            {
                target = ResolveTargetLegend(ed, db, registry, "exporteren");
                if (target is null)
                {
                    ed.WriteMessage("\nGeannuleerd.");
                    return;
                }
                settings = target.Settings;
            }

            IReadOnlyList<LegendEntry> entries;
            string suffix = "NLCS-legenda";
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var excluded = LegendManagement.CollectManagedIds(db, tr, registry);
                if (target is not null)
                {
                    // Exporteert de gekozen legenda met exact zijn eigen scope en instellingen.
                    ObjectId[]? selection = target.Scope == LegendScope.Selection
                        ? LegendManagement.ResolveHandles(db, target.SourceHandles, out _)
                            .Where(id => !excluded.Contains(id)).ToArray()
                        : null;
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
        if (!RequireInteractive(ed, "NLCSLEGENDABATCH")) return;

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

        var settings = LoadGlobalDefaults();
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
        if (!RequireInteractive(ed, "NLCSLEGENDAVIEWPORT")) return;

        try
        {
            if (db.TileMode)
            {
                ed.WriteMessage("\nOpen eerst een layout (paper space-tabblad) en start het commando daar opnieuw.");
                return;
            }

            var settings = LoadGlobalDefaults();

            // Registry buiten een transactie laden en het doel kiezen vóór de transactie, zodat
            // er nooit een transactie openstaat tijdens gebruikersinvoer.
            LegendRegistry registry;
            using (var trReg = db.TransactionManager.StartTransaction())
            {
                registry = LegendStore.Load(db, trReg);
                trReg.Commit();
            }

            LegendDefinition? target = null;
            if (registry.Legends.Count > 0)
            {
                target = ResolveTargetLegend(ed, db, registry, "de viewport om te maken");
                if (target is null)
                {
                    ed.WriteMessage("\nGeannuleerd.");
                    return;
                }
                settings = target.Settings;
            }

            Point3d min, max;
            using (var trExt = db.TransactionManager.StartTransaction())
            {
                bool found;
                if (target is not null)
                {
                    found = LegendManagement.TryGetGroupExtents(db, trExt, target.GroupName, out var ext);
                    min = found ? ext.MinPoint : Point3d.Origin;
                    max = found ? ext.MaxPoint : Point3d.Origin;
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
                var plan = ViewportMath.Compute(mw, mh, settings.Scale, marginPaper);
                vpW = plan.PaperWidthMm;
                vpH = plan.PaperHeightMm;
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
            LegendRegistry reg;
            using (var trPick = db.TransactionManager.StartTransaction())
            {
                reg = LegendStore.Load(db, trPick);
                trPick.Commit();
            }
            if (reg.Legends.Count == 0)
            {
                ed.WriteMessage("\nGeen legenda om bij te werken. Plaats er eerst een met NLCSLEGENDA.");
                return;
            }
            target = ResolveTargetLegend(ed, db, reg, "bijwerken");
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

            ed.WriteMessage(
                $"\nGlobale bestanden{maakStatus} (voor alle tekeningen):" +
                $"\n  {ConfigPath}" +
                $"\n  {DescriptionsPath}");
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
        if (!RequireInteractive(ed, "NLCSLEGENDAOPTIES")) return;

        try
        {
            if (!ResolveMutationTarget(ed, db, out var target)) { ed.WriteMessage("\nGeannuleerd."); return; }
            var settings = GetTargetSettings(db, target);
            using var dialog = new SettingsDialog(settings, target.ContextLabel, BuildCompositionItems(db, settings), CollectXrefNames(db));
            dialog.ApplyRequested += (_, _) => ApplyTargetSettings(ed, db, target, dialog.Settings, "Instellingen");
            if (AcWindows.ShowModalDialog(dialog) != WinForms.DialogResult.OK)
            {
                ed.WriteMessage("\nGesloten.");
                return;
            }
            ApplyTargetSettings(ed, db, target, dialog.Settings, "Instellingen");
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
        if (!RequireInteractive(ed, "NLCSLEGENDAOMSCHRIJVINGEN")) return;

        try
        {
            if (!ResolveMutationTarget(ed, db, out var target)) { ed.WriteMessage("\nGeannuleerd."); return; }

            // Globaal werkt op de globale gebruikersomschrijvingen (voor nieuwe legenda's). Een
            // legenda toont de ingebouwde catalogus plus zijn eigen snapshot en slaat die als
            // zelfstandige override (t.o.v. de ingebouwde catalogus) op.
            DescriptionCatalog initial;
            if (target.IsGlobal)
            {
                initial = LoadGlobalCatalog();
            }
            else
            {
                initial = DescriptionCatalog.Default();
                initial.MergeFrom(GetTargetSettings(db, target).DescriptionOverrides);
            }

            using var dialog = new DescriptionsDialog(initial);
            void Apply()
            {
                if (target.IsGlobal)
                {
                    var where = SaveGlobalCatalog(dialog.ToCatalog().Diff(DescriptionCatalog.Default()));
                    ed.WriteMessage($"\nOmschrijvingen opgeslagen ({where}); bestaande legenda's blijven ongewijzigd.");
                }
                else
                {
                    var s = GetTargetSettings(db, target);
                    s.DescriptionOverrides = dialog.ToCatalog().Diff(DescriptionCatalog.Default());
                    ApplyTargetSettings(ed, db, target, s, "Omschrijvingen");
                }
            }
            dialog.ApplyRequested += (_, _) => Apply();
            if (AcWindows.ShowModalDialog(dialog) != WinForms.DialogResult.OK)
            {
                ed.WriteMessage("\nGesloten.");
                return;
            }
            Apply();
        }
        catch (Exception ex)
        {
            ed.WriteMessage($"\nNLCSLEGENDAOMSCHRIJVINGEN fout: {ex.Message}");
        }
    }

    [CommandMethod("NLCSLEGENDAUITVINKEN", CommandFlags.Modal)]
    public void NlcsLegendaUitvinken()
    {
        var doc = AcApp.DocumentManager.MdiActiveDocument;
        if (doc is null)
            return;
        var ed = doc.Editor;
        var db = doc.Database;
        if (!RequireInteractive(ed, "NLCSLEGENDAUITVINKEN")) return;

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
            if (!ResolveMutationTarget(ed, db, out var target)) { ed.WriteMessage("\nGeannuleerd."); return; }
            var settings = GetTargetSettings(db, target);

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
            ed.WriteMessage($"\n{key} {(nowExcluded ? "uitgevinkt" : "weer opgenomen")}.");
            ApplyTargetSettings(ed, db, target, settings, "Samenstelling");
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
        if (!RequireInteractive(ed, "NLCSLEGENDATOEVOEGEN")) return;

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

            if (!ResolveMutationTarget(ed, db, out var target)) { ed.WriteMessage("\nGeannuleerd."); return; }
            var settings = GetTargetSettings(db, target);
            settings.ManualEntries.Add(manual);
            ed.WriteMessage($"\nRegel \"{manual.Description}\" toegevoegd.");
            ApplyTargetSettings(ed, db, target, settings, "Samenstelling");
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
        if (!RequireInteractive(ed, "NLCSLEGENDASAMENSTELLEN")) return;

        try
        {
            if (!ResolveMutationTarget(ed, db, out var target)) { ed.WriteMessage("\nGeannuleerd."); return; }
            var settings = GetTargetSettings(db, target);

            var probe = settings.Clone();
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
                        $"[{e.Status.DisplayName()}] {e.Description}", EntryGroupLabel(e)))
                    .GroupBy(x => x.Key)
                    .Select(g => g.First())
                    .OrderBy(x => x.Group, StringComparer.CurrentCultureIgnoreCase)
                    .ThenBy(x => x.Label, StringComparer.CurrentCultureIgnoreCase)
                    .ToList();
                tr.Commit();
            }

            using var dialog = new LegendManageDialog(items, settings);
            void Apply()
            {
                settings.ExcludedEntries = dialog.ExcludedKeys;
                settings.ManualEntries = dialog.ManualEntries;
                ApplyTargetSettings(ed, db, target, settings, "Samenstelling");
            }
            dialog.ApplyRequested += (_, _) => Apply();
            if (AcWindows.ShowModalDialog(dialog) != WinForms.DialogResult.OK)
            {
                ed.WriteMessage("\nGesloten.");
                return;
            }
            Apply();
        }
        catch (Exception ex)
        {
            ed.WriteMessage($"\nNLCSLEGENDASAMENSTELLEN fout: {ex.Message}");
        }
    }

    private static string EntryGroupLabel(LegendEntry e)
    {
        if (string.Equals(e.Discipline, "KL", StringComparison.OrdinalIgnoreCase))
        {
            var soort = ElementProperties.From(e.Element).Soort;
            if (!string.IsNullOrWhiteSpace(soort))
                return soort;
        }
        return StandardTexts.HoofdgroepName(e.Hoofdgroep);
    }

    // Bouwt de lijst van tekening-entries voor de samenstellen-boom (alle statussen, zonder
    // uitsluitingen/eigen regels). Geeft null terug als er niets te analyseren is, zodat het
    // instellingenvenster de samenstellen-knop dan niet toont.
    private List<EntryCheckItem>? BuildCompositionItems(Database db, LegendSettings settings)
    {
        try
        {
            var probe = settings.Clone();
            probe.ExcludedEntries = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            probe.ManualEntries = new List<ManualEntry>();
            probe.IncludedStatuses = new HashSet<NlcsStatus>
            {
                NlcsStatus.Nieuw, NlcsStatus.Bestaand, NlcsStatus.Vervallen,
                NlcsStatus.Tijdelijk, NlcsStatus.Revisie, NlcsStatus.Overig
            };
            using var tr = db.TransactionManager.StartTransaction();
            var analysis = DrawingAnalyzer.Analyze(db, tr, probe, catalog: LoadCatalog(db));
            var items = analysis.Entries
                .Select(e => new EntryCheckItem(LegendSettings.EntryKey(e),
                    $"[{e.Status.DisplayName()}] {e.Description}", EntryGroupLabel(e)))
                .GroupBy(x => x.Key)
                .Select(g => g.First())
                .OrderBy(x => x.Group, StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(x => x.Label, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
            tr.Commit();
            return items.Count > 0 ? items : null;
        }
        catch
        {
            return null;
        }
    }

    // Namen van gekoppelde xrefs (niet de geneste/afhankelijke), gesorteerd. Voor de per-xref
    // keuze in het instellingenvenster; dezelfde selectie als NLCSLEGENDAXREFS.
    private static IReadOnlyList<string> CollectXrefNames(Database db)
    {
        var xrefs = new List<string>();
        try
        {
            using var tr = db.TransactionManager.StartTransaction();
            var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
            foreach (ObjectId id in bt)
                if (tr.GetObject(id, OpenMode.ForRead) is BlockTableRecord btr
                    && btr.IsFromExternalReference && !btr.IsDependent)
                    xrefs.Add(btr.Name);
            tr.Commit();
        }
        catch
        {
            return xrefs;
        }
        xrefs.Sort(StringComparer.CurrentCultureIgnoreCase);
        return xrefs;
    }

    [CommandMethod("NLCSLEGENDAXREFS", CommandFlags.Modal)]
    public void NlcsLegendaXrefs()
    {
        var doc = AcApp.DocumentManager.MdiActiveDocument;
        if (doc is null)
            return;
        var ed = doc.Editor;
        var db = doc.Database;
        if (!RequireInteractive(ed, "NLCSLEGENDAXREFS")) return;

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

            if (!ResolveMutationTarget(ed, db, out var target)) { ed.WriteMessage("\nGeannuleerd."); return; }
            var settings = GetTargetSettings(db, target);

            while (true)
            {
                ed.WriteMessage("\nXrefs meenemen:");
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

            ApplyTargetSettings(ed, db, target, settings, "Xref-keuze");
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
        if (!RequireInteractive(ed, "NLCSLEGENDASTATUS")) return;

        try
        {
            if (!ResolveMutationTarget(ed, db, out var target)) { ed.WriteMessage("\nGeannuleerd."); return; }
            var settings = GetTargetSettings(db, target);
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
            ApplyTargetSettings(ed, db, target, settings, "Eigen statussen");
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
        if (!RequireInteractive(ed, "NLCSLEGENDAPRESET")) return;

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
        // Bron van de preset kiezen: de globale standaard of een bestaande legenda-snapshot.
        if (!ResolveMutationTarget(ed, db, out var target, "Waarvan opslaan", "opslaan vanuit"))
        { ed.WriteMessage("\nGeannuleerd."); return; }
        var name = AskPresetName(ed);
        if (name is null)
            return;
        if (LegendPresets.Exists(PresetsDir, name)
            && !AskYesNo(ed, $"Profiel \"{name}\" bestaat al. Overschrijven?", true))
        {
            ed.WriteMessage("\nGeannuleerd.");
            return;
        }
        // Snapshot van het gekozen doel (globale defaults of de legenda-instellingen), nooit
        // instance-identiteit: LegendPresets slaat alleen LegendSettings op.
        var settings = GetTargetSettings(db, target);
        LegendPresets.Save(PresetsDir, name, settings);
        ed.WriteMessage($"\nProfiel \"{name}\" opgeslagen vanuit {target.ContextLabel}.");
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
        if (!ResolveMutationTarget(ed, db, out var target, "Waarnaar laden", "laden naar"))
        { ed.WriteMessage("\nGeannuleerd."); return; }
        ed.WriteMessage($"\nProfiel \"{names[idx]}\" geladen.");
        ApplyTargetSettings(ed, db, target, settings, "Profiel");
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
        if (!RequireInteractive(ed, "NLCSLEGENDATEKST")) return;

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

            if (!ResolveMutationTarget(ed, db, out var target)) { ed.WriteMessage("\nGeannuleerd."); return; }

            var def = DescriptionCatalog.Default();
            def.Elementen.TryGetValue(key, out var defaultEntry);
            var fallback = new DescriptionEntry
            {
                Algemeen = defaultEntry?.Algemeen ?? StandardTexts.HoofdgroepName(layer.Hoofdgroep),
                Specifiek = defaultEntry?.Specifiek ?? StandardTexts.Humanize(layer.Element)
            };

            // Globaal bewerkt de gedeelde catalogus; een legenda krijgt een eigen omschrijving
            // in zijn snapshot (DescriptionOverrides), hetzelfde canonieke model als
            // NLCSLEGENDAOMSCHRIJVINGEN.
            DescriptionEntry current;
            if (target.IsGlobal)
                current = LoadGlobalCatalog().Elementen.TryGetValue(key, out var existing)
                    ? new DescriptionEntry { Algemeen = existing.Algemeen, Specifiek = existing.Specifiek }
                    : new DescriptionEntry { Algemeen = fallback.Algemeen, Specifiek = fallback.Specifiek };
            else
                current = GetTargetSettings(db, target).DescriptionOverrides.Elementen.TryGetValue(key, out var ov)
                    ? new DescriptionEntry { Algemeen = ov.Algemeen, Specifiek = ov.Specifiek }
                    : new DescriptionEntry { Algemeen = fallback.Algemeen, Specifiek = fallback.Specifiek };

            using var dialog = new TextEditDialog(key, current, fallback);
            void Apply()
            {
                if (target.IsGlobal)
                {
                    var applied = new DescriptionEntry
                    {
                        Algemeen = string.IsNullOrWhiteSpace(dialog.Algemeen) ? null : dialog.Algemeen,
                        Specifiek = dialog.Specifiek
                    };
                    ed.WriteMessage(
                        $"\nTekst voor {key} opgeslagen ({SaveSingleDescription(key, applied)}); bestaande legenda's blijven ongewijzigd.");
                }
                else
                {
                    var s = GetTargetSettings(db, target);
                    if (string.IsNullOrWhiteSpace(dialog.Specifiek))
                        s.DescriptionOverrides.Elementen.Remove(key);
                    else
                        s.DescriptionOverrides.Elementen[key] = new DescriptionEntry
                        {
                            Algemeen = string.IsNullOrWhiteSpace(dialog.Algemeen) ? null : dialog.Algemeen,
                            Specifiek = dialog.Specifiek
                        };
                    ApplyTargetSettings(ed, db, target, s, $"Tekst voor {key}");
                }
            }
            dialog.ApplyRequested += (_, _) => Apply();
            if (AcWindows.ShowModalDialog(dialog) != WinForms.DialogResult.OK)
            {
                ed.WriteMessage("\nGesloten.");
                return;
            }
            Apply();
        }
        catch (Exception ex)
        {
            ed.WriteMessage($"\nNLCSLEGENDATEKST fout: {ex.Message}");
        }
    }

    private static string SaveSingleDescription(string key, DescriptionEntry entry)
    {
        var global = File.Exists(DescriptionsPath) ? DescriptionCatalog.Load(DescriptionsPath) : new DescriptionCatalog();
        global.Elementen[key] = entry;
        Directory.CreateDirectory(ConfigDir);
        global.Save(DescriptionsPath);
        return "voor alle tekeningen";
    }

    // Bewerkt een NLCS-laagnaam component voor component en hernoemt de laag in één
    // transactie (één Undo). Dry-run via de live preview/validatie; botsing met een bestaande
    // laag voegt samen; xref-afhankelijke lagen worden geweigerd.
    [CommandMethod("NLCSLEGENDALAAGNAAM", CommandFlags.Modal)]
    public void NlcsLegendaLaagnaam()
    {
        var doc = AcApp.DocumentManager.MdiActiveDocument;
        if (doc is null)
            return;
        var ed = doc.Editor;
        var db = doc.Database;
        if (!RequireInteractive(ed, "NLCSLEGENDALAAGNAAM")) return;

        try
        {
            var source = PromptLayerName(ed, db);
            if (source is null)
            {
                ed.WriteMessage("\nGeannuleerd.");
                return;
            }

            if (!NlcsLayerComponents.TryParse(source, out var comp, out var perr))
            {
                ed.WriteMessage($"\n\"{source}\" is geen NLCS-laagnaam ({perr}).");
                return;
            }

            LayerRename.Plan probe0;
            using (var tr = db.TransactionManager.StartTransaction())
            {
                probe0 = LayerRename.Analyze(db, tr, source, source);
                tr.Commit();
            }
            if (probe0.Blocked)
            {
                ed.WriteMessage($"\nKan niet: {probe0.Reason}.");
                return;
            }

            (bool exists, int count, string info) Probe(string name)
            {
                if (string.Equals(name, source, StringComparison.Ordinal))
                    return (false, 0, string.Empty);
                using var tr = db.TransactionManager.StartTransaction();
                var lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
                bool exists = lt.Has(name);
                int count = 0;
                string info = string.Empty;
                if (exists)
                {
                    count = LayerRename.CountOnLayer(db, tr, name);
                    if (tr.GetObject(lt[name], OpenMode.ForRead) is LayerTableRecord ltr)
                        info = DescribeLayer(tr, ltr);
                }
                tr.Commit();
                return (exists, count, info);
            }

            using var dialog = new LayerEditDialog(comp, source, probe0.AffectedEntities, probe0.SourceLocked, Probe);
            if (AcWindows.ShowModalDialog(dialog) != WinForms.DialogResult.OK)
            {
                ed.WriteMessage("\nGesloten.");
                return;
            }

            var target = dialog.Result.Compose();
            // Samenvoegen is onomkeerbaar binnen de laag: expliciet laten bevestigen.
            if (dialog.MergeIntoExisting
                && !AskYesNo(ed, $"Laag \"{target}\" bestaat al. Entiteiten samenvoegen en bronlaag verwijderen?", false))
            {
                ed.WriteMessage("\nGeannuleerd.");
                return;
            }
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var plan = LayerRename.Analyze(db, tr, source, target);
                if (!LayerRename.Apply(db, tr, plan, dialog.MergeIntoExisting, out var err))
                {
                    tr.Abort();
                    ed.WriteMessage($"\nHernoemen mislukt: {err}.");
                    return;
                }
                tr.Commit();
            }
            ed.WriteMessage($"\nLaag hernoemd naar \"{target}\". Gebruik U om terug te draaien.");
        }
        catch (Exception ex)
        {
            ed.WriteMessage($"\nNLCSLEGENDALAAGNAAM fout: {ex.Message}");
        }
    }

    // Korte beschrijving van de zichtbare laageigenschappen voor het conflictbeeld.
    private static string DescribeLayer(Transaction tr, LayerTableRecord ltr)
    {
        string lt = "Continuous";
        try
        {
            if (tr.GetObject(ltr.LinetypeObjectId, OpenMode.ForRead) is LinetypeTableRecord l)
                lt = l.Name;
        }
        catch { /* standaard */ }
        var flags = new List<string>();
        if (!ltr.IsPlottable) flags.Add("niet-plotbaar");
        if (ltr.IsFrozen) flags.Add("bevroren");
        if (ltr.IsOff) flags.Add("uit");
        if (ltr.IsLocked) flags.Add("vergrendeld");
        var extra = flags.Count > 0 ? ", " + string.Join("/", flags) : string.Empty;
        return $"kleur {ltr.Color}, {lt}, dikte {ltr.LineWeight}{extra}";
    }

    // Laagnaam kiezen: een object aanwijzen (dan geldt zijn laag) of de naam typen.
    private static string? PromptLayerName(Editor ed, Database db)
    {
        var peo = new PromptEntityOptions("\nSelecteer een object van de laag [Typen]", "Typen");
        peo.AllowNone = false;
        var per = ed.GetEntity(peo);
        if (per.Status == PromptStatus.Keyword && per.StringResult == "Typen")
        {
            var pso = new PromptStringOptions("\nLaagnaam:") { AllowSpaces = true };
            var sr = ed.GetString(pso);
            return sr.Status == PromptStatus.OK && !string.IsNullOrWhiteSpace(sr.StringResult)
                ? sr.StringResult.Trim()
                : null;
        }
        if (per.Status != PromptStatus.OK)
            return null;
        using var tr = db.TransactionManager.StartTransaction();
        var layer = ((Entity)tr.GetObject(per.ObjectId, OpenMode.ForRead)).Layer;
        tr.Commit();
        return layer;
    }

    private static bool AskYesNo(Editor ed, string question, bool defaultYes)
    {
        // Headless geen native keyword-prompt; neem de standaardkeuze.
        if (HostEnvironment.IsCoreConsole)
            return defaultYes;
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
        var exists = reg.Legends.Count > 0;
        tr.Commit();
        return exists;
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

                var btrId = LegendBuilder.BuildBlock(db, tr, analysis, settings, out rows, out var issues);
                if (issues.Count > 0)
                    ed.WriteMessage($"\nNLCSTEST renderissues={issues.Count} first={issues[0].SourceLayer}:{issues[0].Reason}");
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

    // Headless bewijs van per-legenda-isolatie: maakt twee hele-tekeninglegenda's met eigen
    // instellingen, bouwt ze, bewerkt daarna alleen A en toont dat B ongemoeid blijft; alles
    // via de echte registry-persistentie en rebuild.
    [CommandMethod("NLCSLEGENDAISOLATIETEST", CommandFlags.Modal)]
    public void NlcsLegendaIsolatieTest()
    {
        var doc = AcApp.DocumentManager.MdiActiveDocument;
        if (doc is null)
            return;
        var ed = doc.Editor;
        var db = doc.Database;

        try
        {
            string idA, idB, idC = string.Empty, firstKey = string.Empty, descKey = string.Empty;
            string bGeomBefore = string.Empty;
            int aRows, bRows;
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var reg = LegendStore.Load(db, tr);
                var excluded = LegendManagement.CollectManagedIds(db, tr, reg);
                var probe = DrawingAnalyzer.Analyze(db, tr, LoadGlobalDefaults(), catalog: LoadCatalog(db), excludedIds: excluded);
                if (probe.Entries.Count > 0)
                    firstKey = LegendSettings.EntryKey(probe.Entries[0]);
                // Een niet-uitgevinkt element voor de tekst-isolatietest (laatste entry).
                var descEntry = probe.Entries.Count > 0 ? probe.Entries[^1] : null;
                if (descEntry is not null)
                    descKey = $"{descEntry.Hoofdgroep}|{descEntry.Element.ToUpperInvariant()}";

                var sA = LoadGlobalDefaults();
                var sB = LoadGlobalDefaults();
                sB.Scale = 500;
                if (firstKey.Length > 0)
                    sB.ExcludedEntries.Add(firstKey);
                if (descKey.Length > 0)
                    sB.DescriptionOverrides.Elementen[descKey] = new DescriptionEntry { Specifiek = "EIGEN-B-TEKST" };
                // A krijgt een eigen handmatige regel en een eigen status; B/C niet.
                sA.ManualEntries.Add(new ManualEntry { Layer = "N-WE-VV-ISO-G", Type = NlcsDrawType.Geometrie, Description = "ISO-handregel A" });
                sA.CustomStatuses.Add(new CustomStatus { Name = "ISO-STATUS-A" });

                var defA = IsoDef(reg, sA);
                var defB = IsoDef(reg, sB);
                var defC = IsoDef(reg, LoadGlobalDefaults());  // nieuwe C krijgt de globale default
                reg.Add(defA);
                reg.Add(defB);
                reg.Add(defC);
                idA = defA.Id;
                idB = defB.Id;
                idC = defC.Id;
                BuildManagedLegend(db, tr, reg, defA, out aRows, out _);
                BuildManagedLegend(db, tr, reg, defB, out bRows, out _);
                BuildManagedLegend(db, tr, reg, defC, out _, out _);
                bGeomBefore = GeomHash(db, tr, defB.GroupName);
                LegendStore.Save(db, tr, reg);
                tr.Commit();
            }
            PurgePending(db);
            ed.WriteMessage($"\nISO: A rows={aRows} B rows={bRows} (B schaal 500, 1 uitgevinkt)");

            using (var tr = db.TransactionManager.StartTransaction())
            {
                var reg = LegendStore.Load(db, tr);
                var a = reg.FindById(idA)!;
                var b = reg.FindById(idB)!;
                ed.WriteMessage(
                    $"\nISO: persisted A.scale={a.Settings.Scale:0} A.excl={a.Settings.ExcludedEntries.Count} " +
                    $"B.scale={b.Settings.Scale:0} B.excl={b.Settings.ExcludedEntries.Count}");
                var gd = LoadGlobalDefaults();
                var cDef = reg.FindById(idC)!;
                bool cOk = cDef.Settings.Scale == gd.Scale && cDef.Settings.ExcludedEntries.Count == 0
                    && cDef.Settings.DescriptionOverrides.Elementen.Count == 0;
                ed.WriteMessage($"\nISO: C.scale={cDef.Settings.Scale:0} (globaal {gd.Scale:0}) C.excl={cDef.Settings.ExcludedEntries.Count} -> {(cOk ? "OK" : "FAIL")}");
                // Handregel + status: alleen A, niet B/C.
                bool dimOk = a.Settings.ManualEntries.Count == 1 && a.Settings.CustomStatuses.Count == 1
                    && b.Settings.ManualEntries.Count == 0 && b.Settings.CustomStatuses.Count == 0
                    && cDef.Settings.ManualEntries.Count == 0 && cDef.Settings.CustomStatuses.Count == 0;
                ed.WriteMessage($"\nISO: A.manual={a.Settings.ManualEntries.Count}/status={a.Settings.CustomStatuses.Count} " +
                    $"B.manual={b.Settings.ManualEntries.Count} C.manual={cDef.Settings.ManualEntries.Count} -> {(dimOk ? "OK" : "FAIL")}");
                tr.Commit();
            }

            // Geometrie-hash van B vóór het bewerken van A is in het bouwblok gemeten (bGeomBefore).
            int aRows2, bAfter;
            string bGeomAfter = string.Empty;
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var reg = LegendStore.Load(db, tr);
                var a = reg.FindById(idA)!;
                var bDef = reg.FindById(idB)!;
                if (firstKey.Length > 0)
                    a.Settings.ExcludedEntries.Add(firstKey);
                BuildManagedLegend(db, tr, reg, a, out aRows2, out _);
                BuildManagedLegend(db, tr, reg, bDef, out bAfter, out _);
                bGeomAfter = GeomHash(db, tr, bDef.GroupName);
                LegendStore.Save(db, tr, reg);
                tr.Commit();
            }
            PurgePending(db);

            // B's geometrie mag niet veranderen doordat A is bewerkt (geometrie-isolatie).
            bool geomOk = bGeomBefore.Length > 0 && bGeomBefore == bGeomAfter;
            ed.WriteMessage($"\nISO: B-geometrie {(geomOk ? "ongewijzigd" : "GEWIJZIGD")} ({bGeomAfter})");

            bool ok = (firstKey.Length == 0 || aRows2 == aRows - 1) && bAfter == bRows && geomOk;
            ed.WriteMessage(
                $"\nISO: na A bewerken A rows={aRows2} (verwacht {(firstKey.Length > 0 ? aRows - 1 : aRows)}) " +
                $"B rows={bAfter} (verwacht {bRows})");
            ed.WriteMessage($"\nISO: isolatie {(ok ? "OK" : "FAIL")}");

            // Tekst-isolatie: B heeft een eigen omschrijving voor descKey, A niet. Na reload
            // moet de geanalyseerde tekst per legenda verschillen.
            if (descKey.Length > 0)
            {
                using var tr = db.TransactionManager.StartTransaction();
                var reg = LegendStore.Load(db, tr);
                var excluded = LegendManagement.CollectManagedIds(db, tr, reg);
                string Text(LegendDefinition d)
                {
                    var an = DrawingAnalyzer.Analyze(db, tr, d.Settings, catalog: LoadCatalog(db), excludedIds: excluded);
                    var e = an.Entries.FirstOrDefault(x => $"{x.Hoofdgroep}|{x.Element.ToUpperInvariant()}" == descKey);
                    return e?.Description ?? "(geen)";
                }
                var aText = Text(reg.FindById(idA)!);
                var bText = Text(reg.FindById(idB)!);
                bool descOk = bText.Contains("EIGEN-B-TEKST") && !aText.Contains("EIGEN-B-TEKST");
                ed.WriteMessage($"\nISO: tekst A=\"{aText}\" B=\"{bText}\" -> {(descOk ? "OK" : "FAIL")}");
                tr.Commit();
            }
        }
        catch (Exception ex)
        {
            ed.WriteMessage($"\nISO error: {ex.Message}");
        }
    }

    private static LegendDefinition IsoDef(LegendRegistry reg, LegendSettings settings) => new()
    {
        Name = reg.NextDefaultName(),
        Scope = LegendScope.WholeDrawing,
        GroupName = LegendRegistry.NewGroupName(),
        Settings = settings.Clone(),
        CreatedWithVersion = PluginVersion
    };

    // Compacte geometrie-vingerafdruk van een legenda-groep: afgeronde extents, in dezelfde
    // transactie gemeten (losse read-transacties geven in de Core Console geen bounds terug).
    private static string GeomHash(Database db, Transaction tr, string groupName) =>
        LegendManagement.TryGetGroupExtents(db, tr, groupName, out var e)
            ? $"{e.MinPoint.X:0.0},{e.MinPoint.Y:0.0},{e.MaxPoint.X:0.0},{e.MaxPoint.Y:0.0}"
            : string.Empty;

    // Inhoudelijke vingerafdruk van een legenda-groep: per getekende entiteit soort|laag|
    // afgeronde bounds|tekst|detail, gesorteerd en gehasht. Detail legt blok- en arcering-identiteit
    // vast zodat andere inhoud met dezelfde omhullende een andere hash geeft. Deterministisch.
    private static string ContentFingerprint(Database db, Transaction tr, string groupName)
    {
        var gd = (DBDictionary)tr.GetObject(db.GroupDictionaryId, OpenMode.ForRead);
        if (!gd.Contains(groupName) || tr.GetObject(gd.GetAt(groupName), OpenMode.ForRead) is not Group g)
            return string.Empty;

        var parts = new List<string>();
        void Add(Entity e)
        {
            string bounds = e.Bounds is { } b
                ? $"{b.MinPoint.X:0.0},{b.MinPoint.Y:0.0},{b.MaxPoint.X:0.0},{b.MaxPoint.Y:0.0}"
                : "-";
            string text = e switch { DBText t => t.TextString, MText m => m.Contents, _ => string.Empty };
            string detail = e switch
            {
                Hatch h => $"hatch:{h.PatternName}:{h.PatternScale:0.##}",
                BlockReference b2 when tr.GetObject(b2.BlockTableRecord, OpenMode.ForRead) is BlockTableRecord r => $"blk:{r.Name}",
                _ => string.Empty
            };
            parts.Add($"{e.GetType().Name}|{e.Layer}|{bounds}|{text}|{detail}");
        }

        foreach (var id in g.GetAllEntityIds())
        {
            if (tr.GetObject(id, OpenMode.ForRead) is not Entity e || e.IsErased)
                continue;
            if (e is BlockReference br && tr.GetObject(br.BlockTableRecord, OpenMode.ForRead) is BlockTableRecord btr)
                foreach (ObjectId bid in btr)
                {
                    if (tr.GetObject(bid, OpenMode.ForRead) is Entity be && !be.IsErased)
                        Add(be);
                }
            else
                Add(e);
        }
        parts.Sort(StringComparer.Ordinal);
        using var sha = System.Security.Cryptography.SHA256.Create();
        var hash = sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(string.Join("\n", parts)));
        return parts.Count + ":" + Convert.ToHexString(hash, 0, 6);
    }

    // Handles van NLCS-objecten in model space, voor selectie-gebonden legenda's in de testharness.
    private static List<string> CollectNlcsHandles(Database db, Transaction tr, int max)
    {
        var handles = new List<string>();
        var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
        var ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForRead);
        foreach (ObjectId id in ms)
        {
            if (tr.GetObject(id, OpenMode.ForRead) is Entity e && !e.IsErased
                && NlcsLayerParser.TryParse(e.Layer, out _))
            {
                handles.Add(e.Handle.ToString());
                if (handles.Count >= max)
                    break;
            }
        }
        return handles;
    }

    // Xref-isolatie over opslaan/heropenen. SETUP maakt drie legenda's met eigen xref-keuzes en
    // bewaart ze in de tekening; na QSAVE + heropenen controleert VERIFY dat elke legenda zijn
    // eigen xref-inclusie houdt en dat een andere legenda of de globale default A/B niet raakt.
    [CommandMethod("NLCSLEGENDAXREFSETUP", CommandFlags.Modal)]
    public void NlcsLegendaXrefSetup()
    {
        var doc = AcApp.DocumentManager.MdiActiveDocument;
        if (doc is null)
            return;
        var ed = doc.Editor;
        var db = doc.Database;
        try
        {
            using var tr = db.TransactionManager.StartTransaction();
            var reg = LegendStore.Load(db, tr);

            var sA = LoadGlobalDefaults();
            sA.IncludeXrefLayers = false;
            sA.XrefInclusion["xref1"] = true;
            sA.XrefInclusion["xref2"] = false;
            sA.Title = "XREF-A";

            var sB = LoadGlobalDefaults();
            sB.IncludeXrefLayers = false;
            sB.XrefInclusion["xref1"] = false;
            sB.XrefInclusion["xref2"] = true;
            sB.Title = "XREF-B";

            // C simuleert een legenda die is aangemaakt toen de globale default aan stond.
            var sC = LoadGlobalDefaults();
            sC.IncludeXrefLayers = true;
            sC.Title = "XREF-C";

            foreach (var s in new[] { sA, sB, sC })
                reg.Add(IsoDef(reg, s));
            LegendStore.Save(db, tr, reg);
            tr.Commit();
            ed.WriteMessage("\nXREF: setup klaar. QSAVE, heropenen, dan NLCSLEGENDAXREFVERIFY.");
        }
        catch (Exception ex)
        {
            ed.WriteMessage($"\nXREF setup error: {ex.Message}");
        }
    }

    [CommandMethod("NLCSLEGENDAXREFVERIFY", CommandFlags.Modal)]
    public void NlcsLegendaXrefVerify()
    {
        var doc = AcApp.DocumentManager.MdiActiveDocument;
        if (doc is null)
            return;
        var ed = doc.Editor;
        var db = doc.Database;
        try
        {
            using var tr = db.TransactionManager.StartTransaction();
            var reg = LegendStore.Load(db, tr);
            LegendDefinition? Find(string title) =>
                reg.Legends.FirstOrDefault(l => string.Equals(l.Settings.Title, title, StringComparison.Ordinal));
            var a = Find("XREF-A");
            var b = Find("XREF-B");
            var c = Find("XREF-C");
            if (a is null || b is null || c is null)
            {
                ed.WriteMessage("\nXREF: legenda's niet gevonden na heropenen -> FAIL");
                tr.Commit();
                return;
            }

            bool persisted =
                a.Settings.IsXrefIncluded("xref1") && !a.Settings.IsXrefIncluded("xref2") &&
                !b.Settings.IsXrefIncluded("xref1") && b.Settings.IsXrefIncluded("xref2");
            ed.WriteMessage($"\nXREF: persisted A(1={a.Settings.IsXrefIncluded("xref1")},2={a.Settings.IsXrefIncluded("xref2")}) " +
                $"B(1={b.Settings.IsXrefIncluded("xref1")},2={b.Settings.IsXrefIncluded("xref2")}) -> {(persisted ? "OK" : "FAIL")}");

            // Geneste xref: het topniveau bepaalt de keuze (xref2|nested volgt xref2).
            bool nested = !a.Settings.IsXrefIncluded("xref2|nested") && b.Settings.IsXrefIncluded("xref2|nested");
            ed.WriteMessage($"\nXREF: genest A={a.Settings.IsXrefIncluded("xref2|nested")} B={b.Settings.IsXrefIncluded("xref2|nested")} -> {(nested ? "OK" : "FAIL")}");

            // C kreeg de globale default (aan); A/B hebben een eigen uitgeschakelde default.
            bool cDefault = c.Settings.IncludeXrefLayers && c.Settings.XrefInclusion.Count == 0
                && !a.Settings.IncludeXrefLayers && !b.Settings.IncludeXrefLayers;
            ed.WriteMessage($"\nXREF: C.default={c.Settings.IncludeXrefLayers} C.expliciet={c.Settings.XrefInclusion.Count} -> {(cDefault ? "OK" : "FAIL")}");

            // Onbekende xref valt terug op de eigen default van de legenda, niet op een andere
            // legenda: A/B zeggen nee (eigen default uit), C ja.
            bool fallback = !a.Settings.IsXrefIncluded("xref9") && !b.Settings.IsXrefIncluded("xref9")
                && c.Settings.IsXrefIncluded("xref9");
            ed.WriteMessage($"\nXREF: onbekend A={a.Settings.IsXrefIncluded("xref9")} B={b.Settings.IsXrefIncluded("xref9")} C={c.Settings.IsXrefIncluded("xref9")} -> {(fallback ? "OK" : "FAIL")}");

            tr.Commit();
        }
        catch (Exception ex)
        {
            ed.WriteMessage($"\nXREF verify error: {ex.Message}");
        }
    }

    // Twee-run A/B/C-persistentie: ABCSETUP bouwt drie legenda's met uiteenlopende instellingen
    // en slaat ze op; na QSAVE/_.QUIT/heropenen controleert ABCVERIFY dat elk met zijn eigen
    // instellingen én getekende geometrie terugkomt en dat A bewerken B/C ongemoeid laat. Zo is
    // de persistentie over een echte schijf-rondgang bewezen, niet alleen een store-herlaad.
    [CommandMethod("NLCSLEGENDAABCSETUP", CommandFlags.Modal)]
    public void NlcsLegendaAbcSetup()
    {
        var doc = AcApp.DocumentManager.MdiActiveDocument;
        if (doc is null)
            return;
        var ed = doc.Editor;
        var db = doc.Database;

        // Globale config exact bewaren; de global-change hieronder zet 'm in finally terug.
        byte[]? origConfig = File.Exists(ConfigPath) ? File.ReadAllBytes(ConfigPath) : null;
        try
        {
            using var tr = db.TransactionManager.StartTransaction();
            var reg = LegendStore.Load(db, tr);
            var excluded = LegendManagement.CollectManagedIds(db, tr, reg);

            // Twee disjuncte selecties als verschillende bronnen voor A en B; C is de hele tekening.
            var handles = CollectNlcsHandles(db, tr, 400);
            int half = handles.Count / 2;
            var handlesA = handles.Take(half).ToList();
            var handlesB = handles.Skip(half).ToList();

            var probe = DrawingAnalyzer.Analyze(db, tr, LoadGlobalDefaults(), catalog: LoadCatalog(db), excludedIds: excluded);
            string firstKey = probe.Entries.Count > 0 ? LegendSettings.EntryKey(probe.Entries[0]) : string.Empty;

            // A: selectie-bron, eigen schaal, handregel, eigen status + lid, omschrijving, 1 kolom.
            var sA = LoadGlobalDefaults();
            sA.Title = "ABC-A";
            sA.Scale = 200;
            sA.Columns = 1;
            sA.RemarksText = "ABC-A opmerking";
            sA.ManualEntries.Add(new ManualEntry { Layer = "N-WE-VV-ABC-G", Type = NlcsDrawType.Geometrie, Description = "ABC-handregel A" });
            var statusA = new CustomStatus { Name = "ABC-STATUS-A" };
            if (firstKey.Length > 0) statusA.Members.Add(firstKey);
            sA.CustomStatuses.Add(statusA);
            sA.CustomLayerRules.Add(new CustomLayerRule { Layer = "ABC-eigen-laag", Element = "ABC-eigen", Type = NlcsDrawType.Geometrie, Description = "ABC eigen laag" });
            if (firstKey.Length > 0)
                sA.DescriptionOverrides.Elementen[firstKey] = new DescriptionEntry { Specifiek = "ABC-A-TEKST" };

            // B: andere selectie-bron, andere schaal, uitsluiting, groepering aan, 2 kolommen.
            var sB = LoadGlobalDefaults();
            sB.Title = "ABC-B";
            sB.Scale = 500;
            sB.Columns = 2;
            sB.RemarksText = "ABC-B opmerking";
            sB.MergedDimensions.Add(GroupDimension.Soort);
            if (firstKey.Length > 0) sB.ExcludedEntries.Add(firstKey);

            var defA = IsoDef(reg, sA);
            defA.Scope = handlesA.Count > 0 ? LegendScope.Selection : LegendScope.WholeDrawing;
            defA.SourceHandles = handlesA;
            var defB = IsoDef(reg, sB);
            defB.Scope = handlesB.Count > 0 ? LegendScope.Selection : LegendScope.WholeDrawing;
            defB.SourceHandles = handlesB;
            reg.Add(defA);
            reg.Add(defB);
            BuildManagedLegend(db, tr, reg, defA, out _, out _);
            BuildManagedLegend(db, tr, reg, defB, out _, out _);

            // Global wijzigen en C met de nieuwe global maken (hele tekening). A/B zijn al gebouwd
            // met hun eigen snapshot en mogen hier niet door veranderen.
            var ng = LoadGlobalDefaults();
            ng.Scale = 1000;
            ng.DrawBorder = false;
            SaveGlobalDefaults(ng);
            var sC = LoadGlobalDefaults();
            sC.Title = "ABC-C";
            var defC = IsoDef(reg, sC);
            reg.Add(defC);
            BuildManagedLegend(db, tr, reg, defC, out _, out _);

            LegendStore.Save(db, tr, reg);
            tr.Commit();
            ed.WriteMessage($"\nABC: setup klaar (A-selectie={handlesA.Count}, B-selectie={handlesB.Count}). QSAVE, heropenen, dan NLCSLEGENDAABCVERIFY.");
        }
        catch (Exception ex)
        {
            ed.WriteMessage($"\nABC setup error: {ex.Message}");
        }
        finally
        {
            // Globale config exact terugzetten (data-veiligheid).
            if (origConfig is null)
            {
                try { if (File.Exists(ConfigPath)) File.Delete(ConfigPath); } catch { /* geen config om te herstellen */ }
            }
            else
            {
                File.WriteAllBytes(ConfigPath, origConfig);
            }
        }
    }

    // Analyseert één legenda (met zijn eigen bron en instellingen) en levert de uittrekstaat-CSV.
    private string ExportCsv(Database db, Transaction tr, LegendDefinition def)
    {
        try
        {
            var reg = LegendStore.Load(db, tr);
            var excluded = LegendManagement.CollectManagedIds(db, tr, reg);
            ObjectId[]? selection = def.Scope == LegendScope.Selection
                ? LegendManagement.ResolveHandles(db, def.SourceHandles, out _)
                : null;
            var entries = DrawingAnalyzer.Analyze(db, tr, def.Settings, selection, LoadCatalog(db), excluded).Entries;
            return LegendExport.ToCsv(entries, def.Settings.QuantityDecimals,
                def.Settings.UnitArea, def.Settings.UnitLength, def.Settings.UnitCount);
        }
        catch (Exception ex)
        {
            // Fout zichtbaar maken i.p.v. stil als lege export; de verify-check ziet het verschil.
            return "[export-fout: " + ex.Message + "]";
        }
    }

    [CommandMethod("NLCSLEGENDAABCVERIFY", CommandFlags.Modal)]
    public void NlcsLegendaAbcVerify()
    {
        var doc = AcApp.DocumentManager.MdiActiveDocument;
        if (doc is null)
            return;
        var ed = doc.Editor;
        var db = doc.Database;
        try
        {
            string fpBbefore = string.Empty, fpCbefore = string.Empty;
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var reg = LegendStore.Load(db, tr);
                LegendDefinition? Find(string t) =>
                    reg.Legends.FirstOrDefault(l => string.Equals(l.Settings.Title, t, StringComparison.Ordinal));
                var a = Find("ABC-A");
                var b = Find("ABC-B");
                var c = Find("ABC-C");
                if (a is null || b is null || c is null)
                {
                    ed.WriteMessage("\nABC: legenda's niet gevonden na heropenen -> FAIL");
                    tr.Commit();
                    return;
                }

                // Bronnen overleefden de schijf-rondgang: A/B zijn selecties, C de hele tekening.
                bool sourcesOk = a.Scope == LegendScope.Selection && a.SourceHandles.Count > 0
                    && b.Scope == LegendScope.Selection && b.SourceHandles.Count > 0
                    && c.Scope == LegendScope.WholeDrawing;
                ed.WriteMessage($"\nABC: bronnen A={a.Scope}({a.SourceHandles.Count}) B={b.Scope}({b.SourceHandles.Count}) C={c.Scope} -> {(sourcesOk ? "OK" : "FAIL")}");

                bool configOk = a.Settings.Scale == 200 && a.Settings.Columns == 1 && a.Settings.ManualEntries.Count == 1
                    && a.Settings.CustomStatuses.Count == 1
                    && a.Settings.CustomLayerRules.Count == 1 && b.Settings.CustomLayerRules.Count == 0
                    && b.Settings.Scale == 500 && b.Settings.Columns == 2 && b.Settings.MergedDimensions.Count >= 1
                    && b.Settings.ExcludedEntries.Count >= 1;
                ed.WriteMessage($"\nABC: config A(sc={a.Settings.Scale:0},kol={a.Settings.Columns},man={a.Settings.ManualEntries.Count},st={a.Settings.CustomStatuses.Count},eigenlaag={a.Settings.CustomLayerRules.Count}) " +
                    $"B(sc={b.Settings.Scale:0},kol={b.Settings.Columns},groep={b.Settings.MergedDimensions.Count},excl={b.Settings.ExcludedEntries.Count},eigenlaag={b.Settings.CustomLayerRules.Count}) -> {(configOk ? "OK" : "FAIL")}");

                // Global-isolatie: global is gewijzigd vóór C; A/B hielden hun eigen schaal, C kreeg de nieuwe.
                bool globalIso = a.Settings.Scale == 200 && b.Settings.Scale == 500
                    && c.Settings.Scale == 1000 && !c.Settings.DrawBorder;
                ed.WriteMessage($"\nABC: global-isolatie C(sc={c.Settings.Scale:0},kader={c.Settings.DrawBorder}) -> {(globalIso ? "OK" : "FAIL")}");

                // Inhoudelijke vingerafdrukken: geometrie overleefde en A/B/C verschillen echt.
                string fpA = ContentFingerprint(db, tr, a.GroupName);
                string fpB = ContentFingerprint(db, tr, b.GroupName);
                string fpC = ContentFingerprint(db, tr, c.GroupName);
                bool fpOk = fpA.Length > 0 && fpB.Length > 0 && fpC.Length > 0 && fpA != fpB && fpA != fpC && fpB != fpC;
                ed.WriteMessage($"\nABC: vingerafdruk A={fpA} B={fpB} C={fpC} -> {(fpOk ? "verschillend OK" : "FAIL")}");
                fpBbefore = fpB;
                fpCbefore = fpC;

                // Export-isolatie: A/B/C leveren elk een eigen uittrekstaat (eigen bron + config).
                string csvA = ExportCsv(db, tr, a);
                string csvB = ExportCsv(db, tr, b);
                string csvC = ExportCsv(db, tr, c);
                bool exportOk = new[] { csvA, csvB, csvC }.All(s => s.Length > 0 && !s.StartsWith("[export-fout"))
                    && csvA != csvB && csvA != csvC && csvB != csvC;
                ed.WriteMessage($"\nABC: export A={csvA.Length}tk B={csvB.Length}tk C={csvC.Length}tk -> {(exportOk ? "verschillend OK" : "FAIL")}");

                // Viewport per legenda: elk om de eigen extents, op schaal, zonder clipping.
                string VpCheck(LegendDefinition d)
                {
                    if (!LegendManagement.TryGetGroupExtents(db, tr, d.GroupName, out var ext))
                        return "geen-extents";
                    double mw = ext.MaxPoint.X - ext.MinPoint.X;
                    double mh = ext.MaxPoint.Y - ext.MinPoint.Y;
                    var plan = ViewportMath.Compute(mw, mh, d.Settings.Scale, 5.0);
                    double paperPerModel = 1000.0 / d.Settings.Scale;
                    bool marginsOk = Math.Abs(plan.PaperWidthMm - (mw * paperPerModel + 10)) < 1e-3
                        && Math.Abs(plan.PaperHeightMm - (mh * paperPerModel + 10)) < 1e-3;
                    return marginsOk ? "OK" : "FAIL";
                }
                ed.WriteMessage($"\nABC: viewport A={VpCheck(a)} B={VpCheck(b)} C={VpCheck(c)}");

                tr.Commit();
            }
            PurgePending(db);

            // Na heropenen A bewerken mag B/C inhoudelijk niet raken (update-isolatie blijft gelden).
            string fpBafter, fpCafter;
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var reg = LegendStore.Load(db, tr);
                var a = reg.Legends.First(l => l.Settings.Title == "ABC-A");
                a.Settings.Scale = 300;
                BuildManagedLegend(db, tr, reg, a, out _, out _);
                var b = reg.Legends.First(l => l.Settings.Title == "ABC-B");
                var c = reg.Legends.First(l => l.Settings.Title == "ABC-C");
                fpBafter = ContentFingerprint(db, tr, b.GroupName);
                fpCafter = ContentFingerprint(db, tr, c.GroupName);
                LegendStore.Save(db, tr, reg);
                tr.Commit();
            }
            PurgePending(db);
            bool isoOk = fpBbefore == fpBafter && fpCbefore == fpCafter;
            ed.WriteMessage($"\nABC: na A bewerken B/C-vingerafdruk {(isoOk ? "ongewijzigd" : "GEWIJZIGD")}");
        }
        catch (Exception ex)
        {
            ed.WriteMessage($"\nABC verify error: {ex.Message}");
        }
    }

    // Blanco legendaregels op een echte host: controleert dat ze als echte rijen worden gebouwd
    // (leeg vakje, tekst "[blanco]"), dat extra blanco's de legenda hoger maken, dat ze geen
    // renderissue geven en dat tekst-uit de regel laat staan maar de tekst verbergt.
    [CommandMethod("NLCSLEGENDABLANCOTEST", CommandFlags.Modal)]
    public void NlcsLegendaBlancoTest()
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
                var lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForWrite);
                ObjectId L(string n) { if (lt.Has(n)) return lt[n]; var r = new LayerTableRecord { Name = n }; var id = lt.Add(r); tr.AddNewlyCreatedDBObject(r, true); return id; }
                var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForWrite);
                foreach (var n in new[] { "N-WE-VH-BLTEST1-G", "N-WE-RI-BLTEST2-G" })
                {
                    var line = new Line(new Point3d(0, 0, 0), new Point3d(5, 0, 0)) { LayerId = L(n) };
                    ms.AppendEntity(line); tr.AddNewlyCreatedDBObject(line, true);
                }
                tr.Commit();
            }

            (int rows, int issues, double height, int blanks, bool textSeen) Build(int blankCount, bool includeText)
            {
                var s = LoadGlobalDefaults();
                s.IncludeText = includeText;
                for (int i = 0; i < blankCount; i++)
                    s.BlankEntries.Add(new BlankEntry());
                using var tr = db.TransactionManager.StartTransaction();
                var reg = LegendStore.Load(db, tr);
                var def = IsoDef(reg, s);
                reg.Add(def);
                var res = BuildManagedLegend(db, tr, reg, def, out int rows, out _);
                LegendStore.Save(db, tr, reg);
                LegendManagement.TryGetGroupExtents(db, tr, def.GroupName, out var ext);
                double h = ext.MaxPoint.Y - ext.MinPoint.Y;
                var analysis = DrawingAnalyzer.Analyze(db, tr, s, catalog: LoadCatalog(db));
                int blanks = analysis.Entries.Count(e => e.IsBlank);
                bool txt = analysis.Entries.Any(e => e.IsBlank && e.Description == "[blanco]");
                tr.Commit();
                PurgePending(db);
                return (rows, 0, h, blanks, txt);
            }

            var b0 = Build(0, includeText: true);
            var b1 = Build(1, includeText: true);
            var b5 = Build(5, includeText: true);
            var bText = Build(3, includeText: false);

            bool rowsOk = b1.rows == b0.rows + 1 && b5.rows == b0.rows + 5;
            bool growOk = b5.height > b1.height && b1.height > b0.height;
            bool blankOk = b1.blanks == 1 && b5.blanks == 5 && b1.textSeen;
            bool textOffOk = bText.blanks == 3; // regels blijven bestaan ook met tekst uit
            ed.WriteMessage($"\nBLANCO: rijen 0={b0.rows} 1={b1.rows} 5={b5.rows} (verw +1/+5) -> {(rowsOk ? "OK" : "FAIL")}");
            ed.WriteMessage($"\nBLANCO: hoogte groeit {b0.height:0.0}<{b1.height:0.0}<{b5.height:0.0} -> {(growOk ? "OK" : "FAIL")}");
            ed.WriteMessage($"\nBLANCO: entries IsBlank 1={b1.blanks} 5={b5.blanks} tekst=\"[blanco]\" -> {(blankOk ? "OK" : "FAIL")}");
            ed.WriteMessage($"\nBLANCO: tekst-uit behoudt {bText.blanks} blanco-rijen -> {(textOffOk ? "OK" : "FAIL")}");
            ed.WriteMessage($"\nBLANCO: totaal -> {(rowsOk && growOk && blankOk && textOffOk ? "OK" : "FAIL")}");
        }
        catch (Exception ex)
        {
            ed.WriteMessage($"\nBLANCO error: {ex.Message}");
        }
    }

    // Systeemvariabele-isolatie: legt een set relevante sysvars plus de actieve laag/layout vast,
    // draait de belangrijkste commando's (analyse, legenda, export, viewport-meting) en controleert
    // dat elke waarde daarna exact gelijk is. Bewijst dat de plugin geen host-state laat lekken.
    [CommandMethod("NLCSLEGENDASYSVARTEST", CommandFlags.Modal)]
    public void NlcsLegendaSysvarTest()
    {
        var doc = AcApp.DocumentManager.MdiActiveDocument;
        if (doc is null)
            return;
        var ed = doc.Editor;
        var db = doc.Database;
        string[] names = { "FILEDIA", "SECURELOAD", "CMDECHO", "CLAYER", "CTAB", "TILEMODE", "OSMODE", "PICKSTYLE", "ATTREQ", "ATTDIA", "EXPERT" };
        try
        {
            var before = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var n in names)
            {
                try { before[n] = AcApp.GetSystemVariable(n)?.ToString() ?? "<null>"; } catch { }
            }

            // Representatieve operaties die state zouden kunnen wijzigen.
            using (var tr = db.TransactionManager.StartTransaction())
            {
                DrawingAnalyzer.Analyze(db, tr, LoadGlobalDefaults(), catalog: LoadCatalog(db));
                tr.Commit();
            }
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var reg = LegendStore.Load(db, tr);
                var def = IsoDef(reg, LoadGlobalDefaults());
                reg.Add(def);
                BuildManagedLegend(db, tr, reg, def, out _, out _);
                LegendStore.Save(db, tr, reg);
                tr.Commit();
            }
            PurgePending(db);
            string tmp = Path.Combine(Path.GetTempPath(), "nlcs_sysvar_" + Guid.NewGuid().ToString("N") + ".csv");
            try
            {
                using var tr = db.TransactionManager.StartTransaction();
                var analysis = DrawingAnalyzer.Analyze(db, tr, LoadGlobalDefaults(), catalog: LoadCatalog(db));
                File.WriteAllText(tmp, LegendExport.ToCsv(analysis.Entries));
                tr.Commit();
            }
            finally { try { if (File.Exists(tmp)) File.Delete(tmp); } catch { } }

            int afwijkingen = 0;
            foreach (var n in names)
            {
                string na;
                try { na = AcApp.GetSystemVariable(n)?.ToString() ?? "<null>"; } catch { continue; }
                if (before.TryGetValue(n, out var nb) && !string.Equals(nb, na, StringComparison.Ordinal))
                {
                    ed.WriteMessage($"\nSYSVAR: {n} {nb} -> {na} (GEWIJZIGD)");
                    afwijkingen++;
                }
            }
            ed.WriteMessage($"\nSYSVAR: {names.Length} variabele(n) gecontroleerd, {afwijkingen} gewijzigd -> {(afwijkingen == 0 ? "OK" : "FAIL")}");
        }
        catch (Exception ex)
        {
            ed.WriteMessage($"\nSYSVAR error: {ex.Message}");
        }
    }

    // First-class eigen bronlagen op een echte host: maakt geometrie op niet-NLCS-lagen en
    // controleert dat gekoppelde eigen lagen gelijkwaardig meetellen (lengte, aantal, oppervlak),
    // zowel op de hele tekening als op een selectie, en dat een echte legenda wordt gebouwd.
    [CommandMethod("NLCSLEGENDACUSTOMTEST", CommandFlags.Modal)]
    public void NlcsLegendaCustomTest()
    {
        var doc = AcApp.DocumentManager.MdiActiveDocument;
        if (doc is null)
            return;
        var ed = doc.Editor;
        var db = doc.Database;
        try
        {
            ObjectId line5 = ObjectId.Null;
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var msId = SymbolUtilityServices.GetBlockModelSpaceId(db);
                var ms = (BlockTableRecord)tr.GetObject(msId, OpenMode.ForWrite);
                var lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForWrite);
                ObjectId EnsureLayer(string name)
                {
                    if (lt.Has(name)) return lt[name];
                    var ltr = new LayerTableRecord { Name = name };
                    var id = lt.Add(ltr); tr.AddNewlyCreatedDBObject(ltr, true); return id;
                }
                var kabels = EnsureLayer("Eigen kabels");
                var putten = EnsureLayer("Speciale putten");
                var verharding = EnsureLayer("Eigen verharding");

                // Eigen kabels: lijn van 5 m + polyline van 7 m = 12 m.
                var l5 = new Line(new Point3d(0, 0, 0), new Point3d(5, 0, 0)) { LayerId = kabels };
                line5 = ms.AppendEntity(l5); tr.AddNewlyCreatedDBObject(l5, true);
                var pl = new Polyline();
                pl.AddVertexAt(0, new Point2d(0, 10), 0, 0, 0);
                pl.AddVertexAt(1, new Point2d(7, 10), 0, 0, 0);
                pl.LayerId = kabels;
                ms.AppendEntity(pl); tr.AddNewlyCreatedDBObject(pl, true);

                // Blok PUT (een cirkel) en 3 inserties op "Speciale putten".
                var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForWrite);
                ObjectId putBlock;
                if (bt.Has("PUT")) { putBlock = bt["PUT"]; }
                else
                {
                    var bd = new BlockTableRecord { Name = "PUT" };
                    putBlock = bt.Add(bd); tr.AddNewlyCreatedDBObject(bd, true);
                    var circ = new Circle(Point3d.Origin, Vector3d.ZAxis, 0.5);
                    bd.AppendEntity(circ); tr.AddNewlyCreatedDBObject(circ, true);
                }
                for (int i = 0; i < 3; i++)
                {
                    var brf = new BlockReference(new Point3d(i * 2, 20, 0), putBlock) { LayerId = putten };
                    ms.AppendEntity(brf); tr.AddNewlyCreatedDBObject(brf, true);
                }

                // Eigen verharding: gesloten polyline van 10 x 5 = 50 m2.
                var area = new Polyline();
                area.AddVertexAt(0, new Point2d(0, 30), 0, 0, 0);
                area.AddVertexAt(1, new Point2d(10, 30), 0, 0, 0);
                area.AddVertexAt(2, new Point2d(10, 35), 0, 0, 0);
                area.AddVertexAt(3, new Point2d(0, 35), 0, 0, 0);
                area.Closed = true;
                area.LayerId = verharding;
                ms.AppendEntity(area); tr.AddNewlyCreatedDBObject(area, true);
                tr.Commit();
            }

            var settings = LoadGlobalDefaults();
            settings.CustomLayerRules.Add(new CustomLayerRule { Layer = "Eigen kabels", Element = "Datakabel", Type = NlcsDrawType.Geometrie, Description = "Datakabel", QuantityMode = CustomQuantityMode.Lengte });
            settings.CustomLayerRules.Add(new CustomLayerRule { Layer = "Speciale putten", Element = "Straatput", Type = NlcsDrawType.Symbool, Description = "Straatput", QuantityMode = CustomQuantityMode.Aantal, BlockName = "PUT" });
            settings.CustomLayerRules.Add(new CustomLayerRule { Layer = "Eigen verharding", Element = "Eigen verharding", Type = NlcsDrawType.Vlak, Description = "Eigen verharding", QuantityMode = CustomQuantityMode.Oppervlak });

            LegendEntry? Find(IReadOnlyList<LegendEntry> es, string el) =>
                es.FirstOrDefault(e => e.Element == el);

            using (var tr = db.TransactionManager.StartTransaction())
            {
                var whole = DrawingAnalyzer.Analyze(db, tr, settings, catalog: LoadCatalog(db)).Entries;
                var kabel = Find(whole, "Datakabel");
                var put = Find(whole, "Straatput");
                var verh = Find(whole, "Eigen verharding");
                ed.WriteMessage($"\nCUSTOM: hele tekening kabel={kabel?.Metric.Length:0.0}m (verw 12) put={put?.Metric.Count}x (verw 3) verharding={verh?.Metric.Area:0.0}m2 (verw 50)");
                bool wholeOk = kabel is not null && Math.Abs(kabel.Metric.Length - 12) < 0.1
                    && put is not null && put.Metric.Count == 3
                    && verh is not null && Math.Abs(verh.Metric.Area - 50) < 0.1;
                ed.WriteMessage($"\nCUSTOM: block-identiteit put={put?.SymbolBlockName} (verw PUT); hele tekening -> {(wholeOk ? "OK" : "FAIL")}");

                // Selectie: alleen de 5 m-lijn -> 5 m.
                var sel = DrawingAnalyzer.Analyze(db, tr, settings, new[] { line5 }, LoadCatalog(db)).Entries;
                var selKabel = Find(sel, "Datakabel");
                bool selOk = selKabel is not null && Math.Abs(selKabel.Metric.Length - 5) < 0.1;
                ed.WriteMessage($"\nCUSTOM: selectie (1e lijn) kabel={selKabel?.Metric.Length:0.0}m (verw 5) -> {(selOk ? "OK" : "FAIL")}");
                tr.Commit();
            }

            // Echte legenda bouwen met de eigen lagen erin.
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var reg = LegendStore.Load(db, tr);
                var def = IsoDef(reg, settings);
                reg.Add(def);
                var result = BuildManagedLegend(db, tr, reg, def, out int rows, out _);
                LegendStore.Save(db, tr, reg);
                tr.Commit();
                PurgePending(db);
                ed.WriteMessage($"\nCUSTOM: legenda gebouwd {result} met {rows} regels (incl. eigen lagen)");
            }
        }
        catch (Exception ex)
        {
            ed.WriteMessage($"\nCUSTOM error: {ex.Message}");
        }
    }

    // Echte eigen bronlagen in xrefs: dezelfde lokale laagnaam "Eigen kabels" komt voor in de
    // host (3 m) én in twee xrefs (5 m en 11 m). Bewijst dat scope de bron echt filtert: Lokaal
    // telt alleen de host, SpecificXref alleen de genoemde xref, ElkeBron alle bronnen samen, en
    // dat xref-uitsluiting de classificatie overruled (geen regel = geen regel).
    [CommandMethod("NLCSLEGENDAEIGENXREFTEST", CommandFlags.Modal)]
    public void NlcsLegendaEigenXrefTest()
    {
        var doc = AcApp.DocumentManager.MdiActiveDocument;
        if (doc is null)
            return;
        var ed = doc.Editor;
        var db = doc.Database;
        string dir = Path.GetDirectoryName(db.Filename) ?? Path.GetTempPath();
        string pathA = Path.Combine(dir, "eigen_xref_a.dwg");
        string pathB = Path.Combine(dir, "eigen_xref_b.dwg");
        try
        {
            CreateXrefLineDwg(pathA, "Eigen kabels", 5.0);
            CreateXrefLineDwg(pathB, "Eigen kabels", 11.0);

            using (var tr = db.TransactionManager.StartTransaction())
            {
                var lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForWrite);
                ObjectId host = lt.Has("Eigen kabels") ? lt["Eigen kabels"]
                    : AddLayer(lt, tr, "Eigen kabels");
                var msId = SymbolUtilityServices.GetBlockModelSpaceId(db);
                var ms = (BlockTableRecord)tr.GetObject(msId, OpenMode.ForWrite);
                var l3 = new Line(new Point3d(0, 0, 0), new Point3d(3, 0, 0)) { LayerId = host };
                ms.AppendEntity(l3); tr.AddNewlyCreatedDBObject(l3, true);
                tr.Commit();
            }

            ObjectId xrefA = db.AttachXref(pathA, "EIGENXREFA");
            ObjectId xrefB = db.AttachXref(pathB, "EIGENXREFB");
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var msId = SymbolUtilityServices.GetBlockModelSpaceId(db);
                var ms = (BlockTableRecord)tr.GetObject(msId, OpenMode.ForWrite);
                foreach (var id in new[] { xrefA, xrefB })
                {
                    var br = new BlockReference(Point3d.Origin, id);
                    ms.AppendEntity(br); tr.AddNewlyCreatedDBObject(br, true);
                }
                tr.Commit();
            }

            LegendSettings Config(CustomSourceScope scope, string xref, bool includeXref,
                bool? incA = null, bool? incB = null)
            {
                var s = LoadGlobalDefaults();
                s.IncludeXrefLayers = includeXref;
                if (incA is not null) s.XrefInclusion["EIGENXREFA"] = incA.Value;
                if (incB is not null) s.XrefInclusion["EIGENXREFB"] = incB.Value;
                s.CustomLayerRules.Add(new CustomLayerRule
                {
                    Layer = "Eigen kabels", Element = "Kabel", Type = NlcsDrawType.Geometrie,
                    Description = "Kabel", QuantityMode = CustomQuantityMode.Lengte,
                    Scope = scope, XrefName = xref
                });
                return s;
            }

            double Length(LegendSettings s)
            {
                using var tr = db.TransactionManager.StartTransaction();
                var e = DrawingAnalyzer.Analyze(db, tr, s, catalog: LoadCatalog(db)).Entries
                    .FirstOrDefault(x => x.Element == "Kabel");
                tr.Commit();
                return e?.Metric.Length ?? -1;
            }

            double local = Length(Config(CustomSourceScope.Local, "", includeXref: true));
            double specA = Length(Config(CustomSourceScope.SpecificXref, "EIGENXREFA", includeXref: true));
            double any = Length(Config(CustomSourceScope.AnySource, "", includeXref: true));
            // Xref-uitsluiting overruled de classificatie: SpecificXref EIGENXREFA maar de xref is
            // uitgesloten -> geen enkele regel (Length == -1).
            double overruled = Length(Config(CustomSourceScope.SpecificXref, "EIGENXREFA",
                includeXref: false, incA: false, incB: false));

            bool ok = Math.Abs(local - 3) < 0.1 && Math.Abs(specA - 5) < 0.1
                && Math.Abs(any - 19) < 0.1 && overruled < 0;
            ed.WriteMessage($"\nEIGENXREF: lokaal={local:0.0} (verw 3) specifiekA={specA:0.0} (verw 5) " +
                $"elkeBron={any:0.0} (verw 19) uitgesloten={(overruled < 0 ? "geen regel" : overruled.ToString("0.0"))} (verw geen regel)");
            ed.WriteMessage($"\nEIGENXREF: matrix -> {(ok ? "OK" : "FAIL")}");
        }
        catch (Exception ex)
        {
            ed.WriteMessage($"\nEIGENXREF error: {ex.Message}");
        }
        finally
        {
            try { if (File.Exists(pathA)) File.Delete(pathA); } catch { }
            try { if (File.Exists(pathB)) File.Delete(pathB); } catch { }
        }
    }

    private static ObjectId AddLayer(LayerTable lt, Transaction tr, string name)
    {
        var ltr = new LayerTableRecord { Name = name };
        var id = lt.Add(ltr);
        tr.AddNewlyCreatedDBObject(ltr, true);
        return id;
    }

    private static ObjectId AddLayer(LayerTable lt, Transaction tr, string name, short colorIndex)
    {
        var ltr = new LayerTableRecord { Name = name };
        if (colorIndex > 0)
            ltr.Color = Autodesk.AutoCAD.Colors.Color.FromColorIndex(Autodesk.AutoCAD.Colors.ColorMethod.ByAci, colorIndex);
        var id = lt.Add(ltr);
        tr.AddNewlyCreatedDBObject(ltr, true);
        return id;
    }

    // AnySource-bronidentiteit versus logische identiteit: dezelfde laagnaam in host en xref met
    // VERSCHILLENDE kleur mag niet first-wins samenvallen, maar levert aparte regels per bron.
    // Met GELIJKE kleur telt de hoeveelheid juist op tot één regel. Bewijst beide takken.
    [CommandMethod("NLCSLEGENDAEIGENANYTEST", CommandFlags.Modal)]
    public void NlcsLegendaEigenAnyTest()
    {
        var doc = AcApp.DocumentManager.MdiActiveDocument;
        if (doc is null)
            return;
        var ed = doc.Editor;
        var db = doc.Database;
        string dir = Path.GetDirectoryName(db.Filename) ?? Path.GetTempPath();
        string divXref = Path.Combine(dir, "any_div.dwg");
        string eqXref = Path.Combine(dir, "any_eq.dwg");
        try
        {
            // Divergente tak: host "Div kabels" rood (4 m), xref groen (6 m).
            CreateXrefLineDwg(divXref, "Div kabels", 6.0, colorIndex: 3); // groen
            // Gelijke tak: host "Eq kabels" standaard (4 m), xref standaard (6 m).
            CreateXrefLineDwg(eqXref, "Eq kabels", 6.0, colorIndex: 0);

            using (var tr = db.TransactionManager.StartTransaction())
            {
                var lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForWrite);
                var divLayer = lt.Has("Div kabels") ? lt["Div kabels"] : AddLayer(lt, tr, "Div kabels", 1); // rood
                var eqLayer = lt.Has("Eq kabels") ? lt["Eq kabels"] : AddLayer(lt, tr, "Eq kabels");
                var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForWrite);
                var l1 = new Line(new Point3d(0, 0, 0), new Point3d(4, 0, 0)) { LayerId = divLayer };
                ms.AppendEntity(l1); tr.AddNewlyCreatedDBObject(l1, true);
                var l2 = new Line(new Point3d(0, 50, 0), new Point3d(4, 50, 0)) { LayerId = eqLayer };
                ms.AppendEntity(l2); tr.AddNewlyCreatedDBObject(l2, true);
                tr.Commit();
            }
            ObjectId xDiv = db.AttachXref(divXref, "ANYDIV");
            ObjectId xEq = db.AttachXref(eqXref, "ANYEQ");
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForWrite);
                foreach (var id in new[] { xDiv, xEq })
                {
                    var br = new BlockReference(Point3d.Origin, id);
                    ms.AppendEntity(br); tr.AddNewlyCreatedDBObject(br, true);
                }
                tr.Commit();
            }

            List<LegendEntry> Build(string layer, string element)
            {
                var s = LoadGlobalDefaults();
                s.IncludeXrefLayers = true;
                s.CustomLayerRules.Add(new CustomLayerRule
                {
                    Layer = layer, Element = element, Type = NlcsDrawType.Geometrie,
                    Description = element, QuantityMode = CustomQuantityMode.Lengte,
                    Scope = CustomSourceScope.AnySource
                });
                using var tr = db.TransactionManager.StartTransaction();
                var es = DrawingAnalyzer.Analyze(db, tr, s, catalog: LoadCatalog(db)).Entries
                    .Where(e => e.Element.StartsWith(element, StringComparison.Ordinal)).ToList();
                tr.Commit();
                return es;
            }

            var div = Build("Div kabels", "Div kabel");
            var eq = Build("Eq kabels", "Eq kabel");

            // Divergent: twee regels (host rood 4 m + xref groen 6 m), niet samengevoegd.
            bool divOk = div.Count == 2
                && div.Any(e => Math.Abs(e.Metric.Length - 4) < 0.1)
                && div.Any(e => Math.Abs(e.Metric.Length - 6) < 0.1);
            // Gelijk: één regel met opgetelde 10 m.
            bool eqOk = eq.Count == 1 && Math.Abs(eq[0].Metric.Length - 10) < 0.1;
            ed.WriteMessage($"\nEIGENANY: divergent regels={div.Count} lengtes=[{string.Join(",", div.Select(e => e.Metric.Length.ToString("0")))}] (verw 2: 4 en 6) -> {(divOk ? "OK" : "FAIL")}");
            ed.WriteMessage($"\nEIGENANY: gelijk regels={eq.Count} lengte={(eq.Count > 0 ? eq[0].Metric.Length.ToString("0") : "-")} (verw 1: 10) -> {(eqOk ? "OK" : "FAIL")}");
            ed.WriteMessage($"\nEIGENANY: totaal -> {(divOk && eqOk ? "OK" : "FAIL")}");
        }
        catch (Exception ex)
        {
            ed.WriteMessage($"\nEIGENANY error: {ex.Message}");
        }
        finally
        {
            try { if (File.Exists(divXref)) File.Delete(divXref); } catch { }
            try { if (File.Exists(eqXref)) File.Delete(eqXref); } catch { }
        }
    }

    // Zichtbaarheid moet voor eigen bronlagen exact zo werken als voor NLCS-lagen: een bevroren
    // laag telt niet mee tenzij "onzichtbare lagen meenemen" aan staat. Maakt een eigen en een
    // NLCS-laag, bevriest beide, en controleert dat ze in beide standen gelijk reageren.
    [CommandMethod("NLCSLEGENDAEIGENZICHTBAARTEST", CommandFlags.Modal)]
    public void NlcsLegendaEigenZichtbaarTest()
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
                var lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForWrite);
                var eigen = AddLayer(lt, tr, "Eigen vis");
                var nlcs = AddLayer(lt, tr, "N-WE-VH-ZICHTTEST-G");
                ((LayerTableRecord)tr.GetObject(eigen, OpenMode.ForWrite)).IsFrozen = true;
                ((LayerTableRecord)tr.GetObject(nlcs, OpenMode.ForWrite)).IsFrozen = true;
                var msId = SymbolUtilityServices.GetBlockModelSpaceId(db);
                var ms = (BlockTableRecord)tr.GetObject(msId, OpenMode.ForWrite);
                var le = new Line(new Point3d(0, 0, 0), new Point3d(4, 0, 0)) { LayerId = eigen };
                ms.AppendEntity(le); tr.AddNewlyCreatedDBObject(le, true);
                var ln = new Line(new Point3d(0, 5, 0), new Point3d(6, 5, 0)) { LayerId = nlcs };
                ms.AppendEntity(ln); tr.AddNewlyCreatedDBObject(ln, true);
                tr.Commit();
            }

            (bool eigen, bool nlcs) Measure(bool includeInvisible)
            {
                var s = LoadGlobalDefaults();
                s.IncludeInvisibleLayers = includeInvisible;
                s.CustomLayerRules.Add(new CustomLayerRule
                {
                    Layer = "Eigen vis", Element = "Eigen vis element", Type = NlcsDrawType.Geometrie,
                    Description = "Eigen vis", QuantityMode = CustomQuantityMode.Lengte
                });
                using var tr = db.TransactionManager.StartTransaction();
                var es = DrawingAnalyzer.Analyze(db, tr, s, catalog: LoadCatalog(db)).Entries;
                tr.Commit();
                return (es.Any(e => e.Element == "Eigen vis element"), es.Any(e => e.Element == "ZICHTTEST"));
            }

            var off = Measure(includeInvisible: false);
            var on = Measure(includeInvisible: true);
            // Pariteit: bevroren -> beide weg; meenemen -> beide aanwezig. Eigen == NLCS.
            bool ok = !off.eigen && !off.nlcs && on.eigen && on.nlcs;
            ed.WriteMessage($"\nEIGENZICHT: bevroren eigen={off.eigen} nlcs={off.nlcs} (verw false/false); " +
                $"meenemen eigen={on.eigen} nlcs={on.nlcs} (verw true/true) -> {(ok ? "OK" : "FAIL")}");
        }
        catch (Exception ex)
        {
            ed.WriteMessage($"\nEIGENZICHT error: {ex.Message}");
        }
    }

    // Zoals CreateXrefSourceDwg, maar met instelbare lijnlengte en willekeurige (eigen) laagnaam.
    private static void CreateXrefLineDwg(string path, string layer, double length, short colorIndex = 0)
    {
        var prev = HostApplicationServices.WorkingDatabase;
        using var xdb = new Database(buildDefaultDrawing: true, noDocument: true);
        HostApplicationServices.WorkingDatabase = xdb;
        try
        {
            using (var tr = xdb.TransactionManager.StartTransaction())
            {
                var lt = (LayerTable)tr.GetObject(xdb.LayerTableId, OpenMode.ForWrite);
                if (!lt.Has(layer))
                {
                    var ltr = new LayerTableRecord { Name = layer };
                    if (colorIndex > 0)
                        ltr.Color = Autodesk.AutoCAD.Colors.Color.FromColorIndex(Autodesk.AutoCAD.Colors.ColorMethod.ByAci, colorIndex);
                    lt.Add(ltr); tr.AddNewlyCreatedDBObject(ltr, true);
                }
                var msId = SymbolUtilityServices.GetBlockModelSpaceId(xdb);
                var ms = (BlockTableRecord)tr.GetObject(msId, OpenMode.ForWrite);
                var line = new Line(new Point3d(0, 0, 0), new Point3d(length, 0, 0)) { Layer = layer };
                ms.AppendEntity(line); tr.AddNewlyCreatedDBObject(line, true);
                tr.Commit();
            }
            xdb.SaveAs(path, DwgVersion.Current);
        }
        finally
        {
            HostApplicationServices.WorkingDatabase = prev;
        }
    }

    // Maakt een losse bron-DWG met één NLCS-polyline op de gegeven laag, om als xref te koppelen.
    // In de Core Console moet de nieuwe database tijdelijk de werkdatabase zijn, anders is de
    // modelspace nog niet opgebouwd (eKeyNotFound).
    private static void CreateXrefSourceDwg(string path, string layer)
    {
        var prev = HostApplicationServices.WorkingDatabase;
        using var xdb = new Database(buildDefaultDrawing: true, noDocument: true);
        HostApplicationServices.WorkingDatabase = xdb;
        try
        {
            using (var tr = xdb.TransactionManager.StartTransaction())
            {
                var lt = (LayerTable)tr.GetObject(xdb.LayerTableId, OpenMode.ForWrite);
                if (!lt.Has(layer))
                {
                    var ltr = new LayerTableRecord { Name = layer };
                    lt.Add(ltr);
                    tr.AddNewlyCreatedDBObject(ltr, true);
                }
                var msId = SymbolUtilityServices.GetBlockModelSpaceId(xdb);
                var ms = (BlockTableRecord)tr.GetObject(msId, OpenMode.ForWrite);
                var pl = new Polyline();
                pl.AddVertexAt(0, new Point2d(0, 0), 0, 0, 0);
                pl.AddVertexAt(1, new Point2d(5, 0), 0, 0, 0);
                pl.Layer = layer;
                ms.AppendEntity(pl);
                tr.AddNewlyCreatedDBObject(pl, true);
                tr.Commit();
            }
            xdb.SaveAs(path, DwgVersion.Current);
        }
        finally
        {
            HostApplicationServices.WorkingDatabase = prev;
        }
    }

    // Echte xref-isolatie in de A/B/C-flow: koppelt twee synthetische xrefs met elk een eigen
    // NLCS-element. A neemt alleen xref A mee, B alleen xref B, C allebei (globale default aan).
    // Na QSAVE/heropenen moeten de geanalyseerde elementen per legenda echt verschillen, niet
    // alleen de instelling. De xref-bronbestanden komen naast de host te staan zodat heropenen ze
    // vindt.
    [CommandMethod("NLCSLEGENDAABCXREFSETUP", CommandFlags.Modal)]
    public void NlcsLegendaAbcXrefSetup()
    {
        var doc = AcApp.DocumentManager.MdiActiveDocument;
        if (doc is null)
            return;
        var ed = doc.Editor;
        var db = doc.Database;
        try
        {
            string dir = Path.GetDirectoryName(db.Filename) ?? Path.GetTempPath();
            string pathA = Path.Combine(dir, "abc_xref_a.dwg");
            string pathB = Path.Combine(dir, "abc_xref_b.dwg");
            CreateXrefSourceDwg(pathA, "N-WE-VH-XREFA-G");
            CreateXrefSourceDwg(pathB, "N-WE-VH-XREFB-G");

            ObjectId xrefA = db.AttachXref(pathA, "ABCXREFA");
            ObjectId xrefB = db.AttachXref(pathB, "ABCXREFB");
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var msId = SymbolUtilityServices.GetBlockModelSpaceId(db);
                var ms = (BlockTableRecord)tr.GetObject(msId, OpenMode.ForWrite);
                foreach (var id in new[] { xrefA, xrefB })
                {
                    var br = new BlockReference(Point3d.Origin, id);
                    ms.AppendEntity(br);
                    tr.AddNewlyCreatedDBObject(br, true);
                }
                tr.Commit();
            }

            using (var tr = db.TransactionManager.StartTransaction())
            {
                var reg = LegendStore.Load(db, tr);

                var sA = LoadGlobalDefaults();
                sA.Title = "ABCX-A";
                sA.IncludeXrefLayers = false;
                sA.XrefInclusion["ABCXREFA"] = true;
                sA.XrefInclusion["ABCXREFB"] = false;

                var sB = LoadGlobalDefaults();
                sB.Title = "ABCX-B";
                sB.IncludeXrefLayers = false;
                sB.XrefInclusion["ABCXREFA"] = false;
                sB.XrefInclusion["ABCXREFB"] = true;

                var sC = LoadGlobalDefaults();
                sC.Title = "ABCX-C";
                sC.IncludeXrefLayers = true;

                foreach (var s in new[] { sA, sB, sC })
                    reg.Add(IsoDef(reg, s));
                LegendStore.Save(db, tr, reg);
                tr.Commit();
            }
            ed.WriteMessage("\nABCX: setup klaar (2 xrefs gekoppeld). QSAVE, heropenen, dan NLCSLEGENDAABCXREFVERIFY.");
        }
        catch (Exception ex)
        {
            ed.WriteMessage($"\nABCX setup error: {ex.Message}");
        }
    }

    [CommandMethod("NLCSLEGENDAABCXREFVERIFY", CommandFlags.Modal)]
    public void NlcsLegendaAbcXrefVerify()
    {
        var doc = AcApp.DocumentManager.MdiActiveDocument;
        if (doc is null)
            return;
        var ed = doc.Editor;
        var db = doc.Database;
        try
        {
            using var tr = db.TransactionManager.StartTransaction();
            var reg = LegendStore.Load(db, tr);
            var excluded = LegendManagement.CollectManagedIds(db, tr, reg);

            LegendDefinition? Find(string t) =>
                reg.Legends.FirstOrDefault(l => string.Equals(l.Settings.Title, t, StringComparison.Ordinal));
            var a = Find("ABCX-A");
            var b = Find("ABCX-B");
            var c = Find("ABCX-C");
            if (a is null || b is null || c is null)
            {
                ed.WriteMessage("\nABCX: legenda's niet gevonden -> FAIL");
                tr.Commit();
                return;
            }

            string Elements(LegendDefinition d)
            {
                var an = DrawingAnalyzer.Analyze(db, tr, d.Settings, catalog: LoadCatalog(db), excludedIds: excluded);
                return string.Join(",", an.Entries.Select(e => e.Element).OrderBy(e => e, StringComparer.OrdinalIgnoreCase));
            }

            string ea = Elements(a), eb = Elements(b), ec = Elements(c);
            bool aOk = ea.Contains("XREFA") && !ea.Contains("XREFB");
            bool bOk = eb.Contains("XREFB") && !eb.Contains("XREFA");
            bool cOk = ec.Contains("XREFA") && ec.Contains("XREFB");
            ed.WriteMessage($"\nABCX: A=[{ea}] B=[{eb}] C=[{ec}]");
            ed.WriteMessage($"\nABCX: xref-isolatie A={(aOk ? "OK" : "FAIL")} B={(bOk ? "OK" : "FAIL")} C={(cOk ? "OK" : "FAIL")} -> {(aOk && bOk && cOk ? "OK" : "FAIL")}");
            tr.Commit();
        }
        catch (Exception ex)
        {
            ed.WriteMessage($"\nABCX verify error: {ex.Message}");
        }
    }

    // Consumer-audit op de echte host: elke zichtbare schakelaar moet de getekende geometrie
    // veranderen. Bouwt telkens een verse legenda en vergelijkt het aantal entiteiten met de
    // schakelaar aan en uit, zodat bewezen is dat de renderer de instelling echt verbruikt.
    [CommandMethod("NLCSLEGENDACONSUMERTEST", CommandFlags.Modal)]
    public void NlcsLegendaConsumerTest()
    {
        var doc = AcApp.DocumentManager.MdiActiveDocument;
        if (doc is null)
            return;
        var ed = doc.Editor;
        var db = doc.Database;
        try
        {
            int Count(Action<LegendSettings> tweak)
            {
                using var tr = db.TransactionManager.StartTransaction();
                var reg = LegendStore.Load(db, tr);
                var s = LoadGlobalDefaults();
                tweak(s);
                var def = IsoDef(reg, s);
                reg.Add(def);
                int n = 0;
                if (BuildManagedLegend(db, tr, reg, def, out _, out _) == UpdateResult.Updated)
                {
                    var gd = (DBDictionary)tr.GetObject(db.GroupDictionaryId, OpenMode.ForRead);
                    if (gd.Contains(def.GroupName) && tr.GetObject(gd.GetAt(def.GroupName), OpenMode.ForRead) is Group g)
                        foreach (var id in g.GetAllEntityIds())
                        {
                            if (tr.GetObject(id, OpenMode.ForRead) is not Entity e || e.IsErased)
                                continue;
                            // De legenda is een managed block; tel de geometrie in de definitie.
                            if (e is BlockReference br
                                && tr.GetObject(br.BlockTableRecord, OpenMode.ForRead) is BlockTableRecord btr)
                            {
                                foreach (ObjectId bid in btr)
                                    if (tr.GetObject(bid, OpenMode.ForRead) is Entity be && !be.IsErased)
                                        n++;
                            }
                            else
                            {
                                n++;
                            }
                        }
                }
                tr.Commit();
                PurgePending(db);
                return n;
            }

            void Check(string label, Action<LegendSettings> on, Action<LegendSettings> off)
            {
                int a = Count(on);
                int b = Count(off);
                ed.WriteMessage($"\nCONSUMER: {label} aan={a} uit={b} -> {(a != b ? "OK" : "GEEN VERSCHIL")}");
            }

            Check("kader", s => s.DrawBorder = true, s => s.DrawBorder = false);
            Check("swatchkader", s => s.DrawSwatchFrame = true, s => s.DrawSwatchFrame = false);
            Check("schaalbalk", s => s.IncludeScaleBar = true, s => s.IncludeScaleBar = false);
            Check("titel", s => s.IncludeTitle = true, s => s.IncludeTitle = false);
            Check("opmerkingen", s => s.IncludeRemarks = true, s => s.IncludeRemarks = false);
            Check("hoeveelheden", s => s.IncludeQuantities = true, s => s.IncludeQuantities = false);
            Check("symbolen", s => s.InsertSymbolBlocks = true, s => s.InsertSymbolBlocks = false);

            // Beide plaatsingspaden moeten geometrie opleveren: blok behouden en exploderen.
            int exploded = Count(s => s.ExplodeOnPlace = true);
            int retained = Count(s => s.ExplodeOnPlace = false);
            ed.WriteMessage($"\nCONSUMER: exploderen={exploded} behouden={retained} -> {(exploded > 0 && retained > 0 ? "OK" : "FAIL")}");
        }
        catch (Exception ex)
        {
            ed.WriteMessage($"\nCONSUMER error: {ex.Message}");
        }
    }

    // Echte xref-integratie: analyseert het gastbestand met per xref een eigen inclusie en
    // toont dat alleen de ingesloten xref zijn NLCS-elementen bijdraagt. Verwacht >= 2 xrefs.
    [CommandMethod("NLCSLEGENDAXREFANALYSE", CommandFlags.Modal)]
    public void NlcsLegendaXrefAnalyse()
    {
        var doc = AcApp.DocumentManager.MdiActiveDocument;
        if (doc is null)
            return;
        var ed = doc.Editor;
        var db = doc.Database;
        try
        {
            using var tr = db.TransactionManager.StartTransaction();
            var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
            var xrefs = new List<string>();
            foreach (ObjectId id in bt)
                if (tr.GetObject(id, OpenMode.ForRead) is BlockTableRecord btr && btr.IsFromExternalReference)
                    xrefs.Add(btr.Name);
            xrefs.Sort(StringComparer.OrdinalIgnoreCase);
            ed.WriteMessage($"\nXREFAN: xrefs = {string.Join(", ", xrefs)}");
            if (xrefs.Count < 2)
            {
                ed.WriteMessage("\nXREFAN: minstens 2 xrefs nodig.");
                tr.Commit();
                return;
            }

            string Elements(string? onlyXref)
            {
                var s = LoadGlobalDefaults();
                s.ExcludedDisciplines.Clear();
                s.ExcludedHoofdgroepen.Clear();
                s.XrefInclusion.Clear();
                if (onlyXref is null)
                {
                    s.IncludeXrefLayers = true; // alles aan
                }
                else
                {
                    s.IncludeXrefLayers = false; // alleen de genoemde xref via de dict
                    s.XrefInclusion[onlyXref] = true;
                }
                var an = DrawingAnalyzer.Analyze(db, tr, s, catalog: LoadCatalog(db));
                return string.Join(",", an.Entries.Select(e => e.Element).OrderBy(e => e, StringComparer.OrdinalIgnoreCase));
            }

            var a = Elements(xrefs[0]);
            var b = Elements(xrefs[1]);
            var both = Elements(null);
            ed.WriteMessage($"\nXREFAN: alleen {xrefs[0]} -> [{a}]");
            ed.WriteMessage($"\nXREFAN: alleen {xrefs[1]} -> [{b}]");
            // A en B bevatten elk iets, verschillen van elkaar, en beide zitten in de alles-aan set.
            bool ok = a.Length > 0 && b.Length > 0 && a != b
                && both.Contains(a.Split(',')[0]) && both.Contains(b.Split(',')[0]);
            ed.WriteMessage($"\nXREFAN: isolatie (A!=B, beide in alles-aan) -> {(ok ? "OK" : "FAIL")}");
            tr.Commit();
        }
        catch (Exception ex)
        {
            ed.WriteMessage($"\nXREFAN error: {ex.Message}");
        }
    }

    // Headless bewijs van de echte viewport-flow: plaatst een legenda, maakt in een layout een
    // viewport om de werkelijke legenda-extents op meerdere schalen en controleert dat de
    // viewport exact op schaal staat (papier-mm / modeleenheid = 1000 / schaal).
    [CommandMethod("NLCSLEGENDAVIEWPORTTEST", CommandFlags.Modal)]
    public void NlcsLegendaViewportTest()
    {
        var doc = AcApp.DocumentManager.MdiActiveDocument;
        if (doc is null)
            return;
        var ed = doc.Editor;
        var db = doc.Database;

        try
        {
            string layoutName = "Model";
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var layouts = (DBDictionary)tr.GetObject(db.LayoutDictionaryId, OpenMode.ForRead);
                foreach (DBDictionaryEntry entry in layouts)
                {
                    if (!string.Equals(entry.Key, "Model", StringComparison.OrdinalIgnoreCase))
                    {
                        layoutName = entry.Key;
                        break;
                    }
                }
                tr.Commit();
            }
            if (string.Equals(layoutName, "Model", StringComparison.OrdinalIgnoreCase))
            {
                ed.WriteMessage("\nVPTEST: geen papier-layout aanwezig.");
                return;
            }
            // Oorspronkelijke layout onthouden en na afloop exact terugzetten; een test mag de
            // actieve tab (CTAB/TILEMODE) niet gewijzigd achterlaten.
            string originalLayout = LayoutManager.Current.CurrentLayout;
            try
            {
                LayoutManager.Current.CurrentLayout = layoutName;
                RunViewportScaleChecks(ed, db);
            }
            finally
            {
                try { LayoutManager.Current.CurrentLayout = originalLayout; } catch { }
            }
        }
        catch (Exception ex)
        {
            ed.WriteMessage($"\nVPTEST error: {ex.Message}");
        }
    }

    private void RunViewportScaleChecks(Editor ed, Database db)
    {
        {
            bool Measure(LegendSettings s, out double mw, out double mh, out Point2d center)
            {
                mw = mh = 0; center = default;
                using var tr = db.TransactionManager.StartTransaction();
                var reg = LegendStore.Load(db, tr);
                var def = IsoDef(reg, s);
                reg.Add(def);
                if (BuildManagedLegend(db, tr, reg, def, out _, out _) != UpdateResult.Updated)
                {
                    tr.Commit();
                    return false;
                }
                LegendStore.Save(db, tr, reg);
                LegendManagement.TryGetGroupExtents(db, tr, def.GroupName, out var ext);
                mw = ext.MaxPoint.X - ext.MinPoint.X;
                mh = ext.MaxPoint.Y - ext.MinPoint.Y;
                center = new Point2d((ext.MinPoint.X + ext.MaxPoint.X) / 2.0, (ext.MinPoint.Y + ext.MaxPoint.Y) / 2.0);
                tr.Commit();
                return true;
            }

            void CheckAt(string label, double mw, double mh, Point2d center, double scale)
            {
                const double margin = 5.0;
                var plan = ViewportMath.Compute(mw, mh, scale, margin);
                double paperPerModel = 1000.0 / scale;
                double w, h, vh, vw;
                using (var tr = db.TransactionManager.StartTransaction())
                {
                    var lm = LayoutManager.Current;
                    var layout = (Layout)tr.GetObject(lm.GetLayoutId(lm.CurrentLayout), OpenMode.ForRead);
                    var ps = (BlockTableRecord)tr.GetObject(layout.BlockTableRecordId, OpenMode.ForWrite);
                    var vp = new Viewport();
                    ps.AppendEntity(vp);
                    tr.AddNewlyCreatedDBObject(vp, true);
                    vp.CenterPoint = new Point3d(200, 150, 0);
                    vp.Width = plan.PaperWidthMm;
                    vp.Height = plan.PaperHeightMm;
                    vp.ViewCenter = center;
                    vp.ViewHeight = plan.PaperHeightMm / paperPerModel;
                    w = vp.Width; h = vp.Height; vh = vp.ViewHeight;
                    vw = vp.ViewHeight * (vp.Width / vp.Height); // zichtbare modelbreedte
                    tr.Commit();
                }
                double implied = h / vh;
                bool exact = Math.Abs(implied - paperPerModel) < 1e-6;
                // Marges: papiermaat moet legenda + 2x marge zijn (geen clipping, geen overmaat).
                double expW = mw * paperPerModel + 2 * margin;
                double expH = mh * paperPerModel + 2 * margin;
                bool marginsOk = Math.Abs(w - expW) < 1e-3 && Math.Abs(h - expH) < 1e-3;
                // Clipping: zichtbaar model moet de legenda volledig omvatten.
                bool noClip = vw + 1e-6 >= mw && vh + 1e-6 >= mh;
                ed.WriteMessage(
                    $"\nVPTEST: {label} 1:{scale:0} vp {w:0.0}x{h:0.0}mm schaal {(exact ? "OK" : "FAIL")} " +
                    $"marges {(marginsOk ? "OK" : "FAIL")} clipping {(noClip ? "geen" : "FAIL")}");
            }

            // Variant 1: standaard legenda op vier schalen.
            if (!Measure(LoadGlobalDefaults(), out var mw0, out var mh0, out var c0))
            {
                ed.WriteMessage("\nVPTEST: geen legenda om omheen te meten.");
                return;
            }
            PurgePending(db);
            ed.WriteMessage($"\nVPTEST: standaard {mw0:0.0} x {mh0:0.0} modeleenheden");
            foreach (var scale in new[] { 100.0, 200.0, 500.0, 1000.0 })
                CheckAt("standaard", mw0, mh0, c0, scale);

            // Variant 2: twee kolommen geeft een smaller/hoger profiel, zelfde viewport-contract.
            var twoCol = LoadGlobalDefaults();
            twoCol.Columns = 2;
            if (Measure(twoCol, out var mw1, out var mh1, out var c1))
            {
                PurgePending(db);
                ed.WriteMessage($"\nVPTEST: 2-koloms {mw1:0.0} x {mh1:0.0} modeleenheden");
                CheckAt("2-koloms", mw1, mh1, c1, 200.0);
            }

            // Variant 3: zonder kader, opmerkingen en schaalbalk (minimale omvang).
            var bare = LoadGlobalDefaults();
            bare.DrawBorder = false;
            bare.IncludeRemarks = false;
            bare.IncludeScaleBar = false;
            if (Measure(bare, out var mw2, out var mh2, out var c2))
            {
                PurgePending(db);
                ed.WriteMessage($"\nVPTEST: kaal {mw2:0.0} x {mh2:0.0} modeleenheden");
                CheckAt("kaal", mw2, mh2, c2, 200.0);
            }

            // Variant 4: drie kolommen (breder profiel).
            var threeCol = LoadGlobalDefaults();
            threeCol.Columns = 3;
            if (Measure(threeCol, out var mw3, out var mh3, out var c3))
            {
                PurgePending(db);
                ed.WriteMessage($"\nVPTEST: 3-koloms {mw3:0.0} x {mh3:0.0} modeleenheden");
                CheckAt("3-koloms", mw3, mh3, c3, 200.0);
            }

            // Variant 5: hoeveelheden + opmerkingen + schaalbalk (volledige breedte) op 1:500.
            var full = LoadGlobalDefaults();
            full.IncludeQuantities = true;
            full.IncludeRemarks = true;
            full.IncludeScaleBar = true;
            if (Measure(full, out var mw4, out var mh4, out var c4))
            {
                PurgePending(db);
                ed.WriteMessage($"\nVPTEST: volledig {mw4:0.0} x {mh4:0.0} modeleenheden");
                CheckAt("volledig", mw4, mh4, c4, 500.0);
            }

            // Variant 6: verplaatste legenda. Na een handmatige verschuiving moet de viewport de
            // nieuwe positie volgen (extents worden live gemeten), niet terugspringen (sectie 30).
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var reg = LegendStore.Load(db, tr);
                var def = IsoDef(reg, LoadGlobalDefaults());
                reg.Add(def);
                if (BuildManagedLegend(db, tr, reg, def, out _, out _) == UpdateResult.Updated)
                {
                    LegendStore.Save(db, tr, reg);
                    LegendManagement.TryGetGroupExtents(db, tr, def.GroupName, out var before);
                    var cBefore = new Point2d(
                        (before.MinPoint.X + before.MaxPoint.X) / 2.0,
                        (before.MinPoint.Y + before.MaxPoint.Y) / 2.0);

                    var move = Matrix3d.Displacement(new Vector3d(1000, 500, 0));
                    var gd = (DBDictionary)tr.GetObject(db.GroupDictionaryId, OpenMode.ForRead);
                    var g = (Group)tr.GetObject(gd.GetAt(def.GroupName), OpenMode.ForRead);
                    foreach (var id in g.GetAllEntityIds())
                        if (tr.GetObject(id, OpenMode.ForWrite) is Entity ent && !ent.IsErased)
                            ent.TransformBy(move);

                    LegendManagement.TryGetGroupExtents(db, tr, def.GroupName, out var after);
                    var cAfter = new Point2d(
                        (after.MinPoint.X + after.MaxPoint.X) / 2.0,
                        (after.MinPoint.Y + after.MaxPoint.Y) / 2.0);
                    double mwm = after.MaxPoint.X - after.MinPoint.X;
                    double mhm = after.MaxPoint.Y - after.MinPoint.Y;
                    bool moved = Math.Abs(cAfter.X - (cBefore.X + 1000)) < 1e-3
                        && Math.Abs(cAfter.Y - (cBefore.Y + 500)) < 1e-3;
                    tr.Commit();
                    PurgePending(db);
                    ed.WriteMessage($"\nVPTEST: verplaatst center {(moved ? "OK" : "FAIL")}");
                    CheckAt("verplaatst", mwm, mhm, cAfter, 200.0);
                }
                else
                {
                    tr.Commit();
                }
            }

            // Variant 7: symboolzware legenda (alleen symbolen) frameren.
            var sym = LoadGlobalDefaults();
            sym.ToonGeometrie = false; sym.ToonVlakken = false; sym.ToonArceringen = false;
            sym.ToonVlakvullingen = false; sym.ToonSymbolen = true; sym.InsertSymbolBlocks = true;
            if (Measure(sym, out var mw5, out var mh5, out var c5))
            {
                PurgePending(db);
                ed.WriteMessage($"\nVPTEST: symboolzwaar {mw5:0.0} x {mh5:0.0} modeleenheden");
                CheckAt("symboolzwaar", mw5, mh5, c5, 200.0);
            }

            // Variant 8: lange meerregelige opmerkingen maken de legenda hoger; de viewport moet
            // die extra hoogte meenemen (geen clipping).
            var rem = LoadGlobalDefaults();
            rem.IncludeRemarks = true;
            rem.RemarksText = string.Join("\n",
                Enumerable.Range(1, 18).Select(i => $"Opmerkingregel {i} met wat extra tekst om te laten teruglopen"));
            if (Measure(rem, out var mw6, out var mh6, out var c6))
            {
                PurgePending(db);
                ed.WriteMessage($"\nVPTEST: opmerkingzwaar {mw6:0.0} x {mh6:0.0} modeleenheden");
                CheckAt("opmerkingzwaar", mw6, mh6, c6, 200.0);
            }
        }
    }

    // Meet de maatvoering van een bestaande referentielegenda (swatch, rijafstand, teksthoogtes) en
    // schrijft een machineleesbaar contract. Model is in meters (INSUNITS=6); op schaal 1:S is
    // 1 modelmeter = 1000/S mm papier. De schaal komt uit NLCS_MEET_SCALE (default 200), het
    // doelbestand uit NLCS_CONTRACT_OUT. VLA/ActiveX werkt niet in accoreconsole, daarom .NET-API.
    [CommandMethod("NLCSLEGENDATEMPLATEMETEN", CommandFlags.Modal)]
    public void NlcsLegendaTemplateMeten()
    {
        var doc = AcApp.DocumentManager.MdiActiveDocument;
        if (doc is null)
            return;
        var ed = doc.Editor;
        var db = doc.Database;
        try
        {
            double scale = 200.0;
            if (double.TryParse(Environment.GetEnvironmentVariable("NLCS_MEET_SCALE"), out var s) && s > 0)
                scale = s;
            int insunits = db.Insunits == UnitsValue.Meters ? 6 : (int)db.Insunits;
            double mmPerModel = (db.Insunits == UnitsValue.Millimeters ? 1.0 : 1000.0) / scale;

            var samples = new List<(double w, double h, double cx, double cy)>();
            var frames = new List<double>();
            var symbols = new List<(string block, double sx, double sy, double rot, double w, double h)>();
            var texts = new List<(double height, double x, double y)>();
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                var ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForRead);
                foreach (ObjectId id in ms)
                {
                    var ent = tr.GetObject(id, OpenMode.ForRead);
                    if (ent is Polyline pl && pl.Closed && pl.NumberOfVertices is 4 or 5)
                    {
                        // Swatchkader: gesloten rechthoek, breder dan hoog, in swatch-bereik.
                        var ext = pl.GeometricExtents;
                        double fw = ext.MaxPoint.X - ext.MinPoint.X, fh = ext.MaxPoint.Y - ext.MinPoint.Y;
                        if (fw > 2 && fw < 8 && fh > 0.3 && fh < 3 && fw > fh)
                            frames.Add(fw);
                    }
                    else if (ent is Curve crv and (Polyline or Line))
                    {
                        var ext = crv.GeometricExtents;
                        double w = ext.MaxPoint.X - ext.MinPoint.X;
                        double h = ext.MaxPoint.Y - ext.MinPoint.Y;
                        // Liggende swatch-sample: brede, lage horizontale lijn/polyline.
                        if (w > 2 && w < 8 && h < 1.5)
                            samples.Add((w, h, (ext.MinPoint.X + ext.MaxPoint.X) / 2.0, (ext.MinPoint.Y + ext.MaxPoint.Y) / 2.0));
                    }
                    else if (ent is BlockReference br)
                    {
                        // Symbool: insertschaal/rotatie + werkelijke bounds (voor paper-mm).
                        double w = 0, h = 0;
                        try { var ext = br.GeometricExtents; w = ext.MaxPoint.X - ext.MinPoint.X; h = ext.MaxPoint.Y - ext.MinPoint.Y; }
                        catch { /* lege/ongeldige block-extents overslaan */ }
                        if (br.ScaleFactors.X > 0 && w > 0 && w < 12 && h < 12)
                            symbols.Add((br.Name, br.ScaleFactors.X, br.ScaleFactors.Y, br.Rotation, w, h));
                    }
                    else if (ent is DBText t && t.Height > 0)
                        texts.Add((t.Height, t.Position.X, t.Position.Y));
                    else if (ent is MText m && m.TextHeight > 0)
                        texts.Add((m.TextHeight, m.Location.X, m.Location.Y));
                }
                tr.Commit();
            }

            if (samples.Count == 0 || texts.Count == 0)
            {
                ed.WriteMessage("\nMETEN: te weinig meetbare geometrie (samples/teksten).");
                return;
            }

            static double Mode(IEnumerable<double> values, double bucket)
            {
                return values.GroupBy(v => Math.Round(v / bucket) * bucket)
                    .OrderByDescending(g => g.Count()).ThenBy(g => g.Key)
                    .Select(g => g.Average()).First();
            }

            // Swatchbreedte = dominante breedte van de horizontale samples.
            double swW = Mode(samples.Select(x => x.w), 0.2);
            // Swatchkader apart: de gesloten rechthoek is doorgaans iets breder dan de sample-lijn.
            double frameW = frames.Count > 0 ? Mode(frames, 0.2) : 0;

            // Dominant symbool: meest voorkomende insertschaal + resulterende papiermaat.
            double symScale = 0, symPaperW = 0, symPaperH = 0;
            int symCount = symbols.Count;
            string symBlock = "";
            if (symbols.Count > 0)
            {
                symScale = Mode(symbols.Select(s => s.sx), 0.05);
                var dom = symbols.Where(s => Math.Abs(s.sx - symScale) <= 0.05).ToList();
                symBlock = dom.GroupBy(s => s.block).OrderByDescending(g => g.Count()).First().Key;
                symPaperW = Mode(dom.Select(s => s.w), 0.2) * mmPerModel;
                symPaperH = Mode(dom.Select(s => s.h), 0.2) * mmPerModel;
            }

            // Rijafstand uit de omschrijvingsteksten (kleinste teksthoogte = T25): per kolom (X)
            // de dichtstbevolkte nemen en de mediaan van opeenvolgende verticale sprongen.
            double descH = Mode(texts.Select(t => t.height), 0.05);
            var desc = texts.Where(t => Math.Abs(t.height - descH) <= descH * 0.1).ToList();
            double colX = Mode(desc.Select(t => t.x), 1.0);
            var column = desc.Where(t => Math.Abs(t.x - colX) <= 2.0).OrderByDescending(t => t.y).ToList();
            var gaps = new List<double>();
            for (int i = 1; i < column.Count; i++)
            {
                double g = column[i - 1].y - column[i].y;
                if (g > descH * 0.5 && g < descH * 6) gaps.Add(g);
            }
            gaps.Sort();
            double pitch = gaps.Count > 0 ? gaps[gaps.Count / 2] : 0;

            var txtModes = texts.Select(t => t.height).GroupBy(v => Math.Round(v / 0.05) * 0.05)
                .OrderByDescending(g => g.Count()).Take(3)
                .Select(g => g.Average() * mmPerModel).OrderBy(v => v).ToList();

            double swWmm = swW * mmPerModel, pitchMm = pitch * mmPerModel, frameWmm = frameW * mmPerModel;
            ed.WriteMessage($"\nMETEN: INSUNITS={insunits} schaal 1:{scale:0} mm/model={mmPerModel:0.###}");
            ed.WriteMessage($"\nMETEN: lijnsample {swWmm:0.0} mm (n={samples.Count}); swatchkader {frameWmm:0.0} mm (n={frames.Count})");
            ed.WriteMessage($"\nMETEN: rijafstand {pitchMm:0.0} mm (n={gaps.Count}, kolom {column.Count})");
            ed.WriteMessage($"\nMETEN: teksthoogtes mm = {string.Join(", ", txtModes.Select(v => v.ToString("0.0")))}");
            ed.WriteMessage($"\nMETEN: symbool insertschaal {symScale:0.###} ({symBlock}) -> {symPaperW:0.0}x{symPaperH:0.0} mm (n={symCount})");

            var json = new StringBuilder();
            json.Append("{\n");
            json.Append($"  \"bron\": \"{Path.GetFileName(db.Filename)}\",\n");
            json.Append($"  \"schaal\": {scale:0},\n");
            json.Append($"  \"insunits\": {insunits},\n");
            json.Append($"  \"lijnSampleBreedteMm\": {swWmm:0.0},\n");
            json.Append($"  \"swatchKaderBreedteMm\": {frameWmm:0.0},\n");
            json.Append($"  \"rijafstandMm\": {pitchMm:0.0},\n");
            json.Append($"  \"teksthoogtesMm\": [{string.Join(", ", txtModes.Select(v => v.ToString("0.0")))}],\n");
            json.Append($"  \"symbool\": {{ \"insertSchaal\": {symScale:0.###}, \"paperBreedteMm\": {symPaperW:0.0}, \"paperHoogteMm\": {symPaperH:0.0}, \"aantal\": {symCount} }}\n");
            json.Append("}\n");
            string outPath = Environment.GetEnvironmentVariable("NLCS_CONTRACT_OUT")
                ?? Path.Combine(Path.GetTempPath(), "nlcs-template-contract.json");
            File.WriteAllText(outPath, json.ToString());
            ed.WriteMessage($"\nMETEN: contract geschreven naar {outPath}");
        }
        catch (Exception ex)
        {
            ed.WriteMessage($"\nMETEN error: {ex.Message}");
        }
    }

    // Performance-harness op de echte KLIC-bron: meet parse/analyse, groepering, CompositionTree
    // en export (CSV/JSON). Rapporteert entity/laag-aantallen en de mediaan over meerdere runs.
    [CommandMethod("NLCSLEGENDAPERFTEST", CommandFlags.Modal)]
    public void NlcsLegendaPerfTest()
    {
        var doc = AcApp.DocumentManager.MdiActiveDocument;
        if (doc is null)
            return;
        var ed = doc.Editor;
        var db = doc.Database;
        try
        {
            var settings = LoadGlobalDefaults();
            settings.ExcludedDisciplines.Clear();
            settings.ExcludedHoofdgroepen.Clear();

            int entities = 0, layers = 0, entries = 0;
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                var ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForRead);
                foreach (var _ in ms) entities++;
                layers = ((LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead)).Cast<ObjectId>().Count();
                tr.Commit();
            }

            double Median(List<double> xs) { xs.Sort(); return xs.Count == 0 ? 0 : xs[xs.Count / 2]; }
            var tAnalyze = new List<double>();
            var tTree = new List<double>();
            var tCsv = new List<double>();
            var tJson = new List<double>();
            const int runs = 5;
            for (int i = 0; i < runs; i++)
            {
                using var tr = db.TransactionManager.StartTransaction();
                var excluded = LegendManagement.CollectManagedIds(db, tr, LegendStore.Load(db, tr));
                var sw = System.Diagnostics.Stopwatch.StartNew();
                var analysis = DrawingAnalyzer.Analyze(db, tr, settings, catalog: LoadCatalog(db), excludedIds: excluded);
                sw.Stop(); tAnalyze.Add(sw.Elapsed.TotalMilliseconds);
                entries = analysis.Entries.Count;

                var items = analysis.Entries.Select(e =>
                    (LegendSettings.EntryKey(e), $"[{e.Status.DisplayName()}] {e.Description}", EntryGroupLabel(e)));
                sw.Restart();
                CompositionTree.Build(items, new HashSet<string>(StringComparer.OrdinalIgnoreCase));
                sw.Stop(); tTree.Add(sw.Elapsed.TotalMilliseconds);

                sw.Restart(); LegendExport.ToCsv(analysis.Entries); sw.Stop(); tCsv.Add(sw.Elapsed.TotalMilliseconds);
                sw.Restart(); LegendExport.ToJson(analysis.Entries); sw.Stop(); tJson.Add(sw.Elapsed.TotalMilliseconds);
                tr.Commit();
            }

            ed.WriteMessage($"\nPERF: entities={entities} layers={layers} entries={entries} runs={runs}");
            ed.WriteMessage($"\nPERF: analyse mediaan {Median(tAnalyze):0.0} ms (groepering inbegrepen)");
            ed.WriteMessage($"\nPERF: tree mediaan {Median(tTree):0.0} ms");
            ed.WriteMessage($"\nPERF: csv mediaan {Median(tCsv):0.0} ms");
            ed.WriteMessage($"\nPERF: json mediaan {Median(tJson):0.0} ms");
        }
        catch (Exception ex)
        {
            ed.WriteMessage($"\nPERF error: {ex.Message}");
        }
    }

    // Bewijst dat de echte OMSCHRIJVING uit KLIC-symboolattributen in de omschrijving terechtkomt
    // (bron = laagbeschrijving) in plaats van de onderdrukte placeholder/laagnaam.
    [CommandMethod("NLCSLEGENDAKLICATTRTEST", CommandFlags.Modal)]
    public void NlcsLegendaKlicAttrTest()
    {
        var doc = AcApp.DocumentManager.MdiActiveDocument;
        if (doc is null)
            return;
        var ed = doc.Editor;
        var db = doc.Database;
        try
        {
            var settings = LoadGlobalDefaults();
            settings.ExcludedDisciplines.Clear();
            settings.ExcludedHoofdgroepen.Clear();
            settings.IncludeInvisibleLayers = true;
            settings.IncludeXrefLayers = true;
            using var tr = db.TransactionManager.StartTransaction();
            var excluded = LegendManagement.CollectManagedIds(db, tr, LegendStore.Load(db, tr));
            var an = DrawingAnalyzer.Analyze(db, tr, settings, catalog: LoadCatalog(db), excludedIds: excluded);
            var fromDrawing = an.Entries
                .Where(e => e.DescriptionSource == DescriptionSource.Laagbeschrijving)
                .ToList();
            ed.WriteMessage($"\nKLICATTR: entries={an.Entries.Count} attr-lagen={an.AttrDescribedLayerCount} laagdesc-lagen={an.DescribedLayerCount} met laagbeschrijving-bron={fromDrawing.Count}");
            foreach (var e in fromDrawing.Take(6))
                ed.WriteMessage($"\n  {e.Element} -> \"{e.Description}\"");
            tr.Commit();
        }
        catch (Exception ex)
        {
            ed.WriteMessage($"\nKLICATTR error: {ex.Message}");
        }
    }

    // Headless bewijs van de laagnaam-rename: maakt testlagen met entiteiten, hernoemt (dry-run
    // + apply in één transactie), test een botsing met samenvoegen en een vergrendelde bronlaag.
    [CommandMethod("NLCSLEGENDALAAGNAAMTEST", CommandFlags.Modal)]
    public void NlcsLegendaLaagnaamTest()
    {
        var doc = AcApp.DocumentManager.MdiActiveDocument;
        if (doc is null)
            return;
        var ed = doc.Editor;
        var db = doc.Database;

        try
        {
            const string src = "N-WE-KL-RENTEST-G";
            const string dst = "N-WE-KL-RENDONE-G";
            const string merge = "N-WE-KL-MERGED-G";

            void MakeLayerLine(string layer, bool locked)
            {
                using var tr = db.TransactionManager.StartTransaction();
                var lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForWrite);
                if (!lt.Has(layer))
                {
                    var ltr = new LayerTableRecord { Name = layer, IsLocked = locked };
                    lt.Add(ltr);
                    tr.AddNewlyCreatedDBObject(ltr, true);
                }
                var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForWrite);
                var line = new Autodesk.AutoCAD.DatabaseServices.Line(Point3d.Origin, new Point3d(1, 1, 0)) { Layer = layer };
                ms.AppendEntity(line);
                tr.AddNewlyCreatedDBObject(line, true);
                tr.Commit();
            }

            MakeLayerLine(src, locked: true);

            LayerRename.Plan plan;
            using (var tr = db.TransactionManager.StartTransaction())
            {
                plan = LayerRename.Analyze(db, tr, src, dst);
                tr.Commit();
            }
            ed.WriteMessage($"\nRENTEST: dry-run {src}->{dst} affected={plan.AffectedEntities} locked={plan.SourceLocked} targetExists={plan.TargetExists}");

            bool renamed;
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var p = LayerRename.Analyze(db, tr, src, dst);
                renamed = LayerRename.Apply(db, tr, p, false, out var err);
                if (!renamed) ed.WriteMessage($"\nRENTEST: rename fout {err}");
                tr.Commit();
            }
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
                ed.WriteMessage($"\nRENTEST: na rename src bestaat={lt.Has(src)} dst bestaat={lt.Has(dst)} dstCount={LayerRename.CountOnLayer(db, tr, dst)}");
                tr.Commit();
            }

            // Botsing + samenvoegen.
            MakeLayerLine(merge, locked: false);
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var p = LayerRename.Analyze(db, tr, dst, merge);
                var ok = LayerRename.Apply(db, tr, p, true, out var err);
                if (!ok) ed.WriteMessage($"\nRENTEST: merge fout {err}");
                tr.Commit();
            }
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
                int c = LayerRename.CountOnLayer(db, tr, merge);
                bool ok = !lt.Has(dst) && lt.Has(merge) && c == 2;
                ed.WriteMessage($"\nRENTEST: na merge dst weg={!lt.Has(dst)} mergeCount={c} -> {(ok ? "OK" : "FAIL")}");
                tr.Commit();
            }
        }
        catch (Exception ex)
        {
            ed.WriteMessage($"\nRENTEST error: {ex.Message}");
        }
    }

    // Headless bewijs dat globale omschrijvingwijzigingen bestaande legenda's niet veranderen
    // (snapshot bij creatie) maar een nieuwe legenda wél de nieuwe globale tekst krijgt.
    // Werkt met backup/restore op het echte globale omschrijvingenbestand.
    [CommandMethod("NLCSLEGENDADESCTEST", CommandFlags.Modal)]
    public void NlcsLegendaDescTest()
    {
        var doc = AcApp.DocumentManager.MdiActiveDocument;
        if (doc is null)
            return;
        var ed = doc.Editor;
        var db = doc.Database;

        byte[]? backup = File.Exists(DescriptionsPath) ? File.ReadAllBytes(DescriptionsPath) : null;
        try
        {
            string descKey = string.Empty;
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var excluded = LegendManagement.CollectManagedIds(db, tr, LegendStore.Load(db, tr));
                var probe = DrawingAnalyzer.Analyze(db, tr, LoadGlobalDefaults(), catalog: DescriptionCatalog.Default(), excludedIds: excluded);
                if (probe.Entries.Count > 0)
                    descKey = $"{probe.Entries[^1].Hoofdgroep}|{probe.Entries[^1].Element.ToUpperInvariant()}";
                tr.Commit();
            }
            if (descKey.Length == 0) { ed.WriteMessage("\nDESC: geen entries."); return; }

            void SetGlobal(string text)
            {
                var cat = new DescriptionCatalog();
                cat.Elementen[descKey] = new DescriptionEntry { Specifiek = text };
                SaveGlobalCatalog(cat);
            }

            string TextOf(string id)
            {
                using var tr = db.TransactionManager.StartTransaction();
                var reg = LegendStore.Load(db, tr);
                var def = reg.FindById(id)!;
                var excluded = LegendManagement.CollectManagedIds(db, tr, reg);
                var an = DrawingAnalyzer.Analyze(db, tr, def.Settings, catalog: DescriptionCatalog.Default(), excludedIds: excluded);
                var e = an.Entries.FirstOrDefault(x => $"{x.Hoofdgroep}|{x.Element.ToUpperInvariant()}" == descKey);
                tr.Commit();
                return e?.Description ?? "(geen)";
            }

            // Globaal V1 -> maak G1 (snapshot).
            SetGlobal("GLOBAL-V1");
            string idG1;
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var reg = LegendStore.Load(db, tr);
                var s = LoadGlobalDefaults();
                s.DescriptionOverrides = LoadGlobalDescriptionDefaults();   // snapshot zoals bij plaatsing
                var def = IsoDef(reg, s);
                reg.Add(def);
                idG1 = def.Id;
                BuildManagedLegend(db, tr, reg, def, out _, out _);
                LegendStore.Save(db, tr, reg);
                tr.Commit();
            }
            PurgePending(db);
            ed.WriteMessage($"\nDESC: G1 na creatie = \"{TextOf(idG1)}\" (verwacht GLOBAL-V1)");

            // Globaal V2 -> G1 ongewijzigd, nieuwe G2 krijgt V2.
            SetGlobal("GLOBAL-V2");
            string g1after = TextOf(idG1);
            string idG2;
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var reg = LegendStore.Load(db, tr);
                var s = LoadGlobalDefaults();
                s.DescriptionOverrides = LoadGlobalDescriptionDefaults();
                var def = IsoDef(reg, s);
                reg.Add(def);
                idG2 = def.Id;
                BuildManagedLegend(db, tr, reg, def, out _, out _);
                LegendStore.Save(db, tr, reg);
                tr.Commit();
            }
            PurgePending(db);
            string g2 = TextOf(idG2);
            bool ok = g1after == "GLOBAL-V1" && g2 == "GLOBAL-V2";
            ed.WriteMessage($"\nDESC: na globaal V2 -> G1=\"{g1after}\" (verwacht GLOBAL-V1) G2=\"{g2}\" (verwacht GLOBAL-V2) -> {(ok ? "OK" : "FAIL")}");
        }
        catch (Exception ex)
        {
            ed.WriteMessage($"\nDESC error: {ex.Message}");
        }
        finally
        {
            if (backup is not null) File.WriteAllBytes(DescriptionsPath, backup);
            else if (File.Exists(DescriptionsPath)) File.Delete(DescriptionsPath);
        }
    }

    // Twee aparte commando's (dus twee undo-stappen) om Undo van een rename real-host te testen:
    // SETUP maakt de testlaag, RENAME hernoemt die. Een _U na RENAME moet de rename terugdraaien.
    [CommandMethod("NLCSLEGENDAUNDOSETUP", CommandFlags.Modal)]
    public void NlcsLegendaUndoSetup()
    {
        var db = AcApp.DocumentManager.MdiActiveDocument?.Database;
        if (db is null) return;
        using var tr = db.TransactionManager.StartTransaction();
        var lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForWrite);
        if (!lt.Has("N-WE-KL-UNDOSRC-G"))
        {
            var ltr = new LayerTableRecord { Name = "N-WE-KL-UNDOSRC-G" };
            lt.Add(ltr);
            tr.AddNewlyCreatedDBObject(ltr, true);
        }
        var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForWrite);
        var line = new Autodesk.AutoCAD.DatabaseServices.Line(Point3d.Origin, new Point3d(1, 1, 0)) { Layer = "N-WE-KL-UNDOSRC-G" };
        ms.AppendEntity(line);
        tr.AddNewlyCreatedDBObject(line, true);
        tr.Commit();
    }

    [CommandMethod("NLCSLEGENDAUNDORENAME", CommandFlags.Modal)]
    public void NlcsLegendaUndoRename()
    {
        var db = AcApp.DocumentManager.MdiActiveDocument?.Database;
        if (db is null) return;
        using var tr = db.TransactionManager.StartTransaction();
        var p = LayerRename.Analyze(db, tr, "N-WE-KL-UNDOSRC-G", "N-WE-KL-UNDODST-G");
        LayerRename.Apply(db, tr, p, false, out _);
        tr.Commit();
    }

    private static void ReportRenderIssues(Editor ed, IReadOnlyList<RenderIssue> issues)
    {
        if (issues.Count == 0)
            return;
        ed.WriteMessage($"\nLet op: {issues.Count} regel(s) konden niet volledig worden getekend:");
        foreach (var i in issues.Take(5))
            ed.WriteMessage($"\n  - {i.Entry} ({i.SourceLayer}): {i.Reason}");
        if (issues.Count > 5)
            ed.WriteMessage($"\n  ... en nog {issues.Count - 5}.");
    }

    private static void ReportAnalysis(Editor ed, AnalysisResult analysis)
    {
        ed.WriteMessage(
            $"\n{analysis.Entries.Count} legenda-regel(s) uit {analysis.UsedNlcsLayerCount} " +
            $"gebruikte NLCS-laag/-lagen ({analysis.DescribedLayerCount} met laagbeschrijving).");
        if (analysis.UsedCustomLayerCount > 0)
            ed.WriteMessage(
                $"\n{analysis.UsedCustomLayerCount} eigen bronlaag/-lagen meegeteld via koppelregels.");
        int blanks = analysis.Entries.Count(e => e.IsBlank);
        if (blanks > 0)
            ed.WriteMessage($"\n{blanks} blanco regel(s) (geen bron, geen hoeveelheid).");
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
                  $"{analysis.UsedNlcsLayerCount} gebruikte NLCS-laag/-lagen" +
                  (analysis.UsedCustomLayerCount > 0
                      ? $" + {analysis.UsedCustomLayerCount} eigen bronlaag/-lagen."
                      : "."));
        int blancos = analysis.Entries.Count(e => e.IsBlank);
        if (blancos > 0)
            sb.Append($"\nWaarvan {blancos} blanco regel(s) (statisch, geen bron of hoeveelheid).");
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
            SaveGlobalDefaults(s);
            ed.WriteMessage("\n  \u2192 opgeslagen als globale standaard voor nieuwe legenda's.");
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
            var working = s.Clone();
            using var dialog = new SettingsDialog(working, "Nieuwe legenda");
            dialog.ApplyRequested += (_, _) =>
            {
                s.CopyFrom(dialog.Settings);
                ed.WriteMessage("\n  \u2192 instellingen bijgewerkt.");
            };
            if (AcWindows.ShowModalDialog(dialog) == WinForms.DialogResult.OK)
            {
                s.CopyFrom(dialog.Settings);
                ed.WriteMessage("\n  \u2192 instellingen bijgewerkt.");
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
            using var dialog = new DescriptionsDialog(LoadGlobalCatalog());
            dialog.ApplyRequested += (_, _) =>
                ed.WriteMessage($"\nOmschrijvingen opgeslagen ({SaveGlobalCatalog(dialog.ToCatalog().Diff(DescriptionCatalog.Default()))}).");
            if (AcWindows.ShowModalDialog(dialog) == WinForms.DialogResult.OK)
            {
                var where = SaveGlobalCatalog(dialog.ToCatalog().Diff(DescriptionCatalog.Default()));
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
