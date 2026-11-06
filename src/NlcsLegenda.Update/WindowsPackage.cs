using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace NlcsLegenda.Update;

[SupportedOSPlatform("windows")]
public static class WindowsPackage
{
    public const string UpgradeCode = "{7B2F4C9A-3E51-4A6D-9C2B-1F0E8D7A6C55}";
    private const string CertificateHash = "E2B9FBE20EB6E4423BA7DC723116CD77584DE3224ADB0DD8E48EF8089ACC826B";

    public static void VerifySignature(string path)
    {
        // ook de bestandsinhoud controleren; alleen een certificaat uitlezen is onvoldoende
        var status = MsiGetFileSignatureInformationW(path, 1, out var context, IntPtr.Zero, IntPtr.Zero);
        try
        {
            if (status != 0 || context == IntPtr.Zero) throw new CryptographicException($"De bestandshandtekening is niet geldig (0x{status:X8}).");
            using var certificate = new X509Certificate2(context);
            if (!Convert.ToHexString(SHA256.HashData(certificate.RawData)).Equals(CertificateHash, StringComparison.Ordinal))
                throw new CryptographicException("Het bestand is niet ondertekend door de verwachte uitgever.");
            var now = DateTime.UtcNow;
            if (now < certificate.NotBefore.ToUniversalTime() || now > certificate.NotAfter.ToUniversalTime())
                throw new CryptographicException("Het uitgeverscertificaat is niet meer geldig. Gebruik de handmatige update-instructies.");
            if (!certificate.Extensions.OfType<X509EnhancedKeyUsageExtension>().Any(e => e.EnhancedKeyUsages.Cast<Oid>().Any(o => o.Value == "1.3.6.1.5.5.7.3.3")))
                throw new CryptographicException("Het certificaat is niet bestemd voor softwareondertekening.");
        }
        finally { if (context != IntPtr.Zero) CertFreeCertificateContext(context); }
    }

    public static Dictionary<string, string> ReadProperties(string path)
    {
        Check(MsiOpenDatabaseW(path, IntPtr.Zero, out var database));
        try
        {
            Check(MsiDatabaseOpenViewW(database, "SELECT `Property`, `Value` FROM `Property`", out var view));
            try
            {
                Check(MsiViewExecute(view, 0));
                var result = new Dictionary<string, string>(StringComparer.Ordinal);
                while (true)
                {
                    var status = MsiViewFetch(view, out var record);
                    if (status == 259) break;
                    Check(status);
                    try { result.Add(ReadString(record, 1), ReadString(record, 2)); }
                    finally { MsiCloseHandle(record); }
                }
                return result;
            }
            finally { MsiCloseHandle(view); }
        }
        finally { MsiCloseHandle(database); }
    }

    public static void VerifyInstaller(string path, ReleaseVersion expected, int? acadYear = null)
    {
        VerifySignature(path);
        var properties = ReadProperties(path);
        if (acadYear is { } year) ReleasePolicy.RequireSignedSupport(properties, year);
        if (!properties.TryGetValue("UpgradeCode", out var code) || !code.Equals(UpgradeCode, StringComparison.OrdinalIgnoreCase) ||
            !properties.TryGetValue("ProductVersion", out var version) || version != expected.ToString() ||
            !properties.TryGetValue("ProductName", out var name) || name != "NLCS Legenda" ||
            properties.TryGetValue("ALLUSERS", out var scope) && scope.Length > 0)
            throw new InvalidDataException("Deze installer hoort niet bij de gekozen NLCS Legenda-update.");
    }

    public static string? InstalledVersion()
    {
        string? installed = null;
        var product = new StringBuilder(39);
        var buffer = new StringBuilder(256);
        for (uint index = 0; ; index++)
        {
            product.Clear();
            var status = MsiEnumRelatedProductsW(UpgradeCode, 0, index, product);
            if (status == 259) break;
            Check(status);
            buffer.Clear(); uint size = 256;
            // alleen de niet-verhoogde installatie van de huidige gebruiker
            status = MsiGetProductInfoExW(product.ToString(), null, 2, "VersionString", buffer, ref size);
            if (status is 1605 or 1608) continue;
            Check(status);
            if (installed is not null) throw new InvalidDataException("Er staan meerdere NLCS Legenda-installaties geregistreerd. Gebruik de handmatige installer.");
            installed = buffer.ToString();
        }
        return installed;
    }

    private static string ReadString(uint record, uint field)
    {
        uint length = 0;
        var status = MsiRecordGetStringW(record, field, null, ref length);
        if (status is not (0 or 234)) Check(status);
        var buffer = new StringBuilder(checked((int)length + 1)); length++;
        Check(MsiRecordGetStringW(record, field, buffer, ref length));
        return buffer.ToString();
    }

    private static void Check(uint status) { if (status != 0) throw new Win32Exception((int)status); }

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("msi.dll", CharSet = CharSet.Unicode)]
    private static extern int MsiGetFileSignatureInformationW(string path, uint flags, out IntPtr certificate, IntPtr hash, IntPtr hashSize);
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("crypt32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CertFreeCertificateContext(IntPtr certificate);
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("msi.dll", CharSet = CharSet.Unicode)]
    private static extern uint MsiOpenDatabaseW(string path, IntPtr mode, out uint database);
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("msi.dll", CharSet = CharSet.Unicode)]
    private static extern uint MsiDatabaseOpenViewW(uint database, string query, out uint view);
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("msi.dll")]
    private static extern uint MsiViewExecute(uint view, uint record);
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("msi.dll")]
    private static extern uint MsiViewFetch(uint view, out uint record);
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("msi.dll", CharSet = CharSet.Unicode)]
    private static extern uint MsiRecordGetStringW(uint record, uint field, StringBuilder? value, ref uint size);
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("msi.dll")]
    private static extern uint MsiCloseHandle(uint handle);
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("msi.dll", CharSet = CharSet.Unicode)]
    private static extern uint MsiEnumRelatedProductsW(string upgradeCode, uint reserved, uint index, StringBuilder product);
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("msi.dll", CharSet = CharSet.Unicode)]
    private static extern uint MsiGetProductInfoExW(string product, string? sid, uint context, string property, StringBuilder value, ref uint size);
}
