using System.Globalization;
using System.IO.Compression;
using System.Xml;
using System.Xml.Linq;

namespace NlcsLegenda.Core;

// InfraCAD Map bewaart KLIC-objectgegevens als gezipte XML-tabellen in de DWG.
// Handles gelden uitsluitend binnen hun eigen bron-database.
public static class KlicMetadata
{
    public const int MaxCompressedBytes = 64 * 1024 * 1024;
    public const long MaxXmlBytes = 256L * 1024 * 1024;

    public static IReadOnlyDictionary<string, string> ReadProviders(IEnumerable<byte[]> tables)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
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
                    var record = XElement.Load(recordReader);
                    string handle = record.Elements().FirstOrDefault(e => e.Name.LocalName == "Handle")?.Value.Trim() ?? "";
                    string provider = record.Elements().FirstOrDefault(e => e.Name.LocalName == "Netbeheerder")?.Value.Trim() ?? "";
                    if (provider.Length == 0 || !ulong.TryParse(handle, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var number)) continue;
                    handle = number.ToString("X", CultureInfo.InvariantCulture);
                    if (result.TryGetValue(handle, out var previous) && !string.Equals(previous, provider, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException("Een KLIC-object heeft tegenstrijdige netbeheerders in de brongegevens.");
                    result.TryAdd(handle, provider);
                }
            }
        }
        return result;
    }
}
