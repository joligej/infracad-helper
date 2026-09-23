namespace NlcsLegenda.Core;

// Viewportberekening voor een legenda in papierruimte. Aanname: modelunits = meter,
// papier = mm, dus papier-mm per modeleenheid = 1000 / schaal. De legenda wordt exact op
// 1:schaal getoond met aan elke kant dezelfde marge in papier-mm.
public readonly record struct ViewportPlan(
    double PaperWidthMm, double PaperHeightMm, double ViewHeightModel, double ViewWidthModel);

public static class ViewportMath
{
    public static double PaperMmPerModel(double scale) => 1000.0 / scale;

    public static ViewportPlan Compute(double modelWidth, double modelHeight, double scale, double marginMm)
    {
        double perModel = PaperMmPerModel(scale);
        double w = modelWidth * perModel + 2 * marginMm;
        double h = modelHeight * perModel + 2 * marginMm;
        return new ViewportPlan(w, h, h / perModel, w / perModel);
    }

    // Terugrekening: de werkelijke marge in papier-mm aan elke kant, horizontaal en verticaal.
    public static (double horizontal, double vertical) PaperMargins(
        ViewportPlan plan, double modelWidth, double modelHeight, double scale)
    {
        double perModel = PaperMmPerModel(scale);
        return ((plan.PaperWidthMm - modelWidth * perModel) / 2.0,
                (plan.PaperHeightMm - modelHeight * perModel) / 2.0);
    }
}
