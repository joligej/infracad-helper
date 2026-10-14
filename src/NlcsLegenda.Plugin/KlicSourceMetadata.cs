using System.IO;
using Autodesk.AutoCAD.DatabaseServices;
using NlcsLegenda.Core;

namespace NlcsLegenda.Plugin;

internal sealed class KlicSourceMetadata
{
    private readonly Dictionary<Database, IReadOnlyDictionary<string, string>> _sources = new();

    public string Provider(Entity entity)
    {
        var source = entity.ObjectId.OriginalDatabase;
        if (!_sources.TryGetValue(source, out var providers))
        {
            try { providers = Read(source); }
            catch (Exception ex) when (ex is InvalidDataException or System.Xml.XmlException)
            {
                throw new InvalidOperationException("KLIC-netbeheerders kunnen niet betrouwbaar worden gelezen. Controleer de InfraCAD-brongegevens.", ex);
            }
            _sources.Add(source, providers);
        }
        return providers.GetValueOrDefault(entity.ObjectId.NonForwardedHandle.ToString(), string.Empty);
    }

    private static IReadOnlyDictionary<string, string> Read(Database source)
    {
        using var tr = source.TransactionManager.StartOpenCloseTransaction();
        var dictionary = (DBDictionary)tr.GetObject(source.NamedObjectsDictionaryId, OpenMode.ForRead);
        foreach (var key in new[] { "INFRACAD_MAP_DATA", "TABLES" })
        {
            if (!dictionary.Contains(key)) return new Dictionary<string, string>();
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
        var result = KlicMetadata.ReadProviders(tables);
        tr.Commit();
        return result;
    }
}
