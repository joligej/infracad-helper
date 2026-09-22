using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using NlcsLegenda.Core;

namespace NlcsLegenda.Plugin;

public partial class Commands
{
    private static List<ObjectId> ExplodeToModelspace(Transaction tr, Database db, BlockReference br)
    {
        var ms = (BlockTableRecord)tr.GetObject(
            SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForWrite);
        var pieces = new DBObjectCollection();
        br.Explode(pieces);
        var ids = new List<ObjectId>();
        foreach (DBObject piece in pieces)
        {
            if (piece is Entity entity)
            {
                ids.Add(ms.AppendEntity(entity));
                tr.AddNewlyCreatedDBObject(entity, true);
            }
        }
        return ids;
    }

    private static List<ObjectId> FinalizePlacement(
        Transaction tr, Database db, BlockReference br, LegendSettings s)
    {
        List<ObjectId> ids;
        if (s.ExplodeOnPlace)
        {
            ids = ExplodeToModelspace(tr, db, br);
            br.Erase();
        }
        else
        {
            // Als blok behouden: op de kaderlaag zetten zodat viewport het terugvindt.
            br.Layer = s.FrameLayer;
            ids = new List<ObjectId> { br.ObjectId };
        }

        AddToLegendGroup(tr, db, ids);
        return ids;
    }

    private const string LegendGroupName = "NLCS-Legenda";

    private static void AddToLegendGroup(
        Transaction tr, Database db, IEnumerable<ObjectId> ids)
    {
        var idc = new ObjectIdCollection();
        foreach (var id in ids)
            idc.Add(id);
        if (idc.Count == 0)
            return;

        var gd = (DBDictionary)tr.GetObject(db.GroupDictionaryId, OpenMode.ForWrite);
        Group group;
        if (gd.Contains(LegendGroupName))
        {
            group = (Group)tr.GetObject(gd.GetAt(LegendGroupName), OpenMode.ForWrite);
        }
        else
        {
            group = new Group("NLCS-legenda (automatisch)", true);
            gd.SetAt(LegendGroupName, group);
            tr.AddNewlyCreatedDBObject(group, true);
        }
        group.Append(idc);
    }

    private static bool TryEraseLegendGroup(Database db, Transaction tr, out Point3d topLeft)
    {
        topLeft = Point3d.Origin;
        var gd = (DBDictionary)tr.GetObject(db.GroupDictionaryId, OpenMode.ForRead);
        if (!gd.Contains(LegendGroupName))
            return false;

        var group = (Group)tr.GetObject(gd.GetAt(LegendGroupName), OpenMode.ForWrite);
        var ids = group.GetAllEntityIds();
        if (ids.Length == 0)
        {
            group.Erase();
            return false;
        }

        // Bijwerken gebruikt de huidige linksbovenhoek, niet het oorspronkelijke plaatsingspunt.
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
        topLeft = new Point3d(
            any && minX < double.MaxValue ? minX : 0,
            any && maxY > double.MinValue ? maxY : 0, 0);
        return true;
    }

    private static Point3d ComputeInsertPoint(Database db, LegendSettings s)
    {
        try
        {
            var min = db.Extmin;
            var max = db.Extmax;
            if (max.X > min.X && max.Y > min.Y)
                return new Point3d(max.X + s.ToModel(20.0), max.Y, 0.0);
        }
        catch
        {
            // Ongeldige extents: val terug op de oorsprong.
        }
        return Point3d.Origin;
    }

    private static void PurgeTempBlock(Database db, ObjectId btrId)
    {
        try
        {
            using var tr = db.TransactionManager.StartTransaction();
            if (tr.GetObject(btrId, OpenMode.ForWrite, false) is BlockTableRecord btr &&
                !btr.IsErased && btr.GetBlockReferenceIds(true, false).Count == 0)
            {
                btr.Erase();
            }
            tr.Commit();
        }
        catch
        {
            // Tijdelijk blok kon niet worden verwijderd; niet kritiek.
        }
    }
}
