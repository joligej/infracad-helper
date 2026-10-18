using System.Runtime.CompilerServices;

namespace NlcsLegenda.Core;

public static class DiagnosticRun
{
    public static bool Execute(Action test, Action<string> report, [CallerMemberName] string name = "")
    {
        ArgumentNullException.ThrowIfNull(test);
        ArgumentNullException.ThrowIfNull(report);
        try { test(); return true; }
        catch (Exception error) when (!ExceptionSafety.IsFatal(error))
        {
            report($"\nFAIL: {name}: {error}");
            return false;
        }
    }
}
