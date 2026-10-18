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
        using var transient = new TransientObjects();
        var pieces = transient.Items;
        var ids = new List<ObjectId>();
        {
            br.Explode(pieces);
            foreach (DBObject piece in pieces)
                if (piece is Entity entity)
                {
                    ids.Add(LegendBuilder.AppendEntity(ms, entity));
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
            br.Layer = s.FrameLayer;
            ids = new List<ObjectId> { br.ObjectId };
        }

        LegendManagement.AddToGroup(db, tr, groupName, ids);
        return ids;
    }

    private static LegendDefinition PlaceLegendAtPoint(
        Transaction tr, Database db, LegendRegistry registry, LegendSettings settings,
        ObjectId[]? selection, ObjectId blockRecordId, BlockReference preview, Point3d point)
    {
        RequireHostObjectId(db, blockRecordId, "legend block");
        RequireHostObjectId(db, preview.ObjectId, "preview reference");
        RequireHostObjectId(db, preview.BlockTableRecord, "preview block reference");
        if (preview.BlockTableRecord != blockRecordId)
            throw new InvalidOperationException("De preview verwijst niet naar het legenda-blok uit de hostdatabase.");

        preview.Position = point;
        var def = new LegendDefinition
        {
            Name = registry.NextDefaultName(),
            Scope = selection is { Length: > 0 } ? LegendScope.Selection : LegendScope.WholeDrawing,
            SourceHandles = selection is { Length: > 0 } ? LegendManagement.ToHandles(selection) : new(),
            GroupName = LegendRegistry.NewGroupName(),
            Settings = settings.Clone(),
            CreatedWithVersion = PluginVersion
        };
        var members = FinalizePlacement(tr, db, preview, settings, def.GroupName);
        foreach (var member in members)
            RequireHostObjectId(db, member, "placed legend entity");

        registry.Add(def);
        LegendStore.Save(db, tr, registry);
        return def;
    }

    private static void RequireHostObjectId(Database host, ObjectId id, string role)
    {
        if (id.IsNull || id.Database != host || id.OriginalDatabase != host)
            throw new InvalidOperationException($"Het {role}-ObjectId hoort niet bij de hostdatabase.");
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
        catch (Autodesk.AutoCAD.Runtime.Exception)
        {
            return Point3d.Origin;
        }
        return Point3d.Origin;
    }

    private static void PurgeTempBlock(Database db, ObjectId btrId)
    {
        if (btrId.IsNull || btrId.IsErased) return;
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
        catch (Autodesk.AutoCAD.Runtime.Exception ex)
        {
            Autodesk.AutoCAD.ApplicationServices.Core.Application.DocumentManager.MdiActiveDocument?.Editor
                .WriteMessage($"\nTijdelijke legendablokken konden niet volledig worden opgeruimd: {ex.Message}");
        }
    }

    private static void PurgeOrphanLegendBlocks(Database db)
    {
        try
        {
            using var tr = db.TransactionManager.StartTransaction();
            var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
            // geneste definities pas na het buitenblok opruimen
            while (true)
            {
                var orphans = new List<ObjectId>();
                foreach (ObjectId id in bt)
                {
                    if (id.IsErased || tr.GetObject(id, OpenMode.ForRead) is not BlockTableRecord btr) continue;
                    if (btr.Name.StartsWith("NLCS_LEGENDA_", StringComparison.OrdinalIgnoreCase)
                        && btr.GetBlockReferenceIds(true, false).Count == 0)
                        orphans.Add(id);
                }
                if (orphans.Count == 0) break;
                foreach (var id in orphans) ((BlockTableRecord)tr.GetObject(id, OpenMode.ForWrite)).Erase();
            }
            tr.Commit();
        }
        catch (Autodesk.AutoCAD.Runtime.Exception ex)
        {
            Autodesk.AutoCAD.ApplicationServices.Core.Application.DocumentManager.MdiActiveDocument?.Editor
                .WriteMessage($"\nTijdelijke legendablokken konden niet volledig worden opgeruimd: {ex.Message}");
        }
    }
}
