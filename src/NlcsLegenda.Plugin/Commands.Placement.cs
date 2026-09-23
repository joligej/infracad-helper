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
        Transaction tr, Database db, BlockReference br, LegendSettings s, string groupName)
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

        LegendManagement.AddToGroup(db, tr, groupName, ids);
        return ids;
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
