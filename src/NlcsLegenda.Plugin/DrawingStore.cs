using Autodesk.AutoCAD.DatabaseServices;

namespace NlcsLegenda.Plugin;

// Legacy tekeningspecifieke JSON in de Named Object Dictionary. Alleen nog voor migratie:
// lezen van oude v1.13-v1.15-config en opruimen. Er wordt niets nieuws meer weggeschreven.
internal static class DrawingStore
{
    private const string RootKey = "NLCSLEGENDA";
    private const string SettingsKey = "SETTINGS";
    private const string DescriptionsKey = "DESCRIPTIONS";

    public static bool HasSettings(Database db) => Read(db, SettingsKey) is not null;

    public static bool HasDescriptions(Database db) => Read(db, DescriptionsKey) is not null;

    public static string? ReadSettings(Database db) => Read(db, SettingsKey);

    public static string? ReadDescriptions(Database db) => Read(db, DescriptionsKey);

    public static void Clear(Database db)
    {
        using var tr = db.TransactionManager.StartTransaction();
        var nod = (DBDictionary)tr.GetObject(db.NamedObjectsDictionaryId, OpenMode.ForRead);
        if (nod.Contains(RootKey))
        {
            nod.UpgradeOpen();
            nod.Remove(RootKey);
        }
        tr.Commit();
    }

    private static string? Read(Database db, string key)
    {
        using var tr = db.TransactionManager.StartTransaction();
        var nod = (DBDictionary)tr.GetObject(db.NamedObjectsDictionaryId, OpenMode.ForRead);
        if (!nod.Contains(RootKey))
            return null;
        var root = (DBDictionary)tr.GetObject(nod.GetAt(RootKey), OpenMode.ForRead);
        if (!root.Contains(key))
            return null;

        var xrec = (Xrecord)tr.GetObject(root.GetAt(key), OpenMode.ForRead);
        var sb = new System.Text.StringBuilder();
        foreach (TypedValue tv in xrec.Data)
            if (tv.TypeCode == (int)DxfCode.Text && tv.Value is string s)
                sb.Append(s);
        tr.Commit();
        return sb.Length > 0 ? sb.ToString() : null;
    }
}
