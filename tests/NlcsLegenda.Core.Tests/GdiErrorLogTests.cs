using System.Runtime.InteropServices;
using NlcsLegenda.Core;
using Xunit;

namespace NlcsLegenda.Core.Tests;

public class GdiErrorLogTests
{
    private static void ThrowGdiForLogTest()
    {
        try { throw new ExternalException("A generic error occurred in GDI+."); }
        catch (ExternalException) { }
    }

    [Fact]
    public void CapturesOriginalExceptionAndCallingMethodWithoutSuppressingIt()
    {
        string folder = Path.Join(Path.GetTempPath(), "nlcs-gdi-" + Guid.NewGuid().ToString("N"));
        string path = Path.Join(folder, "gdi.log");
        try
        {
            using var log = new GdiErrorLog(path);
            var exception = Assert.Throws<ExternalException>((Action)(() => { throw new ExternalException("GDI+ test"); }));
            Assert.Equal("GDI+ test", exception.Message);
            ThrowGdiForLogTest();
            string text = File.ReadAllText(path);
            Assert.Contains("GDI+ test", text);
            Assert.Contains(nameof(ThrowGdiForLogTest), text);
            log.Dispose();
            ThrowGdiForLogTest();
            Assert.Equal(text, File.ReadAllText(path));
        }
        finally { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
    }

    [Fact]
    public void IgnoresOtherFailuresAndToleratesAnUnwritableLog()
    {
        string folder = Path.Join(Path.GetTempPath(), "nlcs-gdi-" + Guid.NewGuid().ToString("N"));
        string path = Path.Join(folder, "gdi.log");
        Directory.CreateDirectory(folder);
        try
        {
            using var log = new GdiErrorLog(path);
            Assert.Throws<ExternalException>((Action)(() => { throw new ExternalException("Andere fout"); }));
            Assert.Throws<InvalidOperationException>((Action)(() => { throw new InvalidOperationException("GDI+ tekst"); }));
            Assert.False(File.Exists(path));
            Directory.CreateDirectory(path);
            Assert.Throws<ExternalException>((Action)(() => { throw new ExternalException("GDI+ oorspronkelijke fout"); }));
        }
        finally { Directory.Delete(folder, true); }
    }

    [Fact]
    public void BoundsRepeatedReportsAndFileSize()
    {
        string folder = Path.Join(Path.GetTempPath(), "nlcs-gdi-" + Guid.NewGuid().ToString("N"));
        string path = Path.Join(folder, "gdi.log");
        Directory.CreateDirectory(folder);
        File.WriteAllText(path, new string('x', 256 * 1024));
        try
        {
            using var log = new GdiErrorLog(path);
            for (int i = 0; i < 20; i++) ThrowGdiForLogTest();
            Assert.Equal(8, File.ReadAllText(path).Split("A generic error occurred in GDI+.").Length - 1);
            Assert.InRange(new FileInfo(path).Length, 1, 256 * 1024);
        }
        finally { Directory.Delete(folder, true); }
    }
}
