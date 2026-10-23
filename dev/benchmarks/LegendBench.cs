using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using NlcsLegenda.Core;
using NlcsLegenda.Plugin;
using App = Autodesk.AutoCAD.ApplicationServices.Core.Application;

namespace NlcsLegenda.Benchmarks;

public sealed class LegendBench
{
    static readonly Assembly Plugin = typeof(Commands).Assembly;
    static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    static object? Call(string type, string method, params object?[] args) =>
        Plugin.GetType("NlcsLegenda.Plugin." + type)!.GetMethod(method, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)!.Invoke(null, args);
    static string Hash(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    static string Number(double x) => Math.Round(x, 8).ToString("R", CultureInfo.InvariantCulture);
    static string Point(Point3d p) => $"{Number(p.X)},{Number(p.Y)},{Number(p.Z)}";

    [CommandMethod("NLCSBENCHPROFILE", CommandFlags.Modal)]
    public void Profile()
    {
        var doc = App.DocumentManager.MdiActiveDocument;
        string? root = Environment.GetEnvironmentVariable("NLCS_BENCH_DIRECTORY")
            ?? Environment.GetEnvironmentVariable("NLCS_GUI_CHECK");
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
        {
            doc.Editor.WriteMessage("\nStel NLCS_BENCH_DIRECTORY in op een bestaande, lege uitvoermap met cases.json.");
            return;
        }
        try
        {
            using var tr = doc.Database.TransactionManager.StartTransaction();
            var samples = new List<object>();
            void Measure(string name, Action action)
            {
                long allocated = GC.GetTotalAllocatedBytes(false);
                var watch = Stopwatch.StartNew();
                action();
                watch.Stop();
                samples.Add(new { name, milliseconds = watch.Elapsed.TotalMilliseconds, bytes = GC.GetTotalAllocatedBytes(false) - allocated });
            }
            IReadOnlyDictionary<string, IReadOnlyList<KlicRecord>> embedded = null!;
            Measure("ingesloten XML", () => embedded = (IReadOnlyDictionary<string, IReadOnlyList<KlicRecord>>)Call("KlicSourceMetadata", "ReadEmbedded", doc.Database, tr)!);
            var entities = ((BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(doc.Database), OpenMode.ForRead))
                .Cast<ObjectId>().Select(id => tr.GetObject(id, OpenMode.ForRead)).OfType<Entity>()
                .Where(e => NlcsLayerParser.TryParse(e.Layer, out _)).ToArray();
            var attached = (IDisposable)Activator.CreateInstance(Plugin.GetType("NlcsLegenda.Plugin.KlicAttachedData")!, doc.Database, doc.Database)!;
            using (attached)
            {
                var read = attached.GetType().GetMethod("Read")!.CreateDelegate<Action<Entity, Transaction, List<KlicRecord>, List<string>>>(attached);
                var objects = new List<KlicObjectData>();
                Measure("Object Data en Property Sets", () => {
                    foreach (var entity in entities)
                    {
                        var handle = entity.Handle.ToString();
                        var records = embedded.GetValueOrDefault(handle)?.ToList() ?? new List<KlicRecord>();
                        var issues = new List<string>();
                        read(entity, tr, records, issues);
                        objects.Add(new KlicObjectData(doc.Database.Filename, handle, "", records, issues, KlicSource.InfraCAD));
                    }
                });
                Measure("beheerder en diameter", () => { foreach (var item in objects) { _ = item.Provider; _ = item.GroupKey(new[] { "DiameterM" }); } });
            }
            Measure("laagstijlen", () => { foreach (var entity in entities) Call("DrawingAnalyzer", "EntityLayerStyle", entity, tr, null); });
            Measure("laagnamen", () => { foreach (var entity in entities) NlcsLayerParser.TryParse(entity.Layer, out _); });
            File.WriteAllText(Path.Join(root, "profile.json"), JsonSerializer.Serialize(new { entities = entities.Length, samples }, Json));
            doc.Editor.WriteMessage("\nNLCSBENCHPROFILE DONE");
        }
        catch (System.Exception ex) { File.WriteAllText(Path.Join(root, "benchmark-error.txt"), ex.ToString()); doc.Editor.WriteMessage("\nNLCSBENCHPROFILE FAIL"); }
    }

    [CommandMethod("NLCSBENCH", CommandFlags.Modal)]
    public void Run()
    {
        var doc = App.DocumentManager.MdiActiveDocument;
        string? root = Environment.GetEnvironmentVariable("NLCS_BENCH_DIRECTORY")
            ?? Environment.GetEnvironmentVariable("NLCS_GUI_CHECK");
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
        {
            doc.Editor.WriteMessage("\nStel NLCS_BENCH_DIRECTORY in op een bestaande, lege uitvoermap met cases.json.");
            return;
        }
        var samples = new List<object>();
        void Progress(string stage) => File.WriteAllText(Path.Join(root, "progress.json"), JsonSerializer.Serialize(new { stage, samples }, Json));
        try
        {
            var config = JsonSerializer.Deserialize<Config>(File.ReadAllText(Path.Join(root, "cases.json")))!;
            if (config.Runs is < 2 or > 20) throw new InvalidOperationException("Kies twee tot twintig herhalingen.");
            if (File.Exists(Path.Join(root, "benchmark.json"))) throw new InvalidOperationException("Deze uitvoermap bevat al een meting.");
            var db = doc.Database;
            string source = config.AttachSource.Length > 0 ? config.AttachSource : db.Filename;
            string SourceHash() => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(source)));
            string sourceHash = SourceHash();
            if (!sourceHash.Equals(config.SourceHash, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Bronbestand wijkt af van de verwachte checksum.");
            if (config.AttachSource is { Length: > 0 })
            {
                var id = db.AttachXref(config.AttachSource, config.XrefName);
                using var tr = db.TransactionManager.StartTransaction();
                var model = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForWrite);
                using var reference = new BlockReference(Point3d.Origin, id);
                model.AppendEntity(reference); tr.AddNewlyCreatedDBObject(reference, true);
                tr.Commit();
            }
            var settings = new LegendSettings { Scale = 500, IncludeXrefLayers = true, IncludeQuantities = true };
            settings.ExampleLibraryPath = GlobalLegendSettings.Load(Path.Join(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "NlcsLegenda")).ExampleLibraryPath;
            var settingsJson = settings.ToJson();
            string? rowsHash = null, shapeHash = null;
            for (int iteration = 0; iteration < config.Runs; iteration++)
            {
                Progress($"analyse {iteration + 1}/{config.Runs}");
                using var tr = db.TransactionManager.StartTransaction();
                var registry = (LegendRegistry)Call("LegendStore", "Load", db, tr)!;
                var excluded = (IReadOnlyCollection<ObjectId>)Call("LegendManagement", "CollectManagedIds", db, tr, registry)!;
                var local = settings.Clone();
                double analyze = 0, group = 0;
                long allocated = GC.GetTotalAllocatedBytes(false);
                var clock = Stopwatch.StartNew();
                var analysis = DrawingAnalyzer.Analyze(db, tr, local, catalog: DescriptionCatalog.Default(), excludedIds: excluded,
                    reportTimings: (a, g) => { analyze = a; group = g; });
                double analyzed = clock.Elapsed.TotalMilliseconds;
                Progress($"tekenen {iteration + 1}/{config.Runs}");
                clock.Restart();
                var block = LegendBuilder.BuildBlock(db, tr, analysis, local, out var count, out var issues);
                double draw = clock.Elapsed.TotalMilliseconds;
                long bytes = GC.GetTotalAllocatedBytes(false) - allocated;
                if (issues.Count > 0) throw new InvalidOperationException("Tekenfout: " + issues[0].Reason);
                string Escape(string value) => JsonSerializer.Serialize(value)[1..^1];
                string rows = JsonSerializer.Serialize(analysis.Entries)
                    .Replace(Escape(db.Filename), Escape(Path.GetFileName(db.Filename)), StringComparison.OrdinalIgnoreCase)
                    .Replace(Escape(Escape(db.Filename)), Escape(Escape(Path.GetFileName(db.Filename))), StringComparison.OrdinalIgnoreCase);
                if (iteration == 0)
                {
                    using var file = File.Create(Path.Join(root, "rows.json.gz"));
                    using var zip = new System.IO.Compression.GZipStream(file, System.IO.Compression.CompressionLevel.Fastest);
                    using var writer = new StreamWriter(zip);
                    writer.Write(rows);
                }
                string data = Hash(rows);
                string shape = Shape(tr, block);
                string shapeDigest = Hash(shape);
                if (rowsHash is not null && rowsHash != data) throw new InvalidOperationException("Regels veranderen tussen metingen.");
                if (shapeHash is not null && shapeHash != shapeDigest) throw new InvalidOperationException("Geometrie verandert tussen metingen.");
                rowsHash = data; shapeHash = shapeDigest;
                if (iteration == 0) File.WriteAllText(Path.Join(root, "legend-shape.txt"), shape);
                samples.Add(new { kind = "build", iteration, analyzeMs = analyze, groupMs = group, analysisTotalMs = analyzed, drawMs = draw,
                    totalMs = analyzed + draw, allocatedBytes = bytes, entries = count, rowsHash = data, shapeHash = shapeDigest,
                    length = analysis.Entries.Sum(e => e.Metric.Length), area = analysis.Entries.Sum(e => e.Metric.Area),
                    objects = analysis.Entries.Sum(e => e.Metric.Count), klicRecords = analysis.Entries.Sum(e => e.KlicObjects.Sum(o => o.Records.Count)) });
                tr.Abort();
            }
            var definition = new LegendDefinition { Name = "Benchmark", GroupName = LegendRegistry.NewGroupName(), Settings = settings.Clone() };
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var registry = (LegendRegistry)Call("LegendStore", "Load", db, tr)!;
                registry.Add(definition);
                Update(db, tr, registry, definition);
                tr.Commit();
            }
            Call("Commands", "PurgePending", db);
            for (int iteration = 0; iteration < config.Runs; iteration++)
            {
                Progress($"bijwerken {iteration + 1}/{config.Runs}");
                long allocated = GC.GetTotalAllocatedBytes(false);
                var clock = Stopwatch.StartNew();
                int count;
                using (var tr = db.TransactionManager.StartTransaction())
                {
                    var registry = (LegendRegistry)Call("LegendStore", "Load", db, tr)!;
                    count = Update(db, tr, registry, registry.FindById(definition.Id)!);
                    tr.Commit();
                }
                Call("Commands", "PurgePending", db);
                double update = clock.Elapsed.TotalMilliseconds;
                long bytes = GC.GetTotalAllocatedBytes(false) - allocated;
                clock.Restart();
                doc.Editor.Regen();
                double regen = clock.Elapsed.TotalMilliseconds;
                using (var tr = db.TransactionManager.StartTransaction())
                {
                    var groups = (DBDictionary)tr.GetObject(db.GroupDictionaryId, OpenMode.ForRead);
                    var legend = (Group)tr.GetObject(groups.GetAt(definition.GroupName), OpenMode.ForRead);
                    var reference = legend.GetAllEntityIds().Select(id => tr.GetObject(id, OpenMode.ForRead)).OfType<BlockReference>().Single();
                    string digest = Hash(Shape(tr, reference.BlockTableRecord));
                    if (shapeHash != digest) throw new InvalidOperationException("Bijwerken verandert de legenda-uitvoer.");
                    samples.Add(new { kind = "update", iteration, updateMs = update, regenMs = regen, totalMs = update + regen,
                        allocatedBytes = bytes, entries = count, shapeHash = digest });
                    tr.Abort();
                }
            }
            if (SourceHash() != sourceHash) throw new InvalidOperationException("Bronbestand is tijdens de meting gewijzigd.");
            var dll = Plugin.Location;
            File.WriteAllText(Path.Join(root, "benchmark.json"), JsonSerializer.Serialize(new {
                scenario = config.Name, version = FileVersionInfo.GetVersionInfo(dll).FileVersion,
                dllHash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(dll))), settings = settingsJson,
                sourceHash = config.SourceHash, samples
            }, Json));
            Progress("klaar");
            doc.Editor.WriteMessage("\nNLCSBENCH DONE");
        }
        catch (System.Exception ex)
        {
            File.WriteAllText(Path.Join(root, "benchmark-error.txt"), ex.ToString());
            doc.Editor.WriteMessage("\nNLCSBENCH FAIL: " + ex.GetBaseException().Message);
        }
    }

    static int Update(Database db, Transaction tr, LegendRegistry registry, LegendDefinition definition)
    {
        object?[] args = { db, tr, registry, definition, 0, "" };
        var result = Call("Commands", "BuildManagedLegend", args);
        if (result?.ToString() != "Updated") throw new InvalidOperationException("Bijwerken mislukt: " + args[5]);
        Call("LegendStore", "Save", db, tr, registry);
        return (int)args[4]!;
    }

    static string Shape(Transaction tr, ObjectId block)
    {
        var parts = new List<string>();
        void Visit(ObjectId id, Matrix3d transform, int depth)
        {
            if (depth > 20) throw new InvalidOperationException("Te diep geneste legenda.");
            foreach (ObjectId objectId in (BlockTableRecord)tr.GetObject(id, OpenMode.ForRead))
            {
                var entity = (Entity)tr.GetObject(objectId, OpenMode.ForRead);
                if (entity is BlockReference reference) { Visit(reference.BlockTableRecord, transform * reference.BlockTransform, depth + 1); continue; }
                var layer = (LayerTableRecord)tr.GetObject(entity.LayerId, OpenMode.ForRead);
                string color = entity.Color.ToString() + ":" + layer.Color;
                string bounds = entity.Bounds is { } b ? Point(b.MinPoint.TransformBy(transform)) + ":" + Point(b.MaxPoint.TransformBy(transform)) : "";
                string detail = entity switch {
                    MText text => string.Join("|", text.Contents, Number(text.TextHeight), Number(text.Width), Number(text.LineSpacingFactor),
                        text.Attachment, Point(text.Location.TransformBy(transform)), ((TextStyleTableRecord)tr.GetObject(text.TextStyleId, OpenMode.ForRead)).FileName),
                    Polyline line => string.Join(";", Enumerable.Range(0, line.NumberOfVertices).Select(i => Point(line.GetPoint3dAt(i).TransformBy(transform))
                        + ":" + Number(line.GetBulgeAt(i)) + ":" + Number(line.GetStartWidthAt(i)) + ":" + Number(line.GetEndWidthAt(i)))) + ":" + line.Closed,
                    Line line => Point(line.StartPoint.TransformBy(transform)) + ":" + Point(line.EndPoint.TransformBy(transform)),
                    Circle circle => Point(circle.Center.TransformBy(transform)) + ":" + Number(circle.Radius),
                    Arc arc => string.Join(":", Point(arc.Center.TransformBy(transform)), Number(arc.Radius), Number(arc.StartAngle), Number(arc.EndAngle)),
                    Hatch hatch => string.Join(":", hatch.PatternName, Number(hatch.PatternScale), Number(hatch.PatternAngle), hatch.HatchStyle,
                        hatch.BackgroundColor, hatch.NumberOfLoops, Number(hatch.Area)),
                    Curve curve => string.Join(";", Enumerable.Range(0, 9).Select(i => Point(curve.GetPointAtParameter(curve.StartParam + (curve.EndParam - curve.StartParam) * i / 8).TransformBy(transform)))),
                    _ => ""
                };
                parts.Add(string.Join("|", entity.GetType().Name, color, entity.LineWeight, layer.LineWeight, entity.LinetypeScale,
                    entity.Linetype, layer.LinetypeObjectId.IsNull ? "" : ((LinetypeTableRecord)tr.GetObject(layer.LinetypeObjectId, OpenMode.ForRead)).Name,
                    entity.Transparency, entity.Visible, layer.IsPlottable, bounds, detail));
            }
        }
        Visit(block, Matrix3d.Identity, 0);
        parts.Sort(StringComparer.Ordinal);
        return string.Join("\n", parts);
    }

    public sealed class Config
    {
        public string Name { get; set; } = "";
        public string SourceHash { get; set; } = "";
        public string AttachSource { get; set; } = "";
        public string XrefName { get; set; } = "BRON";
        public int Runs { get; set; } = 4;
    }
}
