using System.Reflection;
using System.Text.Json.Serialization;
using NlcsLegenda.Core;
using Xunit;
using Xunit.Abstractions;

namespace NlcsLegenda.Core.Tests;

// Geautomatiseerde wiring-audit: elke user-facing scalar-instelling moet een JSON-roundtrip
// overleven (opslag + reload). Zo vangen we "dode" settings die wel in de UI staan maar niet
// persistent zijn. De audit print tevens een matrix in de testoutput.
public class WiringAuditTests
{
    private readonly ITestOutputHelper _out;
    public WiringAuditTests(ITestOutputHelper output) => _out = output;

    private static bool IsScalar(Type t) =>
        t == typeof(bool) || t == typeof(int) || t == typeof(double) || t == typeof(string) || t.IsEnum;

    private static object Mutate(Type t, object? current) => t switch
    {
        _ when t == typeof(bool) => !(bool)(current ?? false),
        _ when t == typeof(int) => (int)(current ?? 0) + 7,
        _ when t == typeof(double) => (double)(current ?? 0) + 1.5,
        _ when t == typeof(string) => ((string?)current ?? "") + "_wiring",
        _ when t.IsEnum => NextEnum(t, current),
        _ => current!
    };

    private static object NextEnum(Type t, object? current)
    {
        var values = Enum.GetValues(t);
        foreach (var v in values)
            if (!Equals(v, current))
                return v;
        return values.GetValue(0)!;
    }

    [Fact]
    public void Elke_scalar_instelling_overleeft_json_roundtrip()
    {
        var props = typeof(LegendSettings).GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p is { CanRead: true, CanWrite: true }
                        && p.GetCustomAttribute<JsonIgnoreAttribute>() is null
                        && IsScalar(p.PropertyType))
            .ToList();

        var s = new LegendSettings();
        // Scale moet > 0 blijven; zet een geldige afwijkende waarde.
        s.Scale = 250;
        var expected = new Dictionary<string, object>();
        foreach (var p in props)
        {
            if (p.Name == nameof(LegendSettings.Scale)) { expected[p.Name] = 250.0; continue; }
            var val = Mutate(p.PropertyType, p.GetValue(s));
            p.SetValue(s, val);
            expected[p.Name] = val;
        }

        var restored = LegendSettings.FromJson(s.ToJson());

        var failed = new List<string>();
        foreach (var p in props)
        {
            var got = p.GetValue(restored);
            if (!Equals(got, expected[p.Name]))
                failed.Add($"{p.Name}: verwacht {expected[p.Name]}, kreeg {got}");
            _out.WriteLine($"{p.Name,-28} {p.PropertyType.Name,-10} roundtrip={(Equals(got, expected[p.Name]) ? "OK" : "FAIL")}");
        }

        Assert.True(failed.Count == 0, "Niet-persistente settings: " + string.Join("; ", failed));
        Assert.True(props.Count >= 40, $"verwacht veel scalar-settings, vond {props.Count}");
    }
}
