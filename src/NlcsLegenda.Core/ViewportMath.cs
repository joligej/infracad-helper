namespace NlcsLegenda.Core;

public readonly record struct ViewportPlan(
    double PaperWidthMm, double PaperHeightMm, double ViewHeightModel, double ViewWidthModel);

public enum ViewportAnchor { Linksboven, Middelpunt }

public static class ViewportMath
{
    public static (double X, double Y) CenterOffset(double width, double height, ViewportAnchor anchor) =>
        anchor == ViewportAnchor.Middelpunt ? (0, 0) : (width / 2, -height / 2);

    public static double PaperMmPerModel(double scale)
    {
        if (!double.IsFinite(scale) || scale <= 0)
            throw new ArgumentOutOfRangeException(nameof(scale), "De schaal moet een eindig getal groter dan nul zijn.");
        return 1000.0 / scale;
    }

    public static ViewportPlan Compute(double modelWidth, double modelHeight, double scale, double marginMm)
    {
        double perModel = PaperMmPerModel(scale);
        double w = modelWidth * perModel + 2 * marginMm;
        double h = modelHeight * perModel + 2 * marginMm;
        return new ViewportPlan(w, h, h / perModel, w / perModel);
    }

    public static (double horizontal, double vertical) PaperMargins(
        ViewportPlan plan, double modelWidth, double modelHeight, double scale)
    {
        double perModel = PaperMmPerModel(scale);
        return ((plan.PaperWidthMm - modelWidth * perModel) / 2.0,
                (plan.PaperHeightMm - modelHeight * perModel) / 2.0);
    }
}
