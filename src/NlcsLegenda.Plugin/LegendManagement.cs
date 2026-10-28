using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using NlcsLegenda.Core;

namespace NlcsLegenda.Plugin;

internal static class LegendManagement
{
    // registratie behouden voor herstel via ongedaan maken
    public static LegendRegistry ActiveRegistry(Database db, Transaction tr, LegendRegistry registry)
    {
        var groups = (DBDictionary)tr.GetObject(db.GroupDictionaryId, OpenMode.ForRead);
        return new LegendRegistry
        {
            Legends = registry.Legends.Where(def => groups.Contains(def.GroupName)
                && tr.GetObject(groups.GetAt(def.GroupName), OpenMode.ForRead) is Group group
                && group.GetAllEntityIds().Any(id => !id.IsNull && id.IsValid && !id.IsErased
                    && tr.GetObject(id, OpenMode.ForRead) is Entity)).ToList()
        };
    }

    internal static IReadOnlyList<string> PruneEmpty(Database db, Transaction tr, LegendRegistry registry, string keepId = "")
    {
        var removed = new List<string>();
        var groups = (DBDictionary)tr.GetObject(db.GroupDictionaryId, OpenMode.ForRead);
        foreach (var definition in registry.Legends.Where(d => d.Id != keepId && d.Rows.Count == 0
            && d.Settings.ManualEntries.Count == 0 && d.Settings.BlankEntries.Count == 0).ToArray())
        {
            if (LegendViewport.Linked(db, tr, definition.Id).Any()) continue;
            Group? group = groups.Contains(definition.GroupName) ? tr.GetObject(groups.GetAt(definition.GroupName), OpenMode.ForRead) as Group : null;
            if (group?.GetAllEntityIds().Any(id => !id.IsNull && id.IsValid && !id.IsErased) == true) continue;
            if (group is not null) { group.UpgradeOpen(); group.Erase(); }
            registry.Remove(definition.Id); removed.Add(definition.Name);
        }
        return removed;
    }

    public static HashSet<ObjectId> CollectManagedIds(Database db, Transaction tr, LegendRegistry registry)
    {
        var ids = new HashSet<ObjectId>();
        var gd = (DBDictionary)tr.GetObject(db.GroupDictionaryId, OpenMode.ForRead);

        void AddGroup(string name)
        {
            if (!gd.Contains(name)) return;
            if (tr.GetObject(gd.GetAt(name), OpenMode.ForRead) is not Group g) return;
            foreach (ObjectId id in g.GetAllEntityIds())
            {
                ids.Add(id);
                if (tr.GetObject(id, OpenMode.ForRead) is BlockReference br && !br.BlockTableRecord.IsNull)
                    AddBlockContents(br.BlockTableRecord, tr, ids, 0);
            }
        }

        foreach (var def in registry.Legends)
            AddGroup(def.GroupName);
        return ids;
    }

    private static void AddBlockContents(ObjectId btrId, Transaction tr, HashSet<ObjectId> ids, int depth)
    {
        if (depth > 8 || tr.GetObject(btrId, OpenMode.ForRead) is not BlockTableRecord btr)
            return;
        foreach (ObjectId id in btr)
        {
            ids.Add(id);
            if (tr.GetObject(id, OpenMode.ForRead) is BlockReference br && !br.BlockTableRecord.IsNull)
                AddBlockContents(br.BlockTableRecord, tr, ids, depth + 1);
        }
    }

    public static void AddToGroup(Database db, Transaction tr, string groupName, IEnumerable<ObjectId> ids)
    {
        using var idc = new ObjectIdCollection();
        foreach (var id in ids) idc.Add(id);
        if (idc.Count == 0) return;

        var gd = (DBDictionary)tr.GetObject(db.GroupDictionaryId, OpenMode.ForWrite);
        using var created = gd.Contains(groupName) ? null : new Group("NLCS-legenda", true);
        var group = created ?? (Group)tr.GetObject(gd.GetAt(groupName), OpenMode.ForWrite);
        if (created is not null)
        {
            gd.SetAt(groupName, created);
            tr.AddNewlyCreatedDBObject(created, true);
        }
        group.Append(idc);
    }

    internal static IEnumerable<Entity> Entities(IEnumerable<ObjectId> ids, Transaction tr, OpenMode mode) =>
        ids.Where(id => !id.IsNull && id.IsValid && !id.IsErased)
            .Select(id => tr.GetObject(id, mode)).OfType<Entity>();

    public static bool TryEraseGroup(Database db, Transaction tr, string groupName, out Point3d topLeft, bool keepGroup = false)
    {
        topLeft = Point3d.Origin;
        var gd = (DBDictionary)tr.GetObject(db.GroupDictionaryId, OpenMode.ForRead);
        if (!gd.Contains(groupName))
            return false;

        var group = (Group)tr.GetObject(gd.GetAt(groupName), OpenMode.ForWrite);
        var ids = group.GetAllEntityIds();
        double minX = double.MaxValue, maxY = double.MinValue;
        bool any = false;
        Point3d? emptyPosition = null;
        foreach (var ent in Entities(ids, tr, OpenMode.ForWrite))
        {
            if (TryEntityExtents(tr, ent, Matrix3d.Identity, 0, out var ext))
            {
                minX = Math.Min(minX, ext.MinPoint.X);
                maxY = Math.Max(maxY, ext.MaxPoint.Y);
                any = true;
            }
            if (ent is BlockReference reference) emptyPosition ??= reference.Position;
            ent.Erase();
        }
        if (keepGroup)
        {
            // groep behouden tegen verweesde legendadelen
        }
        else
        {
            group.Erase();
        }
        if (any) topLeft = new Point3d(minX, maxY, 0);
        else if (emptyPosition is { } point) topLeft = point;
        return any || emptyPosition.HasValue;
    }

    public static bool TryGetGroupExtents(Database db, Transaction tr, string groupName, out Extents3d extents)
    {
        extents = new Extents3d();
        var gd = (DBDictionary)tr.GetObject(db.GroupDictionaryId, OpenMode.ForRead);
        if (!gd.Contains(groupName) || tr.GetObject(gd.GetAt(groupName), OpenMode.ForRead) is not Group g)
            return false;
        bool any = false;
        foreach (var measured in MeasuredExtents(Entities(g.GetAllEntityIds(), tr, OpenMode.ForRead), tr, Matrix3d.Identity, 0))
        {
            extents.AddExtents(measured);
            any = true;
        }
        return any;
    }

    // blokinhoud meten bij ontbrekende grafische grenzen
    public static bool TryGetEntityExtents(Transaction tr, Entity ent, out Extents3d extents)
        => TryEntityExtents(tr, ent, Matrix3d.Identity, 0, out extents);

    private static IEnumerable<Extents3d> MeasuredExtents(IEnumerable<Entity> entities, Transaction tr, Matrix3d transform, int depth) =>
        entities.Select(entity => TryEntityExtents(tr, entity, transform, depth, out var extents) ? (Extents3d?)extents : null)
            .OfType<Extents3d>();

    private static bool TryEntityExtents(Transaction tr, Entity ent, Matrix3d xform, int depth, out Extents3d extents)
    {
        extents = new Extents3d();
        if (depth > 8) return false;
        if (ent is BlockReference br && !br.BlockTableRecord.IsNull
            && tr.GetObject(br.BlockTableRecord, OpenMode.ForRead) is BlockTableRecord btr)
        {
            var m = xform * br.BlockTransform;
            bool any = false;
            foreach (var measured in MeasuredExtents(Entities(btr.Cast<ObjectId>(), tr, OpenMode.ForRead), tr, m, depth + 1))
            {
                extents.AddExtents(measured);
                any = true;
            }
            return any;
        }

        Extents3d? b = null;
        try { b = ent.Bounds; } catch (Autodesk.AutoCAD.Runtime.Exception) { b = null; }
        if (!b.HasValue) return false;
        var val = b.Value;
        if (!xform.IsEqualTo(Matrix3d.Identity))
            val.TransformBy(xform);
        extents = val;
        return true;
    }

    public static ObjectId[] ResolveHandles(Database db, IEnumerable<string> handles, out int missing)
    {
        var result = new List<ObjectId>();
        int miss = 0;
        foreach (var h in handles)
        {
            try
            {
                var handle = new Handle(Convert.ToInt64(h, 16));
                if (db.TryGetObjectId(handle, out var id) && !id.IsErased)
                    result.Add(id);
                else
                    miss++;
            }
            catch (Exception ex) when (ex is FormatException or OverflowException or ArgumentException or Autodesk.AutoCAD.Runtime.Exception)
            {
                miss++;
            }
        }
        missing = miss;
        return result.ToArray();
    }

    public static List<string> ToHandles(IEnumerable<ObjectId> ids)
    {
        return ids.Where(id => !id.IsNull && !id.IsErased).Select(id => id.Handle.Value.ToString("X")).ToList();
    }

    private const string RowApplication = "NLCS_LEGENDA_ROW";

    public static void TagRow(Database db, Transaction tr, IEnumerable<ObjectId> ids, NlcsLegenda.Core.LegendEntry entry)
    {
        var apps = (RegAppTable)tr.GetObject(db.RegAppTableId, OpenMode.ForRead);
        if (!apps.Has(RowApplication))
        {
            apps.UpgradeOpen();
            using var app = new RegAppTableRecord { Name = RowApplication };
            apps.Add(app);
            tr.AddNewlyCreatedDBObject(app, true);
        }
        bool editable = !entry.IsBlank && !entry.IsManual && entry.DescriptionSource != DescriptionSource.EigenKoppeling;
        string token = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(System.Text.Json.JsonSerializer.Serialize(entry.ExclusionKeys))));
        using var data = new ResultBuffer(new TypedValue(1001, RowApplication),
            new TypedValue(1000, editable ? entry.Hoofdgroep : ""), new TypedValue(1000, editable ? entry.Element : ""),
            new TypedValue(1000, token));
        foreach (var id in ids)
        {
            var entity = (Entity)tr.GetObject(id, OpenMode.ForWrite);
            entity.XData = data;
            WriteRowKeys(entity, tr, entry.ExclusionKeys);
        }
    }

    internal static string? RowToken(Entity entity)
    {
        using var data = entity.GetXDataForApplication(RowApplication);
        var values = data?.AsArray();
        return values is { Length: >= 4 } ? values[3].Value as string : null;
    }

    internal static void WriteRowKeys(Entity entity, Transaction tr, IReadOnlyList<string> keys)
    {
        string encoded = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(System.Text.Json.JsonSerializer.Serialize(keys)));
        var chunks = encoded.Chunk(200).Select(c => new TypedValue((int)DxfCode.Text, new string(c))).ToArray();
        if (entity.ExtensionDictionary.IsNull) entity.CreateExtensionDictionary();
        var dictionary = (DBDictionary)tr.GetObject(entity.ExtensionDictionary, OpenMode.ForWrite);
        using var record = new Xrecord();
        using var values = new ResultBuffer(chunks);
        record.Data = values;
        dictionary.SetAt(RowApplication, record);
        tr.AddNewlyCreatedDBObject(record, true);
    }

    internal static IReadOnlyList<string> ReadRowKeys(Entity entity, Transaction tr)
    {
        if (entity.ExtensionDictionary.IsNull) return Array.Empty<string>();
        var dictionary = (DBDictionary)tr.GetObject(entity.ExtensionDictionary, OpenMode.ForRead);
        if (!dictionary.Contains(RowApplication)) return Array.Empty<string>();
        var record = (Xrecord)tr.GetObject(dictionary.GetAt(RowApplication), OpenMode.ForRead);
        using var data = record.Data;
        if (data is null) return Array.Empty<string>();
        string encoded = string.Concat(data.AsArray().Select(v => v.Value as string));
        try { return System.Text.Json.JsonSerializer.Deserialize<string[]>(Convert.FromBase64String(encoded)) ?? Array.Empty<string>(); }
        catch (Exception ex) when (ex is FormatException or System.Text.Json.JsonException) { return Array.Empty<string>(); }
    }

    public static (string Hoofdgroep, string Element)? ReadRow(Entity entity)
    {
        using var data = entity.GetXDataForApplication(RowApplication);
        var values = data?.AsArray();
        return values is { Length: >= 3 } && values[1].Value is string { Length: > 0 } group && values[2].Value is string { Length: > 0 } element
            ? (group, element) : null;
    }

    public static LegendDefinition? FindLegendForEntity(
        Database db, Transaction tr, LegendRegistry registry, ObjectId entId)
    {
        if (entId.IsNull) return null;
        var gd = (DBDictionary)tr.GetObject(db.GroupDictionaryId, OpenMode.ForRead);
        foreach (var def in registry.Legends.Where(def => gd.Contains(def.GroupName)))
        {
            if (tr.GetObject(gd.GetAt(def.GroupName), OpenMode.ForRead) is Group g
                && g.GetAllEntityIds().Contains(entId))
                return def;
        }
        return null;
    }

}
