using Autodesk.AutoCAD.DatabaseServices;

namespace NlcsLegenda.Plugin;

// Tekeningspecifieke JSON staat in de Named Object Dictionary; zo reist de configuratie mee met de DWG.
internal static class DrawingStore
{
    private const string RootKey = "NLCSLEGENDA";
    private const string SettingsKey = "SETTINGS";
    private const string DescriptionsKey = "DESCRIPTIONS";
    private const int ChunkSize = 255;

    public static bool HasSettings(Database db) => Read(db, SettingsKey) is not null;

    public static bool HasDescriptions(Database db) => Read(db, DescriptionsKey) is not null;

    public static string? ReadSettings(Database db) => Read(db, SettingsKey);

    public static string? ReadDescriptions(Database db) => Read(db, DescriptionsKey);

    public static void WriteSettings(Database db, string json) => Write(db, SettingsKey, json);

    public static void WriteDescriptions(Database db, string json) => Write(db, DescriptionsKey, json);

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

    private static void Write(Database db, string key, string json)
    {
        using var tr = db.TransactionManager.StartTransaction();
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

        var values = new List<TypedValue>();
        for (int i = 0; i < json.Length; i += ChunkSize)
            values.Add(new TypedValue((int)DxfCode.Text, json.Substring(i, Math.Min(ChunkSize, json.Length - i))));
        var data = new ResultBuffer(values.ToArray());

        if (root.Contains(key))
        {
            var existing = (Xrecord)tr.GetObject(root.GetAt(key), OpenMode.ForWrite);
            existing.Data = data;
        }
        else
        {
            var xrec = new Xrecord();
            root.SetAt(key, xrec);
            tr.AddNewlyCreatedDBObject(xrec, true);
            xrec.Data = data; // pas na toevoegen aan de database instellen
        }
        tr.Commit();
    }
}
