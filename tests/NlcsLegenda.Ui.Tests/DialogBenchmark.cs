using System.Diagnostics;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Windows.Forms;
using NlcsLegenda.Core;

internal static class DialogBenchmark
{
    public static void Run(string baseline, string input, string output)
    {
        if (File.Exists(output)) throw new ArgumentException("Kies een nieuw uitvoerbestand.");
        using var file = File.OpenRead(input);
        using var zip = new GZipStream(file, CompressionMode.Decompress);
        var entries = JsonSerializer.Deserialize<List<LegendEntry>>(zip)!;
        var largest = entries.MaxBy(e => e.KlicObjects.Count)!;
        var context = new AssemblyLoadContext("baseline", isCollectible: true);
        var assemblies = new[] { context.LoadFromAssemblyPath(Path.GetFullPath(baseline)), typeof(Program).Assembly };
        var samples = new List<object>();
        var hashes = new Dictionary<string, string>();

        Form Create(Assembly assembly, string operation)
        {
            object Make(string name, params object?[] args) => Activator.CreateInstance(assembly.GetType("NlcsLegenda.Plugin." + name)!, args)!;
            if (operation == "klic-details") return (Form)Make("KlicDetailsDialog", largest);
            var type = assembly.GetType("NlcsLegenda.Plugin.OverviewLegend")!;
            var legend = Activator.CreateInstance(type)!;
            foreach (var (name, value) in new (string, object)[] {
                ("Id", "benchmark"), ("Name", "Benchmark"), ("Settings", new LegendSettings()), ("Entries", entries) })
                type.GetProperty(name)!.SetValue(legend, value);
            var array = Array.CreateInstance(type, 1);
            array.SetValue(legend, 0);
            return (Form)Make("OverviewDialog", array, null, null, null);
        }

        foreach (string operation in new[] { "klic-details", "overzicht" })
        {
            for (int pass = -2; pass < 7; pass++)
            foreach (int version in pass % 2 == 0 ? new[] { 0, 1 } : new[] { 1, 0 })
            {
                long allocated = GC.GetTotalAllocatedBytes(true);
                var timer = Stopwatch.StartNew();
                using var form = Create(assemblies[version], operation);
                timer.Stop();
                long bytes = GC.GetTotalAllocatedBytes(true) - allocated;
                string hash = Snapshot(form);
                if (!hashes.TryGetValue(operation, out var expected)) hashes[operation] = hash;
                else if (hash != expected) throw new InvalidOperationException("Andere vensterinhoud: " + operation);
                if (pass >= 0) samples.Add(new { operation, pass, version, ms = timer.Elapsed.TotalMilliseconds, bytes });
            }
        }
        var binaries = assemblies.Select(a => new { path = a.Location, sha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(a.Location))) });
        File.WriteAllText(output, JsonSerializer.Serialize(new { binaries, entries = entries.Count,
            detailObjects = largest.KlicObjects.Count, hashes, samples }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine("Vensterinhoud gelijk; metingen opgeslagen.");
        context.Unload();
    }

    private static string Snapshot(Control control)
    {
        var values = new List<object>();
        void Read(Control parent)
        {
            foreach (Control child in parent.Controls)
            {
                if (child is DataGridView grid)
                    values.Add(new { columns = grid.Columns.Cast<DataGridViewColumn>().Select(c => new { c.HeaderText, c.Visible }).ToArray(),
                        rows = grid.Rows.Cast<DataGridViewRow>().Select(r => r.Cells.Cast<DataGridViewCell>().Select(c => c.Value).ToArray()).ToArray() });
                else if (child is TextBox or Label) values.Add(child.Text);
                Read(child);
            }
        }
        Read(control);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(values))));
    }
}
