using System.IO;
using Autodesk.AutoCAD.Runtime;
using NlcsLegenda.Core;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Core.Application;

namespace NlcsLegenda.Plugin;

public partial class Commands
{
    // Diagnostiek die de globale standaard tijdelijk wijzigt, bewaart beide bestanden.
    private sealed class GlobalSettingsSnapshot : IDisposable
    {
        private readonly bool _directoryExisted = Directory.Exists(ConfigDir);
        private readonly Dictionary<string, (byte[]? Bytes, DateTime Written)> _files =
            new[] { ConfigPath, DescriptionsPath }.ToDictionary(path => path,
                path => (File.Exists(path) ? File.ReadAllBytes(path) : null, File.GetLastWriteTimeUtc(path)));

        public void Dispose()
        {
            var errors = new List<System.Exception>();
            foreach (var file in _files)
            {
                try
                {
                    if (file.Value.Bytes is { } bytes)
                    {
                        File.WriteAllBytes(file.Key, bytes);
                        File.SetLastWriteTimeUtc(file.Key, file.Value.Written);
                    }
                    else if (File.Exists(file.Key)) File.Delete(file.Key);
                }
                catch (System.Exception ex) { errors.Add(ex); }
            }
            if (!_directoryExisted && Directory.Exists(ConfigDir) && !Directory.EnumerateFileSystemEntries(ConfigDir).Any())
                Directory.Delete(ConfigDir);
            if (errors.Count > 0) throw new AggregateException("Globale testinstellingen konden niet worden hersteld.", errors);
        }
    }

    [CommandMethod("NLCSLEGENDAGLOBALSETTINGSTEST", CommandFlags.Modal)]
    public void NlcsLegendaGlobalSettingsTest()
    {
        var doc = AcApp.DocumentManager.MdiActiveDocument;
        if (doc is null) return;
        int failures = 0;
        void Check(string name, bool passed)
        {
            if (!passed) failures++;
            doc.Editor.WriteMessage($"\nGLOBALSETTINGS {name} -> {(passed ? "OK" : "FAIL")}");
        }
        var originals = new[] { ConfigPath, DescriptionsPath }.ToDictionary(p => p,
            p => File.Exists(p) ? File.ReadAllBytes(p) : null);
        bool existed = Directory.Exists(ConfigDir);
        string temp = Path.Combine(Path.GetTempPath(), "nlcs_preset_" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(ConfigDir);
            var settings = new LegendSettings();
            settings.DescriptionOverrides.Elementen["VH|SETTING"] = new DescriptionEntry { Specifiek = "Uit instellingen" };
            settings.Save(ConfigPath);
            var catalog = new DescriptionCatalog();
            catalog.Elementen["VH|CATALOG"] = new DescriptionEntry { Specifiek = "Uit omschrijvingen" };
            catalog.Save(DescriptionsPath);
            var snapshot = GetTargetSettings(doc.Database, MutationTarget.Global);
            LegendPresets.Save(temp, "snapshot", snapshot);
            var loaded = LegendPresets.Load(temp, "snapshot")!;
            Check("globaal profiel bevat beide tekstbronnen", loaded.DescriptionOverrides.Elementen.Count == 2
                && loaded.DescriptionOverrides.Elementen.ContainsKey("VH|CATALOG"));
            catalog.Elementen.Clear();
            catalog.Elementen["VH|NEW"] = new DescriptionEntry { Specifiek = "Nieuw opgeslagen" };
            SaveGlobalCatalog(catalog);
            var current = GetTargetSettings(doc.Database, MutationTarget.Global);
            Check("opnieuw bewerken vervangt oude overrides", current.DescriptionOverrides.Elementen.Count == 1
                && current.DescriptionOverrides.Elementen.ContainsKey("VH|NEW"));
            Check("eerder profiel behoudt eigen snapshot", loaded.DescriptionOverrides.Elementen.ContainsKey("VH|SETTING")
                && !loaded.DescriptionOverrides.Elementen.ContainsKey("VH|NEW"));
            File.WriteAllText(ConfigPath, "{ invalid");
            bool refused = false;
            try { GetTargetSettings(doc.Database, MutationTarget.Global); }
            catch (InvalidDataException) { refused = true; }
            Check("onleesbare globale instellingen worden gemeld", refused);
        }
        catch (System.Exception ex) { failures++; doc.Editor.WriteMessage($"\nGLOBALSETTINGS FAIL: {ex}"); }
        finally
        {
            foreach (var original in originals)
            {
                if (original.Value is null) { if (File.Exists(original.Key)) File.Delete(original.Key); }
                else File.WriteAllBytes(original.Key, original.Value);
            }
            if (!existed && Directory.Exists(ConfigDir) && !Directory.EnumerateFileSystemEntries(ConfigDir).Any()) Directory.Delete(ConfigDir);
            if (Directory.Exists(temp)) Directory.Delete(temp, true);
        }
        doc.Editor.WriteMessage($"\nGLOBALSETTINGS totaal: {(failures == 0 ? "OK" : "FAIL")} ({failures} fouten)");
    }
}
