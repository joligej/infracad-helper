namespace NlcsLegenda.Core;

public enum TargetResolution
{
    None,
    Single,
    Ambiguous
}

public static class LegendTargeting
{
    public static TargetResolution ResolveAuto(LegendRegistry registry, out LegendDefinition? target)
    {
        target = null;
        return registry.Legends.Count switch
        {
            0 => TargetResolution.None,
            1 => Single(registry, out target),
            _ => TargetResolution.Ambiguous
        };
    }

    private static TargetResolution Single(LegendRegistry registry, out LegendDefinition? target)
    {
        target = registry.Legends[0];
        return TargetResolution.Single;
    }

    public static bool TryResolveByToken(
        LegendRegistry registry, string? token, out LegendDefinition? target, out string error)
    {
        target = null;
        error = string.Empty;
        token = token?.Trim() ?? string.Empty;
        if (token.Length == 0)
        {
            error = "geen legenda-id of naam opgegeven";
            return false;
        }

        var byId = registry.Legends
            .Where(l => string.Equals(l.Id, token, StringComparison.OrdinalIgnoreCase)).ToList();
        if (byId.Count == 1)
        {
            target = byId[0];
            return true;
        }

        var byName = registry.Legends
            .Where(l => string.Equals(l.Name, token, StringComparison.OrdinalIgnoreCase)).ToList();
        if (byName.Count == 1)
        {
            target = byName[0];
            return true;
        }
        if (byName.Count > 1)
        {
            error = $"meerdere legenda's heten '{token}'; gebruik de legenda-id";
            return false;
        }

        error = $"geen legenda met id of naam '{token}'";
        return false;
    }
}
