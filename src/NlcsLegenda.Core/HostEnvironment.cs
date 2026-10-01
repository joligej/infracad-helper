namespace NlcsLegenda.Core;

using System.Diagnostics;

// Centrale hostdetectie. accoreconsole.exe draait headless: interactieve editor-/GUI-aanroepen
// (GetEntity, GetPoint, dialogen) crashen daar met een native access violation. Commands
// gebruiken deze vlag om zulke paden vooraf te vermijden.
public static class HostEnvironment
{
    private static bool? _override;

    // Testhaak: forceert de uitkomst in unit-tests. null = echte procesdetectie.
    public static void SetCoreConsoleOverride(bool? value) => _override = value;

    public static bool IsCoreConsole => _override ?? DetectCoreConsole(CurrentProcessName());

    // Puur en los testbaar: bepaalt uit een procesnaam of dit de Core Console is.
    public static bool DetectCoreConsole(string? processName) =>
        !string.IsNullOrEmpty(processName) &&
        processName.StartsWith("accoreconsole", StringComparison.OrdinalIgnoreCase);

    private static string? CurrentProcessName()
    {
        try { return Process.GetCurrentProcess().ProcessName; }
        catch { return null; }
    }
}
