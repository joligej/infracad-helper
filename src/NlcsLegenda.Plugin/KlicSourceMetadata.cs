using System.IO;
using Autodesk.AutoCAD.DatabaseServices;
using NlcsLegenda.Core;

namespace NlcsLegenda.Plugin;

internal sealed class KlicSourceMetadata : IDisposable
{
    private readonly Dictionary<Database, Source> _sources = new();
    private readonly KlicOptions _options;
    private readonly Database _database;
    private readonly Transaction _transaction;

    public KlicSourceMetadata(KlicOptions options, Database database, Transaction transaction)
    {
        _options = options;
        _database = database;
        _transaction = transaction;
    }

    public KlicObjectData Read(Entity entity, string xref)
    {
        var database = entity.ObjectId.OriginalDatabase;
        var ownerTransaction = database == _database ? _transaction : null;
        if (!_sources.TryGetValue(database, out var source))
            _sources.Add(database, source = new Source(database, ownerTransaction));
        string handle = entity.ObjectId.NonForwardedHandle.ToString();
        if (!source.Objects.TryGetValue(handle, out var data))
        {
            var records = source.Embedded.GetValueOrDefault(handle)?.ToList() ?? new List<KlicRecord>();
            var issues = new List<string>();
            void ReadAttached(Transaction tr)
            {
                // oorspronkelijke bronhandle bij doorgestuurde xref-id’s
                var original = (Entity)tr.GetObject(database.GetObjectId(false, entity.ObjectId.NonForwardedHandle, 0), OpenMode.ForRead);
                source.Attached.Read(original, tr, records, issues);
            }
            if (ownerTransaction is not null)
            {
                // bestaande transactie gebruiken voor nog niet opgeslagen objectwijzigingen
                ReadAttached(ownerTransaction);
            }
            else
            {
                // korte leestransactie per xref-object, geen parallelle gewone transactie
                using var tr = database.TransactionManager.StartOpenCloseTransaction();
                ReadAttached(tr);
            }
            data = new KlicObjectData(database.Filename ?? "", handle, "", records, issues, _options.PreferredSource);
            source.Objects.Add(handle, data);
        }
        return xref.Length == 0 ? data : data with { Xref = xref };
    }

    public void Dispose()
    {
        foreach (var source in _sources.Values) source.Attached.Dispose();
    }

    private sealed class Source
    {
        public readonly IReadOnlyDictionary<string, IReadOnlyList<KlicRecord>> Embedded;
        public readonly Dictionary<string, KlicObjectData> Objects = new(StringComparer.OrdinalIgnoreCase);
        public readonly KlicAttachedData Attached;
        public Source(Database db, Transaction? ownerTransaction)
        {
            try
            {
                if (ownerTransaction is not null) Embedded = ReadEmbedded(db, ownerTransaction);
                else
                {
                    using var tr = db.TransactionManager.StartOpenCloseTransaction();
                    Embedded = ReadEmbedded(db, tr);
                }
            }
            catch (Exception ex) when (ex is InvalidDataException or System.Xml.XmlException)
            {
                throw new InvalidOperationException("KLIC-gegevens kunnen niet betrouwbaar worden gelezen. Controleer de InfraCAD-brongegevens.", ex);
            }
            Attached = new KlicAttachedData(db);
        }
    }

    private static IReadOnlyDictionary<string, IReadOnlyList<KlicRecord>> ReadEmbedded(Database source, Transaction tr)
    {
        var dictionary = (DBDictionary)tr.GetObject(source.NamedObjectsDictionaryId, OpenMode.ForRead);
        foreach (var key in new[] { "INFRACAD_MAP_DATA", "TABLES" })
        {
            if (!dictionary.Contains(key)) return new Dictionary<string, IReadOnlyList<KlicRecord>>();
            dictionary = tr.GetObject(dictionary.GetAt(key), OpenMode.ForRead) as DBDictionary
                ?? throw new InvalidDataException("Ongeldige KLIC-metadatadictionary.");
        }
        var tables = new List<byte[]>();
        long total = 0;
        foreach (DBDictionaryEntry entry in dictionary)
        {
            if (tr.GetObject(entry.Value, OpenMode.ForRead) is not Xrecord record) continue;
            using var data = record.Data;
            if (data is null) continue;
            using var bytes = new MemoryStream();
            foreach (TypedValue value in data)
                if (value.TypeCode == 310 && value.Value is byte[] chunk)
                {
                    total += chunk.Length;
                    if (total > KlicMetadata.MaxCompressedBytes) throw new InvalidDataException("KLIC-metadata is te groot.");
                    bytes.Write(chunk);
                }
            if (bytes.Length > 0) tables.Add(bytes.ToArray());
        }
        return KlicMetadata.ReadRecords(tables);
    }
}
