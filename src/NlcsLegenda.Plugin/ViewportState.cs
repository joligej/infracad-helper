using System.Runtime.InteropServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Runtime;

namespace NlcsLegenda.Plugin;

internal static class ViewportState
{
#if NET10_0_OR_GREATER
    private const string DatabaseLibrary = "acdb26.dll";
#else
    private const string DatabaseLibrary = "acdb25.dll";
#endif

    [StructLayout(LayoutKind.Sequential)]
    private struct EntityName { public long First; public long Second; }

    [DllImport(DatabaseLibrary, CallingConvention = CallingConvention.Cdecl,
        EntryPoint = "?acdbGetAdsName@@YA?AW4ErrorStatus@Acad@@AEAY01_JVAcDbObjectId@@@Z")]
    private static extern ErrorStatus GetName(out EntityName name, ObjectId id);

    [DllImport("accore.dll", CallingConvention = CallingConvention.Cdecl, EntryPoint = "acdbEntGet")]
    private static extern IntPtr GetData(EntityName name);

    public static bool IsEnabled(Viewport viewport)
    {
        if (viewport.ObjectId.IsNull) return viewport.On;
        var status = GetName(out var name, viewport.ObjectId);
        if (status != ErrorStatus.OK) throw new Autodesk.AutoCAD.Runtime.Exception(status);
        var pointer = GetData(name);
        if (pointer == IntPtr.Zero) throw new InvalidOperationException("De toestand van het kaartvenster kon niet worden gelezen.");
        using var data = ResultBuffer.Create(pointer, true);
        // on en number beschrijven ook het actieve blad; dxf 90 bewaart de uit-stand
        foreach (TypedValue item in data)
            if (item.TypeCode == 90) return (Convert.ToInt32(item.Value) & 131072) == 0;
        throw new InvalidOperationException("De opgeslagen toestand van het kaartvenster ontbreekt.");
    }
}
