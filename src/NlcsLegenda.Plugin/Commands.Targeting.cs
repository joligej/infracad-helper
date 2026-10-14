using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using NlcsLegenda.Core;

namespace NlcsLegenda.Plugin;

public partial class Commands
{
    private enum MutationScope { GlobalDefaults, Legend }

    // Waar een instelmutatie op landt: de globale standaard (voor nieuwe legenda's) of één
    // bestaande legenda op LegendId.
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

    // Zonder legenda wordt de standaard bewerkt; één aanwezige legenda is eenduidig.
    // De standaard blijft rechtstreeks bereikbaar via NLCSLEGENDASTANDAARD.
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
            return true;
        var def = ResolveTargetLegend(ed, db, reg, pickVerb);
        if (def is null)
            return false;
        target = MutationTarget.ForLegend(def);
        return true;
    }

    // Basisinstellingen voor het doel: de globale standaard of een kopie van de legenda-snapshot.
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

    // Schrijft de bewerkte instellingen terug. Globaal: opslaan als standaard; bestaande
    // legenda's blijven ongemoeid. Legenda: via de centrale rebuild-flow, zodat de echte
    // legenda meeverandert in plaats van alleen een losse snapshot.
    private void ApplyTargetSettings(
        Editor ed, Database db, MutationTarget target, LegendSettings settings, string what)
    {
        if (target.IsGlobal)
        {
            var where = SaveGlobalDefaults(settings);
            ed.WriteMessage(
                $"\n{what} opgeslagen als globale standaard ({where}); bestaande legenda's blijven ongewijzigd.");
            return;
        }
        ApplySettingsAndRebuild(ed, db, target.LegendId, settings);
    }
}
