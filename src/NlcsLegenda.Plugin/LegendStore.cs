using Autodesk.AutoCAD.DatabaseServices;
using NlcsLegenda.Core;

namespace NlcsLegenda.Plugin;

// De registry van beheerde legenda's staat onder een EIGEN NOD-root, los van de oude
// v1.13-root "NLCSLEGENDA". Zo kan een oude "tekeningconfig wissen"-actie (die de
// NLCSLEGENDA-root verwijdert) de legenda-registry nooit vernietigen. Alle lees/schrijf
// gaat via een bestaande transactie, zodat metadata en geometrie samen committen.
internal static class LegendStore
{
    private const string RegistryRoot = "NLCSLEGENDA_REGISTRY";
    private const string RegistryKey = "REGISTRY";
    // Ruim onder de 255-char Xrecord-tekstlimiet; splitst nooit midden in een surrogate pair.
    private const int ChunkSize = 200;

    public static LegendRegistry Load(Database db, Transaction tr)
    {
        var json = ReadRaw(db, tr);
        if (json is null)
            return new LegendRegistry();
        if (LegendRegistry.TryParse(json, out var reg, out _))
            return reg;
        // Ongeldige of niet-ondersteunde registry: geef de (mogelijk niet-ondersteunde)
        // versie terug zodat de aanroeper strikt kan afhandelen; nooit stil defaults schrijven.
        return reg;
    }

    // Leest de registry strikt: false bij ongeldige of niet-ondersteunde metadata.
    public static bool TryLoad(Database db, Transaction tr, out LegendRegistry registry, out string error)
    {
        var json = ReadRaw(db, tr);
        if (json is null)
        {
            registry = new LegendRegistry();
            error = string.Empty;
            return true;
        }
        return LegendRegistry.TryParse(json, out registry, out error);
    }

    public static bool Exists(Database db, Transaction tr) => ReadRaw(db, tr) is not null;

    public static void Save(Database db, Transaction tr, LegendRegistry registry)
    {
        var nod = (DBDictionary)tr.GetObject(db.NamedObjectsDictionaryId, OpenMode.ForWrite);
        DBDictionary root;
        if (nod.Contains(RegistryRoot))
        {
            root = (DBDictionary)tr.GetObject(nod.GetAt(RegistryRoot), OpenMode.ForWrite);
        }
        else
        {
            root = new DBDictionary();
            nod.SetAt(RegistryRoot, root);
            tr.AddNewlyCreatedDBObject(root, true);
        }

        var data = new ResultBuffer(Chunk(registry.ToJson()));
        if (root.Contains(RegistryKey))
        {
            ((Xrecord)tr.GetObject(root.GetAt(RegistryKey), OpenMode.ForWrite)).Data = data;
        }
        else
        {
            var xrec = new Xrecord();
            root.SetAt(RegistryKey, xrec);
            tr.AddNewlyCreatedDBObject(xrec, true);
            xrec.Data = data;
        }
    }

    private static string? ReadRaw(Database db, Transaction tr)
    {
        var nod = (DBDictionary)tr.GetObject(db.NamedObjectsDictionaryId, OpenMode.ForRead);
        if (!nod.Contains(RegistryRoot))
            return null;
        var root = (DBDictionary)tr.GetObject(nod.GetAt(RegistryRoot), OpenMode.ForRead);
        if (!root.Contains(RegistryKey))
            return null;
        var xrec = (Xrecord)tr.GetObject(root.GetAt(RegistryKey), OpenMode.ForRead);
        var sb = new System.Text.StringBuilder();
        foreach (TypedValue tv in xrec.Data)
            if (tv.TypeCode == (int)DxfCode.Text && tv.Value is string s)
                sb.Append(s);
        return sb.Length > 0 ? sb.ToString() : null;
    }

    private static TypedValue[] Chunk(string json)
    {
        var values = new List<TypedValue>();
        int i = 0;
        while (i < json.Length)
        {
            int len = Math.Min(ChunkSize, json.Length - i);
            // Splits nooit tussen een high/low surrogate.
            if (i + len < json.Length && char.IsHighSurrogate(json[i + len - 1]))
                len--;
            values.Add(new TypedValue((int)DxfCode.Text, json.Substring(i, len)));
            i += len;
        }
        if (values.Count == 0)
            values.Add(new TypedValue((int)DxfCode.Text, string.Empty));
        return values.ToArray();
    }
}
