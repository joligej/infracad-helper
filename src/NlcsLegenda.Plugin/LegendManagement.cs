using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using NlcsLegenda.Core;

namespace NlcsLegenda.Plugin;

// Groep-, handle-, migratie- en locator-helpers voor beheerde legenda's. Alles werkt op
// een bestaande transactie zodat registry en geometrie samen committen.
internal static class LegendManagement
{
    // Oude v1.13-groepnaam; wordt bij migratie geadopteerd.
    public const string LegacyGroupName = "NLCS-Legenda";

    // Alle ObjectIds die bij een beheerde (of legacy) legenda horen. De analyse sluit deze
    // uit, zodat geplaatste legenda's de brontelling niet beïnvloeden.
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
        AddGroup(LegacyGroupName);
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

    // Adopteert een oude v1.13-legenda (group "NLCS-Legenda") + eventuele legacy
    // tekeninginstellingen als één beheerde legenda. Idempotent: als de registry al
    // legenda's bevat of de legacy-groep ontbreekt, gebeurt er niets.
    // Resultaat van een poging om oude tekeningbrede config over te nemen.
    public enum LegacyAdoption { None, Adopted, Corrupt }

    public static bool MigrateLegacyIfNeeded(
        Database db, Transaction tr, LegendRegistry registry, LegendSettings globalDefaults, string version, out string message)
    {
        message = string.Empty;

        var settingsJson = DrawingStore.ReadSettings(db, tr);
        var descJson = DrawingStore.ReadDescriptions(db, tr);
        var gd = (DBDictionary)tr.GetObject(db.GroupDictionaryId, OpenMode.ForRead);
        bool hasLegacyGroup = gd.Contains(LegacyGroupName)
            && tr.GetObject(gd.GetAt(LegacyGroupName), OpenMode.ForRead) is Group lg
            && lg.GetAllEntityIds().Length > 0;
        bool hasSettings = settingsJson is not null;
        bool hasDesc = descJson is not null;

        if (!hasLegacyGroup && !hasSettings && !hasDesc)
            return false;

        // Strict parse: onleesbare kritieke staat niet stil vervangen door defaults en wissen.
        LegendSettings? legacySettings = null;
        if (hasSettings)
        {
            if (!LegendSettings.TryParse(settingsJson, out var parsed))
            {
                message = "Oude tekeninginstellingen zijn onleesbaar; migratie overgeslagen zodat niets verloren gaat.";
                return false;
            }
            legacySettings = parsed;
        }
        DescriptionCatalog? legacyDesc = null;
        if (hasDesc)
        {
            if (!DescriptionCatalog.TryParse(descJson, out var parsedD))
            {
                message = "Oude omschrijvingen zijn onleesbaar; migratie overgeslagen zodat niets verloren gaat.";
                return false;
            }
            legacyDesc = parsedD;
        }

        // Bestaan er al beheerde legenda's, dan zijn hun instellingen al gemigreerd. Oude
        // tekeningbrede omschrijvingen clonen we als basis naar elke legenda (eigen override
        // wint), zodat ze niet stil wegvallen nu LoadCatalog ze niet meer live leest.
        if (registry.Legends.Count > 0)
        {
            if (!hasSettings && !hasDesc)
                return false;
            if (legacyDesc is { Elementen.Count: > 0 })
                foreach (var l in registry.Legends)
                {
                    var merged = legacyDesc.Clone();
                    merged.MergeFrom(l.Settings.DescriptionOverrides);
                    l.Settings.DescriptionOverrides = merged;
                }
            DrawingStore.Clear(db, tr);
            LegendStore.Save(db, tr, registry);
            message = "Oude tekeningconfig overgenomen in bestaande legenda's.";
            return true;
        }

        if (!hasLegacyGroup)
        {
            // Alleen oude config, nog geen legenda: laten staan tot de eerste nieuwe plaatsing
            // die de config als startpunt overneemt (TryReadLegacyStart). Niets wissen.
            message = "Oude tekeningconfig gevonden; wordt bij de volgende nieuwe legenda overgenomen.";
            return false;
        }

        // Legacy GROUP (v1.13-geometrie) wordt een beheerde legenda; alles atomair in deze tr.
        var settings = legacySettings ?? globalDefaults.Clone();
        if (legacyDesc is { Elementen.Count: > 0 })
            settings.DescriptionOverrides = legacyDesc.Clone();

        var newGroupName = LegendRegistry.NewGroupName();
        RenameGroup(db, tr, LegacyGroupName, newGroupName);
        registry.Add(new LegendDefinition
        {
            Name = registry.NextDefaultName(),
            Scope = LegendScope.WholeDrawing,
            GroupName = newGroupName,
            Settings = settings,
            CreatedWithVersion = version
        });
        DrawingStore.Clear(db, tr);
        LegendStore.Save(db, tr, registry);
        message = hasSettings || hasDesc
            ? "Bestaande legenda, instellingen en omschrijvingen overgenomen."
            : "Bestaande legenda overgenomen.";
        return true;
    }

    // Leest oude tekeningbrede config in de huidige transactie en geeft de startinstellingen
    // voor een nieuwe legenda terug (vóór opties en renderen). Strict: corrupt -> None/Corrupt,
    // niets wissen. Wissen gebeurt pas atomair bij het opslaan van de legenda (de aanroeper).
    public static LegacyAdoption TryReadLegacyStart(
        Database db, Transaction tr, LegendSettings globalDefaults, out LegendSettings start, out string message)
    {
        start = globalDefaults.Clone();
        message = string.Empty;
        var settingsJson = DrawingStore.ReadSettings(db, tr);
        var descJson = DrawingStore.ReadDescriptions(db, tr);
        if (settingsJson is null && descJson is null)
            return LegacyAdoption.None;

        LegendSettings? adoptedSettings = null;
        if (settingsJson is not null)
        {
            if (!LegendSettings.TryParse(settingsJson, out var parsed))
            {
                message = "Oude tekeninginstellingen zijn onleesbaar; niet overgenomen en niet gewist.";
                return LegacyAdoption.Corrupt;
            }
            adoptedSettings = parsed;
        }
        DescriptionCatalog? adoptedDesc = null;
        if (descJson is not null)
        {
            if (!DescriptionCatalog.TryParse(descJson, out var parsedD))
            {
                message = "Oude omschrijvingen zijn onleesbaar; niet overgenomen en niet gewist.";
                return LegacyAdoption.Corrupt;
            }
            adoptedDesc = parsedD;
        }

        if (adoptedSettings is not null)
            start = adoptedSettings;
        if (adoptedDesc is { Elementen.Count: > 0 })
            start.DescriptionOverrides = adoptedDesc.Clone();
        message = "Oude tekeningconfig overgenomen in de nieuwe legenda.";
        return LegacyAdoption.Adopted;
    }

    private static void RenameGroup(Database db, Transaction tr, string oldName, string newName)
    {
        var gd = (DBDictionary)tr.GetObject(db.GroupDictionaryId, OpenMode.ForWrite);
        if (!gd.Contains(oldName)) return;
        var groupId = gd.GetAt(oldName);
        gd.Remove(oldName);
        gd.SetAt(newName, tr.GetObject(groupId, OpenMode.ForWrite));
    }
}
