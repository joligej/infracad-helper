using System.Collections;
using System.Reflection;
using Autodesk.AutoCAD.DatabaseServices;
using NlcsLegenda.Core;

namespace NlcsLegenda.Plugin;

internal static class CivilProfileExamples
{
    internal static object? Property(object instance, string name)
    {
        for (var type = instance.GetType(); type is not null; type = type.BaseType)
            if (type.GetProperty(name, BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly) is { CanRead: true } property)
                return property.GetValue(instance);
        return null;
    }
    private static ObjectId Id(object instance, string name) => Property(instance, name) is ObjectId id ? id : ObjectId.Null;
    public static bool Supports(Entity entity) => entity.GetType().FullName is "Autodesk.Civil.DatabaseServices.Profile"
        or "Autodesk.Civil.DatabaseServices.ProfileViewPart" or "Autodesk.Civil.DatabaseServices.Pipe" or "Autodesk.Civil.DatabaseServices.Structure";

    private static Entity ModelPart(Entity entity, Transaction tr) => entity.GetType().Name == "ProfileViewPart"
        ? (Entity)tr.GetObject(Id(entity, "ModelPartId"), OpenMode.ForRead) : entity;

    private static string Collection(Entity entity) => entity.GetType().Name switch
    { "Profile" => "GraphOverrides", "Pipe" => "PipeOverrides", "Structure" => "StructureOverrides", _ => "" };
    private static string PartProperty(Entity entity) => entity.GetType().Name switch
    { "Profile" => "ProfileId", "Pipe" => "PipeId", "Structure" => "StructId", _ => "" };

    private sealed record ViewOverride(bool Draw, bool UseStyle, ObjectId StyleId);

    private static ViewOverride? Override(Entity source, Entity view, Transaction tr)
    {
        var part = ModelPart(source, tr); var collection = Property(view, Collection(part));
        using var ownedCollection = collection as IDisposable;
        if (collection is not IEnumerable items) return null;
        foreach (var item in items.Cast<object>())
        {
            using var ownedItem = item as IDisposable;
            var id = Id(item, PartProperty(part)); if (id.IsNull) continue;
            var candidate = tr.GetObject(id, OpenMode.ForRead) as Entity;
            if (candidate is not null && candidate.GetType().Name == "ProfileViewPart") id = Id(candidate, "ModelPartId");
            if (!id.IsNull && id.NonForwardedHandle == part.ObjectId.NonForwardedHandle)
                return new(Property(item, "Draw") is not false, Property(item, "UseOverrideStyle") is true, Id(item, "OverrideStyleId"));
        }
        return null;
    }

    private static DBObject Style(Entity source, Transaction tr, string viewHandle)
    {
        var part = ModelPart(source, tr); var style = Id(part, "StyleId");
        if (viewHandle.Length > 0)
        {
            var owner = (BlockTableRecord)tr.GetObject(source.OwnerId, OpenMode.ForRead);
            var view = owner.Cast<ObjectId>().Select(id => tr.GetObject(id, OpenMode.ForRead)).OfType<Entity>()
                .FirstOrDefault(e => e.ObjectId.NonForwardedHandle.ToString() == viewHandle && e.GetType().Name == "ProfileView");
            if (view is null) throw new InvalidOperationException("Het gekoppelde Civil 3D-profielvenster ontbreekt.");
            var item = Override(source, view, tr) ?? throw new InvalidOperationException("Het onderdeel staat niet meer in het gekoppelde profielvenster.");
            if (!item.Draw) throw new InvalidOperationException("Het Civil 3D-onderdeel staat uit in het profielvenster.");
            if (item.UseStyle) style = item.StyleId;
        }
        if (style.IsNull) throw new InvalidOperationException("De Civil 3D-profielstijl ontbreekt.");
        return tr.GetObject(style, OpenMode.ForRead);
    }

    internal static Entity ProfileShape(Entity source, Transaction tr, string viewHandle)
    {
        if (source.GetType().Name == "ProfileViewPart") return source;
        var owner = (BlockTableRecord)tr.GetObject(source.OwnerId, OpenMode.ForRead);
        var parts = owner.Cast<ObjectId>().Select(id => tr.GetObject(id, OpenMode.ForRead)).OfType<Entity>()
            .Where(e => e.GetType().Name == "ProfileViewPart" && !Id(e, "ModelPartId").IsNull
                && Id(e, "ModelPartId").NonForwardedHandle == source.ObjectId.NonForwardedHandle).ToArray();
        if (viewHandle.Length > 0)
        {
            var view = owner.Cast<ObjectId>().Select(id => tr.GetObject(id, OpenMode.ForRead)).OfType<Entity>()
                .FirstOrDefault(e => e.ObjectId.NonForwardedHandle.ToString() == viewHandle && e.GetType().Name == "ProfileView");
            if (view is not null && LegendManagement.TryGetEntityExtents(tr, view, out var bounds))
                parts = parts.Where(e => LegendManagement.TryGetEntityExtents(tr, e, out var box)
                    && box.MinPoint.X <= bounds.MaxPoint.X && box.MaxPoint.X >= bounds.MinPoint.X
                    && box.MinPoint.Y <= bounds.MaxPoint.Y && box.MaxPoint.Y >= bounds.MinPoint.Y).ToArray();
        }
        if (parts.Length != 1) throw new InvalidOperationException("Kies de put of arcering rechtstreeks in het gewenste profielvenster.");
        return parts[0];
    }

    public static IReadOnlyList<CivilExampleComponent> Components(Entity source, Transaction tr)
    {
        if (!Supports(source)) return Array.Empty<CivilExampleComponent>();
        var result = new List<CivilExampleComponent>();
        var owner = (BlockTableRecord)tr.GetObject(source.OwnerId, OpenMode.ForRead);
        var views = owner.Cast<ObjectId>().Select(id => tr.GetObject(id, OpenMode.ForRead)).OfType<Entity>()
            .Where(e => e.GetType().Name == "ProfileView" && Override(source, e, tr) is { Draw: true }).ToArray();
        if (source.GetType().Name == "ProfileViewPart" && LegendManagement.TryGetEntityExtents(tr, source, out var partBounds))
            views = views.Where(view => LegendManagement.TryGetEntityExtents(tr, view, out var box)
                && partBounds.MinPoint.X <= box.MaxPoint.X && partBounds.MaxPoint.X >= box.MinPoint.X
                && partBounds.MinPoint.Y <= box.MaxPoint.Y && partBounds.MaxPoint.Y >= box.MinPoint.Y).ToArray();
        void Add(string viewHandle, string prefix)
        {
            var style = Style(source, tr, viewHandle); var method = style.GetType().GetMethod("GetDisplayStyleProfile");
            if (method is null) return;
            foreach (var component in Enum.GetValues(method.GetParameters()[0].ParameterType))
            {
                var display = method.Invoke(style, new[] { component });
                using var ownedDisplay = display as IDisposable;
                if (display is not null && SupportedComponent(component.ToString()!) && Property(display, "Visible") is true)
                    result.Add(new(component.ToString()!, prefix + " · " + Label(component.ToString()!), viewHandle));
            }
        }
        if (views.Length > 0)
            foreach (var view in views) Add(view.ObjectId.NonForwardedHandle.ToString(), Property(view, "Name")?.ToString() ?? "Profielvenster");
        else Add("", "Profielstijl van onderdeel");
        return result;
    }

    private static bool SupportedComponent(string component) => component is "Line" or "Curve" or "Centerline"
        or "InsideWalls" or "OutsideWalls" or "EndLine" or "Structure" or "StructureHatch";

    private static string Label(string component) => component switch
    {
        "Line" => "Lijn", "Curve" => "Boog", "Centerline" => "Hartlijn", "InsideWalls" => "Binnenwanden",
        "OutsideWalls" => "Buitenwanden", "EndLine" => "Eindlijn", "Structure" => "Put", "StructureHatch" => "Putarcering",
        "CrossingPipeInsideWall" => "Kruising binnenwand", "CrossingPipeOutsideWall" => "Kruising buitenwand",
        "CrossingPipeHatch" => "Kruising arcering", _ => component
    };

    public static LayerDisplayStyle Display(Entity source, Transaction tr, LegendObjectLink link, LayerDisplayStyle? parent, LayerDisplayStyle? parentLayer, ObjectId viewport = default)
    {
        if (!SupportedComponent(link.Component)) throw new InvalidOperationException("Kies een lijn, wand, put of putarcering. Neem andere markeringen als getekend bronvoorbeeld over.");
        var style = Style(source, tr, link.ProfileViewHandle);
        var method = style.GetType().GetMethod("GetDisplayStyleProfile") ?? throw new InvalidOperationException("De Civil 3D-profielstijl wordt niet ondersteund.");
        var component = Enum.Parse(method.GetParameters()[0].ParameterType, link.Component);
        var display = method.Invoke(style, new[] { component }) ?? throw new InvalidOperationException("De Civil 3D-weergave ontbreekt.");
        using var ownedDisplay = display as IDisposable;
        if (Property(display, "Visible") is not true) throw new InvalidOperationException("De gekoppelde Civil 3D-stijlcomponent staat uit.");
        var layerName = Property(display, "Layer") as string ?? source.Layer;
        var layers = (LayerTable)tr.GetObject(source.Database.LayerTableId, OpenMode.ForRead);
        string prefix = source.Layer.Contains('|') ? source.Layer[..(source.Layer.LastIndexOf('|') + 1)] : "";
        if (layers.Has(prefix + layerName)) layerName = prefix + layerName;
        var layer = LayerNaming.LocalName(layerName) == "0" ? parentLayer ?? DrawingAnalyzer.EntityLayerStyle(source, tr, viewport: viewport)
            : layers.Has(layerName) ? SourceViewport.LayerStyle((LayerTableRecord)tr.GetObject(layers[layerName], OpenMode.ForRead), tr, viewport)
            : throw new InvalidOperationException("De laag van de Civil 3D-stijl ontbreekt: " + layerName);
        var color = Property(display, "Color") as Autodesk.AutoCAD.Colors.Color ?? layer.Color;
        string line = Property(display, "Linetype") as string ?? "ByLayer";
        var inherited = line.Equals("ByLayer", StringComparison.OrdinalIgnoreCase) ? layer
            : line.Equals("ByBlock", StringComparison.OrdinalIgnoreCase) ? parent ?? layer : null;
        var lines = (LinetypeTable)tr.GetObject(source.Database.LinetypeTableId, OpenMode.ForRead);
        if (inherited is null && lines.Has(prefix + line)) line = prefix + line;
        string identity = inherited?.LinetypeIdentity ?? (lines.Has(line) ? DrawingAnalyzer.LinetypeSignature(lines[line], tr)
            : throw new InvalidOperationException("Het lijntype van de Civil 3D-stijl ontbreekt: " + line));
        var weight = Property(display, "Lineweight") is LineWeight value ? value : LineWeight.ByLayer;
        return new LayerDisplayStyle { Color = DrawingAnalyzer.ResolveColor(color, parent, layer),
            Linetype = inherited?.Linetype ?? line, LinetypeIdentity = identity,
            LinetypeScale = Property(display, "LinetypeScale") is double scale ? scale : 1,
            LineWeight = weight == LineWeight.ByLayer ? layer.LineWeight : weight == LineWeight.ByBlock ? parent?.LineWeight ?? layer.LineWeight : weight,
            Transparency = layer.Transparency };
    }
}
