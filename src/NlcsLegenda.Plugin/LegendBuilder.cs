using Autodesk.AutoCAD.Colors;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using NlcsLegenda.Core;
using Color = Autodesk.AutoCAD.Colors.Color;

namespace NlcsLegenda.Plugin;

public static class LegendBuilder
{
    // Verhoudingen voor de opmaak (t.o.v. de bijbehorende teksthoogte of swatch-maat).
    private const double SubHeaderHeightRatio = 0.85;   // subkop t.o.v. kophoogte
    private const double TitleUnderlineOffset = 1.35;    // onderstreping onder de titel
    private const double SubHeaderIndentRatio = 0.15;    // inspringing subkop in de swatch
    private const double MarkerRadiusRatio = 0.28;       // straal symboolmarkering
    private const double SymbolFitRatio = 0.75;          // max. vulgraad in het vakje voor te grote symbolen
    private const double RemarksTitleRatio = 1.15;       // kop opmerkingen t.o.v. teksthoogte

    public static ObjectId BuildBlock(
        Database db, Transaction tr, AnalysisResult analysis, LegendSettings s, out int rowCount)
    {
        rowCount = 0;

        var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForWrite);
        var btrName = "NLCS_LEGENDA_" + Guid.NewGuid().ToString("N");
        var btr = new BlockTableRecord { Name = btrName, Origin = Point3d.Origin };
        var btrId = bt.Add(btr);
        tr.AddNewlyCreatedDBObject(btr, true);

        EnsureLayer(tr, db, s.FrameLayer, 7);
        EnsureLayer(tr, db, s.TextLayer, 7);
        EnsureLayer(tr, db, s.SwatchFrameLayer, 7, plottable: false);
        var styleId = ResolveTextStyle(tr, db, s.TextStyle);

        double swatchW = s.ToModel(s.SwatchWidthMm);
        double swatchH = s.ToModel(s.SwatchHeightMm);
        double textGap = s.ToModel(s.TextGapMm);
        double textH = s.ToModel(s.TextHeightMm);
        double headerH = s.ToModel(s.HeaderTextHeightMm);
        double subHeaderH = headerH * SubHeaderHeightRatio;
        double titleH = s.ToModel(s.TitleTextHeightMm);
        double margin = s.ToModel(s.BorderMarginMm);
        double colWidth = s.ToModel(s.ColumnWidthMm);

        var layout = LegendLayoutEngine.Compute(analysis.Entries, s);
        foreach (var item in layout.Items)
        {
            switch (item.Kind)
            {
                case LegendItemKind.Title:
                    AddText(btr, tr, item.Text, new Point3d(item.X, item.YTop - titleH, 0),
                        titleH, s.TextLayer, styleId);
                    var underline = new Line(
                        new Point3d(layout.MinX, item.YTop - titleH * TitleUnderlineOffset, 0),
                        new Point3d(layout.MaxX, item.YTop - titleH * TitleUnderlineOffset, 0)) { Layer = s.FrameLayer };
                    btr.AppendEntity(underline);
                    tr.AddNewlyCreatedDBObject(underline, true);
                    break;

                case LegendItemKind.Header:
                    AddText(btr, tr, item.Text, new Point3d(item.X, item.YTop - headerH, 0),
                        headerH, s.TextLayer, styleId);
                    break;

                case LegendItemKind.SubHeader:
                    AddText(btr, tr, item.Text, new Point3d(item.X + swatchW * SubHeaderIndentRatio, item.YTop - subHeaderH, 0),
                        subHeaderH, s.TextLayer, styleId);
                    break;

                case LegendItemKind.Entry:
                    try
                    {
                        DrawEntry(btr, tr, db, item, analysis, s,
                            swatchW, swatchH, textGap, textH, colWidth, styleId);
                    }
                    catch
                    {
                        // Eén regel die niet te tekenen is mag de rest niet blokkeren.
                    }
                    rowCount++;
                    break;
            }
        }

        var (bottomY, rightX) = DrawBelowLegend(btr, tr, s, analysis, layout, textH, styleId);

        if (s.DrawBorder)
        {
            double borderRight = Math.Max(layout.MaxX, rightX);
            var border = MakeRectangle(
                layout.MinX - margin, bottomY - margin,
                (borderRight - layout.MinX) + 2 * margin, (layout.MaxY - bottomY) + 2 * margin, s.FrameLayer);
            btr.AppendEntity(border);
            tr.AddNewlyCreatedDBObject(border, true);
        }

        return btrId;
    }

    private static List<string> BuildFooterLines(LegendSettings s, AnalysisResult analysis)
    {
        var lines = new List<string>();
        if (s.IncludeFooter)
        {
            var footer = s.FormatScale();
            if (s.IncludeDate)
                footer += "   " + s.FormatDate(DateTime.Now);
            lines.Add(footer);
        }
        if (s.IncludeTotalsRow)
        {
            var nl = System.Globalization.CultureInfo.GetCultureInfo("nl-NL");
            string fmt = "N" + Math.Max(0, s.QuantityDecimals);
            int count = analysis.Entries.Sum(e => e.Metric.Count);
            double length = analysis.Entries.Sum(e => e.Metric.Length);
            double area = analysis.Entries.Sum(e => e.Metric.Area);
            lines.Add($"{s.TotalsPrefix} {count} {s.UnitCount}, " +
                      $"{length.ToString(fmt, nl)} {s.UnitLength}, " +
                      $"{area.ToString(fmt, nl)} {s.UnitArea}");
        }
        return lines;
    }

    private static (double bottomY, double rightX) DrawBelowLegend(
        BlockTableRecord btr, Transaction tr, LegendSettings s, AnalysisResult analysis,
        LegendLayout layout, double textH, ObjectId styleId)
    {
        double left = layout.MinX;
        double right = layout.MaxX;
        double pad = textH * 1.1;
        double y = layout.MinY;
        double rightX = right;

        var footerLines = BuildFooterLines(s, analysis);
        if (footerLines.Count > 0)
        {
            y -= pad * 0.5;
            var separator = new Line(new Point3d(left, y, 0), new Point3d(right, y, 0)) { Layer = s.FrameLayer };
            btr.AppendEntity(separator);
            tr.AddNewlyCreatedDBObject(separator, true);
            y -= textH * 0.5;
            foreach (var line in footerLines)
            {
                y -= textH;
                AddText(btr, tr, line, new Point3d(left, y, 0), textH, s.TextLayer, styleId);
                y -= textH * 0.5;
            }
        }

        if (s.IncludeScaleBar)
        {
            y -= pad;
            (y, var barRight) = DrawScaleBar(btr, tr, s, left, y, textH, styleId);
            rightX = Math.Max(rightX, barRight);
        }

        if (s.IncludeRemarks && !string.IsNullOrWhiteSpace(s.RemarksText))
        {
            y -= pad;
            // MText wrapt exact op de beschikbare breedte, binnen de legenda/het kader.
            double widthModel = Math.Min(s.ToModel(s.RemarksWidthMm), right - left);
            y = DrawRemarks(btr, tr, s, left, y, textH, widthModel, styleId);
        }

        return (y, rightX);
    }

    private static (double bottomY, double rightX) DrawScaleBar(
        BlockTableRecord btr, Transaction tr, LegendSettings s, double left, double top,
        double textH, ObjectId styleId)
    {
        int segments = Math.Max(1, s.ScaleBarSegments);
        double segMeters = s.ScaleBarSegmentMeters > 0
            ? s.ScaleBarSegmentMeters
            : NiceStep(50.0 * s.Scale / 1000.0 / segments); // richt op ~50 mm papier totaal
        if (segMeters <= 0)
            segMeters = 1.0;

        double barH = s.ToModel(s.ScaleBarHeightMm);
        double barBottom = top - barH;
        double barWidth = segMeters * segments;

        var frame = MakeRectangle(left, barBottom, barWidth, barH, s.FrameLayer);
        btr.AppendEntity(frame);
        tr.AddNewlyCreatedDBObject(frame, true);

        for (int i = 0; i < segments; i += 2)
        {
            double x0 = left + i * segMeters;
            var seg = MakeRectangle(x0, barBottom, segMeters, barH, s.FrameLayer);
            var segId = btr.AppendEntity(seg);
            tr.AddNewlyCreatedDBObject(seg, true);
            AddSolidFill(btr, tr, segId, s.FrameLayer);
        }

        double labelH = textH * 0.85;
        double labelY = barBottom - labelH * 1.4;
        var nl = System.Globalization.CultureInfo.GetCultureInfo("nl-NL");
        for (int i = 0; i <= segments; i++)
        {
            double meters = i * segMeters;
            string label = i == segments
                ? $"{meters.ToString("0.##", nl)} {s.UnitLength}"
                : meters.ToString("0.##", nl);
            AddTextCentered(btr, tr, label, new Point3d(left + i * segMeters, labelY, 0), labelH, s.TextLayer, styleId);
        }

        return (labelY - labelH * 0.4, left + barWidth);
    }

    private static double DrawRemarks(
        BlockTableRecord btr, Transaction tr, LegendSettings s, double left, double top,
        double textH, double widthModel, ObjectId styleId)
    {
        double y = top;
        if (!string.IsNullOrWhiteSpace(s.RemarksTitle))
        {
            double titleH = textH * RemarksTitleRatio;
            y -= titleH;
            AddText(btr, tr, s.RemarksTitle, new Point3d(left, y, 0), titleH, s.TextLayer, styleId);
            var underline = new Line(
                new Point3d(left, y - titleH * 0.35, 0),
                new Point3d(left + TextWidthEstimate(s.RemarksTitle, titleH), y - titleH * 0.35, 0))
            { Layer = s.FrameLayer };
            btr.AppendEntity(underline);
            tr.AddNewlyCreatedDBObject(underline, true);
            y -= titleH * 0.7;
        }

        var mt = new MText();
        mt.SetDatabaseDefaults();
        mt.Layer = s.TextLayer;
        if (!styleId.IsNull)
            mt.TextStyleId = styleId;
        mt.TextHeight = textH;
        mt.Width = Math.Max(widthModel, textH * 4);
        mt.Attachment = AttachmentPoint.TopLeft;
        mt.Location = new Point3d(left, y, 0);
        mt.Contents = ToMTextContents(s.RemarksText);
        btr.AppendEntity(mt);
        tr.AddNewlyCreatedDBObject(mt, true);

        return y - mt.ActualHeight;
    }

    // MText heeft eigen escapes voor accolades, backslashes en regeleindes.
    private static string ToMTextContents(string text) =>
        text.Replace("\\", "\\\\").Replace("{", "\\{").Replace("}", "\\}")
            .Replace("\r\n", "\n").Replace("\r", "\n").Replace("\n", "\\P");

    private static double NiceStep(double value)
    {
        if (value <= 0)
            return 1.0;
        double exp = Math.Floor(Math.Log10(value));
        double pow = Math.Pow(10, exp);
        double f = value / pow;
        double nice = f < 1.5 ? 1 : f < 3 ? 2 : f < 4 ? 2.5 : f < 7.5 ? 5 : 10;
        return nice * pow;
    }

    private static double TextWidthEstimate(string text, double height) => text.Length * height * 0.6;

    private static void DrawEntry(
        BlockTableRecord btr, Transaction tr, Database db, LegendPlacement item, AnalysisResult analysis,
        LegendSettings s, double swatchW, double swatchH, double textGap, double textH,
        double colWidth, ObjectId styleId)
    {
        var entry = item.Entry!;
        double x = item.X;
        double top = item.YTop;
        double bottom = top - swatchH;
        double midY = (top + bottom) / 2.0;

        foreach (var lyr in entry.LayersByType.Values)
            EnsureLayer(tr, db, lyr, 7);

        // Swatch-kaders staan op een niet-plotbare hulplijnlaag.
        var rect = MakeRectangle(x, bottom, swatchW, swatchH, s.SwatchFrameLayer);
        btr.AppendEntity(rect);
        tr.AddNewlyCreatedDBObject(rect, true);

        if (entry.HasHatch)
        {
            var hatchLayer = entry.HatchOrFillLayer!;
            analysis.HatchSamples.TryGetValue(hatchLayer, out var sample);
            AddHatch(btr, tr, x, bottom, swatchW, swatchH, hatchLayer, sample, s.HatchScaleFactor);
        }

        bool symbolDrawn = false;
        if (entry.SymbolLayer is { } symLayer && s.InsertSymbolBlocks && entry.SymbolBlockName is { } blk)
        {
            symbolDrawn = TryInsertSymbol(btr, tr, db, blk, symLayer,
                x + swatchW / 2, midY, swatchW, swatchH);
        }

        if (entry.GeometryLayer is { } geoLayer)
        {
            if (entry.IsArea)
            {
                var inner = MakeRectangle(x, bottom, swatchW, swatchH, geoLayer);
                btr.AppendEntity(inner);
                tr.AddNewlyCreatedDBObject(inner, true);
            }
            else
            {
                var line = new Polyline();
                line.AddVertexAt(0, new Point2d(x, midY), 0, 0, 0);
                line.AddVertexAt(1, new Point2d(x + swatchW, midY), 0, 0, 0);
                line.Layer = geoLayer;
                btr.AppendEntity(line);
                tr.AddNewlyCreatedDBObject(line, true);
            }
        }
        else if (!symbolDrawn && entry.SymbolLayer is { } symLayer2)
        {
            var marker = new Circle(new Point3d(x + swatchW / 2, midY, 0), Vector3d.ZAxis,
                Math.Min(swatchW, swatchH) * MarkerRadiusRatio) { Layer = symLayer2 };
            btr.AppendEntity(marker);
            tr.AddNewlyCreatedDBObject(marker, true);
        }

        // Omschrijving als MText met vaste breedte: AutoCAD breekt exact op de kolombreedte
        // af (geen horizontale overloop) en de tekst wordt op het bandmidden gecentreerd.
        if (s.IncludeText && !string.IsNullOrWhiteSpace(entry.Description))
        {
            double textColWidth = colWidth - swatchW - textGap
                - (s.IncludeQuantities ? s.ToModel(s.QuantityColumnWidthMm) : 0.0);
            if (textColWidth < textH * 3)
                textColWidth = textH * 3;

            var mt = new MText();
            mt.SetDatabaseDefaults();
            mt.Layer = s.TextLayer;
            if (!styleId.IsNull)
                mt.TextStyleId = styleId;
            mt.TextHeight = textH;
            mt.Width = textColWidth;
            mt.Attachment = AttachmentPoint.MiddleLeft;
            mt.Location = new Point3d(x + swatchW + textGap, top - item.RowHeight / 2.0, 0);
            mt.Contents = ToMTextContents(entry.Description);
            btr.AppendEntity(mt);
            tr.AddNewlyCreatedDBObject(mt, true);
        }

        if (s.IncludeQuantities)
        {
            var quantity = entry.QuantityText(s.QuantityDecimals, s.UnitArea, s.UnitLength, s.UnitCount);
            if (!string.IsNullOrEmpty(quantity))
            {
                AddTextRight(btr, tr, quantity,
                    new Point3d(x + colWidth, midY - textH * 0.5, 0), textH, s.TextLayer, styleId);
            }
        }
    }

    private static bool TryInsertSymbol(
        BlockTableRecord btr, Transaction tr, Database db, string blockName, string layer,
        double centerX, double centerY, double swatchW, double swatchH)
    {
        try
        {
            var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
            if (!bt.Has(blockName))
                return false;
            var defId = bt[blockName];

            // Explode-to-primitives voorkomt dat annotatieve blokschaal de swatch opblaast.
            var primitives = ExplodeToPrimitives(btr, tr, defId, out var probeOk);
            if (!probeOk || primitives.Count == 0)
            {
                foreach (var id in primitives)
                    EraseSafe(tr, id);
                return false;
            }

            double minX = double.MaxValue, minY = double.MaxValue;
            double maxX = double.MinValue, maxY = double.MinValue;
            foreach (var id in primitives)
            {
                if (tr.GetObject(id, OpenMode.ForRead) is not Entity e)
                    continue;
                if (!TryEntityExtents(e, out var lo, out var hi))
                    continue;
                minX = Math.Min(minX, lo.X); minY = Math.Min(minY, lo.Y);
                maxX = Math.Max(maxX, hi.X); maxY = Math.Max(maxY, hi.Y);
            }

            double extW = maxX - minX;
            double extH = maxY - minY;
            if (extW <= 1e-9 || extH <= 1e-9)
            {
                foreach (var id in primitives)
                    EraseSafe(tr, id);
                return false;
            }

            double capW = swatchW * SymbolFitRatio;
            double capH = swatchH * SymbolFitRatio;
            // Symbolen worden niet vergroot; alleen te grote symbolen worden passend geschaald.
            double fit = Math.Min(1.0, Math.Min(capW / extW, capH / extH));

            var center = new Point3d((minX + maxX) / 2.0, (minY + maxY) / 2.0, 0);
            var m = Matrix3d.Displacement(new Vector3d(centerX - center.X, centerY - center.Y, 0))
                    * Matrix3d.Scaling(fit, center);

            foreach (var id in primitives)
            {
                if (tr.GetObject(id, OpenMode.ForWrite) is not Entity e)
                    continue;
                e.TransformBy(m);
                e.Layer = layer;
            }
            return true;
        }
        catch
        {
            return false;
        }
    }

    // Annotatieve of geneste blokken eerst naar gewone geometrie, daarna pas schalen.
    private static List<ObjectId> ExplodeToPrimitives(
        BlockTableRecord btr, Transaction tr, ObjectId defId, out bool ok)
    {
        var result = new List<ObjectId>();
        ok = false;
        try
        {
            var probe = new BlockReference(Point3d.Origin, defId);
            btr.AppendEntity(probe);
            tr.AddNewlyCreatedDBObject(probe, true);

            var queue = new Queue<ObjectId>();
            queue.Enqueue(probe.ObjectId);
            int guard = 0;
            while (queue.Count > 0 && guard++ < 5000)
            {
                var id = queue.Dequeue();
                if (tr.GetObject(id, OpenMode.ForWrite) is not Entity ent)
                    continue;
                if (ent is BlockReference bref)
                {
                    var pieces = new DBObjectCollection();
                    try { bref.Explode(pieces); }
                    catch { pieces.Dispose(); continue; }
                    foreach (DBObject obj in pieces)
                    {
                        if (obj is Entity pe)
                        {
                            btr.AppendEntity(pe);
                            tr.AddNewlyCreatedDBObject(pe, true);
                            queue.Enqueue(pe.ObjectId);
                        }
                        else
                        {
                            obj.Dispose();
                        }
                    }
                    bref.Erase();
                }
                else
                {
                    result.Add(id);
                }
            }
            ok = true;
        }
        catch
        {
            ok = false;
        }
        return result;
    }

    private static bool TryEntityExtents(Entity ent, out Point3d min, out Point3d max)
    {
        min = Point3d.Origin; max = Point3d.Origin;
        try
        {
            var ge = ent.GeometricExtents;
            min = ge.MinPoint; max = ge.MaxPoint;
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static void EraseSafe(Transaction tr, ObjectId id)
    {
        try
        {
            if (tr.GetObject(id, OpenMode.ForWrite) is Entity e && !e.IsErased)
                e.Erase();
        }
        catch
        {
            // Opruimen is 'best effort': als het object niet te wissen is, is dat niet erg.
        }
    }

    private static Polyline MakeRectangle(double x, double yBottom, double w, double h, string layer)
    {
        var pl = new Polyline();
        pl.AddVertexAt(0, new Point2d(x, yBottom), 0, 0, 0);
        pl.AddVertexAt(1, new Point2d(x + w, yBottom), 0, 0, 0);
        pl.AddVertexAt(2, new Point2d(x + w, yBottom + h), 0, 0, 0);
        pl.AddVertexAt(3, new Point2d(x, yBottom + h), 0, 0, 0);
        pl.Closed = true;
        pl.Layer = layer;
        return pl;
    }

    private static void AddText(
        BlockTableRecord btr, Transaction tr, string text, Point3d pos,
        double height, string layer, ObjectId styleId)
    {
        var t = new DBText();
        t.SetDatabaseDefaults();
        t.TextString = text;
        t.Height = height;
        t.Position = pos;
        t.Layer = layer;
        if (!styleId.IsNull)
            t.TextStyleId = styleId;
        btr.AppendEntity(t);
        tr.AddNewlyCreatedDBObject(t, true);
    }

    private static void AddTextRight(
        BlockTableRecord btr, Transaction tr, string text, Point3d alignPoint,
        double height, string layer, ObjectId styleId)
    {
        var t = new DBText();
        t.SetDatabaseDefaults();
        t.TextString = text;
        t.Height = height;
        t.Layer = layer;
        if (!styleId.IsNull)
            t.TextStyleId = styleId;
        t.HorizontalMode = TextHorizontalMode.TextRight;
        t.VerticalMode = TextVerticalMode.TextBase;
        t.Position = alignPoint;
        t.AlignmentPoint = alignPoint;
        btr.AppendEntity(t);
        tr.AddNewlyCreatedDBObject(t, true);
    }

    private static void AddTextCentered(
        BlockTableRecord btr, Transaction tr, string text, Point3d alignPoint,
        double height, string layer, ObjectId styleId)
    {
        var t = new DBText();
        t.SetDatabaseDefaults();
        t.TextString = text;
        t.Height = height;
        t.Layer = layer;
        if (!styleId.IsNull)
            t.TextStyleId = styleId;
        t.HorizontalMode = TextHorizontalMode.TextCenter;
        t.VerticalMode = TextVerticalMode.TextBase;
        t.Position = alignPoint;
        t.AlignmentPoint = alignPoint;
        btr.AppendEntity(t);
        tr.AddNewlyCreatedDBObject(t, true);
    }

    private static void AddSolidFill(
        BlockTableRecord btr, Transaction tr, ObjectId boundaryId, string layer)
    {
        var hatch = new Hatch();
        hatch.SetDatabaseDefaults();
        hatch.Layer = layer;
        hatch.SetHatchPattern(HatchPatternType.PreDefined, "SOLID");
        btr.AppendEntity(hatch);
        tr.AddNewlyCreatedDBObject(hatch, true);
        hatch.Associative = false;
        try
        {
            hatch.AppendLoop(HatchLoopTypes.Default, new ObjectIdCollection { boundaryId });
            hatch.EvaluateHatch(true);
        }
        catch
        {
            hatch.Erase();
        }
    }

    private static void AddHatch(
        BlockTableRecord btr, Transaction tr, double x, double yBottom, double w, double h,
        string layer, HatchSample? sample, double scaleFactor)
    {
        var hatch = new Hatch();
        hatch.SetDatabaseDefaults();
        hatch.Layer = layer;

        var patternName = sample?.PatternName ?? "SOLID";
        var patternType = sample?.PatternType ?? HatchPatternType.PreDefined;
        try
        {
            hatch.SetHatchPattern(patternType, patternName);
        }
        catch
        {
            hatch.SetHatchPattern(HatchPatternType.PreDefined, "SOLID");
        }

        if (sample is not null)
        {
            try { hatch.PatternScale = Math.Max(sample.PatternScale * scaleFactor, 1e-6); } catch { /* houd standaard */ }
            try { hatch.PatternAngle = sample.PatternAngle; } catch { /* houd standaard */ }
            try { hatch.SetHatchPattern(hatch.PatternType, hatch.PatternName); } catch { /* negeren */ }
        }

        btr.AppendEntity(hatch);
        tr.AddNewlyCreatedDBObject(hatch, true);

        // Niet-associatieve hatch: lus direct uit punten, zodat explode/update betrouwbaar blijft.
        hatch.Associative = false;
        var pts = new Point2dCollection
        {
            new Point2d(x, yBottom),
            new Point2d(x + w, yBottom),
            new Point2d(x + w, yBottom + h),
            new Point2d(x, yBottom + h)
        };
        var bulges = new DoubleCollection { 0.0, 0.0, 0.0, 0.0 };
        try
        {
            hatch.AppendLoop(HatchLoopTypes.Polyline, pts, bulges);
            hatch.EvaluateHatch(true);
        }
        catch
        {
            hatch.Erase();
        }
    }

    private static void EnsureLayer(Transaction tr, Database db, string name, short colorIndex, bool plottable = true)
    {
        var lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
        if (lt.Has(name))
        {
            // Bestaande hulplijnlaag alsnog niet-plotbaar maken.
            if (!plottable)
            {
                var existing = (LayerTableRecord)tr.GetObject(lt[name], OpenMode.ForRead);
                if (existing.IsPlottable)
                {
                    existing.UpgradeOpen();
                    existing.IsPlottable = false;
                }
            }
            return;
        }
        lt.UpgradeOpen();
        var ltr = new LayerTableRecord
        {
            Name = name,
            Color = Color.FromColorIndex(ColorMethod.ByAci, colorIndex),
            IsPlottable = plottable
        };
        lt.Add(ltr);
        tr.AddNewlyCreatedDBObject(ltr, true);
    }

    // InfraCAD-tekeningen hebben vaak een tekststijl met NLCS in de naam.
    private static ObjectId ResolveTextStyle(Transaction tr, Database db, string name)
    {
        var tst = (TextStyleTable)tr.GetObject(db.TextStyleTableId, OpenMode.ForRead);
        if (!string.IsNullOrWhiteSpace(name) && tst.Has(name))
            return tst[name];

        foreach (ObjectId id in tst)
        {
            if (tr.GetObject(id, OpenMode.ForRead) is TextStyleTableRecord ts &&
                ts.Name.Contains("NLCS", StringComparison.OrdinalIgnoreCase))
                return id;
        }
        return db.Textstyle;
    }
}
