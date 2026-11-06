namespace NlcsLegenda.Update;

public static class UpdateCache
{
    public static void Clean(UpdateStore store, ReleaseVersion installed)
    {
        try
        {
            using var lease = store.TryLease("installation");
            if (lease is null) return;
            foreach (var directory in Children(Path.Join(store.DirectoryPath, "Packages"))
                .Where(d => ReleaseVersion.TryParse(d.Name, out var version) && version.CompareTo(installed) <= 0))
            {
                RemoveKnownFiles(directory, file => file == $"NlcsLegenda-{directory.Name}.msi");
            }
            foreach (var directory in Children(Path.Join(store.DirectoryPath, "Runner"))
                .Where(d => Guid.TryParseExact(d.Name, "N", out _) && d.LastWriteTimeUtc <= DateTime.UtcNow.AddDays(-2)))
            {
                RemoveKnownFiles(directory, file => file is "NlcsLegenda.Updater.exe" or "request.json");
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { store.Log("Tijdelijke updatebestanden: " + ex.Message); }
    }

    private static IEnumerable<DirectoryInfo> Children(string path)
    {
        var root = new DirectoryInfo(path);
        if (!root.Exists || root.Attributes.HasFlag(FileAttributes.ReparsePoint)) return Array.Empty<DirectoryInfo>();
        return root.EnumerateDirectories().Where(d => !d.Attributes.HasFlag(FileAttributes.ReparsePoint));
    }

    private static void RemoveKnownFiles(DirectoryInfo directory, Func<string, bool> known)
    {
        var files = directory.GetFiles();
        if (directory.GetDirectories().Length > 0 || files.Any(f => !known(f.Name) || f.Attributes.HasFlag(FileAttributes.ReparsePoint))) return;
        foreach (var file in files.OrderBy(f => f.Extension == ".exe" ? 0 : 1)) file.Delete();
        directory.Delete();
    }
}
