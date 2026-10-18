using System.IO;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Runtime;
using NlcsLegenda.Core;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Core.Application;

namespace NlcsLegenda.Plugin;

public partial class Commands
{
    [CommandMethod("NLCSLEGENDAPRESETTARGETTEST", CommandFlags.Modal)]
    public void NlcsLegendaPresetTargetTest()
    {
        var doc = AcApp.DocumentManager.MdiActiveDocument;
        if (doc is null) return;
        DiagnosticRun.Execute(() =>
        {
            using var snapshot = new GlobalSettingsSnapshot();
            var db = doc.Database;
            if (!ResolvePresetTarget(doc.Editor, db, out var globalTarget, "Test lege tekening", "test") || !globalTarget.IsGlobal)
                throw new InvalidOperationException("Globale profielkeuze zonder legenda ontbreekt.");
            var settings = new LegendSettings
            {
                Scale = 200,
                IncludeRemarks = false,
                IncludeScaleBar = false,
                ManualEntries = new() { new() { Layer = "PRESET_LINE", Description = "Profielregel" } }
            };
            if (ApplyTargetSettings(doc.Editor, db, globalTarget, settings, "Testprofiel") is { } error)
                throw new InvalidOperationException(error);
            var dir = Path.Join(Path.GetDirectoryName(db.Filename), "profielen");
            LegendPresets.Save(dir, "globaal", GetTargetSettings(db, globalTarget));
            string id;
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var registry = LegendStore.Load(db, tr);
                var local = IsoDef(registry, settings.Clone()); local.Name = "Profiel lokaal";
                registry.Add(local); id = local.Id;
                if (BuildManagedLegend(db, tr, registry, local, out _, out _) != UpdateResult.Updated)
                    throw new InvalidOperationException("Lokale testlegenda ontbreekt.");
                LegendStore.Save(db, tr, registry); tr.Commit();
            }
            if (!ResolvePresetTarget(doc.Editor, db, out var localTarget, "Test lokaal", "test") || localTarget.LegendId != id)
                throw new InvalidOperationException("Lokale profielkeuze ontbreekt.");
            var localSettings = LegendPresets.Load(dir, "globaal")!; localSettings.Scale = 500;
            if (ApplyTargetSettings(doc.Editor, db, localTarget, localSettings, "Testprofiel") is { } localError)
                throw new InvalidOperationException(localError);
            LegendPresets.Save(dir, "lokaal", GetTargetSettings(db, localTarget));
            if (Math.Abs(GetTargetSettings(db, globalTarget).Scale - 200) > 1e-9)
                throw new InvalidOperationException("Lokaal profiel wijzigde de globale standaard.");
            if (!ResolvePresetTarget(doc.Editor, db, out globalTarget, "Test globaal met legenda", "test") || !globalTarget.IsGlobal)
                throw new InvalidOperationException("Globale profielkeuze met legenda ontbreekt.");
            var globalSettings = LegendPresets.Load(dir, "lokaal")!; globalSettings.Scale = 1000;
            if (ApplyTargetSettings(doc.Editor, db, globalTarget, globalSettings, "Testprofiel") is { } globalError)
                throw new InvalidOperationException(globalError);
            if (Math.Abs(GetTargetSettings(db, globalTarget).Scale - 1000) > 1e-9
                || Math.Abs(GetTargetSettings(db, localTarget).Scale - 500) > 1e-9)
                throw new InvalidOperationException("Globaal profiel wijzigde de bestaande legenda.");
            doc.Editor.WriteMessage("\nPRESETTARGET globale en lokale profielen, zonder en met legenda, gescheiden -> OK");
        }, message => doc.Editor.WriteMessage(message), "NlcsLegendaPresetTargetTest");
    }
}
