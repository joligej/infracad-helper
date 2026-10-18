using System.Runtime.ExceptionServices;

namespace NlcsLegenda.Core;

internal class FileOperations
{
    public virtual byte[]? Read(string path)
    {
        try { return File.ReadAllBytes(path); }
        catch (FileNotFoundException) { return null; }
        catch (DirectoryNotFoundException) { return null; }
    }
    public virtual void Write(string path, byte[] bytes) => File.WriteAllBytes(path, bytes);
    public virtual void Move(string source, string target) => File.Move(source, target, overwrite: true);
    public virtual void Delete(string path) => File.Delete(path);
}

internal static class AtomicFiles
{
    internal static void Write(IReadOnlyDictionary<string, string> contents, FileOperations files)
    {
        var originals = contents.Keys.ToDictionary(path => path, files.Read);
        var staged = new Dictionary<string, string>();
        var temporary = new List<string>();
        var replaced = new List<string>();
        var errors = new List<Exception>();
        string Stage(string target, byte[] bytes)
        {
            string path = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
            temporary.Add(path);
            files.Write(path, bytes);
            return path;
        }
        try
        {
            foreach (var item in contents)
                staged.Add(item.Key, Stage(item.Key, System.Text.Encoding.UTF8.GetBytes(item.Value)));
            foreach (var item in staged)
            {
                files.Move(item.Value, item.Key);
                replaced.Add(item.Key);
            }
        }
        catch (Exception error) when (!ExceptionSafety.IsFatal(error))
        {
            errors.Add(error);
            foreach (string target in replaced.AsEnumerable().Reverse())
            {
                try
                {
                    if (originals[target] is { } bytes) files.Move(Stage(target, bytes), target);
                    else files.Delete(target);
                }
                catch (Exception restoreError) when (restoreError is IOException or UnauthorizedAccessException)
                { errors.Add(restoreError); }
            }
        }
        finally
        {
            foreach (string path in temporary)
            {
                try { files.Delete(path); }
                catch (Exception cleanupError) when (cleanupError is IOException or UnauthorizedAccessException)
                { errors.Add(cleanupError); }
            }
        }
        if (errors.Count == 1) ExceptionDispatchInfo.Capture(errors[0]).Throw();
        if (errors.Count > 1) throw new AggregateException("Opslaan, terugzetten of opruimen van de globale standaard is mislukt.", errors);
    }
}
