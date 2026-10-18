using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using NlcsLegenda.Core;

namespace NlcsLegenda.Plugin;

public partial class Commands
{
    private enum MutationScope { GlobalDefaults, Legend }

    private sealed class MutationTarget
    {
        public MutationScope Scope { get; init; }
        public string LegendId { get; init; } = string.Empty;
        public string ContextLabel { get; init; } = "Globale standaard \u2013 geldt voor nieuwe legenda's";

        public bool IsGlobal => Scope == MutationScope.GlobalDefaults;

        public static readonly MutationTarget Global = new();

        public static MutationTarget ForLegend(LegendDefinition def) => new()
        {
            Scope = MutationScope.Legend,
            LegendId = def.Id,
            ContextLabel = $"Legenda: {def.Name}"
        };
    }

    private static bool ResolveMutationTarget(Editor ed, Database db, out MutationTarget target,
        string promptVerb = "Waarop toepassen", string pickVerb = "aanpassen")
    {
        target = MutationTarget.Global;
        LegendRegistry reg;
        using (var tr = db.TransactionManager.StartTransaction())
        {
            reg = LegendManagement.ActiveRegistry(db, tr, LegendStore.Load(db, tr));
            tr.Commit();
        }
        if (reg.Legends.Count == 0)
        {
            ed.WriteMessage("\nGeen legenda aanwezig. Plaats eerst een legenda, of kies Globale standaard voor nieuwe legenda's.");
            return false;
        }
        var def = ResolveTargetLegend(ed, db, reg, pickVerb);
        if (def is null)
            return false;
        target = MutationTarget.ForLegend(def);
        return true;
    }

    private static bool ResolvePresetTarget(Editor ed, Database db, out MutationTarget target,
        string prompt, string pickVerb)
    {
        target = MutationTarget.Global;
        var options = new PromptKeywordOptions("\n" + prompt) { AllowNone = true };
        options.Keywords.Add("Legenda");
        options.Keywords.Add("Standaard");
        options.Keywords.Default = "Legenda";
        var choice = ed.GetKeywords(options);
        if (choice.Status != PromptStatus.OK) return false;
        if (choice.StringResult == "Standaard") return true;
        return ResolveMutationTarget(ed, db, out target, pickVerb: pickVerb);
    }

    private static LegendSettings GetTargetSettings(Database db, MutationTarget target)
    {
        if (target.IsGlobal)
            return LoadGlobalDefaults();
        using var tr = db.TransactionManager.StartTransaction();
        var reg = LegendStore.Load(db, tr);
        var def = reg.FindById(target.LegendId);
        var clone = (def ?? throw new InvalidOperationException("De gekozen legenda bestaat niet meer.")).Settings.Clone();
        tr.Commit();
        return clone;
    }

    private string? ApplyTargetSettings(
        Editor ed, Database db, MutationTarget target, LegendSettings settings, string what)
    {
        if (target.IsGlobal)
        {
            var where = SaveGlobalDefaults(settings);
            ed.WriteMessage(
                $"\n{what} opgeslagen als globale standaard ({where}); bestaande legenda's blijven ongewijzigd.");
            return null;
        }
        return ApplySettingsAndRebuild(ed, db, target.LegendId, settings);
    }
}
