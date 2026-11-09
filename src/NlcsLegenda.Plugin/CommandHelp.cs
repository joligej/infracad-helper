using System.Runtime.InteropServices;
using NlcsLegenda.Core;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Core.Application;

namespace NlcsLegenda.Plugin;

internal static class CommandHelp
{
    private static bool _pending;

    [DllImport("accore.dll", CharSet = CharSet.Unicode, CallingConvention = CallingConvention.Cdecl,
        EntryPoint = "acedSetFunHelp")]
    private static extern int SetHelp(string command, string file, string topic, int flags);

    public static void Initialize()
    {
        if (_pending || HostEnvironment.IsCoreConsole) return;
        // commando's zijn pas na initialize geregistreerd
        AcApp.Idle += Register;
        _pending = true;
    }

    public static void Shutdown()
    {
        if (!_pending) return;
        AcApp.Idle -= Register;
        _pending = false;
    }

    private static void Register(object? sender, EventArgs e)
    {
        Shutdown();
        try
        {
            foreach (var command in OfflineHelp.Commands)
            {
                var address = OfflineHelp.Find(command);
                if (address is null) continue;
                // de native helpviewer zoekt relatieve html-paden niet op
                if (SetHelp(command, address.LocalPath, "", 0) != 5100)
                    throw new InvalidOperationException("F1 kon niet worden gekoppeld aan " + command + ".");
            }
        }
        catch (Exception ex) when (!ExceptionSafety.IsFatal(ex))
        {
            AcApp.DocumentManager.MdiActiveDocument?.Editor.WriteMessage(
                "\nNLCS Legenda: commandohulp kon niet worden gekoppeld. Gebruik Hulp in het lint. " + ex.Message);
        }
    }
}
