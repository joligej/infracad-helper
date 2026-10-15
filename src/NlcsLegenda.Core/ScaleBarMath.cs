namespace NlcsLegenda.Core;

public static class ScaleBarMath
{
    public static double NiceStep(double value)
    {
        if (value <= 0)
            return 1.0;

        double exp = Math.Floor(Math.Log10(value));
        double pow = Math.Pow(10, exp);
        double f = value / pow;
        double nice = f < 1.5 ? 1 : f < 3 ? 2 : f < 4 ? 2.5 : f < 7.5 ? 5 : 10;
        return nice * pow;
    }
}
