using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using NlcsLegenda.Core;

namespace NlcsLegenda.Plugin;

internal sealed record LinkedSource(Entity Entity, LayerDisplayStyle Style, LayerDisplayStyle Layer,
    IReadOnlyList<ExampleDisplay> Display, ObjectId ViewportId);

internal static class LinkedLegendExamples
{
    internal static LegendObjectLink Create(Database db, Transaction tr, ObjectId selected, IEnumerable<ObjectId> containers)
    {
        var chain = containers.Reverse().ToArray();
        string drawing = db.FingerprintGuid.ToString();
        foreach (var id in chain)
        {
            if (tr.GetObject(id, OpenMode.ForRead) is not BlockReference reference) throw new InvalidOperationException("De bronkoppeling bevat een ongeldig blok.");
            var block = (BlockTableRecord)tr.GetObject(reference.BlockTableRecord, OpenMode.ForRead);
            if (block.IsFromExternalReference)
                drawing = (block.GetXrefDatabase(false) ?? throw new InvalidOperationException("De bron-xref is niet geladen.")).FingerprintGuid.ToString();
        }
        var entity = (Entity)tr.GetObject(selected, OpenMode.ForRead);
        if (entity is BlockReference br && ((BlockTableRecord)tr.GetObject(br.BlockTableRecord, OpenMode.ForRead)).IsFromExternalReference)
            throw new InvalidOperationException("Kies één onderdeel binnen de xref, niet de volledige xref.");
        return new() { DrawingId = drawing, ContainerHandles = chain.Select(id => id.NonForwardedHandle.ToString()).ToList(),
            ObjectHandle = selected.NonForwardedHandle.ToString(), Label = entity.Layer + " · " + entity.GetType().Name };
    }

    internal static LegendObjectLink Find(Database db, Transaction tr, IReadOnlyList<LegendSource> sources)
    {
        var path = new HashSet<ObjectId>();
        LegendObjectLink? Walk(ObjectId blockId, List<ObjectId> chain, string xref, string drawing)
        {
            if (!path.Add(blockId) || path.Count > 30) return null;
            try
            {
                var block = (BlockTableRecord)tr.GetObject(blockId, OpenMode.ForRead);
                if (block.IsUnloaded) return null;
                foreach (ObjectId id in block)
                {
                    if (id.IsErased || tr.GetObject(id, OpenMode.ForRead) is not Entity entity) continue;
                    string key = xref.ToUpperInvariant() + ":" + id.NonForwardedHandle;
                    if (sources.Any(s => s.Id == key && (s.DrawingId.Length == 0 || s.DrawingId == drawing)))
                        return Create(db, tr, id, chain.AsEnumerable().Reverse());
                    if (entity is not BlockReference reference) continue;
                    var child = (BlockTableRecord)tr.GetObject(reference.BlockTableRecord, OpenMode.ForRead);
                    string childSource = child.IsFromExternalReference ? (xref.Length > 0 ? xref + "|" + LayerNaming.LocalName(child.Name) : child.Name) : xref;
                    string childDrawing = drawing;
                    if (child.IsFromExternalReference && !child.IsUnloaded)
                        childDrawing = (xref.Length > 0 ? drawing + ">" : "") + child.GetXrefDatabase(false)?.FingerprintGuid + ":" + id.NonForwardedHandle;
                    var found = Walk(child.ObjectId, chain.Append(id).ToList(), childSource, childDrawing);
                    if (found is not null) return found;
                }
                return null;
            }
            finally { path.Remove(blockId); }
        }
        return Walk(SymbolUtilityServices.GetBlockModelSpaceId(db), new(), "", db.FingerprintGuid.ToString())
            ?? throw new InvalidOperationException("Het gekozen bronvoorbeeld is niet meer beschikbaar. Kies het opnieuw via Gekoppeld voorbeeld.");
    }

    internal static LinkedSource Resolve(Database db, Transaction tr, LegendObjectLink link)
    {
        ObjectId viewport = ObjectId.Null;
        if (link.MapViewportHandle.Length > 0)
        {
            var views = LegendManagement.ResolveHandles(db, new[] { link.MapViewportHandle }, out _);
            if (views.Length != 1 || tr.GetObject(views[0], OpenMode.ForRead) is not Viewport { On: true })
                throw new InvalidOperationException("Het gekoppelde kaartvenster ontbreekt of staat uit.");
            viewport = views[0];
        }
        void RequireVisible(Entity entity)
        {
            var layer = (LayerTableRecord)tr.GetObject(entity.LayerId, OpenMode.ForRead);
            if (!entity.Visible || layer.IsOff || layer.IsFrozen || !layer.IsPlottable
                || !viewport.IsNull && ((Viewport)tr.GetObject(viewport, OpenMode.ForRead)).GetFrozenLayers().Cast<ObjectId>().Contains(entity.LayerId))
                throw new InvalidOperationException("Het gekoppelde bronobject of zijn bronblok is verborgen, bevroren of niet plottend.");
        }
        BlockTableRecord? owner = null; LayerDisplayStyle? parent = null, parentLayer = null;
        string drawing = db.FingerprintGuid.ToString();
        ObjectId Find(string handle)
        {
            if (owner is not null)
                return owner.Cast<ObjectId>().FirstOrDefault(id => id.NonForwardedHandle.ToString().Equals(handle, StringComparison.OrdinalIgnoreCase));
            return LegendManagement.ResolveHandles(db, new[] { handle }, out _).FirstOrDefault();
        }
        foreach (var id in link.ContainerHandles.Select(Find))
        {
            if (id.IsNull || id.IsErased || tr.GetObject(id, OpenMode.ForRead) is not BlockReference reference)
                throw new InvalidOperationException("Een gekoppeld bronblok of een xref ontbreekt.");
            RequireVisible(reference);
            var layer = DrawingAnalyzer.EntityLayerStyle(reference, tr, parentLayer, viewport);
            parent = DrawingAnalyzer.EntityStyle(reference, tr, parent, layer); parentLayer = layer;
            owner = (BlockTableRecord)tr.GetObject(reference.BlockTableRecord, OpenMode.ForRead);
            if (owner.IsFromExternalReference)
            {
                if (owner.IsUnloaded) throw new InvalidOperationException("De gekoppelde xref is ontladen.");
                drawing = (owner.GetXrefDatabase(false) ?? throw new InvalidOperationException("De gekoppelde xref is niet beschikbaar.")).FingerprintGuid.ToString();
            }
        }
        if (link.DrawingId.Length > 0 && drawing != link.DrawingId) throw new InvalidOperationException("De bron is vervangen door een andere tekening; koppel het voorbeeld opnieuw.");
        var objectId = Find(link.ObjectHandle);
        if (objectId.IsNull || objectId.IsErased || tr.GetObject(objectId, OpenMode.ForRead) is not Entity entity)
            throw new InvalidOperationException("Het gekoppelde bronobject ontbreekt.");
        var sourceLayer = DrawingAnalyzer.EntityLayerStyle(entity, tr, parentLayer, viewport);
        RequireVisible(entity);
        var sourceStyle = link.Component.Length > 0 ? CivilProfileExamples.Display(entity, tr, link, parent, parentLayer, viewport)
            : DrawingAnalyzer.EntityStyle(entity, tr, parent, sourceLayer);
        var display = link.Component.Length == 0 && entity is BlockReference
            ? ManagedLegendExamples.Read(db, tr, new[] { entity.ObjectId }, sourceStyle, sourceLayer, viewport, includeText: true) : new[] { ManagedLegendExamples.Display(sourceStyle) };
        return new(entity, sourceStyle, sourceLayer, display, viewport);
    }

    internal static void Refresh(Database db, Transaction tr, LegendDefinition definition)
    {
        foreach (var manual in definition.Settings.ManualEntries.Where(m => m.Link is not null && m.ExampleMode == ExampleMode.FollowSource))
        {
            LinkedSource source;
            try { source = Resolve(db, tr, manual.Link!); }
            catch (System.Exception ex) when (!ExceptionSafety.IsFatal(ex)) { continue; }
            manual.SampleBlock = Capture(db, tr, source, manual.Link!, definition.Settings);
            manual.SymbolBlock = manual.SampleBlock; manual.SampleScale = definition.Settings.Scale;
        }
    }

    internal static string Capture(Database db, Transaction tr, LinkedSource source, LegendObjectLink link, LegendSettings settings)
    {
        var previous = HostApplicationServices.WorkingDatabase;
        try { HostApplicationServices.WorkingDatabase = db; return CaptureCore(db, tr, source, link, settings); }
        finally { HostApplicationServices.WorkingDatabase = previous; }
    }

    private static string CaptureCore(Database db, Transaction tr, LinkedSource source, LegendObjectLink link, LegendSettings settings)
    {
        string result;
        if (link.Component.Length == 0)
            result = LegendRowSamples.Capture(db, tr, new[] { source.Entity.ObjectId }, source.Style, source.Layer, source.ViewportId);
        else
        {
            var blocks = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForWrite);
            using var sample = new BlockTableRecord { Name = "NLCS_ROW_" + Guid.NewGuid().ToString("N") };
            blocks.Add(sample); tr.AddNewlyCreatedDBObject(sample, true);
            using var imports = new LegendBuilder.SymbolImportSession(db, tr);
            var ids = new List<ObjectId>();
            void Add(Entity entity)
            {
                entity.SetDatabaseDefaults(db); entity.Layer = "0";
                LegendBuilder.ApplyEntityStyle(entity, source.Style, imports);
                sample.AppendEntity(entity); tr.AddNewlyCreatedDBObject(entity, true); ids.Add(entity.ObjectId);
            }
            if (link.Component.StartsWith("Structure", StringComparison.Ordinal) || link.Component.Contains("Hatch", StringComparison.Ordinal))
            {
                var shape = CivilProfileExamples.ProfileShape(source.Entity, tr, link.ProfileViewHandle);
                string prefix = source.Entity.Layer.Contains('|') ? source.Entity.Layer[..source.Entity.Layer.LastIndexOf('|')] : "";
                var imported = imports.ImportExploded(shape.ObjectId, prefix);
                string name = ((BlockTableRecord)tr.GetObject(imported, OpenMode.ForRead)).Name;
                var rendering = new SymbolSample(name, "", SymbolTransform.Identity) { InsertionStyle = source.Style, InsertionLayerStyle = source.Layer };
                if (!LegendBuilder.TryInsertSymbol(sample, tr, db, imports, name, "0", SymbolTransform.Identity, rendering,
                    0, 0, 1, true, out var created, out var error)) throw new InvalidOperationException(error);
                foreach (var id in created)
                {
                    var entity = (Entity)tr.GetObject(id, OpenMode.ForWrite);
                    if (entity is DBText or MText || link.Component.Contains("Hatch", StringComparison.Ordinal) != (entity is Hatch)) { entity.Erase(); continue; }
                    LegendBuilder.ApplyEntityStyle(entity, source.Style, imports); ids.Add(id);
                }
                if (ids.Count == 0) throw new InvalidOperationException("Deze Civil 3D-component levert geen zelfstandig voorbeeld. Kies een zichtbare lijncomponent of een getekend voorbeeld.");
            }
            else
            {
                double width = settings.ToModel(settings.SwatchWidthMm), gap = settings.ToModel(settings.SwatchHeightMm) / 2;
                using var line = new Line(Point3d.Origin, new(width, 0, 0));
                Add(line);
                if (link.Component.EndsWith("Walls", StringComparison.Ordinal))
                {
                    using var other = new Line(new(0, gap, 0), new(width, gap, 0));
                    Add(other);
                }
            }
            result = LegendRowSamples.Capture(db, tr, ids);
            foreach (var id in ids) tr.GetObject(id, OpenMode.ForWrite).Erase();
            sample.Erase();
        }
        Normalize(db, tr, result, settings.ToModel(settings.SwatchWidthMm), settings.ToModel(settings.SwatchHeightMm));
        return result;
    }

    private static void Normalize(Database db, Transaction tr, string name, double width, double height)
    {
        var table = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
        var block = (BlockTableRecord)tr.GetObject(table[name], OpenMode.ForRead);
        var entities = block.Cast<ObjectId>().Select(id => tr.GetObject(id, OpenMode.ForWrite)).OfType<Entity>().ToArray();
        var extents = entities.Select(e => LegendManagement.TryGetEntityExtents(tr, e, out var box) ? box : (Extents3d?)null).OfType<Extents3d>().ToArray();
        if (extents.Length == 0) throw new InvalidOperationException("Het bronvoorbeeld heeft geen zichtbare geometrie.");
        var bounds = new Extents3d();
        foreach (var extent in extents) bounds.AddExtents(extent);
        double factor = Math.Min(width / Math.Max(1e-9, bounds.MaxPoint.X - bounds.MinPoint.X), height / Math.Max(1e-9, bounds.MaxPoint.Y - bounds.MinPoint.Y));
        var center = bounds.MinPoint + (bounds.MaxPoint - bounds.MinPoint) / 2;
        var transform = Matrix3d.Scaling(factor, Point3d.Origin) * Matrix3d.Displacement(-center.GetAsVector());
        foreach (var entity in entities) entity.TransformBy(transform);
    }
}
