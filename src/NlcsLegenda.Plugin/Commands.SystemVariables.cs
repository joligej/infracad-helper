using System.IO;
using Autodesk.AutoCAD.Runtime;
using NlcsLegenda.Core;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Core.Application;

namespace NlcsLegenda.Plugin;

public partial class Commands
{
    private static readonly string[] AuditedSystemVariables =
    {
        "FILEDIA", "CMDDIA", "SECURELOAD", "PICKFIRST", "PICKSTYLE", "CTAB", "TILEMODE",
        "CLAYER", "OSMODE", "CMDECHO", "ATTDIA", "ATTREQ", "EXPERT", "SNAPMODE", "ORTHOMODE"
    };

    private static Dictionary<string, object> CaptureSystemVariables() => AuditedSystemVariables.ToDictionary(
        name => name, name => AcApp.GetSystemVariable(name)
            ?? throw new InvalidOperationException($"Systeemvariabele {name} is niet leesbaar."));

    // Alleen voor de diagnostiek: ook bij een mislukte proef blijven de oorspronkelijke typen/waarden intact.
    private static void RestoreSystemVariables(Dictionary<string, object> values)
    {
        var failures = new List<string>();
        foreach (var pair in values)
        {
            try
            {
                if (!Equals(pair.Value, AcApp.GetSystemVariable(pair.Key)))
                    AcApp.SetSystemVariable(pair.Key, pair.Value);
                if (!Equals(pair.Value, AcApp.GetSystemVariable(pair.Key)))
                    failures.Add(pair.Key);
            }
            catch (System.Exception ex) { failures.Add($"{pair.Key}: {ex.Message}"); }
        }
        if (failures.Count > 0)
            throw new InvalidOperationException("Systeemvariabelen niet hersteld: " + string.Join(", ", failures));
    }

    [CommandMethod("NLCSLEGENDASYSVARTEST", CommandFlags.Modal)]
    public void NlcsLegendaSysvarTest()
    {
        var doc = AcApp.DocumentManager.MdiActiveDocument;
        if (doc is null) return;
        var db = doc.Database;
        var ed = doc.Editor;
        Dictionary<string, object>? original = null;
        string temp = Path.Join(Path.GetTempPath(), "nlcs_sysvars_" + Guid.NewGuid().ToString("N"));
        int checks = 0;
        bool passed = false;
        void Check(string name, Action operation)
        {
            var before = CaptureSystemVariables();
            try { operation(); }
            finally
            {
                var after = CaptureSystemVariables();
                var changed = before.Where(p => !Equals(p.Value, after[p.Key])).Select(p =>
                    $"{p.Key}: {p.Value} -> {after[p.Key]}").ToArray();
                if (changed.Length > 0)
                    throw new InvalidOperationException($"{name}: {string.Join(", ", changed)}");
            }
            checks++;
            ed.WriteMessage($"\nSYSVAR {name}: FILEDIA={before["FILEDIA"]}, SECURELOAD={before["SECURELOAD"]}; 15 waarden gelijk -> OK");
        }
        try
        {
            original = CaptureSystemVariables();
            Directory.CreateDirectory(temp);
            NlcsLegendaAuditFixture();
            foreach (short filedia in new short[] { 0, 1 })
            {
                AcApp.SetSystemVariable("FILEDIA", filedia);
                AcApp.SetSystemVariable("SECURELOAD", (short)(filedia + 1));
                if (!Equals(AcApp.GetSystemVariable("FILEDIA"), filedia)
                    || Convert.ToInt32(AcApp.GetSystemVariable("SECURELOAD")) != filedia + 1)
                    throw new InvalidOperationException("Sentinelwaarden konden niet worden ingesteld.");
                var settings = new LegendSettings();
                string definitionId = string.Empty;
                Check("analyse", () =>
                {
                    using var tr = db.TransactionManager.StartTransaction();
                    if (DrawingAnalyzer.Analyze(db, tr, settings).Entries.Count == 0)
                        throw new InvalidOperationException("De proef bevat geen bronregels.");
                    tr.Commit();
                });
                Check("plaatsen", () =>
                {
                    using var tr = db.TransactionManager.StartTransaction();
                    var registry = LegendStore.Load(db, tr);
                    var definition = IsoDef(registry, settings);
                    definitionId = definition.Id;
                    registry.Add(definition);
                    if (BuildManagedLegend(db, tr, registry, definition, out var rows, out _) != UpdateResult.Updated || rows == 0)
                        throw new InvalidOperationException("Testlegenda is niet gebouwd.");
                    LegendStore.Save(db, tr, registry);
                    tr.Commit();
                    PurgePending(db);
                });
                Check("bijwerken", () =>
                {
                    using var tr = db.TransactionManager.StartTransaction();
                    var registry = LegendStore.Load(db, tr);
                    var definition = registry.Legends.Single(d => d.Id == definitionId);
                    if (BuildManagedLegend(db, tr, registry, definition, out var rows, out _) != UpdateResult.Updated || rows == 0)
                        throw new InvalidOperationException("Testlegenda is niet bijgewerkt.");
                    LegendStore.Save(db, tr, registry);
                    tr.Commit();
                    PurgePending(db);
                });
                Check("export", () =>
                {
                    using var tr = db.TransactionManager.StartTransaction();
                    var analysis = DrawingAnalyzer.Analyze(db, tr, settings);
                    File.WriteAllText(Path.Join(temp, "regels.csv"), LegendExport.ToCsv(analysis.Entries));
                    File.WriteAllText(Path.Join(temp, "regels.json"), LegendExport.ToJson(analysis.Entries));
                    tr.Commit();
                });
                Check("eigen laag en blanco", () =>
                {
                    var custom = settings.Clone();
                    custom.CustomLayerRules.Add(new CustomLayerRule { Layer = "Sysvar kabel", Element = "Sysvar", Type = NlcsDrawType.Geometrie });
                    custom.BlankEntries.Add(new BlankEntry());
                    using var tr = db.TransactionManager.StartTransaction();
                    DrawingAnalyzer.Analyze(db, tr, custom);
                    tr.Commit();
                });
                Check("configuratiebestand", () =>
                {
                    string path = Path.Join(temp, "settings.json");
                    settings.Save(path);
                    LegendSettings.Load(path);
                });
                Check("onleesbare DWG", () =>
                {
                    string path = Path.Join(temp, "invalid.dwg");
                    File.WriteAllText(path, "Geen DWG");
                    if (BatchExport.AnalyzeFile(path, settings, DescriptionCatalog.Default()).Error is null)
                        throw new InvalidOperationException("Ongeldige DWG werd geaccepteerd.");
                });
                Check("mislukte uitvoer", () =>
                {
                    try { File.WriteAllText(temp, LegendExport.ToCsv(Array.Empty<LegendEntry>())); }
                    catch (UnauthorizedAccessException) { return; }
                    catch (IOException) { return; }
                    throw new InvalidOperationException("Schrijven over een map werd niet geweigerd.");
                });
                Check("genest herstel na uitzondering", () =>
                {
                    var outer = CaptureSystemVariables();
                    try
                    {
                        AcApp.SetSystemVariable("FILEDIA", (short)(1 - filedia));
                        var inner = CaptureSystemVariables();
                        try { AcApp.SetSystemVariable("FILEDIA", filedia); throw new OperationCanceledException(); }
                        catch (OperationCanceledException) { }
                        finally { RestoreSystemVariables(inner); }
                        if (!Equals(AcApp.GetSystemVariable("FILEDIA"), (short)(1 - filedia)))
                            throw new InvalidOperationException("Genest herstel verloor de buitenste waarde.");
                    }
                    finally { RestoreSystemVariables(outer); }
                });
                if (HostEnvironment.IsCoreConsole)
                {
                    Check("viewport headless weigering", NlcsLegendaViewport);
                    Check("opties headless weigering", NlcsLegendaOpties);
                    Check("omschrijvingen headless weigering", NlcsLegendaOmschrijvingen);
                    Check("samenstellen headless weigering", NlcsLegendaSamenstellen);
                    Check("tekst headless weigering", NlcsLegendaTekst);
                    Check("laagnaam headless weigering", NlcsLegendaLaagnaam);
                    Check("batch headless weigering", NlcsLegendaBatch);
                    Check("help headless weigering", NlcsLegendaHelp);
                }
            }
            passed = true;
        }
        catch (System.Exception ex) { ed.WriteMessage($"\nSYSVAR FAIL: {ex.Message}"); }
        finally
        {
            try { if (original is not null) RestoreSystemVariables(original); }
            catch (System.Exception ex) { passed = false; ed.WriteMessage($"\nSYSVAR FAIL: {ex.Message}"); }
            try { if (Directory.Exists(temp)) Directory.Delete(temp, recursive: true); }
            catch (IOException ex) { passed = false; ed.WriteMessage($"\nSYSVAR FAIL: tijdelijke uitvoer niet verwijderd: {ex.Message}"); }
            catch (UnauthorizedAccessException ex) { passed = false; ed.WriteMessage($"\nSYSVAR FAIL: tijdelijke uitvoer niet verwijderd: {ex.Message}"); }
        }
        ed.WriteMessage($"\nSYSVAR totaal: {(passed ? "OK" : "FAIL")}; {AuditedSystemVariables.Length} variabelen; {checks} controles; FILEDIA=0/1");
    }
}
