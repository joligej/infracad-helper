using System.Collections;
using System.Collections.Concurrent;
using System.Linq.Expressions;
using System.Globalization;
using System.IO;
using System.Reflection;
using Autodesk.AutoCAD.DatabaseServices;
using NlcsLegenda.Core;

namespace NlcsLegenda.Plugin;

internal sealed class KlicAttachedData : IDisposable
{
    private readonly Database _database;
    private readonly Database _mapDatabase;
    private readonly Dictionary<ObjectId, (string Name, (string Name, int Id)[] Fields)> _definitions = new();
    private IReadOnlyDictionary<string, string[]> _tables = new Dictionary<string, string[]>();
    private object? _odTables;
    private bool _mapInitialized;
    private string? _mapIssue;
    private object? _readMode;
    private MethodInfo? _getObjectRecords;
    private readonly Dictionary<string, (string Name, int Index)[]> _knownFields = new(StringComparer.OrdinalIgnoreCase);
    private static readonly ConcurrentDictionary<(Type, string), Func<object, object?>> Getters = new();
    private static readonly ConcurrentDictionary<(Type, string), Func<object, object, object?>> Indexers = new();
    private static readonly ConcurrentDictionary<Type, Func<object, int, object?>> PropertyValues = new();
    public KlicAttachedData(Database database, Database mapDatabase)
    {
        _database = database;
        _mapDatabase = mapDatabase;
    }

    public void Read(Entity entity, Transaction tr, List<KlicRecord> records, List<string> issues)
    {
        if (entity.ExtensionDictionary.IsNull) return;
        var dictionary = (DBDictionary)tr.GetObject(entity.ExtensionDictionary, OpenMode.ForRead);
        if (dictionary.Contains("AEC_PROPERTY_SETS"))
        {
            try { ReadPropertySets(dictionary.GetAt("AEC_PROPERTY_SETS"), tr, records); }
            catch (Exception ex) when (IsAdapterFailure(ex)) { issues.Add("Property Sets niet leesbaar: " + Message(ex)); }
        }
        bool hasObjectData = false;
        foreach (DBDictionaryEntry entry in dictionary)
        {
            string type = entry.Value.ObjectClass.DxfName;
            if (type == "IRD_OBJ_RECORD") { hasObjectData = true; break; }
            if (type == "ACAD_PROXY_OBJECT" && tr.GetObject(entry.Value, OpenMode.ForRead) is ProxyObject proxy
                && proxy.OriginalDxfName == "IRD_OBJ_RECORD") { hasObjectData = true; break; }
        }
        if (!hasObjectData) return;
        EnsureMap();
        if (_mapIssue is not null) { issues.Add(_mapIssue); return; }
        string context = "objectrecords", fieldContext = "";
        try
        {
            _getObjectRecords ??= Method(_odTables!, "GetObjectRecords", typeof(uint), typeof(DBObject), _readMode!.GetType(), typeof(bool));
            using var rows = (IDisposable)_getObjectRecords.Invoke(_odTables, new[] { (object)0u, entity, _readMode, false })!;
            foreach (var row in (IEnumerable)rows)
            {
                using var rowLifetime = row as IDisposable;
                string name = (string)Get(row, "TableName")!;
                if (!name.StartsWith("KLIC_", StringComparison.OrdinalIgnoreCase)) continue;
                if (!_tables.TryGetValue(name, out var fields))
                    throw new InvalidDataException("De Object Data-tabel ontbreekt in de brontekening: " + name);
                context = name;
                if ((int)Get(row, "Count")! != fields.Length) throw new InvalidDataException("Object Data-veldindeling wijkt af.");
                if (!_knownFields.TryGetValue(name, out var known))
                    _knownFields.Add(name, known = fields.Select((key, index) => (Name: key, Index: index))
                        .Where(f => KlicFields.Find(f.Name) is not null).ToArray());
                var values = new Dictionary<string, string>(known.Length);
                fieldContext = "";
                foreach (var field in known)
                {
                    fieldContext = field.Name;
                    using var value = (IDisposable)Get(row, "Item", field.Index)!;
                    string property = Get(value, "Type")!.ToString() switch
                    {
                        "Character" => "StrValue", "Integer" => "Int32Value", "Real" => "DoubleValue",
                        _ => throw new InvalidDataException("Onverwacht Object Data-veldtype.")
                    };
                    values.Add(field.Name, Convert.ToString(Get(value, property), CultureInfo.InvariantCulture) ?? "");
                }
                if (values.Count > 0) records.Add(new KlicRecord(KlicSource.ObjectData, name, values));
            }
        }
        catch (Exception ex) when (IsAdapterFailure(ex)) { issues.Add("Object Data niet volledig leesbaar (" + context
            + (fieldContext.Length == 0 ? "" : "/" + fieldContext) + "): " + Message(ex)); }
    }

    private void ReadPropertySets(ObjectId id, Transaction tr, List<KlicRecord> records)
    {
        foreach (DBDictionaryEntry entry in (DBDictionary)tr.GetObject(id, OpenMode.ForRead))
        {
            var set = tr.GetObject(entry.Value, OpenMode.ForRead);
            if (set is ProxyObject) throw new InvalidOperationException("AEC-ondersteuning is niet geladen in deze host.");
            var definitionId = (ObjectId)Get(set, "PropertySetDefinition")!;
            if (!_definitions.TryGetValue(definitionId, out var definition))
            {
                var def = tr.GetObject(definitionId, OpenMode.ForRead);
                string name = (string)Get(set, "PropertySetDefinitionName")!;
                var fields = new List<(string Name, int Id)>();
                if (name.StartsWith("KLIC_", StringComparison.OrdinalIgnoreCase))
                    foreach (var field in (IEnumerable)Get(def, "Definitions")!)
                    {
                        string key = (string)Get(field, "Name")!;
                        if (KlicFields.Find(key) is not null) fields.Add((key, (int)Get(field, "Id")!));
                    }
                _definitions.Add(definitionId, definition = (name, fields.ToArray()));
            }
            if (definition.Fields.Length == 0) continue;
            var getAt = PropertyValues.GetOrAdd(set.GetType(), type =>
            {
                var target = Expression.Parameter(typeof(object));
                var index = Expression.Parameter(typeof(int));
                var method = type.GetMethod("GetAt", new[] { typeof(int) }) ?? throw new MissingMethodException(type.FullName, "GetAt");
                return Expression.Lambda<Func<object, int, object?>>(Expression.Convert(
                    Expression.Call(Expression.Convert(target, type), method, index), typeof(object)), target, index).Compile();
            });
            var values = new Dictionary<string, string>(definition.Fields.Length);
            foreach (var field in definition.Fields)
                values.Add(field.Name, Convert.ToString(getAt(set, field.Id), CultureInfo.InvariantCulture) ?? "");
            records.Add(new KlicRecord(KlicSource.PropertySets, definition.Name, values));
        }
    }

    private void EnsureMap()
    {
        if (_mapInitialized) return;
        _mapInitialized = true;
        if (HostEnvironment.IsCoreConsole)
        {
            _mapIssue = "Object Data niet gecontroleerd: Map-API is niet beschikbaar in Core Console.";
            return;
        }
        try
        {
            var assembly = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == "ManagedMapApi");
            if (assembly is null)
            {
                string file = Path.Join(Path.GetDirectoryName(typeof(Database).Assembly.Location)!, "Map", "ManagedMapApi.dll");
                if (!File.Exists(file)) throw new InvalidOperationException("Deze AutoCAD-host heeft geen Map-API.");
                assembly = Assembly.LoadFrom(file);
            }
            var application = (assembly.GetType("Autodesk.Gis.Map.HostMapApplicationServices", true)!.GetProperty("Application")
                ?? throw new MissingMemberException("Map.Application ontbreekt.")).GetValue(null)
                ?? throw new InvalidOperationException("Map.Application is niet beschikbaar.");
            if (!Autodesk.AutoCAD.ApplicationServices.Core.Application.DocumentManager.Cast<Autodesk.AutoCAD.ApplicationServices.Document>()
                .Any(d => d.Database.UnmanagedObject == _mapDatabase.UnmanagedObject))
                throw new InvalidOperationException("Object Data vereist een geopende hoofdtekening in Civil 3D/Map.");
            var project = Method(application, "GetProjectForDB", typeof(Database)).Invoke(application, new object[] { _mapDatabase });
            if (project is null) throw new InvalidOperationException("Deze hoofdtekening is niet beschikbaar als Map-project.");
            if (((Database)Get(project, "Database")!).UnmanagedObject != _mapDatabase.UnmanagedObject)
                throw new InvalidOperationException("Map-project hoort bij een andere hoofdtekening.");
            _odTables = Get(project, "ODTables")!;
            _readMode = Enum.Parse(assembly.GetType("Autodesk.Gis.Map.Constants.OpenMode", true)!, "OpenForRead");
            _tables = _database.UnmanagedObject == _mapDatabase.UnmanagedObject
                ? ReadFields(_odTables)
                : MapSourceSchema.Read(_database, application);
            if (_tables.Count == 0) _mapIssue = "Object Data aanwezig, maar geen leesbare KLIC-tabellen gevonden.";
        }
        catch (Exception ex) when (IsAdapterFailure(ex)) { _mapIssue = "Object Data niet gecontroleerd: " + Message(ex); }
    }

    internal static IReadOnlyDictionary<string, string[]> ReadFields(object tables)
    {
        var result = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        foreach (string name in (IEnumerable)Method(tables, "GetTableNames").Invoke(tables, null)!)
        {
            if (!name.StartsWith("KLIC_", StringComparison.OrdinalIgnoreCase)) continue;
            using var table = (IDisposable)Get(tables, "Item", name)!;
            using var definitions = (IDisposable)Get(table, "FieldDefinitions")!;
            var fields = new string[(int)Get(definitions, "Count")!];
            for (int i = 0; i < fields.Length; i++)
            {
                using var field = (IDisposable)Get(definitions, "Item", i)!;
                fields[i] = (string)Get(field, "Name")!;
            }
            result.Add(name, fields);
        }
        return result;
    }

    private static bool IsAdapterFailure(Exception error) => !ExceptionSafety.IsFatal(error)
        && error is Autodesk.AutoCAD.Runtime.Exception or TargetInvocationException or TypeLoadException
            or MissingMemberException or InvalidOperationException or InvalidCastException or ArgumentException
            or IOException or UnauthorizedAccessException or NotSupportedException or FormatException
            or System.Runtime.InteropServices.ExternalException;

    private static MethodInfo Method(object target, string name, params Type[] types) =>
        target.GetType().GetMethod(name, types) ?? throw new MissingMethodException(target.GetType().FullName, name);

    // alleen api-toegang bewaren; gegevens bij iedere analyse opnieuw lezen
    private static object? Get(object obj, string name) => Getters.GetOrAdd((obj.GetType(), name), key =>
    {
        var property = key.Item1.GetProperty(key.Item2) ?? throw new MissingMemberException(key.Item1.Name, key.Item2);
        var target = Expression.Parameter(typeof(object));
        return Expression.Lambda<Func<object, object?>>(Expression.Convert(
            Expression.Property(Expression.Convert(target, key.Item1), property), typeof(object)), target).Compile();
    })(obj);

    private static object? Get(object obj, string name, object index) => Indexers.GetOrAdd((obj.GetType(), name), key =>
    {
        var property = key.Item1.GetProperty(key.Item2) ?? throw new MissingMemberException(key.Item1.Name, key.Item2);
        var target = Expression.Parameter(typeof(object));
        var argument = Expression.Parameter(typeof(object));
        return Expression.Lambda<Func<object, object, object?>>(Expression.Convert(Expression.MakeIndex(
            Expression.Convert(target, key.Item1), property,
            new[] { Expression.Convert(argument, property.GetIndexParameters()[0].ParameterType) }), typeof(object)), target, argument).Compile();
    })(obj, index);
    private static string Message(Exception ex)
    {
        if (ex is TargetInvocationException { InnerException: { } inner }) ex = inner;
        var code = ex.GetType().GetProperty("ErrorCode")?.GetValue(ex);
        return ex.Message + (code is null ? "" : " (" + code + ")");
    }
    public void Dispose()
    {
        // tabellenverzameling eigendom van het map-project
        _odTables = null;
    }
}
