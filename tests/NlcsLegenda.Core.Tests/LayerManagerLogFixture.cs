namespace Autodesk.AutoCAD.LayerManager;

internal static class LogFixture
{
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    internal static void Fail() => throw new NullReferenceException("hostmelding");
}
