using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using NlcsLegenda.Core;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Core.Application;
using AcWindows = Autodesk.AutoCAD.ApplicationServices.Application;

namespace NlcsLegenda.Plugin;

public partial class Commands
{
    private static void PickSourceViews(System.Windows.Forms.Form form, LegendSettings settings)
    {
        var doc = AcApp.DocumentManager.MdiActiveDocument;
        using var interaction = doc.Editor.StartUserInteraction(form.Handle);
        if (doc.Database.TileMode) { doc.Editor.WriteMessage("\nGa eerst naar een layout om kaartvensters te kiezen."); return; }
        bool append = false;
        if (settings.SourceViewportHandles.Count > 0)
        {
            var mode = new PromptKeywordOptions("\nBronvensters [Vervangen/Toevoegen]", "Vervangen Toevoegen") { AllowNone = true };
            mode.Keywords.Default = "Vervangen";
            var answer = doc.Editor.GetKeywords(mode);
            if (answer.Status != PromptStatus.OK) return;
            append = answer.StringResult == "Toevoegen";
        }
        var pick = doc.Editor.GetSelection(new PromptSelectionOptions { MessageForAdding = "\nSelecteer de kaartvensters (geen legenda of overzichtskaart): " },
            new SelectionFilter(new[] { new TypedValue((int)DxfCode.Start, "VIEWPORT") }));
        if (pick.Status != PromptStatus.OK) return;
        using var tr = doc.Database.TransactionManager.StartTransaction();
        var registry = LegendStore.Load(doc.Database, tr);
        var own = registry.Legends.SelectMany(l => LegendViewport.Linked(doc.Database, tr, l.Id)).Select(v => v.ObjectId).ToHashSet();
        var ids = pick.Value.GetObjectIds().Where(id => !own.Contains(id) && tr.GetObject(id, OpenMode.ForRead) is Viewport { Number: > 1 }).ToArray();
        if (ids.Length != pick.Value.Count) { doc.Editor.WriteMessage("\nSelecteer alleen kaartvensters; de bronkeuze is behouden."); return; }
        settings.SourceViewportHandles = (append ? settings.SourceViewportHandles : new List<string>())
            .Concat(LegendManagement.ToHandles(ids)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    [CommandMethod("NLCSLEGENDABRONVENSTER", CommandFlags.Modal)]
    public void SelectSourceViewports()
    {
        var doc = AcApp.DocumentManager.MdiActiveDocument;
        if (doc is null || !RequireInteractive(doc.Editor, "NLCSLEGENDABRONVENSTER")) return;
        try
        {
            if (!ResolveMutationTarget(doc.Editor, doc.Database, out var target, pickVerb: "bronvensters kiezen voor")) return;
            var choice = new PromptKeywordOptions("\nBrongebied [Kaartvensters/Toevoegen/HeleTekening]", "Kaartvensters Toevoegen HeleTekening") { AllowNone = true };
            choice.Keywords.Default = "Kaartvensters";
            var answer = doc.Editor.GetKeywords(choice);
            if (answer.Status != PromptStatus.OK) return;
            ObjectId[] selected = Array.Empty<ObjectId>();
            if (answer.StringResult != "HeleTekening")
            {
                if (doc.Database.TileMode) { doc.Editor.WriteMessage("\nGa naar de layout en kies daar de kaartvensters. Laat overzichtskaart en legendavenster buiten de selectie."); return; }
                var pick = doc.Editor.GetSelection(new PromptSelectionOptions { MessageForAdding = "\nSelecteer de kaartviewports die bij deze legenda horen: " },
                    new SelectionFilter(new[] { new TypedValue((int)DxfCode.Start, "VIEWPORT") }));
                if (pick.Status != PromptStatus.OK) return;
                selected = pick.Value.GetObjectIds();
            }
            using var tr = doc.Database.TransactionManager.StartTransaction();
            var registry = LegendStore.Load(doc.Database, tr);
            var definition = registry.FindById(target.LegendId) ?? throw new InvalidOperationException("Legenda ontbreekt.");
            var legendViews = registry.Legends.SelectMany(l => LegendViewport.Linked(doc.Database, tr, l.Id)).Select(v => v.ObjectId).ToHashSet();
            foreach (var id in selected)
                if (legendViews.Contains(id) || tr.GetObject(id, OpenMode.ForRead) is not Viewport { Number: > 1 })
                    throw new InvalidOperationException("Kies alleen kaartvensters, geen papier- of legendavenster.");
            definition.Scope = LegendScope.WholeDrawing; definition.SourceHandles.Clear();
            definition.Settings.SourceViewportHandles = (answer.StringResult == "Toevoegen" ? definition.Settings.SourceViewportHandles : new List<string>())
                .Concat(LegendManagement.ToHandles(selected)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            SourceViewport.Read(doc.Database, tr, definition.Settings);
            if (BuildManagedLegend(doc.Database, tr, registry, definition, out _, out _) != UpdateResult.Updated)
                throw new InvalidOperationException("De legenda kon niet worden bijgewerkt.");
            LegendStore.Save(doc.Database, tr, registry); tr.Commit();
            PurgePending(doc.Database); doc.Editor.Regen();
            doc.Editor.WriteMessage("\nBrongebied bewaard; de gekozen kaartvensters zijn ongewijzigd.");
        }
        catch (System.Exception ex) when (!ExceptionSafety.IsFatal(ex)) { doc.Editor.WriteMessage("\nBrongebied niet gewijzigd: " + ex.Message); }
    }

    [CommandMethod("NLCSLEGENDABRONCONTROLE", CommandFlags.Modal)]
    public void ExplainLegendSources()
    {
        var doc = AcApp.DocumentManager.MdiActiveDocument;
        if (doc is null || !RequireInteractive(doc.Editor, "NLCSLEGENDABRONCONTROLE")) return;
        try
        {
            if (!ResolveMutationTarget(doc.Editor, doc.Database, out var target, pickVerb: "bronnen controleren van")) return;
            using var tr = doc.Database.TransactionManager.StartTransaction();
            var definition = LegendStore.Load(doc.Database, tr).FindById(target.LegendId) ?? throw new InvalidOperationException("Legenda ontbreekt.");
            using var dialog = new UpdatePreviewDialog(definition.Name, PreviewChanges(doc.Database, definition), inspectionOnly: true);
            AcWindows.ShowModalDialog(dialog);
        }
        catch (System.Exception ex) when (!ExceptionSafety.IsFatal(ex)) { doc.Editor.WriteMessage("\nBroncontrole afgebroken: " + ex.Message); }
    }
}
