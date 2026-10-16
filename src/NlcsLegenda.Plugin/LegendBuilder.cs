using System.IO;
using Autodesk.AutoCAD.Colors;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using NlcsLegenda.Core;
using Color = Autodesk.AutoCAD.Colors.Color;

namespace NlcsLegenda.Plugin;

public static class LegendBuilder
{
    private const double SubHeaderHeightRatio = 1.0;
    private const double SubHeaderIndentRatio = 0.15;

    private sealed record PreparedSymbol(IReadOnlyList<ObjectId> Entities, double Width, double Height, bool IsExample = false);

    public static ObjectId BuildBlock(
        Database db, Transaction tr, AnalysisResult analysis, LegendSettings s,
        out int rowCount, out IReadOnlyList<RenderIssue> issues)
    {
        rowCount = 0;
        var issueList = new List<RenderIssue>();
        issues = issueList;

        var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForWrite);
        var btrName = "NLCS_LEGENDA_" + Guid.NewGuid().ToString("N");
        var btr = new BlockTableRecord { Name = btrName, Origin = Point3d.Origin };
        var btrId = bt.Add(btr);
        tr.AddNewlyCreatedDBObject(btr, true);

        if (s.DrawSwatchFrame && new[] { s.FrameLayer, s.TextLayer, s.HeaderTextLayer }
            .Any(layer => string.Equals(layer, s.SwatchFrameLayer, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("Kies voor de hulpkaders een aparte, niet-plotbare laag.");
        if (!double.IsFinite(s.LineSpacingFactor) || s.LineSpacingFactor < 0.25 || s.LineSpacingFactor > 4)
            throw new InvalidOperationException("De regelhoogte-factor moet tussen 0,25 en 4 liggen.");
        EnsureLayer(tr, db, s.FrameLayer, 7);
        if (s.DrawSwatchFrame)
            EnsureLayer(tr, db, s.SwatchFrameLayer, 40, plottable: false, weight: LineWeight.LineWeight018);
        EnsureLayer(tr, db, s.TextLayer, 7, weight: LineWeight.LineWeight025);
        EnsureLayer(tr, db, s.HeaderTextLayer, 130, weight: LineWeight.LineWeight050);
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

        // werkelijke symboolhoogte reserveren vóór de indeling
        using var imports = new SymbolImportSession(db, tr);
        var symbols = new Dictionary<LegendEntry, PreparedSymbol>();
        var savedExamples = ExampleLibrary.ReadCatalog(db, tr).ToDictionary(e => e.BlockName, StringComparer.Ordinal);
        var exampleChoices = LegendOrder.AlignChoices(s.ExampleOverrides, analysis.Entries);
        ExampleLibrary? examples = null;
        try
        {
            foreach (var entry in analysis.Entries)
            {
                string key = LegendOrder.Key(entry);
                bool chosen = exampleChoices.TryGetValue(key, out var name);
                if (chosen && string.IsNullOrEmpty(name)) continue;
                LegendExample? example = chosen && savedExamples.TryGetValue(name!, out var stored) && bt.Has(stored.BlockName) ? stored : null;
                if (example is null && !string.IsNullOrWhiteSpace(s.ExampleLibraryPath))
                {
                    examples ??= new ExampleLibrary(s.ExampleLibraryPath, db, tr);
                    example = chosen ? examples.Examples.FirstOrDefault(e => e.BlockName == name)
                        : LegendExample.Match(examples.Examples, entry, s.Scale);
                    if (example is not null) examples.Import(db, tr, example);
                }
                if (chosen && example is null) throw new InvalidOperationException($"Het gekozen voorbeeld voor '{entry.Description}' ontbreekt.");
                if (example is null) continue;
                var reference = new BlockReference(Point3d.Origin, bt[example.BlockName])
                { ScaleFactors = new Scale3d(s.Scale / example.Scale), Layer = "0" };
                AppendEntity(btr, reference); tr.AddNewlyCreatedDBObject(reference, true);
                double factor = s.Scale / example.Scale;
                reference.TransformBy(Matrix3d.Displacement(new Vector3d(-example.AnchorX * factor, -example.AnchorY * factor, 0)));
                var bounds = reference.GeometricExtents;
                symbols.Add(entry, new PreparedSymbol(new[] { reference.ObjectId },
                    2 * Math.Max(Math.Abs(bounds.MinPoint.X), Math.Abs(bounds.MaxPoint.X)),
                    2 * Math.Max(Math.Abs(bounds.MinPoint.Y), Math.Abs(bounds.MaxPoint.Y)), true));
            }
        }
        finally { examples?.Dispose(); }
        if (s.InsertSymbolBlocks && analysis.Entries.Any(e => e.IsManual && e.SymbolLayer is not null && e.SymbolBlockName is null))
            throw new InvalidOperationException("Vul bij iedere eigen symboolregel een bloknaam in.");
        if (s.InsertSymbolBlocks)
            foreach (var entry in analysis.Entries.Where(e => !symbols.ContainsKey(e) && e.SymbolLayer is not null && e.SymbolBlockName is not null))
            {
                string layer = entry.SymbolLayer!;
                string name = entry.SymbolBlockName!;
                string renderLayer;
                if (!entry.IsManual && analysis.LayerStyles.TryGetValue(layer, out var display))
                    renderLayer = EnsureStyledLayer(tr, db, layer, display);
                else { EnsureLayer(tr, db, layer, 7); renderLayer = layer; }
                var sample = entry.IsManual ? null : analysis.SymbolSamples.GetValueOrDefault(layer);
                if (!TryInsertSymbol(btr, tr, db, imports, name, renderLayer, sample?.Transform ?? SymbolTransform.Identity,
                    sample, 0, 0, s.ModelUnitsPerPaperMm, s.IncludeInvisibleLayers, out var ids, out var reason))
                    throw new InvalidOperationException($"Symbool '{entry.Description}': {reason}");
                var bounds = new Extents3d();
                foreach (var id in ids)
                {
                    if (!TryEntityExtents((Entity)tr.GetObject(id, OpenMode.ForRead), out var lo, out var hi))
                        throw new InvalidOperationException($"Symbool '{entry.Description}' heeft een onderdeel zonder meetbare afmetingen.");
                    bounds.AddExtents(new Extents3d(lo, hi));
                }
                symbols.Add(entry, new PreparedSymbol(ids, bounds.MaxPoint.X - bounds.MinPoint.X, bounds.MaxPoint.Y - bounds.MinPoint.Y));
            }
        double sampleColumnWidth = Math.Max(swatchW, symbols.Values.Select(v => v.Width).DefaultIfEmpty(0).Max());
        colWidth += sampleColumnWidth - swatchW;
        var layoutSettings = s.Clone();
        layoutSettings.SwatchWidthMm = sampleColumnWidth / s.ModelUnitsPerPaperMm;
        layoutSettings.ColumnWidthMm = colWidth / s.ModelUnitsPerPaperMm;

        double MeasureTextHeight(string text, double width)
        {
            using var mt = CreateDescription(db, s, styleId, text, Math.Max(textH * 3, width - TextBearing(tr, styleId, textH)));
            return mt.ActualHeight;
        }
        double MeasureHeadingHeight(string text, double height)
        {
            using var heading = new MText();
            SetLegendDefaults(heading, db);
            heading.TextStyleId = styleId;
            heading.TextHeight = height;
            heading.Contents = "\\L" + MTextFormat.Escape(text) + "\\l";
            return heading.ActualHeight;
        }
        double extraHeight = 0;
        if (s.MaxLegendHeightMm > 0)
        {
            // notities en schaalbalk meenemen in de beschikbare hoogte
            var existing = btr.Cast<ObjectId>().ToHashSet();
            var emptyLayout = new LegendLayout { Items = Array.Empty<LegendPlacement>(), Columns = 1,
                MinX = 0, MaxX = colWidth, MinY = 0, MaxY = 0 };
            extraHeight = -DrawBelowLegend(btr, tr, s, analysis, emptyLayout, textH, styleId).bottomY;
            foreach (var id in btr.Cast<ObjectId>().Where(id => !existing.Contains(id)).ToArray())
                ((Entity)tr.GetObject(id, OpenMode.ForWrite)).Erase();
            extraHeight += s.DrawBorder ? 2 * margin : 0;
            layoutSettings.MaxLegendHeightMm -= extraHeight / s.ModelUnitsPerPaperMm;
            if (layoutSettings.MaxLegendHeightMm <= 0)
                throw new InvalidOperationException("De maximale hoogte is te klein voor de opmerkingen en schaalbalk. Vergroot de hoogte of zet deze op 0.");
        }
        var layout = LegendLayoutEngine.Compute(analysis.Entries, layoutSettings, MeasureTextHeight,
            entry => symbols.TryGetValue(entry, out var symbol) ? symbol.Height : 0, MeasureHeadingHeight);
        if (s.MaxLegendHeightMm > 0 && layout.Height + extraHeight > s.ToModel(s.MaxLegendHeightMm) + 1e-8)
            throw new InvalidOperationException("Een regel past niet binnen de maximale legendahoogte. Vergroot de hoogte of zet deze op 0.");
        foreach (var item in layout.Items)
        {
            switch (item.Kind)
            {
                case LegendItemKind.Title:
                    AddText(btr, tr, item.Text, new Point3d(item.X, item.YTop - titleH, 0),
                        titleH, s.HeaderTextLayer, styleId, underline: true);
                    break;

                case LegendItemKind.Header:
                    AddText(btr, tr, item.Text, new Point3d(item.X, item.YTop - headerH, 0),
                        headerH, s.TextLayer, styleId, underline: true);
                    break;

                case LegendItemKind.SubHeader:
                    AddText(btr, tr, item.Text, new Point3d(item.X + swatchW * SubHeaderIndentRatio, item.YTop - subHeaderH, 0),
                        subHeaderH, s.TextLayer, styleId, underline: true);
                    break;

                case LegendItemKind.Entry:
                    try
                    {
                        DrawEntry(btr, tr, db, imports, item, analysis, s,
                            swatchW, swatchH, textGap, textH, colWidth, styleId, sampleColumnWidth,
                            symbols.GetValueOrDefault(item.Entry!));
                        rowCount++;
                    }
                    catch (Exception ex)
                    {
                        throw new InvalidOperationException(
                            $"Legenda niet gewijzigd: '{item.Entry!.Description}' ({item.Entry.PrimaryLayer}) kon niet worden getekend.", ex);
                    }
                    break;
            }
        }

        var (bottomY, rightX) = DrawBelowLegend(btr, tr, s, analysis, layout, textH, styleId);

        if (s.DrawBorder)
        {
            double borderRight = Math.Max(layout.MaxX, rightX);
            var border = MakeRectangle(btr.Database,
                layout.MinX - margin, bottomY - margin,
                (borderRight - layout.MinX) + 2 * margin, (layout.MaxY - bottomY) + 2 * margin, s.FrameLayer);
            AppendEntity(btr, border);
            tr.AddNewlyCreatedDBObject(border, true);
        }

        ValidateHostLinetypeReferences(db, tr, btrId);
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

        if (s.IncludeRemarks && !string.IsNullOrWhiteSpace(s.RemarksText))
        {
            y -= s.ToModel(s.HeaderSpacingMm);
            double widthModel = Math.Min(s.ToModel(s.RemarksWidthMm), right - left);
            y = DrawRemarks(btr, tr, s, left, y, textH, widthModel, styleId);
        }

        if (s.IncludeScaleBar)
        {
            y -= pad;
            (y, var barRight) = DrawScaleBar(btr, tr, s, left, y, textH, styleId);
            rightX = Math.Max(rightX, barRight);
        }

        foreach (var line in BuildFooterLines(s, analysis))
        {
            double height = s.ToModel(1.8);
            y -= s.ToModel(0.5) + height;
            AddText(btr, tr, line, new Point3d(left, y, 0), height, s.TextLayer, styleId);
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
            : 10.0 * s.Scale / 1000.0;
        if (segMeters <= 0)
            segMeters = 1.0;

        double barH = s.ToModel(s.ScaleBarHeightMm);
        double labelH = s.ToModel(1.8);
        double barBottom = top - barH - s.ToModel(1.0) - labelH;
        double barWidth = segMeters * segments;

        var frame = MakeRectangle(btr.Database, left, barBottom, barWidth, barH, s.FrameLayer);
        AppendEntity(btr, frame);
        tr.AddNewlyCreatedDBObject(frame, true);

        for (int i = 0; i < segments; i++)
        {
            double x0 = left + i * segMeters;
            var seg = MakeRectangle(btr.Database, x0, barBottom + (i % 2 == 0 ? barH / 2 : 0), segMeters, barH / 2, s.FrameLayer);
            var segId = AppendEntity(btr, seg);
            tr.AddNewlyCreatedDBObject(seg, true);
            AddSolidFill(btr, tr, segId, s.FrameLayer);
        }

        double labelY = barBottom + barH + s.ToModel(1.0);
        var nl = System.Globalization.CultureInfo.GetCultureInfo("nl-NL");
        for (int i = 0; i <= segments; i++)
        {
            double meters = i * segMeters;
            string label = i == segments
                ? $"{meters.ToString("0.##", nl)}{s.UnitLength}"
                : meters.ToString("0.##", nl);
            AddTextCentered(btr, tr, label, new Point3d(left + i * segMeters, labelY, 0), labelH, s.TextLayer, styleId);
        }

        return (barBottom, left + barWidth);
    }

    private static double DrawRemarks(
        BlockTableRecord btr, Transaction tr, LegendSettings s, double left, double top,
        double textH, double widthModel, ObjectId styleId)
    {
        double y = top;
        if (!string.IsNullOrWhiteSpace(s.RemarksTitle))
        {
            double titleH = s.ToModel(s.TitleTextHeightMm);
            y -= titleH;
            var heading = AddText(btr, tr, s.RemarksTitle, new Point3d(left, y, 0), titleH, s.HeaderTextLayer, styleId, underline: true);
            y = Math.Min(y - (s.ToModel(s.HeaderSpacingMm) - textH),
                heading.GeometricExtents.MinPoint.Y - textH * 0.3);
        }

        using var mt = new MText();
        SetLegendDefaults(mt, btr.Database);
        mt.Layer = s.TextLayer;
        if (!styleId.IsNull)
            mt.TextStyleId = styleId;
        mt.TextHeight = textH;
        mt.Width = Math.Max(widthModel, textH * 4);
        mt.Attachment = AttachmentPoint.TopLeft;
        mt.Location = new Point3d(left + TextBearing(tr, styleId, textH), y, 0);
        // alinea-inspringing ten opzichte van de teksthoogte
        mt.Contents = (s.RemarksText.StartsWith("-\t", StringComparison.Ordinal) ? "\\pxi-2,l2;" : string.Empty)
            + MTextFormat.Escape(s.RemarksText);
        AppendEntity(btr, mt);
        tr.AddNewlyCreatedDBObject(mt, true);

        return y - mt.ActualHeight;
    }

    private static void DrawEntry(
        BlockTableRecord btr, Transaction tr, Database db, SymbolImportSession imports, LegendPlacement item, AnalysisResult analysis,
        LegendSettings s, double swatchW, double swatchH, double textGap, double textH,
        double colWidth, ObjectId styleId, double sampleColumnWidth, PreparedSymbol? prepared)
    {
        var entry = item.Entry!;
        double x = item.X;
        double top = item.YTop;
        double midY = top - item.RowHeight / 2.0;
        double bottom = midY - swatchH / 2.0;

        var rowEntities = new List<ObjectId>();
        var renderLayers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var lyr in entry.LayersByType.Values)
        {
            if (renderLayers.ContainsKey(lyr))
                continue;
            if (!entry.IsManual && analysis.LayerStyles.TryGetValue(lyr, out var style))
                renderLayers[lyr] = EnsureStyledLayer(tr, db, lyr, style);
            else
            {
                EnsureLayer(tr, db, lyr, 7);
                renderLayers[lyr] = lyr;
            }
        }

        // tekenvolgorde: vlak, vulling, arcering, lijn, symbool
        if (prepared?.IsExample != true && entry.VlakLayer is { } vlakLayer)
        {
            var area = MakeRectangle(btr.Database, x, bottom, swatchW, swatchH, renderLayers[vlakLayer]);
            rowEntities.Add(AppendEntity(btr, area));
            tr.AddNewlyCreatedDBObject(area, true);
        }

        if (prepared?.IsExample != true && entry.FillLayer is { } fillLayer)
        {
            var fillBoundary = MakeRectangle(btr.Database, x, bottom, swatchW, swatchH, renderLayers[fillLayer]);
            var fillId = AppendEntity(btr, fillBoundary);
            tr.AddNewlyCreatedDBObject(fillBoundary, true);
            rowEntities.Add(fillId);
            rowEntities.Add(AddSolidFill(btr, tr, fillId, renderLayers[fillLayer]));
        }

        if (prepared?.IsExample != true && entry.HatchLayer is { } hatchLayer)
        {
            var pattern = entry.ManualHatchPattern ?? "SOLID";
            var sample = entry.IsManual ? new HatchSample {
                PatternName = pattern, PatternType = HatchPatternType.PreDefined,
                Transparency = new TransparencyValue(TransparencyMethod.ByLayer, 255),
                IsSolid = pattern.Equals("SOLID", StringComparison.OrdinalIgnoreCase)
            } : analysis.HatchSamples.GetValueOrDefault(hatchLayer);
            rowEntities.Add(AddHatch(btr, tr, imports, x, bottom, swatchW, swatchH, renderLayers[hatchLayer], sample,
                s.HatchScaleFactor * (sample?.ScaleForLegend(s.Scale) ?? 1)));
        }

        if (prepared?.IsExample != true && entry.GeometryLayer is { } geoLayer)
        {
            var band = SeparationBandProfile.For(entry);
            void AddLine(double offsetMm, bool edge)
            {
                using var line = new Polyline();
                SetLegendDefaults(line, db);
                double y = midY + s.ToModel(offsetMm);
                bool bandDirection = band is not null;
                line.AddVertexAt(0, new Point2d(x + (bandDirection ? swatchW : 0), y), 0, 0, 0);
                line.AddVertexAt(1, new Point2d(x + (bandDirection ? 0 : swatchW), y), 0, 0, 0);
                line.Layer = renderLayers[geoLayer];
                if (band is null && analysis.LayerStyles.TryGetValue(geoLayer, out var sourceStyle))
                    line.ConstantWidth = sourceStyle.PolylineWidth;
                if (edge)
                {
                    var style = new LayerDisplayStyle
                    {
                        Color = Color.FromColorIndex(ColorMethod.ByAci, 210),
                        LineWeight = LineWeight.LineWeight035,
                        Linetype = "Continuous", LinetypeIdentity = "Continuous"
                    };
                    line.Layer = EnsureStyledLayer(tr, db, "NLCS_LEGENDA_KANTVERHARDING", style);
                    line.Color = style.Color;
                    line.LineWeight = style.LineWeight;
                    line.LinetypeId = ResolveStyleLinetype(tr, db, style);
                }
                rowEntities.Add(AppendEntity(btr, line));
                tr.AddNewlyCreatedDBObject(line, true);
            }
            if (band is not null)
            {
                AddLine(-band.OuterOffsetMm, true);
                AddLine(-band.InnerOffsetMm, false);
                AddLine(band.InnerOffsetMm, false);
                AddLine(band.OuterOffsetMm, true);
            }
            else AddLine(0, false);
        }

        if (prepared is not null)
        {
            var move = Matrix3d.Displacement(new Vector3d(x + sampleColumnWidth / 2, midY, 0));
            foreach (var id in prepared.Entities)
            {
                ((Entity)tr.GetObject(id, OpenMode.ForWrite)).TransformBy(move);
                rowEntities.Add(id);
            }
        }

        foreach (ObjectId id in rowEntities)
        {
            if (tr.GetObject(id, OpenMode.ForWrite) is not Entity entity)
                continue;
            foreach (var rendered in renderLayers)
                if (!entry.IsManual && entity.Layer == rendered.Value && analysis.LayerStyles.TryGetValue(rendered.Key, out var style))
                {
                    if (rendered.Key != entry.SymbolLayer)
                    {
                        entity.Color = style.Color;
                        entity.LineWeight = style.LineWeight;
                        entity.Transparency = style.Transparency.ToAutoCadValue();
                        entity.LinetypeId = ResolveStyleLinetype(tr, db, style);
                        entity.LinetypeScale = style.LinetypeScale;
                    }
                }
        }

        if (s.DrawSwatchFrame)
        {
            double frameHeight = Math.Max(swatchH, prepared?.Height ?? 0);
            var frame = MakeRectangle(btr.Database, x, midY - frameHeight / 2, sampleColumnWidth, frameHeight, s.SwatchFrameLayer);
            AppendEntity(btr, frame);
            tr.AddNewlyCreatedDBObject(frame, true);
        }

        if (s.IncludeText && !string.IsNullOrWhiteSpace(entry.Description))
        {
            double textColWidth = colWidth - sampleColumnWidth - textGap - TextBearing(tr, styleId, textH)
                - (s.IncludeQuantities ? s.ToModel(s.QuantityColumnWidthMm) : 0.0);
            if (textColWidth < textH * 3)
                textColWidth = textH * 3;

            var mt = CreateDescription(db, s, styleId, entry.DisplayDescription, textColWidth);
            // gelijke basislijn met en zonder onderlengte
            mt.Attachment = AttachmentPoint.TopLeft;
            mt.Location = new Point3d(x + sampleColumnWidth + textGap + TextBearing(tr, styleId, textH),
                midY + Math.Max(textH * (1 - TemplateDefaults.TextBaselineOffsetRatio), mt.ActualHeight / 2), 0);
            rowEntities.Add(AppendEntity(btr, mt));
            tr.AddNewlyCreatedDBObject(mt, true);
        }

        if (s.IncludeQuantities)
        {
            var quantity = entry.QuantityText(s.QuantityDecimals, s.UnitArea, s.UnitLength, s.UnitCount);
            if (!string.IsNullOrEmpty(quantity))
            {
                var quantityText = AddTextRight(btr, tr, quantity,
                    new Point3d(x + colWidth, midY - textH * 0.5, 0), textH, s.TextLayer, styleId);
                rowEntities.Add(quantityText.ObjectId);
            }
        }
        LegendManagement.TagRow(db, tr, rowEntities, entry);
    }

    private static MText CreateDescription(Database db, LegendSettings s, ObjectId styleId,
        string text, double width)
    {
        var mt = new MText();
        SetLegendDefaults(mt, db);
        mt.Layer = s.TextLayer;
        mt.TextStyleId = styleId;
        mt.TextHeight = s.ToModel(s.TextHeightMm);
        mt.Width = width;
        mt.LineSpacingStyle = LineSpacingStyle.AtLeast;
        mt.LineSpacingFactor = s.LineSpacingFactor;
        mt.Contents = MTextFormat.Escape(text);
        return mt;
    }

    private static bool TryInsertSymbol(
        BlockTableRecord btr, Transaction tr, Database db, SymbolImportSession imports, string blockName, string layer,
        SymbolTransform sourceTransform, SymbolSample? sample,
        double centerX, double centerY, double modelPerPaperMm, bool includeInvisible,
        out IReadOnlyList<ObjectId> placed, out string failureReason)
    {
        failureReason = string.Empty;
        placed = Array.Empty<ObjectId>();
        var primitives = new List<ObjectId>();
        var created = new List<ObjectId>();
        try
        {
            var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
            if (sample?.IsPrimitive != true && !bt.Has(blockName))
            {
                failureReason = $"Symboolblok '{blockName}' ontbreekt in de hostdatabase.";
                return false;
            }
            bool probeOk;
            if (sample is { IsPrimitive: true })
            {
                var primitive = imports.ImportPrimitive(sample.ReferenceId, btr.ObjectId, sample.SourceXref);
                primitive.XData = null;
                if (sample.InsertionStyle is { } style) ApplyEntityStyle(primitive, style, tr, db);
                primitives.Add(primitive.ObjectId); created.Add(primitive.ObjectId); probeOk = true;
            }
            else
            {
                var defId = imports.Import(bt[blockName], sample?.SourceXref);
                // annotatieve vergroting voorkomen door het symbool te ontleden
                primitives = ExplodeToPrimitives(btr, tr, db, imports, layer, defId, created, includeInvisible, sample, out probeOk);
            }
            if (!probeOk || primitives.Count == 0)
            {
                foreach (var id in created.Concat(primitives).Distinct())
                    EraseSafe(tr, id);
                failureReason = $"Symboolblok '{blockName}' bevat geen omzetbare geometrie.";
                return false;
            }

            for (int i = 0; i < primitives.Count; i++)
            {
                var entity = (Entity)tr.GetObject(primitives[i], OpenMode.ForWrite);
                Entity place = entity is DBText dbText
                    ? SymbolTextToMText(btr, tr, dbText)
                    : entity;
                if (place.ObjectId != entity.ObjectId)
                    created.Add(place.ObjectId);
                primitives[i] = place.ObjectId;
            }

            var transform = sourceTransform.ToMatrix();
            bool uniformInXy = Math.Abs(sourceTransform.XAxis.Length - sourceTransform.YAxis.Length) < 1e-9
                && Math.Abs(sourceTransform.XAxis.DotProduct(sourceTransform.YAxis)) < 1e-9;
            if (!uniformInXy)
            {
                bt.UpgradeOpen();
                var local = new BlockTableRecord { Name = "NLCS_LEGENDA_SYMBOL_" + Guid.NewGuid().ToString("N") };
                var localId = bt.Add(local); tr.AddNewlyCreatedDBObject(local, true);
                using var ids = new ObjectIdCollection(primitives.ToArray());
                local.AssumeOwnershipOf(ids);
                ((DrawOrderTable)tr.GetObject(local.DrawOrderTableId, OpenMode.ForWrite)).SetRelativeDrawOrder(ids);
                foreach (var id in primitives) ((Entity)tr.GetObject(id, OpenMode.ForWrite)).Layer = layer;
                using var reference = new BlockReference(Point3d.Origin, localId);
                SetLegendDefaults(reference, db);
                reference.Layer = layer;
                reference.BlockTransform = transform;
                var referenceId = AppendEntity(btr, reference); tr.AddNewlyCreatedDBObject(reference, true);
                created.Add(referenceId);
                primitives = new List<ObjectId> { referenceId };
            }
            else
                foreach (var id in primitives) TransformPlanarSymbol((Entity)tr.GetObject(id, OpenMode.ForWrite), transform);

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
            if (extW <= 1e-9 && extH <= 1e-9)
            {
                foreach (var id in created.Concat(primitives).Distinct())
                    EraseSafe(tr, id);
                failureReason = $"Symboolblok '{blockName}' heeft geen bruikbare extents.";
                return false;
            }

            // modelmaat behouden, behalve bij een expliciete nlcs-schaalvariant
            double fit = sample is null ? modelPerPaperMm
                : sample.SourceScale is > 1 ? modelPerPaperMm * 1000 / sample.SourceScale.Value : 1;

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
            using (var order = new ObjectIdCollection(primitives.ToArray()))
                ((DrawOrderTable)tr.GetObject(btr.DrawOrderTableId, OpenMode.ForWrite)).SetRelativeDrawOrder(order);
            var texts = primitives.Where(id => tr.GetObject(id, OpenMode.ForRead) is MText).ToArray();
            if (texts.Length > 0)
            {
                using var textIds = new ObjectIdCollection(texts);
                ((DrawOrderTable)tr.GetObject(btr.DrawOrderTableId, OpenMode.ForWrite)).MoveToTop(textIds);
            }
            placed = primitives;
            return true;
        }
        catch (Exception ex)
        {
            foreach (var id in created.Concat(primitives).Distinct())
                EraseSafe(tr, id);
            failureReason = $"Symboolblok '{blockName}' kon niet worden weergegeven: {ex.Message}";
            return false;
        }
    }

    internal static void TransformPlanarSymbol(Entity entity, Matrix3d transform)
    {
        try { entity.TransformBy(transform); }
        catch (Autodesk.AutoCAD.Runtime.Exception ex) when (ex.ErrorStatus == Autodesk.AutoCAD.Runtime.ErrorStatus.CannotScaleNonUniformly && entity.IsPlanar)
        {
            using var plane = entity.GetPlane();
            var normal = plane.Normal.GetNormal();
            var u = normal.GetPerpendicularVector().GetNormal();
            var v = normal.CrossProduct(u).GetNormal();
            var a = u.TransformBy(transform);
            var b = v.TransformBy(transform);
            double scale = a.Length;
            if (scale < 1e-12 || Math.Abs(scale - b.Length) > scale * 1e-9
                || Math.Abs(a.DotProduct(b)) > scale * scale * 1e-9) throw;
            var n = a.CrossProduct(b).GetNormal() * scale;
            entity.TransformBy(Matrix3d.AlignCoordinateSystem(plane.PointOnPlane, u, v, normal,
                plane.PointOnPlane.TransformBy(transform), a, b, n));
        }
    }

    private static Database OpenXrefSnapshot(Database loaded)
    {
        var snapshot = new Database(false, true);
        try
        {
            snapshot.ReadDwgFile(loaded.Filename, FileOpenMode.OpenForReadAndAllShare, true, string.Empty);
            snapshot.CloseInput(true);
            if (snapshot.FingerprintGuid != loaded.FingerprintGuid || snapshot.VersionGuid != loaded.VersionGuid)
                throw new InvalidOperationException($"Het bronbestand '{Path.GetFileName(loaded.Filename)}' is gewijzigd. Herlaad de xref en probeer opnieuw.");
            return snapshot;
        }
        catch
        {
            snapshot.Dispose();
            throw;
        }
    }

    // bronkopieën alleen hergebruiken binnen één bouwactie
    private sealed class SymbolImportSession(Database host, Transaction tr) : IDisposable
    {
        private readonly Dictionary<(Database, string), SymbolSnapshot> _sources = new();
        private readonly Dictionary<(ObjectId, string), ObjectId> _definitions = new();
        private readonly List<ObjectId> _importedBlocks = new();

        public Hatch CopyHatch(HatchSample sample, ObjectId owner)
        {
            var original = sample.SourceId.OriginalDatabase;
            if (original == host) return (Hatch)((Hatch)tr.GetObject(sample.SourceId, OpenMode.ForRead)).Clone();
            string prefix = string.IsNullOrEmpty(sample.SourceXref) ? string.Empty : sample.SourceXref + "|";
            var key = (original, prefix);
            if (!_sources.TryGetValue(key, out var snapshot))
                _sources.Add(key, snapshot = new SymbolSnapshot(original, prefix));
            ObjectId copyId;
            using (var sourceTr = snapshot.Database.TransactionManager.StartTransaction())
            {
                var sourceId = snapshot.Database.GetObjectId(false, sample.SourceId.NonForwardedHandle, 0);
                using var copy = (Hatch)((Hatch)sourceTr.GetObject(sourceId, OpenMode.ForRead)).Clone();
                copy.XData = null;
                var model = (BlockTableRecord)sourceTr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(snapshot.Database), OpenMode.ForWrite);
                copyId = model.AppendEntity(copy); sourceTr.AddNewlyCreatedDBObject(copy, true);
                copy.Associative = false; copy.RemoveAssociatedObjectIds();
                sourceTr.Commit();
            }
            using var ids = new ObjectIdCollection { copyId };
            using var mapping = new IdMapping();
            snapshot.Database.WblockCloneObjects(ids, owner, mapping, DuplicateRecordCloning.Ignore, false);
            return (Hatch)tr.GetObject(mapping[copyId].Value, OpenMode.ForWrite);
        }

        public Entity ImportPrimitive(ObjectId sourceId, ObjectId owner, string sourceXref)
        {
            using var ids = new ObjectIdCollection { sourceId };
            using var mapping = new IdMapping();
            if (sourceId.OriginalDatabase == host)
                host.DeepCloneObjects(ids, owner, mapping, false);
            else
            {
                string prefix = string.IsNullOrEmpty(sourceXref) ? string.Empty : sourceXref + "|";
                var key = (sourceId.OriginalDatabase, prefix);
                if (!_sources.TryGetValue(key, out var snapshot))
                    _sources.Add(key, snapshot = new SymbolSnapshot(sourceId.OriginalDatabase, prefix));
                sourceId = snapshot.Database.GetObjectId(false, sourceId.NonForwardedHandle, 0);
                ids.Clear(); ids.Add(sourceId);
                snapshot.Database.WblockCloneObjects(ids, owner, mapping, DuplicateRecordCloning.Ignore, false);
            }
            TrackBlocks(mapping);
            return (Entity)tr.GetObject(mapping[sourceId].Value, OpenMode.ForWrite);
        }

        public BlockReference ImportReference(ObjectId sourceId, ObjectId owner, string sourceXref)
        {
            using var ids = new ObjectIdCollection { sourceId };
            using var mapping = new IdMapping();
            if (sourceId.OriginalDatabase == host)
                host.DeepCloneObjects(ids, owner, mapping, false);
            else
            {
                var source = (BlockReference)tr.GetObject(sourceId, OpenMode.ForRead);
                Import(source.BlockTableRecord, sourceXref);
                string prefix = string.IsNullOrEmpty(sourceXref) ? string.Empty : sourceXref + "|";
                var snapshot = _sources[(sourceId.OriginalDatabase, prefix)];
                var localId = snapshot.Database.GetObjectId(false, sourceId.NonForwardedHandle, 0);
                ids.Clear(); ids.Add(localId);
                snapshot.Database.WblockCloneObjects(ids, owner, mapping, DuplicateRecordCloning.Ignore, false);
                sourceId = localId;
            }
            TrackBlocks(mapping);
            var copy = (BlockReference)tr.GetObject(mapping[sourceId].Value, OpenMode.ForWrite);
            var inverse = copy.BlockTransform.Inverse();
            foreach (ObjectId id in copy.AttributeCollection)
                TransformPlanarSymbol((AttributeReference)tr.GetObject(id, OpenMode.ForWrite), inverse);
            copy.BlockTransform = Matrix3d.Identity;
            return copy;
        }

        public ObjectId Import(ObjectId definitionId, string? sourceXref = null)
        {
            var original = definitionId.OriginalDatabase;
            if (original == host) return definitionId;
            var definition = (BlockTableRecord)tr.GetObject(definitionId, OpenMode.ForRead);
            int separator = definition.Name.LastIndexOf('|');
            string xrefPrefix = sourceXref is null ? (separator >= 0 ? definition.Name[..(separator + 1)] : string.Empty)
                : string.IsNullOrEmpty(sourceXref) ? string.Empty : sourceXref + "|";
            if (_definitions.TryGetValue((definitionId, xrefPrefix), out var cached)) return cached;
            var key = (original, xrefPrefix);
            if (!_sources.TryGetValue(key, out var snapshot))
                _sources.Add(key, snapshot = new SymbolSnapshot(original, xrefPrefix));
            // anonieme xref-blokken krijgen in de host een andere naam
            var sourceId = snapshot.Database.GetObjectId(false, definitionId.NonForwardedHandle, 0);
            using var ids = new ObjectIdCollection { sourceId };
            using var mapping = new IdMapping();
            snapshot.Database.WblockCloneObjects(ids, host.BlockTableId, mapping, DuplicateRecordCloning.Ignore, false);
            TrackBlocks(mapping);
            var hostLayers = (LayerTable)tr.GetObject(host.LayerTableId, OpenMode.ForRead);
            foreach (IdPair pair in mapping)
                if (snapshot.OriginalLayers.TryGetValue(pair.Key, out var layerName) && layerName != "0" && hostLayers.Has(xrefPrefix + layerName))
                {
                    var from = (LayerTableRecord)tr.GetObject(hostLayers[xrefPrefix + layerName], OpenMode.ForRead);
                    var to = (LayerTableRecord)tr.GetObject(pair.Value, OpenMode.ForWrite);
                    to.Color = from.Color; to.LineWeight = from.LineWeight; to.Transparency = from.Transparency;
                    to.IsOff = from.IsOff; to.IsFrozen = from.IsFrozen; to.IsPlottable = from.IsPlottable;
                    var lt = (LinetypeTableRecord)tr.GetObject(from.LinetypeObjectId, OpenMode.ForRead);
                    to.LinetypeObjectId = ResolveStyleLinetype(tr, host, new LayerDisplayStyle
                    {
                        Color = from.Color,
                        Linetype = lt.Name,
                        LinetypeIdentity = DrawingAnalyzer.LinetypeSignature(from.LinetypeObjectId, tr)
                    });
                }
            var mapped = mapping[sourceId];
            if (mapped.Value.IsNull || mapped.Value.Database != host)
                throw new InvalidOperationException("Het symbool is niet naar de hostdatabase gekopieerd.");

            _definitions.Add((definitionId, xrefPrefix), mapped.Value);
            return mapped.Value;
        }

        private void TrackBlocks(IdMapping mapping)
        {
            foreach (IdPair pair in mapping)
                if (pair.IsCloned && pair.Value.ObjectClass.IsDerivedFrom(Autodesk.AutoCAD.Runtime.RXObject.GetClass(typeof(BlockTableRecord)))
                    && !_importedBlocks.Contains(pair.Value)) _importedBlocks.Add(pair.Value);
        }
        public void Dispose()
        {
            try
            {
                for (int pass = 0; pass < _importedBlocks.Count; pass++)
                {
                    bool erased = false;
                    foreach (var imported in _importedBlocks)
                        if (!imported.IsErased && tr.GetObject(imported, OpenMode.ForWrite) is BlockTableRecord block
                            && block.GetBlockReferenceIds(true, false).Count == 0)
                        { block.Erase(); erased = true; }
                    if (!erased) break;
                }
            }
            finally { foreach (var source in _sources.Values) source.Dispose(); }
        }
    }

    private sealed class SymbolSnapshot : IDisposable
    {
        public Database Database { get; }
        public Dictionary<ObjectId, string> OriginalLayers { get; }

        public SymbolSnapshot(Database loaded, string xrefPrefix)
        {
            Database = OpenXrefSnapshot(loaded);
            try
            {
                using var transaction = Database.TransactionManager.StartTransaction();
                // aparte bronruimte voor koppelingen met verschillende laaginstellingen
                string identity = Database.FingerprintGuid + Database.VersionGuid + xrefPrefix;
                string prefix = "NLCS_SRC_" + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
                    System.Text.Encoding.UTF8.GetBytes(identity)))[..20] + "_";
                var originalLayers = new Dictionary<ObjectId, string>();
                foreach (var tableId in new[] { Database.BlockTableId, Database.LayerTableId, Database.LinetypeTableId, Database.TextStyleTableId })
                    foreach (ObjectId id in (SymbolTable)transaction.GetObject(tableId, OpenMode.ForRead))
                    {
                        var record = (SymbolTableRecord)transaction.GetObject(id, OpenMode.ForRead);
                        if (record is BlockTableRecord { IsLayout: true }) continue;
                        if (record is LayerTableRecord)
                        {
                            originalLayers[id] = record.Name;
                            if (record.Name == "0") continue;
                        }
                        if (record is LinetypeTableRecord && (record.Name.Equals("ByLayer", StringComparison.OrdinalIgnoreCase)
                            || record.Name.Equals("ByBlock", StringComparison.OrdinalIgnoreCase) || record.Name.Equals("Continuous", StringComparison.OrdinalIgnoreCase))) continue;
                        record.UpgradeOpen();
                        record.Name = prefix + record.Handle;
                    }
                var materials = (DBDictionary)transaction.GetObject(Database.MaterialDictionaryId, OpenMode.ForWrite);
                var materialEntries = new List<(string Key, ObjectId Value)>();
                foreach (DBDictionaryEntry item in materials) materialEntries.Add((item.Key, item.Value));
                foreach (var item in materialEntries)
                {
                    if (item.Key.Equals("ByLayer", StringComparison.OrdinalIgnoreCase) || item.Key.Equals("ByBlock", StringComparison.OrdinalIgnoreCase)
                        || item.Key.Equals("Global", StringComparison.OrdinalIgnoreCase)) continue;
                    string name = prefix + item.Value.Handle;
                    materials.SetName(item.Key, name);
                    ((Material)transaction.GetObject(item.Value, OpenMode.ForWrite)).Name = name;
                }

                OriginalLayers = originalLayers;
                transaction.Commit();
            }
            catch { Database.Dispose(); throw; }
        }
        public void Dispose() => Database.Dispose();
    }

    private static List<ObjectId> ExplodeToPrimitives(
        BlockTableRecord btr, Transaction tr, Database db, SymbolImportSession imports, string layer, ObjectId defId,
        List<ObjectId> created, bool includeInvisible, SymbolSample? sample, out bool ok)
    {
        var result = new List<ObjectId>();
        ok = false;
        using var probe = sample is not null && !sample.ReferenceId.IsNull
            ? imports.ImportReference(sample.ReferenceId, btr.ObjectId, sample.SourceXref)
            : new BlockReference(Point3d.Origin, defId);
        SetLegendDefaults(probe, db);
        probe.Layer = layer;
        if (probe.ObjectId.IsNull)
        {
            AppendEntity(btr, probe);
            tr.AddNewlyCreatedDBObject(probe, true);
        }
        created.Add(probe.ObjectId);
        var insertionLayer = sample?.InsertionLayerStyle ?? DrawingAnalyzer.EntityLayerStyle(probe, tr);
        if (sample?.InsertionStyle is { } insertionStyle) ApplyEntityStyle(probe, insertionStyle, tr, db);
        var queue = new Stack<(ObjectId Id, LayerDisplayStyle Layer)>();
        queue.Push((probe.ObjectId, insertionLayer));
        int guard = 0;
        while (queue.Count > 0)
        {
            if (++guard > 5000) throw new InvalidOperationException("Het symbool bevat te veel geneste objecten.");
            var (id, inheritedLayer) = queue.Pop();
            var entity = (Entity)tr.GetObject(id, OpenMode.ForWrite);
            if (entity is not BlockReference reference) { result.Add(id); continue; }
            using var pieces = new DBObjectCollection();
            try
            {
                // gelijke xy-schaal behouden bij een vlak symbool met afwijkende z-schaal
                var scale = reference.ScaleFactors;
                var definition = (BlockTableRecord)tr.GetObject(reference.BlockTableRecord, OpenMode.ForRead);
                bool flat = Math.Abs(Math.Abs(scale.X) - Math.Abs(scale.Y)) < 1e-9
                    && Math.Abs(Math.Abs(scale.X) - Math.Abs(scale.Z)) > 1e-9
                    && definition.Cast<ObjectId>().All(child => tr.GetObject(child, OpenMode.ForRead) is Entity e
                        && TryEntityExtents(e, out var low, out var high) && Math.Abs(low.Z) < 1e-9 && Math.Abs(high.Z) < 1e-9);
                if (flat) reference.ScaleFactors = new Scale3d(scale.X, scale.Y, Math.CopySign(Math.Abs(scale.X), scale.Z));
                reference.Explode(pieces);
                var parentStyle = DrawingAnalyzer.EntityStyle(reference, tr, null, inheritedLayer);
                foreach (ObjectId attributeId in reference.AttributeCollection)
                {
                    var attribute = (AttributeReference)tr.GetObject(attributeId, OpenMode.ForWrite);
                    if (string.IsNullOrWhiteSpace(attribute.TextString) || !includeInvisible && (attribute.Invisible || !attribute.Visible)) continue;
                    var attributeLayer = DrawingAnalyzer.EntityLayerStyle(attribute, tr, inheritedLayer);
                    var attributeStyle = DrawingAnalyzer.EntityStyle(attribute, tr, parentStyle, attributeLayer);
                    if (!((LayerTableRecord)tr.GetObject(attribute.LayerId, OpenMode.ForRead)).IsPlottable) continue;
                    using var multiline = attribute.IsMTextAttribute ? attribute.MTextAttribute : null;
                    var text = multiline ?? SymbolTextToMText(btr, tr, attribute);
                    if (multiline is not null)
                    {
                        AppendEntity(btr, text); tr.AddNewlyCreatedDBObject(text, true);
                        attribute.Erase();
                    }
                    ApplyEntityStyle(text, attributeStyle, tr, db);
                    created.Add(text.ObjectId);
                    result.Add(text.ObjectId);
                }
                var sourceIds = definition.Cast<ObjectId>().ToArray();
                var sourceOrder = ((DrawOrderTable)tr.GetObject(definition.DrawOrderTableId, OpenMode.ForRead)).GetFullDrawOrder(0).Cast<ObjectId>().ToArray();
                var indexes = sourceIds.Select((sourceId, index) => (sourceId, index)).ToDictionary(pair => pair.sourceId, pair => pair.index);
                var ordered = sourceIds.Length == pieces.Count && sourceOrder.Length == sourceIds.Length
                    ? sourceOrder.Select(sourceId => pieces[indexes[sourceId]]) : pieces.Cast<DBObject>();
                var children = new List<(ObjectId Id, LayerDisplayStyle Layer)>();
                foreach (var piece in ordered.OfType<Entity>())
                {
                    if (piece is AttributeDefinition { Constant: false }
                        && (reference.ObjectId != probe.ObjectId || sample is not null && !sample.ReferenceId.IsNull)) continue;
                    if (piece is AttributeDefinition { Invisible: true } && !includeInvisible) continue;
                    if (piece.LinetypeId.Database != db || piece.LayerId.Database != db)
                        throw new InvalidOperationException("Een symboolafhankelijkheid hoort niet bij de hostdatabase.");
                    if (!includeInvisible && !piece.Visible) continue;
                    var pieceLayer = (LayerTableRecord)tr.GetObject(piece.LayerId, OpenMode.ForRead);
                    if (!pieceLayer.IsPlottable) continue;
                    if (!includeInvisible && LayerNaming.LocalName(pieceLayer.Name) != "0" && (pieceLayer.IsOff || pieceLayer.IsFrozen)) continue;
                    piece.Visible = true;
                    var pieceLayerStyle = DrawingAnalyzer.EntityLayerStyle(piece, tr, inheritedLayer);
                    var pieceStyle = DrawingAnalyzer.EntityStyle(piece, tr, parentStyle, pieceLayerStyle);
                    if (piece is Hatch hatch) hatch.BackgroundColor = DrawingAnalyzer.ResolveColor(hatch.BackgroundColor, parentStyle, pieceLayerStyle);
                    if (piece.Layer == "0") piece.LayerId = reference.LayerId;
                    ApplyEntityStyle(piece, pieceStyle, tr, db);
                    AppendEntity(btr, piece);
                    created.Add(piece.ObjectId);
                    tr.AddNewlyCreatedDBObject(piece, true);
                    children.Add((piece.ObjectId, pieceLayerStyle));
                }
                for (int i = children.Count - 1; i >= 0; i--) queue.Push(children[i]);
            }
            finally
            {
                foreach (DBObject obj in pieces)
                    if (obj.ObjectId.IsNull) obj.Dispose();
            }
            reference.Erase();
        }
        ok = true;
        return result;
    }

    private static void ApplyEntityStyle(Entity entity, LayerDisplayStyle style, Transaction tr, Database db)
    {
        entity.Color = style.Color;
        entity.LineWeight = style.LineWeight;
        entity.Transparency = style.Transparency.ToAutoCadValue();
        entity.LinetypeId = ResolveStyleLinetype(tr, db, style);
        entity.LinetypeScale = style.LinetypeScale;
    }

    private static void ValidateHostLinetypeReferences(Database db, Transaction tr, ObjectId blockRecordId)
    {
        var visited = new HashSet<ObjectId>();
        void Check(ObjectId id)
        {
            if (!id.IsNull && (id.Database != db || id.OriginalDatabase != db))
                throw new InvalidOperationException("Een legenda-afhankelijkheid hoort niet bij de hostdatabase.");
        }
        void Visit(ObjectId id)
        {
            Check(id);
            if (tr.GetObject(id, OpenMode.ForRead) is not Entity entity) return;
            Check(entity.LayerId); Check(entity.LinetypeId); Check(entity.MaterialId); Check(entity.ExtensionDictionary);
            if (entity is MText text) Check(text.TextStyleId);
            if (entity is DBText) throw new InvalidOperationException("Een legenda bevat nog DBText.");
            foreach (ObjectId reactor in entity.GetPersistentReactorIds()) Check(reactor);
            if (entity is Hatch hatch)
                for (int i = 0; i < hatch.NumberOfLoops; i++)
                    foreach (ObjectId boundary in hatch.GetAssociatedObjectIdsAt(i)) Check(boundary);
            if (entity is BlockReference reference && visited.Add(reference.BlockTableRecord))
            {
                Check(reference.BlockTableRecord);
                foreach (ObjectId child in (BlockTableRecord)tr.GetObject(reference.BlockTableRecord, OpenMode.ForRead)) Visit(child);
            }
        }
        foreach (ObjectId id in (BlockTableRecord)tr.GetObject(blockRecordId, OpenMode.ForRead)) Visit(id);
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
        }
    }

    internal static ObjectId AppendEntity(BlockTableRecord btr, Entity entity)
    {
        // cad vervangt bylayer-transparantie bij toevoegen
        var transparency = entity.Transparency;
        var id = btr.AppendEntity(entity);
        entity.Transparency = transparency;
        return id;
    }

    internal static void SetLegendDefaults(Entity entity, Database db)
    {
        entity.SetDatabaseDefaults(db);
        entity.Color = Color.FromColorIndex(ColorMethod.ByLayer, 256);
        entity.LineWeight = LineWeight.ByLayer;
        entity.Transparency = new Transparency(TransparencyMethod.ByLayer);
        entity.LinetypeId = db.ByLayerLinetype;
        entity.LinetypeScale = 1;
    }

    private static Polyline MakeRectangle(Database db, double x, double yBottom, double w, double h, string layer)
    {
        var pl = new Polyline();
        SetLegendDefaults(pl, db);
        pl.AddVertexAt(0, new Point2d(x, yBottom), 0, 0, 0);
        pl.AddVertexAt(1, new Point2d(x + w, yBottom), 0, 0, 0);
        pl.AddVertexAt(2, new Point2d(x + w, yBottom + h), 0, 0, 0);
        pl.AddVertexAt(3, new Point2d(x, yBottom + h), 0, 0, 0);
        pl.Closed = true;
        pl.Layer = layer;
        return pl;
    }

    internal static MText SymbolTextToMText(BlockTableRecord btr, Transaction tr, DBText dbt)
    {
        Point3d anchor;
        try
        {
            var ge = dbt.GeometricExtents;
            anchor = new Point3d((ge.MinPoint.X + ge.MaxPoint.X) / 2.0,
                (ge.MinPoint.Y + ge.MaxPoint.Y) / 2.0, (ge.MinPoint.Z + ge.MaxPoint.Z) / 2.0);
        }
        catch
        {
            anchor = dbt.Position;
        }

        var mt = new MText();
        mt.SetDatabaseDefaults(btr.Database);
        mt.Normal = dbt.Normal;
        mt.Layer = dbt.Layer;
        mt.Color = dbt.Color;
        mt.LineWeight = dbt.LineWeight;
        mt.LinetypeId = dbt.LinetypeId;
        mt.LinetypeScale = dbt.LinetypeScale;
        mt.Transparency = dbt.Transparency;
        mt.MaterialId = dbt.MaterialId;
        // alleen de stijlnaam overnemen uit de brontekening
        if (!dbt.TextStyleId.IsNull)
        {
            var host = btr.Database;
            var styleName = ReadTextStyleName(dbt.TextStyleId, host, tr);
            var styleId = ResolveTextStyle(tr, host, styleName);
            if (styleId.IsNull || styleId.Database != host)
                throw new InvalidOperationException(
                    $"De symbooltekststijl '{styleName}' kon niet naar de hostdatabase worden vertaald.");
            mt.TextStyleId = styleId;
        }
        mt.TextHeight = dbt.Height > 1e-9 ? dbt.Height : mt.TextHeight;
        mt.Rotation = dbt.Rotation;
        mt.Attachment = AttachmentPoint.MiddleCenter;
        mt.Location = anchor;

        var inv = System.Globalization.CultureInfo.InvariantCulture;
        string prefix = string.Empty;
        if (Math.Abs(dbt.WidthFactor - 1.0) > 1e-6)
            prefix += $"\\W{dbt.WidthFactor.ToString("0.####", inv)};";
        double obliqueDeg = dbt.Oblique * 180.0 / Math.PI;
        if (Math.Abs(obliqueDeg) > 1e-6)
            prefix += $"\\Q{obliqueDeg.ToString("0.###", inv)};";
        mt.Contents = prefix + MTextFormat.Escape(dbt.TextString);
        AppendEntity(btr, mt);
        tr.AddNewlyCreatedDBObject(mt, true);
        dbt.Erase();
        return mt;
    }

    private static string ReadTextStyleName(ObjectId styleId, Database host, Transaction hostTransaction)
    {
        var source = styleId.Database
            ?? throw new InvalidOperationException("De symbooltekststijl heeft geen database-eigenaar.");
        if (source == host)
        {
            return hostTransaction.GetObject(styleId, OpenMode.ForRead) is TextStyleTableRecord hostStyle
                ? hostStyle.Name
                : throw new InvalidOperationException("De symbooltekststijl is geen tekststijlrecord.");
        }

        using var sourceTransaction = source.TransactionManager.StartTransaction();
        var sourceStyle = sourceTransaction.GetObject(styleId, OpenMode.ForRead) as TextStyleTableRecord
            ?? throw new InvalidOperationException("De bron-symbooltekststijl is geen tekststijlrecord.");
        var name = sourceStyle.Name;
        sourceTransaction.Commit();
        return name;
    }

    private static MText AddMText(
        BlockTableRecord btr, Transaction tr, string text, Point3d anchor,
        AttachmentPoint attach, double height, string layer, ObjectId styleId, bool underline = false)
    {
        var mt = new MText();
        SetLegendDefaults(mt, btr.Database);
        mt.Layer = layer;
        if (!styleId.IsNull)
            mt.TextStyleId = styleId;
        mt.TextHeight = height;
        mt.Attachment = attach;
        mt.Width = 0.0;
        mt.Location = anchor;
        mt.Contents = underline ? "\\L" + MTextFormat.Escape(text) + "\\l" : MTextFormat.Escape(text);
        AppendEntity(btr, mt);
        tr.AddNewlyCreatedDBObject(mt, true);
        return mt;
    }

    private static double TextBearing(Transaction tr, ObjectId styleId, double height)
        => Path.GetFileName(((TextStyleTableRecord)tr.GetObject(styleId, OpenMode.ForRead)).FileName)
            .Equals("NLCS-ISO.ttf", StringComparison.OrdinalIgnoreCase) ? height * 0.1 : 0;

    private static MText AddText(
        BlockTableRecord btr, Transaction tr, string text, Point3d pos,
        double height, string layer, ObjectId styleId, bool underline = false)
    {
        return AddMText(btr, tr, text, new Point3d(pos.X + TextBearing(tr, styleId, height), pos.Y + height, pos.Z),
            AttachmentPoint.TopLeft, height, layer, styleId, underline);
    }

    private static MText AddTextRight(
        BlockTableRecord btr, Transaction tr, string text, Point3d alignPoint,
        double height, string layer, ObjectId styleId)
    {
        return AddMText(btr, tr, text, new Point3d(alignPoint.X, alignPoint.Y + height / 2.0, alignPoint.Z),
            AttachmentPoint.MiddleRight, height, layer, styleId);
    }

    private static void AddTextCentered(
        BlockTableRecord btr, Transaction tr, string text, Point3d alignPoint,
        double height, string layer, ObjectId styleId)
    {
        AddMText(btr, tr, text, new Point3d(alignPoint.X, alignPoint.Y + height / 2.0, alignPoint.Z),
            AttachmentPoint.MiddleCenter, height, layer, styleId);
    }

    private static ObjectId AddSolidFill(
        BlockTableRecord btr, Transaction tr, ObjectId boundaryId, string layer)
    {
        var hatch = new Hatch();
        try
        {
            SetLegendDefaults(hatch, btr.Database);
            hatch.Layer = layer;
            hatch.SetHatchPattern(HatchPatternType.PreDefined, "SOLID");
            AppendEntity(btr, hatch);
            tr.AddNewlyCreatedDBObject(hatch, true);
            hatch.Associative = false;
            using (var loop = new ObjectIdCollection { boundaryId })
                hatch.AppendLoop(HatchLoopTypes.External, loop);
            hatch.EvaluateHatch(true);
            using var back = new ObjectIdCollection { hatch.ObjectId };
            ((DrawOrderTable)tr.GetObject(btr.DrawOrderTableId, OpenMode.ForWrite)).MoveToBottom(back);
            return hatch.ObjectId;
        }
        catch
        {
            if (!hatch.ObjectId.IsNull)
                EraseSafe(tr, hatch.ObjectId);
            else
                hatch.Dispose();
            throw;
        }
    }

    private static ObjectId AddHatch(
        BlockTableRecord btr, Transaction tr, SymbolImportSession imports, double x, double yBottom, double w, double h,
        string layer, HatchSample? sample, double scaleFactor)
    {
        using var hatch = sample is { IsSolid: false } && !sample.SourceId.IsNull
            ? imports.CopyHatch(sample, btr.ObjectId)
            : new Hatch();
        try
        {
            bool embedded = sample is { IsSolid: false } && !sample.SourceId.IsNull;
            var patternName = sample?.PatternName ?? "SOLID";
            var patternType = sample?.PatternType ?? HatchPatternType.PreDefined;
            if (embedded)
            {
                if (hatch.ObjectId.IsNull)
                {
                    AppendEntity(btr, hatch);
                    tr.AddNewlyCreatedDBObject(hatch, true);
                }
                // ingesloten patroon behouden, ook zonder hetzelfde pat-bestand
                hatch.Associative = false;
                hatch.RemoveAssociatedObjectIds();
                hatch.TransformBy(sample!.SourceTransform);
                var min = hatch.GeometricExtents.MinPoint;
                hatch.TransformBy(Matrix3d.Displacement(Point3d.Origin - min));
                hatch.TransformBy(Matrix3d.Scaling(scaleFactor, Point3d.Origin));
                hatch.TransformBy(Matrix3d.Displacement(new Vector3d(x, yBottom, 0)));
                while (hatch.NumberOfLoops > 0) hatch.RemoveLoopAt(hatch.NumberOfLoops - 1);
                hatch.Normal = Vector3d.ZAxis; hatch.Elevation = 0;
                hatch.XData = null;
            }
            SetLegendDefaults(hatch, btr.Database);
            hatch.MaterialId = ((DBDictionary)tr.GetObject(btr.Database.MaterialDictionaryId, OpenMode.ForRead)).GetAt("ByLayer");
            hatch.Layer = layer;
            if (!embedded) hatch.SetHatchPattern(patternType, patternName);
            if (sample is not null)
            {
                var patternScale = sample.IsSolid ? 1.0 : sample.PatternScale * scaleFactor;
                if (!sample.IsSolid && (!double.IsFinite(patternScale) || patternScale <= 0))
                    throw new InvalidOperationException($"Arcering '{patternName}' heeft een ongeldige render-schaal.");
                if (!double.IsFinite(sample.PatternAngle))
                    throw new InvalidOperationException($"Arcering '{patternName}' heeft een ongeldige render-hoek.");
                if (!embedded)
                {
                    hatch.PatternScale = patternScale;
                    if (sample.PatternType == HatchPatternType.UserDefined)
                    {
                        hatch.PatternSpace = sample.PatternSpace * scaleFactor;
                        hatch.PatternDouble = sample.PatternDouble;
                    }
                    hatch.PatternAngle = sample.IsSolid ? 0.0 : sample.PatternAngle;
                }
                if (sample.Color is not null) hatch.Color = sample.Color;
                if (sample.BackgroundColor is not null) hatch.BackgroundColor = sample.BackgroundColor;
                hatch.Transparency = sample.Transparency.ToAutoCadValue();
            }

            if (hatch.ObjectId.IsNull)
            {
                AppendEntity(btr, hatch);
                tr.AddNewlyCreatedDBObject(hatch, true);
            }
            hatch.Associative = false;
            var pts = new Point2dCollection
            {
                new Point2d(x, yBottom),
                new Point2d(x + w, yBottom),
                new Point2d(x + w, yBottom + h),
                new Point2d(x, yBottom + h),
                new Point2d(x, yBottom)
            };
            var bulges = new DoubleCollection { 0.0, 0.0, 0.0, 0.0, 0.0 };
            hatch.AppendLoop(HatchLoopTypes.External | HatchLoopTypes.Polyline, pts, bulges);
            if (!embedded) hatch.SetHatchPattern(patternType, patternName);
            hatch.EvaluateHatch(true);
            using var back = new ObjectIdCollection { hatch.ObjectId };
            ((DrawOrderTable)tr.GetObject(btr.DrawOrderTableId, OpenMode.ForWrite)).MoveToBottom(back);
            return hatch.ObjectId;
        }
        catch
        {
            if (!hatch.ObjectId.IsNull) EraseSafe(tr, hatch.ObjectId);
            throw;
        }
    }

    // eigen weergavelaag bij afwijkende stijl; bronlaag behouden
    private static string EnsureStyledLayer(Transaction tr, Database db, string name, LayerDisplayStyle style)
    {
        var lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
        bool nameExists = lt.Has(name);
        if (nameExists)
        {
            var existing = (LayerTableRecord)tr.GetObject(lt[name], OpenMode.ForRead);
            if (LayerMatchesStyle(tr, existing, style))
                return name;
        }

        string styledName = nameExists ? StyledLayerName(name, style) : name;
        for (int collision = 0; lt.Has(styledName); collision++)
        {
            var existing = (LayerTableRecord)tr.GetObject(lt[styledName], OpenMode.ForRead);
            if (LayerMatchesStyle(tr, existing, style))
                return styledName;
            styledName = StyledLayerName(name, style, collision + 1);
        }

        lt.UpgradeOpen();
        var ltr = new LayerTableRecord
        {
            Name = styledName,
            Color = style.Color,
            LineWeight = style.LineWeight
        };
        ltr.LinetypeObjectId = ResolveStyleLinetype(tr, db, style);
        lt.Add(ltr);
        tr.AddNewlyCreatedDBObject(ltr, true);
        ltr.Transparency = style.Transparency.ToAutoCadValue();
        return styledName;
    }

    private static ObjectId ResolveStyleLinetype(Transaction tr, Database db, LayerDisplayStyle style)
    {
        var table = (LinetypeTable)tr.GetObject(db.LinetypeTableId, OpenMode.ForRead);
        if (style.LinetypeIdentity == "Continuous" || style.Linetype.Equals("Continuous", StringComparison.OrdinalIgnoreCase)) return table["Continuous"];
        if (!table.Has(style.Linetype))
            throw new InvalidOperationException($"Linetype '{style.Linetype}' ontbreekt.");
        var id = table[style.Linetype];
        if (id.OriginalDatabase == db) return id;
        string importedName = "NLCS_LT_" + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(style.LinetypeIdentity)))[..20];
        if (table.Has(importedName)) return table[importedName];
        using var source = OpenXrefSnapshot(id.OriginalDatabase);
        using var sourceTr = source.TransactionManager.StartTransaction();
        var sourceTable = (LinetypeTable)sourceTr.GetObject(source.LinetypeTableId, OpenMode.ForRead);
        var sourceId = sourceTable[LayerNaming.LocalName(style.Linetype)];
        using var ids = new ObjectIdCollection { sourceId };
        using var mapping = new IdMapping();
        source.WblockCloneObjects(ids, db.LinetypeTableId, mapping, DuplicateRecordCloning.MangleName, false);
        var imported = mapping[sourceId].Value;
        ((LinetypeTableRecord)tr.GetObject(imported, OpenMode.ForWrite)).Name = importedName;
        sourceTr.Commit();
        return imported;
    }

    private static bool LayerMatchesStyle(Transaction tr, LayerTableRecord layer, LayerDisplayStyle style)
    {
        string linetype = DrawingAnalyzer.LinetypeSignature(layer.LinetypeObjectId, tr);
        string expected = style.LinetypeIdentity.Length > 0 ? style.LinetypeIdentity
            : DrawingAnalyzer.LinetypeSignature(((LinetypeTable)tr.GetObject(layer.Database.LinetypeTableId, OpenMode.ForRead))[style.Linetype], tr);
        return !layer.IsOff && !layer.IsFrozen && layer.Color.ColorMethod == style.Color.ColorMethod
            && layer.Color.ColorIndex == style.Color.ColorIndex
            && layer.Color.ColorValue.ToArgb() == style.Color.ColorValue.ToArgb()
            && StringComparer.Ordinal.Equals(linetype, expected)
            && layer.LineWeight == style.LineWeight
            && TransparencyValue.From(layer.Transparency).Signature == style.Transparency.Signature;
    }

    private static string StyledLayerName(string name, LayerDisplayStyle style, int collision = 0)
    {
        string key = string.Join("|",
            style.Color.ColorMethod,
            style.Color.ColorIndex.ToString(System.Globalization.CultureInfo.InvariantCulture),
            style.Color.ColorValue.ToArgb().ToString(System.Globalization.CultureInfo.InvariantCulture),
            style.LinetypeIdentity,
            style.LineWeight,
            style.Transparency.Signature);
        string hash = Convert.ToHexString(
            System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(key)))[..12];
        string suffix = $" #R{hash}" + (collision == 0 ? string.Empty : $"_{collision + 1}");
        int prefixLength = Math.Max(1, 255 - suffix.Length);
        string prefix = name.Length <= prefixLength ? name : name[..prefixLength];
        return prefix + suffix;
    }

    private static void EnsureLayer(Transaction tr, Database db, string name, short colorIndex,
        bool plottable = true, LineWeight weight = LineWeight.ByLineWeightDefault)
    {
        var lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
        if (lt.Has(name))
        {
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
            IsPlottable = plottable,
            LineWeight = weight
        };
        lt.Add(ltr);
        tr.AddNewlyCreatedDBObject(ltr, true);
    }

    private static ObjectId ResolveTextStyle(Transaction tr, Database db, string name)
    {
        var tst = (TextStyleTable)tr.GetObject(db.TextStyleTableId, OpenMode.ForRead);
        ObjectId FindHostStyle(string candidate)
        {
            if (string.IsNullOrWhiteSpace(candidate) || !tst.Has(candidate))
                return ObjectId.Null;
            var id = tst[candidate];
            return !id.IsNull && id.Database == db ? id : ObjectId.Null;
        }

        if (!string.IsNullOrWhiteSpace(name))
        {
            var requested = FindHostStyle(name);
            if (!requested.IsNull) return requested;
            throw new InvalidOperationException($"Tekststijl '{name}' ontbreekt in deze tekening. Kies een bestaande stijl of laat de tekststijl leeg voor de templateopmaak.");
        }

        // afwijkende gedeelde stijl behouden; eigen correcte stijl gebruiken
        string styleName = "NLCS-ISO";
        for (int suffix = 0; tst.Has(styleName); suffix++)
        {
            var existing = (TextStyleTableRecord)tr.GetObject(tst[styleName], OpenMode.ForRead);
            if (Path.GetFileName(existing.FileName).Equals("NLCS-ISO.ttf", StringComparison.OrdinalIgnoreCase)
                && Math.Abs(existing.XScale - 1) < 1e-9 && Math.Abs(existing.ObliquingAngle) < 1e-9
                && !existing.IsVertical && existing.FlagBits == 0)
                return existing.ObjectId;
            styleName = "NLCS_LEGENDA_ISO" + (suffix == 0 ? string.Empty : "_" + suffix);
        }

        string font;
        try { font = HostApplicationServices.Current.FindFile("NLCS-ISO.ttf", db, FindFileHint.FontFile); }
        catch (Autodesk.AutoCAD.Runtime.Exception ex)
        {
            throw new InvalidOperationException("Lettertype NLCS-ISO.ttf ontbreekt. Installeer het NLCS-lettertype of kies een beschikbare tekststijl bij Instellingen.", ex);
        }
        tst.UpgradeOpen();
        var created = new TextStyleTableRecord { Name = styleName, FileName = font, TextSize = 0, XScale = 1 };
        var createdId = tst.Add(created);
        tr.AddNewlyCreatedDBObject(created, true);
        return createdId;
    }
}
