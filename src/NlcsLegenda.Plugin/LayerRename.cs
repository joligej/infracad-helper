using Autodesk.AutoCAD.DatabaseServices;

namespace NlcsLegenda.Plugin;

// Hernoemt een laag component-voor-component naar een nieuwe (NLCS-)naam. De eigenlijke rename
// van de LayerTableRecord werkt door op alle entiteiten die naar die laag verwijzen; alleen bij
// een naamsbotsing worden entiteiten expliciet naar de bestaande doellaag verplaatst. Alles
// gebeurt in de transactie van de aanroeper: committen = toepassen, aborten = rollback.
internal static class LayerRename
{
    internal sealed class Plan
    {
        public string Source { get; init; } = string.Empty;
        public string Target { get; init; } = string.Empty;
        public int AffectedEntities { get; set; }
        public bool TargetExists { get; set; }
        public bool SourceLocked { get; set; }
        public bool Blocked { get; set; }
        public string? Reason { get; set; }
        public bool NoChange => string.Equals(Source, Target, System.StringComparison.Ordinal);
    }

    // Bepaalt wat een rename zou doen zonder iets te wijzigen (dry-run). Weigert xref-
    // afhankelijke lagen en lagen die niet bestaan.
    public static Plan Analyze(Database db, Transaction tr, string source, string target)
    {
        var plan = new Plan { Source = source, Target = target };
        var lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
        if (!lt.Has(source))
        {
            plan.Blocked = true;
            plan.Reason = $"laag \"{source}\" bestaat niet in deze tekening";
            return plan;
        }
        var ltr = (LayerTableRecord)tr.GetObject(lt[source], OpenMode.ForRead);
        if (ltr.IsDependent)
        {
            plan.Blocked = true;
            plan.Reason = "dit is een xref-afhankelijke laag; die kan niet worden hernoemd";
            return plan;
        }
        plan.SourceLocked = ltr.IsLocked;
        plan.AffectedEntities = CountOnLayer(db, tr, source);
        if (!plan.NoChange && lt.Has(target))
        {
            plan.TargetExists = true;
            var tgt = (LayerTableRecord)tr.GetObject(lt[target], OpenMode.ForRead);
            if (tgt.IsDependent)
            {
                plan.Blocked = true;
                plan.Reason = $"doellaag \"{target}\" is xref-afhankelijk; samenvoegen kan niet";
            }
        }
        return plan;
    }

    // Voert de rename uit. Bij een botsing met een bestaande laag worden de entiteiten naar die
    // laag verplaatst (merge) en wordt de bronlaag verwijderd als die daarna leeg en niet in
    // gebruik is. Geeft false met reden als er niet veilig kan worden doorgegaan.
    public static bool Apply(Database db, Transaction tr, Plan plan, bool mergeIntoExisting, out string error)
    {
        error = string.Empty;
        if (plan.Blocked)
        {
            error = plan.Reason ?? "geblokkeerd";
            return false;
        }
        if (plan.NoChange)
            return true;

        var lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForWrite);
        if (!lt.Has(plan.Source))
        {
            error = "bronlaag niet meer gevonden";
            return false;
        }

        if (!plan.TargetExists)
        {
            var ltr = (LayerTableRecord)tr.GetObject(lt[plan.Source], OpenMode.ForWrite);
            ltr.Name = plan.Target;
            return true;
        }

        if (!mergeIntoExisting)
        {
            error = $"laag \"{plan.Target}\" bestaat al";
            return false;
        }

        var targetId = lt[plan.Target];
        if (((LayerTableRecord)tr.GetObject(targetId, OpenMode.ForRead)).IsDependent)
        {
            error = $"doellaag \"{plan.Target}\" is xref-afhankelijk; samenvoegen kan niet";
            return false;
        }

        // Bronlaag ontgrendelen zodat de entiteiten verplaatst mogen worden (lock blokkeert
        // anders de wijziging). De bronlaag wordt daarna toch verwijderd.
        var srcLtr = (LayerTableRecord)tr.GetObject(lt[plan.Source], OpenMode.ForWrite);
        if (srcLtr.IsLocked)
            srcLtr.IsLocked = false;

        ReassignEntities(db, tr, plan.Source, targetId);

        // Bronlaag opruimen als die niet in gebruik is (niet de huidige laag en leeg).
        if (db.Clayer != lt[plan.Source] && CountOnLayer(db, tr, plan.Source) == 0)
            srcLtr.Erase();
        return true;
    }

    internal static int CountOnLayer(Database db, Transaction tr, string layer)
    {
        int count = 0;
        var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
        foreach (ObjectId btrId in bt)
        {
            var btr = (BlockTableRecord)tr.GetObject(btrId, OpenMode.ForRead);
            if (btr.IsFromExternalReference || btr.IsFromOverlayReference)
                continue;
            foreach (ObjectId entId in btr)
            {
                if (tr.GetObject(entId, OpenMode.ForRead) is Entity e
                    && string.Equals(e.Layer, layer, System.StringComparison.Ordinal))
                    count++;
            }
        }
        return count;
    }

    private static void ReassignEntities(Database db, Transaction tr, string source, ObjectId targetLayerId)
    {
        var targetName = ((LayerTableRecord)tr.GetObject(targetLayerId, OpenMode.ForRead)).Name;
        var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
        foreach (ObjectId btrId in bt)
        {
            var btr = (BlockTableRecord)tr.GetObject(btrId, OpenMode.ForRead);
            if (btr.IsFromExternalReference || btr.IsFromOverlayReference)
                continue;
            foreach (ObjectId entId in btr)
            {
                if (tr.GetObject(entId, OpenMode.ForRead) is Entity e
                    && string.Equals(e.Layer, source, System.StringComparison.Ordinal))
                {
                    e.UpgradeOpen();
                    e.Layer = targetName;
                }
            }
        }
    }
}
