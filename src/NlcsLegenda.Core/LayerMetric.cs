namespace NlcsLegenda.Core;

public readonly record struct LayerMetric(int Count, double Length, double Area)
{
    public static LayerMetric Empty => new(0, 0.0, 0.0);

    public static LayerMetric operator +(LayerMetric a, LayerMetric b) =>
        new(a.Count + b.Count, a.Length + b.Length, a.Area + b.Area);

    public LayerMetric Add(int count = 0, double length = 0, double area = 0) =>
        new(Count + count, Length + length, Area + area);
}
