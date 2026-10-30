using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using NlcsLegenda.Core;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Core.Application;
using AcWindows = Autodesk.AutoCAD.ApplicationServices.Application;

namespace NlcsLegenda.Plugin;

public partial class Commands
{
    private static bool PreviewUpdate(Database db, LegendDefinition definition)
    {
        using var dialog = new UpdatePreviewDialog(definition.Name, PreviewChanges(db, definition));
        return AcWindows.ShowModalDialog(dialog) == System.Windows.Forms.DialogResult.OK;
    }

    private static IReadOnlyList<LegendChange> PreviewChanges(Database db, LegendDefinition definition)
    {
        using var tr = db.TransactionManager.StartTransaction();
        var registry = LegendStore.Load(db, tr);
        var working = definition.Clone();
        var excluded = LegendManagement.CollectManagedIds(db, tr, registry);
        var selected = working.Scope == LegendScope.Selection && working.Settings.SourceViewportHandles.Count == 0 ? LegendManagement.ResolveHandles(db, working.SourceHandles, out _) : null;
        var analysis = DrawingAnalyzer.Analyze(db, tr, working.Settings, selected, DescriptionCatalog.Default(), excluded, trackSuppressedContents: working.Settings.RetainMissingRows);
        LegendRowSamples.Seed(db, tr, working, analysis);
        analysis = ManagedLegendExamples.Prepare(db, tr, working, analysis);
        analysis = LegendRowSamples.Retain(analysis, working);
        var composition = LegendComposition.Apply(working, analysis.Entries);
        return LegendUpdatePlan.Compare(working.Rows, composition.Entries).Concat(composition.Notices).Concat(analysis.Coverage).ToArray();
    }

    [CommandMethod("NLCSLEGENDAOVERNEMEN", CommandFlags.Modal)]
    public void AdoptLegendRow()
    {
        var doc = AcApp.DocumentManager.MdiActiveDocument;
        if (doc is null || !RequireInteractive(doc.Editor, "NLCSLEGENDAOVERNEMEN")) return;
        try
        {
            var target = MutationTarget.Global;
            var mode = new PromptKeywordOptions("\nBestaande getekende regel [BestaandeLegenda/NieuweLegenda]", "BestaandeLegenda NieuweLegenda") { AllowNone = true };
            using (var check = doc.Database.TransactionManager.StartTransaction())
                mode.Keywords.Default = LegendManagement.ActiveRegistry(doc.Database, check, LegendStore.Load(doc.Database, check)).Legends.Count == 0 ? "NieuweLegenda" : "BestaandeLegenda";
            var answer = doc.Editor.GetKeywords(mode);
            if (answer.Status != PromptStatus.OK) return;
            bool create = answer.StringResult == "NieuweLegenda";
            if (!create && !ResolveMutationTarget(doc.Editor, doc.Database, out target, pickVerb: "een regel toevoegen aan")) return;
            var textResult = doc.Editor.GetEntity("\nKies de omschrijving van de bestaande regel: ");
            if (textResult.Status != PromptStatus.OK) return;
            var selected = doc.Editor.GetSelection(new PromptSelectionOptions { MessageForAdding = "\nSelecteer alleen het voorbeeld bij deze regel (zonder omschrijving of kader): " });
            if (selected.Status != PromptStatus.OK) return;
            using var tr = doc.Database.TransactionManager.StartTransaction();
            var text = tr.GetObject(textResult.ObjectId, OpenMode.ForRead) switch { MText m => m.Text, DBText t => t.TextString, _ => "" };
            if (string.IsNullOrWhiteSpace(text)) throw new InvalidOperationException("Kies een tekst of alineatekst als omschrijving.");
            var ids = selected.Value.GetObjectIds().Where(id => id != textResult.ObjectId).ToArray();
            if (ids.Length == 0) return;
            var statusChoice = new PromptKeywordOptions("\nStatus [Nieuw/Bestaand/Vervallen/Tijdelijk/Revisie]", "Nieuw Bestaand Vervallen Tijdelijk Revisie") { AllowNone = true };
            statusChoice.Keywords.Default = "Bestaand";
            var status = doc.Editor.GetKeywords(statusChoice);
            if (status.Status != PromptStatus.OK) return;
            var registry = LegendStore.Load(doc.Database, tr);
            var definition = create ? CreateAdoptedLegend(doc.Database, tr, registry, textResult.ObjectId, ids)
                : registry.FindById(target.LegendId) ?? throw new InvalidOperationException("De legenda ontbreekt.");
            AdoptRow(doc.Database, tr, definition, ids, text, Enum.Parse<NlcsStatus>(status.StringResult));
            // origineel pas verwijderen binnen dezelfde geslaagde wijziging
            if (!create)
                foreach (var id in ids.Append(textResult.ObjectId)) ((Entity)tr.GetObject(id, OpenMode.ForWrite)).Erase();
            if (BuildManagedLegend(doc.Database, tr, registry, definition, out _, out _) != UpdateResult.Updated)
                throw new InvalidOperationException("De regel kon niet worden opgenomen.");
            LegendStore.Save(doc.Database, tr, registry); tr.Commit();
            PurgePending(doc.Database);
            doc.Editor.WriteMessage("\nRegel overgenomen; tekst, status en voorbeeld blijven bij volgende updates bewaard.");
        }
        catch (System.Exception ex) when (!ExceptionSafety.IsFatal(ex)) { doc.Editor.WriteMessage("\nOvernemen afgebroken; bestaande regel behouden: " + ex.Message); }
    }

    internal static LegendDefinition CreateAdoptedLegend(Database db, Transaction tr, LegendRegistry registry,
        ObjectId description, IReadOnlyList<ObjectId> sample)
    {
        var text = (Entity)tr.GetObject(description, OpenMode.ForRead);
        var owner = (BlockTableRecord)tr.GetObject(text.OwnerId, OpenMode.ForRead);
        if (!owner.IsLayout || sample.Any(id => ((Entity)tr.GetObject(id, OpenMode.ForRead)).OwnerId != owner.ObjectId))
            throw new InvalidOperationException("Kies omschrijving en voorbeeld in dezelfde model- of papierruimte.");
        var settings = new LegendSettings { IncludeTitle = false, IncludeGroupHeaders = false, IncludeFooter = false,
            IncludeRemarks = false, IncludeScaleBar = false, FitExistingWindow = true, Columns = 1 };

        var definition = new LegendDefinition { Name = registry.NextDefaultName(), Scope = LegendScope.Selection,
            GroupName = LegendRegistry.NewGroupName(), Settings = settings, SpaceHandle = owner.Handle.ToString() };
        LegendManagement.AddToGroup(db, tr, definition.GroupName, sample.Append(description));
        registry.Add(definition);
        return definition;
    }

    internal static ManualEntry AdoptRow(Database db, Transaction tr, LegendDefinition definition,
        IReadOnlyList<ObjectId> sample, string description, NlcsStatus status)
    {
        var block = LegendRowSamples.Capture(db, tr, sample);
        var manual = new ManualEntry { Id = Guid.NewGuid().ToString("N"), Layer = "0", Type = NlcsDrawType.Symbool,
            Description = description, Status = status, SymbolBlock = block, SampleBlock = block, SampleScale = LegendViewport.IsPaperSpace(db, tr, definition) ? 1000 : definition.Settings.Scale };
        definition.Settings.ManualEntries.Add(manual);
        return manual;
    }
}
