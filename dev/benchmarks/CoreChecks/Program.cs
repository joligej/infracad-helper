using System.Collections;
using System.Diagnostics;
using System.IO.Compression;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

if (args.Length != 4 || File.Exists(args[3]))
    throw new ArgumentException("Gebruik: CoreChecks oude-core.dll nieuwe-core.dll rows.json.gz nieuwe-uitvoer.json");

using var file = File.OpenRead(args[2]);
using var zip = new GZipStream(file, CompressionMode.Decompress);
using var reader = new StreamReader(zip);
string json = reader.ReadToEnd();
var calls = args.Take(2).Select((path, version) =>
{
    var assembly = new AssemblyLoadContext("core" + version, isCollectible: true).LoadFromAssemblyPath(Path.GetFullPath(path));
    var type = assembly.GetType("NlcsLegenda.Core.LegendEntry")!;
    var rows = (IEnumerable)JsonSerializer.Deserialize(json, typeof(List<>).MakeGenericType(type))!;
    var objects = rows.Cast<object>().Select(row => type.GetProperty("KlicObjects")!.GetValue(row)).ToArray();
    var issues = assembly.GetType("NlcsLegenda.Core.KlicSummary")!.GetMethod("Issues")!;
    var export = assembly.GetType("NlcsLegenda.Core.LegendExport")!;
    object?[] exportArguments = { rows, 0, "m²", "m", "st", new[] { "Netbeheerder", "DiameterM", "Materiaal" } };
    return new Dictionary<string, Func<string>>
    {
        ["broncontrole"] = () => string.Join("\n", objects.Select(value => (string)issues.Invoke(null, new[] { value })!)),
        ["csv"] = () => (string)export.GetMethod("ToCsv")!.Invoke(null, exportArguments)!,
        ["json"] = () => (string)export.GetMethod("ToJson")!.Invoke(null, exportArguments)!
    };
}).ToArray();

string Hash(string result) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(result)));
var hashes = new Dictionary<string, string>();
var samples = new List<object>();
foreach (string operation in calls[0].Keys)
{
    hashes[operation] = Hash(calls[0][operation]());
    if (Hash(calls[1][operation]()) != hashes[operation])
        throw new InvalidOperationException("Andere uitvoer bij " + operation);
    for (int warmup = 0; warmup < 3; warmup++)
        foreach (var call in calls) call[operation]();

    for (int pass = 0; pass < 10; pass++)
        foreach (int version in pass % 2 == 0 ? new[] { 0, 1 } : new[] { 1, 0 })
        {
            long allocated = GC.GetTotalAllocatedBytes(true);
            var timer = Stopwatch.StartNew();
            string result = calls[version][operation]();
            timer.Stop();
            samples.Add(new { operation, pass, version, ms = timer.Elapsed.TotalMilliseconds,
                bytes = GC.GetTotalAllocatedBytes(true) - allocated });
            if (Hash(result) != hashes[operation]) throw new InvalidOperationException("Uitvoer veranderd bij " + operation);
        }
}

var assemblies = args.Take(2).Select(path => new
{
    file = Path.GetFullPath(path), sha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)))
}).ToArray();
File.WriteAllText(args[3], JsonSerializer.Serialize(new { assemblies, hashes, samples }, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine("Broncontrole, CSV en JSON gelijk; metingen opgeslagen.");
