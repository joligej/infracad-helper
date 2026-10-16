using System.IO;
using System.Text.Json;
using Autodesk.AutoCAD.DatabaseServices;
using NlcsLegenda.Core;

namespace NlcsLegenda.Plugin;

public sealed class ExampleLibrary : IDisposable
{
    public const string CatalogKey = "NLCS_LEGENDA_VOORBEELDEN";
    private readonly Database? _source;
    private readonly string _path;
    private bool _catalogSaved;
    public IReadOnlyList<LegendExample> Examples { get; }

    public ExampleLibrary(string path, Database? host = null, Transaction? transaction = null)
    {
        _path = path;
        if (string.IsNullOrWhiteSpace(path)) { Examples = Array.Empty<LegendExample>(); return; }
        if (!File.Exists(path))
        {
            Examples = host is not null && transaction is not null ? ReadCatalog(host, transaction, CacheKey(path)) : Array.Empty<LegendExample>();
            if (Examples.Count == 0) throw new FileNotFoundException("De voorbeeldenbibliotheek ontbreekt. Kies de bibliotheek opnieuw in Overzicht.", path);
            return;
        }
        _source = new Database(false, true);
        try
        {
            _source.ReadDwgFile(path, FileOpenMode.OpenForReadAndAllShare, true, "");
            _source.CloseInput(true);
            using var tr = _source.TransactionManager.StartTransaction();
            Examples = ReadCatalog(_source, tr);
            if (Examples.Count == 0) throw new InvalidDataException("Dit bestand bevat geen legendavoorbeelden.");
            var bt = (BlockTable)tr.GetObject(_source.BlockTableId, OpenMode.ForRead);
            if (Examples.Any(e => !bt.Has(e.BlockName) || !double.IsFinite(e.Scale) || e.Scale <= 0))
                throw new InvalidDataException("De voorbeeldenbibliotheek bevat een ongeldig voorbeeld.");
        }
        catch { _source.Dispose(); throw; }
    }

    public static IReadOnlyList<LegendExample> ReadCatalog(Database db, Transaction tr, string key = CatalogKey)
    {
        var nod = (DBDictionary)tr.GetObject(db.NamedObjectsDictionaryId, OpenMode.ForRead);
        if (!nod.Contains(key)) return Array.Empty<LegendExample>();
        using var data = ((Xrecord)tr.GetObject(nod.GetAt(key), OpenMode.ForRead)).Data;
        string json = string.Concat(data.AsArray().Select(v => (string)v.Value));
        var examples = JsonSerializer.Deserialize<LegendExample[]>(json) ?? Array.Empty<LegendExample>();
        if (examples.Any(e => e is null || string.IsNullOrWhiteSpace(e.BlockName) || e.Name is null || e.Section is null || e.SourceKeys is null || e.SourceKeys.Any(k => k is null)
            || !double.IsFinite(e.Scale) || e.Scale <= 0 || !double.IsFinite(e.AnchorX) || !double.IsFinite(e.AnchorY))
            || examples.Select(e => e.BlockName).Distinct(StringComparer.Ordinal).Count() != examples.Length)
            throw new InvalidDataException("De voorbeeldenbibliotheek bevat ongeldige gegevens.");
        return examples;
    }

    public static void WriteCatalog(Database db, Transaction tr, IEnumerable<LegendExample> examples, string key = CatalogKey)
    {
        string json = JsonSerializer.Serialize(examples);
        using var data = new ResultBuffer(Enumerable.Range(0, (json.Length + 199) / 200)
            .Select(i => new TypedValue((int)DxfCode.Text, json.Substring(i * 200, Math.Min(200, json.Length - i * 200)))).ToArray());
        var nod = (DBDictionary)tr.GetObject(db.NamedObjectsDictionaryId, OpenMode.ForWrite);
        if (nod.Contains(key)) ((Xrecord)tr.GetObject(nod.GetAt(key), OpenMode.ForWrite)).Data = data;
        else
        {
            using var record = new Xrecord { Data = data };
            nod.SetAt(key, record); tr.AddNewlyCreatedDBObject(record, true);
        }
    }

    public ObjectId Import(Database db, Transaction tr, LegendExample example)
    {
        var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
        if (!bt.Has(example.BlockName))
        {
            if (_source is null) throw new FileNotFoundException($"Het voorbeeld '{example.Name}' staat nog niet in deze tekening. Kies de voorbeeldenbibliotheek opnieuw in Overzicht.", _path);
            using var sourceTr = _source.TransactionManager.StartTransaction();
            var sourceBt = (BlockTable)sourceTr.GetObject(_source.BlockTableId, OpenMode.ForRead);
            using var ids = new ObjectIdCollection { sourceBt[example.BlockName] };
            using var mapping = new IdMapping();
            _source.WblockCloneObjects(ids, db.BlockTableId, mapping, DuplicateRecordCloning.Ignore, false);
            var translated = mapping.Cast<IdPair>().Where(p => !p.Value.IsNull).ToDictionary(p => p.Key, p => p.Value);
            foreach (IdPair pair in mapping)
            {
                if (!pair.IsCloned || sourceTr.GetObject(pair.Key, OpenMode.ForRead) is not BlockTableRecord from) continue;
                var to = (BlockTableRecord)tr.GetObject(pair.Value, OpenMode.ForRead);
                var order = (DrawOrderTable)sourceTr.GetObject(from.DrawOrderTableId, OpenMode.ForRead);
                using var ordered = new ObjectIdCollection();
                foreach (ObjectId id in order.GetFullDrawOrder(0))
                    if (translated.TryGetValue(id, out var target) && tr.GetObject(target, OpenMode.ForRead).OwnerId == to.ObjectId) ordered.Add(target);
                if (ordered.Count > 0) ((DrawOrderTable)tr.GetObject(to.DrawOrderTableId, OpenMode.ForWrite)).SetRelativeDrawOrder(ordered);
            }
        }
        if (!_catalogSaved)
        {
            WriteCatalog(db, tr, Examples, CacheKey(_path));
            _catalogSaved = true;
        }
        var known = ReadCatalog(db, tr).ToDictionary(e => e.BlockName, StringComparer.Ordinal);
        known[example.BlockName] = example;
        WriteCatalog(db, tr, known.Values);
        return bt[example.BlockName];
    }

    private static string CacheKey(string path) => CatalogKey + "_" + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
        System.Text.Encoding.UTF8.GetBytes(Path.GetFullPath(path).ToUpperInvariant())))[..20];

    public void Dispose() => _source?.Dispose();
}
