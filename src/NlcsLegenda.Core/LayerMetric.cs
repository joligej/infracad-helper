namespace NlcsLegenda.Core;

public readonly record struct LayerMetric(int Count, double Length, double Area)
{
    public string? AreaIssue { get; init; }
    public bool HasUnknownArea => !string.IsNullOrEmpty(AreaIssue);

    public static string? MergeIssues(string? a, string? b)
    {
        var messages = new[] { a, b }.Where(s => !string.IsNullOrEmpty(s)).SelectMany(s => s!.Split('\n')).Distinct();
        string result = string.Join("\n", messages);
        return result.Length == 0 ? null : result;
    }

    public static LayerMetric Empty => new(0, 0.0, 0.0);

    public static LayerMetric operator +(LayerMetric a, LayerMetric b) =>
        new(a.Count + b.Count, a.Length + b.Length, a.Area + b.Area) { AreaIssue = MergeIssues(a.AreaIssue, b.AreaIssue) };

    public LayerMetric Add(int count = 0, double length = 0, double area = 0) =>
        new(Count + count, Length + length, Area + area) { AreaIssue = AreaIssue };
}
