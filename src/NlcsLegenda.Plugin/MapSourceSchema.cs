using System.IO;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Core.Application;

namespace NlcsLegenda.Plugin;

internal static class MapSourceSchema
{
    private static readonly Dictionary<IntPtr, CachedSchema> Cache = new();

    public static IReadOnlyDictionary<string, string[]> Read(Database source, object application)
    {
        var key = source.UnmanagedObject;
        if (!Cache.TryGetValue(key, out var cached))
        {
            cached = new CachedSchema(source);
            Cache.Add(key, cached);
            source.DatabaseToBeDestroyed += (_, _) => Cache.Remove(key);
        }
        if (cached.Fields is not null && cached.Version == source.VersionGuid && cached.Source.FingerprintGuid == source.FingerprintGuid)
            return cached.Fields;

        var folder = Directory.CreateTempSubdirectory("NlcsLegenda-map-");
        Document? document = null;
        IReadOnlyDictionary<string, string[]> fields;
        var active = AcApp.DocumentManager.MdiActiveDocument;
        var working = HostApplicationServices.WorkingDatabase;
        try
        {
            string path = Path.Join(folder.FullName, "schema.dwg");
            File.Copy(source.Filename, path);
            document = AcApp.DocumentManager.Open(path, true);
            if (document.Database.FingerprintGuid != source.FingerprintGuid || document.Database.VersionGuid != source.VersionGuid)
                throw new InvalidOperationException("De xref-bron is na het laden gewijzigd. Herlaad deze xref om de Object Data opnieuw te lezen.");
            var project = application.GetType().GetMethod("GetProjectForDB", new[] { typeof(Database) })!
                .Invoke(application, new object[] { document.Database })!;
            var tables = project.GetType().GetProperty("ODTables")!.GetValue(project)!;
            fields = KlicAttachedData.ReadFields(tables);
        }
        finally
        {
            try
            {
                if (document is not null)
                {
                    if (AcApp.DocumentManager.MdiActiveDocument == document)
                        AcApp.DocumentManager.MdiActiveDocument = active;
                    document.CloseAndDiscard();
                }
            }
            finally
            {
                HostApplicationServices.WorkingDatabase = working;
                folder.Delete(true);
            }
        }
        cached.Version = source.VersionGuid;
        cached.Fields = fields;
        return fields;
    }

    private sealed class CachedSchema
    {
        public readonly Database Source;
        public string Version = "";
        public IReadOnlyDictionary<string, string[]>? Fields;

        public CachedSchema(Database database)
        {
            Source = database;
            database.ObjectModified += (_, e) => Invalidate(e.DBObject);
            database.ObjectAppended += (_, e) => Invalidate(e.DBObject);
            database.ObjectErased += (_, e) => Invalidate(e.DBObject);
        }

        private void Invalidate(DBObject value)
        {
            if (value.GetRXClass().DxfName.StartsWith("IRD_", StringComparison.Ordinal))
                Fields = null;
        }
    }
}
