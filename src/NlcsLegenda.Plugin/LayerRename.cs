using Autodesk.AutoCAD.DatabaseServices;

namespace NlcsLegenda.Plugin;

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
        if (!plan.NoChange && lt.Has(target) && lt[target] != lt[source])
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

        var srcLtr = (LayerTableRecord)tr.GetObject(lt[plan.Source], OpenMode.ForWrite);
        bool wasLocked = srcLtr.IsLocked;
        try
        {
            if (wasLocked) srcLtr.IsLocked = false;
            ReassignEntities(db, tr, plan.Source, targetId);
            if (db.Clayer != srcLtr.ObjectId && CountOnLayer(db, tr, plan.Source) == 0)
                srcLtr.Erase();
            return true;
        }
        finally
        {
            // oorspronkelijk slot van de behouden bronlaag herstellen
            if (!srcLtr.IsErased) srcLtr.IsLocked = wasLocked;
        }
    }

    internal static int CountOnLayer(Database db, Transaction tr, string layer) =>
        LayerEntities(db, tr, layer).Count();

    private static void ReassignEntities(Database db, Transaction tr, string source, ObjectId targetLayerId)
    {
        foreach (var id in LayerEntities(db, tr, source).ToArray())
            ((Entity)tr.GetObject(id, OpenMode.ForWrite)).LayerId = targetLayerId;
    }

    private static IEnumerable<ObjectId> LayerEntities(Database db, Transaction tr, string layer)
    {
        var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
        foreach (ObjectId btrId in bt)
        {
            var btr = (BlockTableRecord)tr.GetObject(btrId, OpenMode.ForRead);
            if (btr.IsFromExternalReference || btr.IsFromOverlayReference) continue;
            foreach (ObjectId entId in btr)
            {
                if (tr.GetObject(entId, OpenMode.ForRead) is not Entity entity) continue;
                if (string.Equals(entity.Layer, layer, StringComparison.OrdinalIgnoreCase))
                    yield return entId;
                // attributen apart van de bloktabel doorlopen
                if (entity is BlockReference reference)
                    foreach (ObjectId attributeId in reference.AttributeCollection)
                        if (tr.GetObject(attributeId, OpenMode.ForRead) is Entity attribute
                            && string.Equals(attribute.Layer, layer, StringComparison.OrdinalIgnoreCase))
                            yield return attributeId;
            }
        }
    }
}
