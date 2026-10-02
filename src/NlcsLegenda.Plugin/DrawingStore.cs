using Autodesk.AutoCAD.DatabaseServices;

namespace NlcsLegenda.Plugin;

// Legacy tekeningspecifieke JSON in de Named Object Dictionary (oude v1.13-v1.15-config).
// Alleen nog voor migratie: lezen en opruimen. De transaction-aware varianten lezen/wissen
// binnen een bestaande transactie, zodat migratie en opruiming samen committen of terugrollen.
internal static class DrawingStore
{
    private const string RootKey = "NLCSLEGENDA";
    private const string SettingsKey = "SETTINGS";
    private const string DescriptionsKey = "DESCRIPTIONS";

    public static bool HasSettings(Database db) => InReadTx(db, tr => Read(db, tr, SettingsKey) is not null);

    public static bool HasDescriptions(Database db) => InReadTx(db, tr => Read(db, tr, DescriptionsKey) is not null);

    public static string? ReadSettings(Database db) => InReadTx(db, tr => Read(db, tr, SettingsKey));

    public static string? ReadDescriptions(Database db) => InReadTx(db, tr => Read(db, tr, DescriptionsKey));

    public static string? ReadSettings(Database db, Transaction tr) => Read(db, tr, SettingsKey);

    public static string? ReadDescriptions(Database db, Transaction tr) => Read(db, tr, DescriptionsKey);

    public static bool HasAny(Database db, Transaction tr) =>
        Read(db, tr, SettingsKey) is not null || Read(db, tr, DescriptionsKey) is not null;

    public static void Clear(Database db)
    {
        using var tr = db.TransactionManager.StartTransaction();
        Clear(db, tr);
        tr.Commit();
    }

    public static void Clear(Database db, Transaction tr)
    {
        var nod = (DBDictionary)tr.GetObject(db.NamedObjectsDictionaryId, OpenMode.ForRead);
        if (nod.Contains(RootKey))
        {
            nod.UpgradeOpen();
            nod.Remove(RootKey);
        }
    }

    // Alleen voor tests/legacy-setup: schrijft een legacy record in de huidige transactie.
    public static void Write(Database db, Transaction tr, bool settings, string json)
    {
        var key = settings ? SettingsKey : DescriptionsKey;
        var nod = (DBDictionary)tr.GetObject(db.NamedObjectsDictionaryId, OpenMode.ForWrite);
        DBDictionary root;
        if (nod.Contains(RootKey))
        {
            root = (DBDictionary)tr.GetObject(nod.GetAt(RootKey), OpenMode.ForWrite);
        }
        else
        {
            root = new DBDictionary();
            nod.SetAt(RootKey, root);
            tr.AddNewlyCreatedDBObject(root, true);
        }

        var rb = new ResultBuffer();
        for (int i = 0; i < json.Length; i += 250)
            rb.Add(new TypedValue((int)DxfCode.Text, json.Substring(i, System.Math.Min(250, json.Length - i))));

        if (root.Contains(key))
            root.Remove(key);
        var xrec = new Xrecord { Data = rb };
        root.SetAt(key, xrec);
        tr.AddNewlyCreatedDBObject(xrec, true);
    }

    private static T InReadTx<T>(Database db, System.Func<Transaction, T> f)
    {
        using var tr = db.TransactionManager.StartTransaction();
        var result = f(tr);
        tr.Commit();
        return result;
    }

    private static string? Read(Database db, Transaction tr, string key)
    {
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
        return sb.Length > 0 ? sb.ToString() : null;
    }
}
