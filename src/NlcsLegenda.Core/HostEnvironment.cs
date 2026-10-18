namespace NlcsLegenda.Core;

// interactieve aanroepen onveilig in de core console
public static class HostEnvironment
{
    private static bool? _override;
    public static void SetCoreConsoleOverride(bool? value) => _override = value;
    public static bool IsCoreConsole => _override ?? DetectCoreConsole(Path.GetFileNameWithoutExtension(Environment.ProcessPath));
    public static bool DetectCoreConsole(string? processName) =>
        !string.IsNullOrEmpty(processName) &&
        processName.StartsWith("accoreconsole", StringComparison.OrdinalIgnoreCase);
}
