using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using NlcsLegenda.Core;

namespace NlcsLegenda.Plugin;

// Groep-, handle- en locator-helpers voor beheerde legenda's. Alles werkt op een bestaande
// transactie zodat registry en geometrie samen committen.
internal static class LegendManagement
{
    // Alle ObjectIds die bij een beheerde legenda horen. De analyse sluit deze uit, zodat
    // geplaatste legenda's de brontelling niet beïnvloeden.
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
                // Als een behouden legenda-blok is geselecteerd, sluit ook zijn inhoud uit.
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
        var idc = new ObjectIdCollection();
        foreach (var id in ids) idc.Add(id);
        if (idc.Count == 0) return;

        var gd = (DBDictionary)tr.GetObject(db.GroupDictionaryId, OpenMode.ForWrite);
        Group group;
        if (gd.Contains(groupName))
        {
            group = (Group)tr.GetObject(gd.GetAt(groupName), OpenMode.ForWrite);
        }
        else
        {
            group = new Group("NLCS-legenda", true);
            gd.SetAt(groupName, group);
            tr.AddNewlyCreatedDBObject(group, true);
        }
        group.Append(idc);
    }

    // Wist de geometrie van een groep en geeft de huidige linksbovenhoek terug, zodat een
    // update op dezelfde plek komt. Geeft false als er geen bruikbare geometrie was.
    public static bool TryEraseGroup(Database db, Transaction tr, string groupName, out Point3d topLeft)
    {
        topLeft = Point3d.Origin;
        var gd = (DBDictionary)tr.GetObject(db.GroupDictionaryId, OpenMode.ForRead);
        if (!gd.Contains(groupName))
            return false;

        var group = (Group)tr.GetObject(gd.GetAt(groupName), OpenMode.ForWrite);
        var ids = group.GetAllEntityIds();
        double minX = double.MaxValue, maxY = double.MinValue;
        bool any = false;
        foreach (var id in ids)
        {
            if (tr.GetObject(id, OpenMode.ForWrite) is not Entity ent || ent.IsErased)
                continue;
            var ext = ent.Bounds;
            if (ext.HasValue)
            {
                minX = Math.Min(minX, ext.Value.MinPoint.X);
                maxY = Math.Max(maxY, ext.Value.MaxPoint.Y);
                any = true;
            }
            ent.Erase();
        }
        group.Erase();
        if (any)
            topLeft = new Point3d(minX, maxY, 0);
        return any;
    }

    public static bool TryGetGroupExtents(Database db, Transaction tr, string groupName, out Extents3d extents)
    {
        extents = new Extents3d();
        var gd = (DBDictionary)tr.GetObject(db.GroupDictionaryId, OpenMode.ForRead);
        if (!gd.Contains(groupName) || tr.GetObject(gd.GetAt(groupName), OpenMode.ForRead) is not Group g)
            return false;
        bool any = false;
        foreach (var id in g.GetAllEntityIds())
        {
            if (tr.GetObject(id, OpenMode.ForRead) is Entity ent && !ent.IsErased && ent.Bounds.HasValue)
            {
                extents.AddExtents(ent.Bounds.Value);
                any = true;
            }
        }
        return any;
    }

    // Resolvet opgeslagen Handles naar bestaande ObjectIds; ontbrekende handles worden
    // geteld maar overgeslagen (geen crash door een verdwenen bronobject).
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
            catch
            {
                miss++;
            }
        }
        missing = miss;
        return result.ToArray();
    }

    public static List<string> ToHandles(IEnumerable<ObjectId> ids)
    {
        var list = new List<string>();
        foreach (var id in ids)
            if (!id.IsNull && !id.IsErased)
                list.Add(id.Handle.Value.ToString("X"));
        return list;
    }

    // Vindt de beheerde legenda waartoe een aangeklikt object hoort, via group-lidmaatschap
    // (de registry/group is de waarheid, niet een gekopieerde marker).
    public static LegendDefinition? FindLegendForEntity(
        Database db, Transaction tr, LegendRegistry registry, ObjectId entId)
    {
        if (entId.IsNull) return null;
        var gd = (DBDictionary)tr.GetObject(db.GroupDictionaryId, OpenMode.ForRead);
        foreach (var def in registry.Legends)
        {
            if (!gd.Contains(def.GroupName)) continue;
            if (tr.GetObject(gd.GetAt(def.GroupName), OpenMode.ForRead) is Group g
                && GroupContains(g, tr, entId))
                return def;
        }
        return null;
    }

    private static bool GroupContains(Group g, Transaction tr, ObjectId target)
    {
        foreach (ObjectId id in g.GetAllEntityIds())
        {
            if (id == target) return true;
            // Aangeklikte losse entiteit binnen een behouden legenda-blok.
            if (tr.GetObject(id, OpenMode.ForRead) is BlockReference && id == target) return true;
        }
        return false;
    }

}
