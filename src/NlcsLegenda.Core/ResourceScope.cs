using System.Runtime.ExceptionServices;

namespace NlcsLegenda.Core;

public sealed class ResourceScope : IDisposable
{
    private readonly Stack<Action> _cleanup = new();
    private bool _disposed;

    public T Own<T>(T resource) where T : IDisposable
    {
        Defer(resource.Dispose);
        return resource;
    }

    public void Defer(Action cleanup)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(cleanup);
        _cleanup.Push(cleanup);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        var errors = new List<Exception>();
        while (_cleanup.TryPop(out var cleanup))
        {
            try { cleanup(); }
            catch (Exception error) when (!ExceptionSafety.IsFatal(error)) { errors.Add(error); }
        }
        if (errors.Count == 1) ExceptionDispatchInfo.Capture(errors[0]).Throw();
        if (errors.Count > 1) throw new AggregateException("Opruimen is niet volledig gelukt.", errors);
    }
}
