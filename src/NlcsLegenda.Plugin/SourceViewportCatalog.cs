using Autodesk.AutoCAD.DatabaseServices;

namespace NlcsLegenda.Plugin;

internal static class SourceViewportCatalog
{
    public static IReadOnlyList<SourceViewOption> Read(Database db, Transaction tr)
    {
        var result = new List<SourceViewOption>();
        var layouts = ((DBDictionary)tr.GetObject(db.LayoutDictionaryId, OpenMode.ForRead)).Cast<System.Collections.DictionaryEntry>()
            .Select(p => (Layout)tr.GetObject((ObjectId)p.Value!, OpenMode.ForRead)).Where(l => !l.ModelType).OrderBy(l => l.TabOrder);
        foreach (var layout in layouts)
        {
            using var ids = layout.GetViewports();
            var paper = ids.Count > 0 ? ids[0] : ObjectId.Null;
            var views = ((BlockTableRecord)tr.GetObject(layout.BlockTableRecordId, OpenMode.ForRead)).Cast<ObjectId>()
                .Select(id => tr.GetObject(id, OpenMode.ForRead)).OfType<Viewport>()
                .Where(v => v.ObjectId != paper && v.Number != 1 && LegendViewport.Read(v, tr) is null)
                .OrderByDescending(v => v.CenterPoint.Y).ThenBy(v => v.CenterPoint.X).ToArray();
            for (int i = 0; i < views.Length; i++)
            {
                var view = views[i];
                bool enabled = ViewportState.IsEnabled(view);
                bool validScale = double.IsFinite(view.CustomScale) && view.CustomScale > 0;
                string state = !enabled ? "Uitgeschakeld" : view.PerspectiveOn ? "Perspectief; kies een vlakke kaart"
                    : !validScale ? "Geen geldige schaal" : view.Locked ? "Vergrendeld" : "Niet vergrendeld";
                result.Add(new(view.Handle.ToString(), layout.LayoutName, i + 1, validScale ? 1000 / view.CustomScale : 0,
                    view.CenterPoint.X, view.CenterPoint.Y, view.Width, view.Height, state, enabled && !view.PerspectiveOn && validScale));
            }
        }
        return result;
    }
}