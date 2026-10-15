namespace NlcsLegenda.Core;

using System.Diagnostics;

// interactieve aanroepen onveilig in de core console
public static class HostEnvironment
{
    private static bool? _override;

    public static void SetCoreConsoleOverride(bool? value) => _override = value;

    public static bool IsCoreConsole => _override ?? DetectCoreConsole(CurrentProcessName());

    public static bool DetectCoreConsole(string? processName) =>
        !string.IsNullOrEmpty(processName) &&
        processName.StartsWith("accoreconsole", StringComparison.OrdinalIgnoreCase);

    private static string? CurrentProcessName()
    {
        try { using var process = Process.GetCurrentProcess(); return process.ProcessName; }
        catch { return null; }
    }
}
