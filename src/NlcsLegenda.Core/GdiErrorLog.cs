using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Text;

namespace NlcsLegenda.Core;

public sealed class GdiErrorLog : IDisposable
{
    private const int MaxBytes = 256 * 1024;
    private readonly string _path;
    private readonly object _gate = new();
    private int _reports;
    private bool _disposed;
    [ThreadStatic] private static bool _writing;

    public GdiErrorLog(string path)
    {
        _path = path;
        AppDomain.CurrentDomain.FirstChanceException += OnException;
    }

    private void OnException(object? sender, FirstChanceExceptionEventArgs args)
    {
        if (_writing || args.Exception is not ExternalException
            || !args.Exception.Message.Contains("GDI+", StringComparison.OrdinalIgnoreCase)) return;
        WriteGuarded(() =>
        {
            lock (_gate)
            {
                if (_disposed || _reports >= 8) return;
                _reports++;
                try
                {
                    // volledige aanroep vóór het afwikkelen
                    var text = $"{DateTime.UtcNow:O} | NLCS {typeof(GdiErrorLog).Assembly.GetName().Version} | .NET {Environment.Version} | proces {Environment.ProcessId}\n"
                        + args.Exception + "\n" + Environment.StackTrace + "\n\n";
                    if (text.Length > MaxBytes / 4) text = text[..(MaxBytes / 4)];
                    Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
                    if (File.Exists(_path) && new FileInfo(_path).Length + Encoding.UTF8.GetByteCount(text) > MaxBytes)
                        File.WriteAllText(_path, text);
                    else
                        File.AppendAllText(_path, text);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
                {
                    // diagnose zonder vervanging van de oorspronkelijke fout
                }
            }
        });
    }

    private static void WriteGuarded(Action write)
    {
        _writing = true;
        try { write(); }
        finally { _writing = false; }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            AppDomain.CurrentDomain.FirstChanceException -= OnException;
        }
    }
}
