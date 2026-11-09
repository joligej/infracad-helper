using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using NlcsLegenda.Core;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Core.Application;
using AcWindows = Autodesk.AutoCAD.ApplicationServices.Application;

namespace NlcsLegenda.Plugin;

public partial class Commands
{
    private static SourceViewportDialog CreateSourceViewDialog(Database db, LegendSettings settings)
    {
        using var tr = db.TransactionManager.StartTransaction();
        var views = SourceViewportCatalog.Read(db, tr);
        tr.Commit();
        return new SourceViewportDialog(views, settings.SourceViewportHandles);
    }

    private static void PickSourceViews(System.Windows.Forms.Form form, LegendSettings settings)
    {
        var db = AcApp.DocumentManager.MdiActiveDocument.Database;
        try
        {
            using var dialog = CreateSourceViewDialog(db, settings);
            if (dialog.ShowDialog(form) != System.Windows.Forms.DialogResult.OK) return;
            var working = settings.Clone();
            working.SourceViewportHandles = dialog.SelectedHandles;
            using var tr = db.TransactionManager.StartTransaction();
            SourceViewport.Read(db, tr, working);
            tr.Commit();
            settings.SourceViewportHandles = working.SourceViewportHandles;
        }
        catch (System.Exception ex) when (!ExceptionSafety.IsFatal(ex))
        {
            System.Windows.Forms.MessageBox.Show(form, ex.Message, "Bronkeuze niet gewijzigd",
                System.Windows.Forms.MessageBoxButtons.OK, System.Windows.Forms.MessageBoxIcon.Warning);
        }
    }

    [CommandMethod("NLCS_LEGENDA", "NLCSLEGENDABRONVENSTER", null, CommandFlags.Modal, null, "help-NLCSLEGENDABRONVENSTER.html", "")]
    public void SelectSourceViewports()
    {
        var doc = AcApp.DocumentManager.MdiActiveDocument;
        if (doc is null || !RequireInteractive(doc.Editor, "NLCSLEGENDABRONVENSTER")) return;
        try
        {
            if (!ResolveMutationTarget(doc.Editor, doc.Database, out var target, pickVerb: "bronvensters kiezen voor")) return;
            var choice = new PromptKeywordOptions("\nBrongebied [Lijst/Kaartvensters/Toevoegen/HeleTekening]", "Lijst Kaartvensters Toevoegen HeleTekening") { AllowNone = true };
            choice.Keywords.Default = "Lijst";
            var answer = doc.Editor.GetKeywords(choice);
            if (answer.Status != PromptStatus.OK) return;
            ObjectId[] selected = Array.Empty<ObjectId>();
            if (answer.StringResult == "Lijst")
            {
                LegendSettings current;
                using (var read = doc.Database.TransactionManager.StartTransaction())
                {
                    current = LegendStore.Load(doc.Database, read).FindById(target.LegendId)!.Settings.Clone();
                    read.Commit();
                }
                using var dialog = CreateSourceViewDialog(doc.Database, current);
                if (AcWindows.ShowModalDialog(dialog) != System.Windows.Forms.DialogResult.OK) return;
                selected = LegendManagement.ResolveHandles(doc.Database, dialog.SelectedHandles, out int missing);
                if (missing > 0) throw new InvalidOperationException("Een gekozen kaartvenster ontbreekt; kies de bronnen opnieuw.");
            }
            else if (answer.StringResult != "HeleTekening")
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
            var allowed = SourceViewportCatalog.Read(doc.Database, tr).Where(v => v.Available).Select(v => v.Handle).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var id in selected)
                if (!allowed.Contains(id.Handle.ToString()))
                    throw new InvalidOperationException("Kies alleen beschikbare kaartvensters, geen papier- of legendavenster.");
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

    [CommandMethod("NLCS_LEGENDA", "NLCSLEGENDABRONCONTROLE", null, CommandFlags.Modal, null, "help-NLCSLEGENDABRONCONTROLE.html", "")]
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
