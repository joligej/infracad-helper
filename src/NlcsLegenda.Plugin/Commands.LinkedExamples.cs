using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using NlcsLegenda.Core;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Core.Application;
using AcWindows = Autodesk.AutoCAD.ApplicationServices.Application;

namespace NlcsLegenda.Plugin;

public partial class Commands
{
    [CommandMethod("NLCSLEGENDAVOORBEELDKOPPELEN", CommandFlags.Modal)]
    public void LinkLegendExample()
    {
        var doc = AcApp.DocumentManager.MdiActiveDocument;
        if (doc is null || !RequireInteractive(doc.Editor, "NLCSLEGENDAVOORBEELDKOPPELEN")) return;
        try
        {
            if (!ResolveMutationTarget(doc.Editor, doc.Database, out var target, pickVerb: "een bronvoorbeeld koppelen aan")) return;
            var selected = doc.Editor.GetNestedEntity(new PromptNestedEntityOptions("\nKies een bronobject, profielonderdeel of eigen markering (ook binnen een xref): "));
            if (selected.Status != PromptStatus.OK) return;
            using var tr = doc.Database.TransactionManager.StartTransaction();
            var registry = LegendStore.Load(doc.Database, tr);
            var definition = registry.FindById(target.LegendId) ?? throw new InvalidOperationException("Legenda ontbreekt.");
            var managed = LegendManagement.CollectManagedIds(doc.Database, tr, registry);
            if (managed.Contains(selected.ObjectId) || selected.GetContainers().Any(managed.Contains))
                throw new InvalidOperationException("Kies het bronobject in de kaart of het profiel, niet een legendavoorbeeld.");
            var link = LinkedLegendExamples.Create(doc.Database, tr, selected.ObjectId, selected.GetContainers());
            var entity = (Entity)tr.GetObject(selected.ObjectId, OpenMode.ForRead);
            var components = CivilProfileExamples.Components(entity, tr);
            if (CivilProfileExamples.Supports(entity) && components.Count == 0) throw new InvalidOperationException("Dit Civil 3D-onderdeel heeft geen zichtbare profielcomponent.");
            var ownViews = registry.Legends.SelectMany(l => LegendViewport.Linked(doc.Database, tr, l.Id)).Select(v => v.ObjectId).ToHashSet();
            var viewports = new List<(string Handle, string Label)>();
            foreach (DBDictionaryEntry layoutItem in (DBDictionary)tr.GetObject(doc.Database.LayoutDictionaryId, OpenMode.ForRead))
            {
                var layout = (Layout)tr.GetObject(layoutItem.Value, OpenMode.ForRead); if (layout.ModelType) continue;
                foreach (ObjectId id in (BlockTableRecord)tr.GetObject(layout.BlockTableRecordId, OpenMode.ForRead))
                    if (!ownViews.Contains(id) && tr.GetObject(id, OpenMode.ForRead) is Viewport { Number: not 1 } view)
                        viewports.Add((view.Handle.ToString(), layout.LayoutName + " · kaartvenster " + view.Handle));
            }
            using var dialog = new LinkedExampleDialog(definition, components, link.Label, viewports);
            if (AcWindows.ShowModalDialog(dialog) != System.Windows.Forms.DialogResult.OK) return;
            if (dialog.SelectedComponent is { } component) { link.Component = component.Name; link.ProfileViewHandle = component.ProfileViewHandle; }
            link.MapViewportHandle = dialog.MapViewportHandle;
            var source = LinkedLegendExamples.Resolve(doc.Database, tr, link);
            var manual = dialog.Existing ?? new ManualEntry { Id = Guid.NewGuid().ToString("N"), Layer = "0", Type = NlcsDrawType.Symbool };
            manual.Link = link; manual.Description = dialog.Description; manual.Status = dialog.Status;
            manual.ComparisonKey = dialog.ComparisonKey; manual.ExampleMode = dialog.Follow ? ExampleMode.FollowSource : ExampleMode.Fixed;
            manual.LinkedSources = new() { new LegendSource("GEKOPPELD:" + string.Join(":", link.ContainerHandles.Append(link.ObjectHandle)),
                entity.Layer, NlcsDrawType.Symbool, link.Component.Length > 0 ? entity.GetType().Name + ":" + link.Component : "", manual.Status)
                { DrawingId = link.DrawingId + ":" + string.Join(":", link.ContainerHandles), SourceHandle = link.ObjectHandle } };
            if (dialog.Existing is null || dialog.Follow || manual.SampleBlock.Length == 0)
            {
                manual.SampleBlock = LinkedLegendExamples.Capture(doc.Database, tr, source, link, definition.Settings);
                manual.SymbolBlock = manual.SampleBlock; manual.SampleScale = definition.Settings.Scale;
            }
            if (dialog.Existing is null) definition.Settings.ManualEntries.Add(manual);
            if (BuildManagedLegend(doc.Database, tr, registry, definition, out _, out _) != UpdateResult.Updated)
                throw new InvalidOperationException("Het voorbeeld kon niet worden opgenomen.");
            LegendStore.Save(doc.Database, tr, registry); tr.Commit(); PurgePending(doc.Database); doc.Editor.Regen();
            doc.Editor.WriteMessage("\nBronvoorbeeld gekoppeld; het bronobject is ongewijzigd.");
        }
        catch (System.Exception ex) when (!ExceptionSafety.IsFatal(ex)) { doc.Editor.WriteMessage("\nVoorbeeld niet gekoppeld: " + ex.GetBaseException().Message); }
    }
}
