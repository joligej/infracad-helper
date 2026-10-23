using System.Globalization;
using System.IO.Compression;
using System.Xml;
using System.Text;

namespace NlcsLegenda.Core;

// gezipte xml-tabellen; handles alleen geldig binnen de brontekening
public static class KlicMetadata
{
    public const int MaxCompressedBytes = 64 * 1024 * 1024;
    public const long MaxXmlBytes = 256L * 1024 * 1024;

    public static IReadOnlyDictionary<string, string> ReadProviders(IEnumerable<byte[]> tables)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (handle, records) in ReadRecords(tables))
        {
            var providers = records.SelectMany(r => r.Values.TryGetValue("Netbeheerder", out var value) && value.Trim().Length > 0
                ? new[] { value.Trim() } : Array.Empty<string>()).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            if (providers.Length > 1) throw new InvalidDataException("Een KLIC-object heeft tegenstrijdige netbeheerders in de brongegevens.");
            if (providers.Length == 1) result.Add(handle, providers[0]);
        }
        return result;
    }

    public static IReadOnlyDictionary<string, IReadOnlyList<KlicRecord>> ReadRecords(IEnumerable<byte[]> tables)
    {
        var result = new Dictionary<string, List<KlicRecord>>(StringComparer.OrdinalIgnoreCase);
        long compressed = 0, expanded = 0;
        foreach (var bytes in tables)
        {
            compressed += bytes.Length;
            if (compressed > MaxCompressedBytes) throw new InvalidDataException("KLIC-metadatatabellen zijn te groot.");
            using var buffer = new MemoryStream(bytes, writable: false);
            using var zip = new ZipArchive(buffer, ZipArchiveMode.Read);
            foreach (var entry in zip.Entries)
            {
                expanded += entry.Length;
                if (expanded > MaxXmlBytes) throw new InvalidDataException("Uitgepakte KLIC-metadatatabellen zijn te groot.");
                if (entry.Length == 0) continue;
                using var stream = entry.Open();
                using var xml = XmlReader.Create(stream, new XmlReaderSettings
                {
                    DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null,
                    MaxCharactersInDocument = MaxXmlBytes, IgnoreWhitespace = true
                });
                if (xml.MoveToContent() != XmlNodeType.Element || xml.LocalName != "KLIC") continue;
                while (xml.Read())
                {
                    if (xml.NodeType != XmlNodeType.Element || xml.Depth != 1) continue;
                    using var recordReader = xml.ReadSubtree();
                    recordReader.MoveToContent();
                    string table = recordReader.LocalName;
                    string? handle = null;
                    Dictionary<string, string>? fields = null;
                    bool duplicate = false;
                    while (recordReader.Read())
                    {
                        if (recordReader.NodeType != XmlNodeType.Element || recordReader.Depth != 1) continue;
                        string key = recordReader.LocalName;
                        if (key == "Handle" && handle is null) handle = ReadValue(recordReader).Trim();
                        else if (KlicFields.Find(key) is not null)
                        {
                            fields ??= new Dictionary<string, string>(StringComparer.Ordinal);
                            if (!fields.TryAdd(key, ReadValue(recordReader).Trim())) duplicate = true;
                        }
                    }
                    if (!ulong.TryParse(handle, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var number)) continue;
                    if (duplicate) throw new InvalidDataException("Dubbel veld in KLIC-brongegevens.");
                    if (fields is null) continue;
                    handle = number.ToString("X", CultureInfo.InvariantCulture);
                    if (!result.TryGetValue(handle, out var records)) result.Add(handle, records = new());
                    records.Add(new KlicRecord(KlicSource.InfraCAD, table, fields));
                }
            }
        }
        return result.ToDictionary(kv => kv.Key, kv => (IReadOnlyList<KlicRecord>)kv.Value, StringComparer.OrdinalIgnoreCase);
    }

    private static string ReadValue(XmlReader xml)
    {
        if (xml.IsEmptyElement) return "";
        int depth = xml.Depth;
        string value = "";
        StringBuilder? parts = null;
        while (xml.Read() && !(xml.NodeType == XmlNodeType.EndElement && xml.Depth == depth))
        {
            if (xml.NodeType is not (XmlNodeType.Text or XmlNodeType.CDATA or XmlNodeType.SignificantWhitespace)) continue;
            if (value.Length == 0) value = xml.Value;
            else (parts ??= new StringBuilder(value)).Append(xml.Value);
        }
        return parts?.ToString() ?? value;
    }

}
