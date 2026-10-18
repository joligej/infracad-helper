using Autodesk.AutoCAD.DatabaseServices;
using NlcsLegenda.Core;

namespace NlcsLegenda.Plugin;

internal sealed class TransientObjects : IDisposable
{
    public DBObjectCollection Items { get; } = new();
    private bool _disposed;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        using var cleanup = new ResourceScope();
        cleanup.Own(Items);
        // geregistreerde objecten blijven van de transactie
        foreach (DBObject item in Items)
            cleanup.Defer(() => { if (item.ObjectId.IsNull) item.Dispose(); });
    }
}
