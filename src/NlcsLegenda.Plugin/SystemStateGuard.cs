using Autodesk.AutoCAD.DatabaseServices;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Core.Application;

namespace NlcsLegenda.Plugin;

// Legt opgegeven AutoCAD-systeemvariabelen vast en zet ze bij Dispose exact terug, ook bij
// exception, cancel of vroege return. De oorspronkelijke objectwaarde wordt bewaard zodat het
// runtime-type (short/int/double/string) niet via een stringomweg verloren gaat. Geneste guards
// werken omdat elke guard alleen zijn eigen vastgelegde waarden terugzet.
public sealed class SystemStateGuard : IDisposable
{
    private readonly List<(string Name, object Value)> _vars = new();
    private bool _disposed;

    private SystemStateGuard() { }

    public static SystemStateGuard Capture(params string[] names)
    {
        var guard = new SystemStateGuard();
        foreach (var name in names)
        {
            try
            {
                var value = AcApp.GetSystemVariable(name);
                if (value is not null)
                    guard._vars.Add((name, value));
            }
            catch
            {
                // Variabele bestaat niet op deze host/runtime: overslaan, niets terug te zetten.
            }
        }
        return guard;
    }

    // Zet een variabele en zorgt dat de oorspronkelijke waarde is vastgelegd voor herstel.
    public void Set(string name, object value)
    {
        if (!_vars.Exists(v => string.Equals(v.Name, name, StringComparison.OrdinalIgnoreCase)))
        {
            try
            {
                var original = AcApp.GetSystemVariable(name);
                if (original is not null)
                    _vars.Add((name, original));
            }
            catch { /* onbekende variabele: niet beheren */ }
        }
        AcApp.SetSystemVariable(name, value);
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        // In omgekeerde volgorde terugzetten, zodat geneste wijzigingen netjes afpellen.
        for (int i = _vars.Count - 1; i >= 0; i--)
        {
            try { AcApp.SetSystemVariable(_vars[i].Name, _vars[i].Value); }
            catch { /* host weigert terugzetten: niets zinnigs te doen, niet laten crashen */ }
        }
    }
}
