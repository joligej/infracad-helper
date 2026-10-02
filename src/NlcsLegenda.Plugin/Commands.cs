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
            // Nieuwe legenda begint bij de globale standaard. Is de registry nog leeg en staat
            // er oude tekeningconfig, dan vormt die de basis - vóór de opties en vóór het
            // renderen, zodat de geometrie en de opgeslagen snapshot gelijk zijn.
            var settings = LoadGlobalDefaults();
            bool adoptedLegacy = false;
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var reg = LegendStore.Load(db, tr);
                if (reg.Legends.Count == 0)
                {
                    var adoption = LegendManagement.TryReadLegacyStart(
                        db, tr, LoadGlobalDefaults(), out var startSettings, out var legacyMsg);
                    if (adoption == LegendManagement.LegacyAdoption.Adopted)
                    {
                        settings = startSettings;
                        adoptedLegacy = true;
                        ed.WriteMessage("\n" + legacyMsg);
                    }
                    else if (adoption == LegendManagement.LegacyAdoption.Corrupt)
                    {
                        ed.WriteMessage("\n" + legacyMsg);
                    }
                }
                tr.Commit();
            }

            // Onafhankelijke omschrijving-snapshot: leg de huidige globale gebruikersomschrijvingen
            // vast in de legenda, tenzij er al (legacy) overrides zijn overgenomen.
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
                    // Oude tekeningconfig pas nu wissen: atomair met het opslaan van de legenda.
                    if (adoptedLegacy)
                        DrawingStore.Clear(db, tr);
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
            using (var tr0 = db.TransactionManager.StartTransaction())
            {
                var reg0 = LegendStore.Load(db, tr0);
                MaybeMigrate(db, tr0, reg0);
                tr0.Commit();
            }

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
            var db = doc!.Database;
            var drawingSettings = DrawingStore.HasSettings(db);
            var drawingDesc = DrawingStore.HasDescriptions(db);

            ed.WriteMessage(
                $"\nGlobale bestanden{maakStatus} (voor alle tekeningen):" +
                $"\n  {ConfigPath}" +
                $"\n  {DescriptionsPath}");

            // Oude tekeningspecifieke configuratie uit v1.13-v1.15 kan nog in de tekening
            // staan; die wordt alleen nog gelezen voor migratie en kan hier worden gewist.
            if (drawingSettings || drawingDesc)
            {
                ed.WriteMessage(
                    "\nOude tekeningspecifieke configuratie gevonden (uit een vorige versie):" +
                    $"\n  Instellingen: {(drawingSettings ? "ja" : "nee")}" +
                    $"\n  Omschrijvingen: {(drawingDesc ? "ja" : "nee")}");
            }

            if ((drawingSettings || drawingDesc) &&
                AskYesNo(ed, "Oude tekeningspecifieke NLCS-configuratie uit deze tekening wissen?", false))
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
        if (!RequireInteractive(ed, "NLCSLEGENDAOPTIES")) return;

        try
        {
            if (!ResolveMutationTarget(ed, db, out var target)) { ed.WriteMessage("\nGeannuleerd."); return; }
            var settings = GetTargetSettings(db, target);
            using var dialog = new SettingsDialog(settings, target.ContextLabel);
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
        var name = AskPresetName(ed);
        if (name is null)
            return;
        if (LegendPresets.Exists(PresetsDir, name)
            && !AskYesNo(ed, $"Profiel \"{name}\" bestaat al. Overschrijven?", true))
        {
            ed.WriteMessage("\nGeannuleerd.");
            return;
        }
        LegendPresets.Save(PresetsDir, name, LoadGlobalDefaults());
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
        if (!ResolveMutationTarget(ed, db, out var target)) { ed.WriteMessage("\nGeannuleerd."); return; }
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
        var gd = (DBDictionary)tr.GetObject(db.GroupDictionaryId, OpenMode.ForRead);
        var exists = reg.Legends.Count > 0 || gd.Contains(LegendManagement.LegacyGroupName);
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

            int aRows2, bAfter;
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var reg = LegendStore.Load(db, tr);
                var a = reg.FindById(idA)!;
                if (firstKey.Length > 0)
                    a.Settings.ExcludedEntries.Add(firstKey);
                BuildManagedLegend(db, tr, reg, a, out aRows2, out _);
                BuildManagedLegend(db, tr, reg, reg.FindById(idB)!, out bAfter, out _);
                LegendStore.Save(db, tr, reg);
                tr.Commit();
            }
            PurgePending(db);

            bool ok = (firstKey.Length == 0 || aRows2 == aRows - 1) && bAfter == bRows;
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
            string groupName;
            Point3d min, max;
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var reg = LegendStore.Load(db, tr);
                var def = IsoDef(reg, LoadGlobalDefaults());
                reg.Add(def);
                if (BuildManagedLegend(db, tr, reg, def, out _, out _) != UpdateResult.Updated)
                {
                    tr.Commit();
                    ed.WriteMessage("\nVPTEST: geen legenda om omheen te meten.");
                    return;
                }
                LegendStore.Save(db, tr, reg);
                groupName = def.GroupName;
                LegendManagement.TryGetGroupExtents(db, tr, groupName, out var ext);
                min = ext.MinPoint;
                max = ext.MaxPoint;
                tr.Commit();
            }
            PurgePending(db);

            double mw = max.X - min.X, mh = max.Y - min.Y;
            var center = new Point2d((min.X + max.X) / 2.0, (min.Y + max.Y) / 2.0);
            ed.WriteMessage($"\nVPTEST: legenda {mw:0.0} x {mh:0.0} modeleenheden");

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
            LayoutManager.Current.CurrentLayout = layoutName;

            foreach (var scale in new[] { 100.0, 200.0, 500.0, 1000.0 })
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
                    w = vp.Width;
                    h = vp.Height;
                    vh = vp.ViewHeight;
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
                    $"\nVPTEST: 1:{scale:0} vp {w:0.0}x{h:0.0}mm schaal {(exact ? "OK" : "FAIL")} " +
                    $"marges {(marginsOk ? "OK" : "FAIL")} clipping {(noClip ? "geen" : "FAIL")}");
            }
        }
        catch (Exception ex)
        {
            ed.WriteMessage($"\nVPTEST error: {ex.Message}");
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

    // Headless bewijs van veilige legacy-migratie: adoptie leest instellingen + omschrijvingen,
    // lezen wist niets, wissen is atomair (rollback bewaart de oude data), en corrupte kritieke
    // staat wordt niet overgenomen en niet gewist.
    [CommandMethod("NLCSLEGENDAMIGRATIETEST", CommandFlags.Modal)]
    public void NlcsLegendaMigratieTest()
    {
        var doc = AcApp.DocumentManager.MdiActiveDocument;
        if (doc is null)
            return;
        var ed = doc.Editor;
        var db = doc.Database;

        void Setup(string? settingsJson, string? descJson)
        {
            using var tr = db.TransactionManager.StartTransaction();
            DrawingStore.Clear(db, tr);
            if (settingsJson is not null) DrawingStore.Write(db, tr, true, settingsJson);
            if (descJson is not null) DrawingStore.Write(db, tr, false, descJson);
            tr.Commit();
        }

        bool LegacyExists()
        {
            using var tr = db.TransactionManager.StartTransaction();
            var any = DrawingStore.HasAny(db, tr);
            tr.Commit();
            return any;
        }

        try
        {
            // CASE D: instellingen + omschrijvingen. Adoptie leest beide, wist niets.
            Setup("{\"scale\":777}", "{\"elementen\":{\"RI|PUT\":{\"specifiek\":\"LEGACY-PUT\"}}}");
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var ad = LegendManagement.TryReadLegacyStart(db, tr, LoadGlobalDefaults(), out var start, out _);
                ed.WriteMessage($"\nMIG: adopt={ad} scale={start.Scale:0} descOverrides={start.DescriptionOverrides.Elementen.Count}");
                tr.Commit();
            }
            ed.WriteMessage($"\nMIG: na read legacy bestaat={LegacyExists()} (verwacht True)");

            // Atomaire clear bij 'opslaan'.
            using (var tr = db.TransactionManager.StartTransaction()) { DrawingStore.Clear(db, tr); tr.Commit(); }
            ed.WriteMessage($"\nMIG: na clear legacy bestaat={LegacyExists()} (verwacht False)");

            // Rollback: clear binnen een transactie die abort -> legacy overleeft.
            Setup("{\"scale\":42}", null);
            using (var tr = db.TransactionManager.StartTransaction()) { DrawingStore.Clear(db, tr); tr.Abort(); }
            ed.WriteMessage($"\nMIG: na rollback legacy bestaat={LegacyExists()} (verwacht True)");

            // Corrupt: kapotte kritieke staat niet overnemen, niet wissen.
            Setup("{ kapot", null);
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var ad = LegendManagement.TryReadLegacyStart(db, tr, LoadGlobalDefaults(), out _, out _);
                ed.WriteMessage($"\nMIG: corrupt adopt={ad} (verwacht Corrupt)");
                tr.Commit();
            }
            ed.WriteMessage($"\nMIG: corrupt legacy bestaat={LegacyExists()} (verwacht True, niet gewist)");

            using (var tr = db.TransactionManager.StartTransaction()) { DrawingStore.Clear(db, tr); tr.Commit(); }
            ed.WriteMessage("\nMIG: klaar");
        }
        catch (Exception ex)
        {
            ed.WriteMessage($"\nMIG error: {ex.Message}");
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
