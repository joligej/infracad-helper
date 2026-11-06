using System.Globalization;

namespace NlcsLegenda.Update;

public readonly record struct ReleaseVersion(int Major, int Minor, int Patch) : IComparable<ReleaseVersion>
{
    public static bool TryParse(string? value, out ReleaseVersion version)
    {
        version = default;
        if (string.IsNullOrWhiteSpace(value)) return false;
        var text = value.StartsWith('v') ? value[1..] : value;
        var parts = text.Split('.');
        if (parts.Length != 3) return false;
        var numbers = new int[3];
        for (var i = 0; i < parts.Length; i++)
            if (parts[i].Length == 0 || parts[i].Any(c => c is < '0' or > '9') ||
                parts[i].Length > 1 && parts[i][0] == '0' ||
                !int.TryParse(parts[i], NumberStyles.None, CultureInfo.InvariantCulture, out numbers[i])) return false;
        // windows installer gebruikt drie versievelden
        if (numbers[0] > 255 || numbers[1] > 255 || numbers[2] > 65535) return false;
        version = new(numbers[0], numbers[1], numbers[2]);
        return true;
    }

    public static ReleaseVersion FromAssembly(Version version) => new(version.Major, version.Minor, Math.Max(0, version.Build));
    public int CompareTo(ReleaseVersion other) => (Major, Minor, Patch).CompareTo((other.Major, other.Minor, other.Patch));
    public override string ToString() => $"{Major}.{Minor}.{Patch}";
}
