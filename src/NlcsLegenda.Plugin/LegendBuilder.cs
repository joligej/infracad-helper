using System.IO;
using Autodesk.AutoCAD.Colors;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using NlcsLegenda.Core;
using Color = Autodesk.AutoCAD.Colors.Color;

namespace NlcsLegenda.Plugin;

public static class LegendBuilder
{
    // Verhoudingen voor de opmaak (t.o.v. de bijbehorende teksthoogte of swatch-maat).
    private const double SubHeaderHeightRatio = 1.0;    // hoofdgroepkop even hoog als de statuskop
    private const double SubHeaderIndentRatio = 0.15;    // inspringing subkop in de swatch

    private sealed record PreparedSymbol(IReadOnlyList<ObjectId> Entities, double Width, double Height);

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

        // Meet de werkelijk getekende symbolen vóór de indeling. Een groot symbool
        // vergroot zijn rij/kolom; zijn formaat wordt niet naar een vakje vervormd.
        using var imports = new SymbolImportSession(db, tr);
        var symbols = new Dictionary<LegendEntry, PreparedSymbol>();
        if (s.InsertSymbolBlocks)
            foreach (var entry in analysis.Entries.Where(e => e.SymbolLayer is not null && e.SymbolBlockName is not null))
            {
                string layer = entry.SymbolLayer!;
                string name = entry.SymbolBlockName!;
                string renderLayer;
                if (analysis.LayerStyles.TryGetValue(layer, out var display))
                    renderLayer = EnsureStyledLayer(tr, db, layer, display);
                else { EnsureLayer(tr, db, layer, 7); renderLayer = layer; }
                analysis.SymbolSamples.TryGetValue(layer, out var sample);
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
            heading.SetDatabaseDefaults(db);
            heading.TextStyleId = styleId;
            heading.TextHeight = height;
            heading.Contents = "\\L" + MTextFormat.Escape(text) + "\\l";
            return heading.ActualHeight;
        }
        var layout = LegendLayoutEngine.Compute(analysis.Entries, layoutSettings, MeasureTextHeight,
            entry => symbols.TryGetValue(entry, out var symbol) ? symbol.Height : 0, MeasureHeadingHeight);
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
                        DrawEntry(btr, tr, db, item, analysis, s,
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
            var border = MakeRectangle(
                layout.MinX - margin, bottomY - margin,
                (borderRight - layout.MinX) + 2 * margin, (layout.MaxY - bottomY) + 2 * margin, s.FrameLayer);
            btr.AppendEntity(border);
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
            : 10.0 * s.Scale / 1000.0; // elk segment is standaard 10 mm op papier
        if (segMeters <= 0)
            segMeters = 1.0;

        double barH = s.ToModel(s.ScaleBarHeightMm);
        double labelH = s.ToModel(1.8);
        double barBottom = top - barH - s.ToModel(1.0) - labelH;
        double barWidth = segMeters * segments;

        var frame = MakeRectangle(left, barBottom, barWidth, barH, s.FrameLayer);
        btr.AppendEntity(frame);
        tr.AddNewlyCreatedDBObject(frame, true);

        for (int i = 0; i < segments; i++)
        {
            double x0 = left + i * segMeters;
            var seg = MakeRectangle(x0, barBottom + (i % 2 == 0 ? barH / 2 : 0), segMeters, barH / 2, s.FrameLayer);
            var segId = btr.AppendEntity(seg);
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

        var mt = new MText();
        mt.SetDatabaseDefaults();
        mt.Layer = s.TextLayer;
        if (!styleId.IsNull)
            mt.TextStyleId = styleId;
        mt.TextHeight = textH;
        mt.Width = Math.Max(widthModel, textH * 4);
        mt.Attachment = AttachmentPoint.TopLeft;
        mt.Location = new Point3d(left + TextBearing(tr, styleId, textH), y, 0);
        // De alinea-inspringing is relatief aan de teksthoogte, niet aan de DWG-schaal.
        mt.Contents = (s.RemarksText.StartsWith("-\t", StringComparison.Ordinal) ? "\\pxi-2,l2;" : string.Empty)
            + MTextFormat.Escape(s.RemarksText);
        btr.AppendEntity(mt);
        tr.AddNewlyCreatedDBObject(mt, true);

        return y - mt.ActualHeight;
    }

    private static void DrawEntry(
        BlockTableRecord btr, Transaction tr, Database db, LegendPlacement item, AnalysisResult analysis,
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
            if (analysis.LayerStyles.TryGetValue(lyr, out var style))
                renderLayers[lyr] = EnsureStyledLayer(tr, db, lyr, style);
            else
            {
                EnsureLayer(tr, db, lyr, 7);
                renderLayers[lyr] = lyr;
            }
        }

        // Elke elementsoort rendert volgens zijn eigen semantiek en wordt van achter naar
        // voren gestapeld: eerst vlak/vulling/arcering als achtergrond, dan de G-lijn als
        // lijnsample, dan het symbool. Zo blijft een G altijd een lijn, ook als dezelfde
        // entry daarnaast een arcering of vulling heeft.

        // Vlak (GV): begrensd oppervlak, weergegeven als rechthoek op de bronlaag.
        if (entry.VlakLayer is { } vlakLayer)
        {
            var area = MakeRectangle(x, bottom, swatchW, swatchH, renderLayers[vlakLayer]);
            rowEntities.Add(btr.AppendEntity(area));
            tr.AddNewlyCreatedDBObject(area, true);
        }

        // Vlakvulling (V): egale (SOLID) vulling van het vakje.
        if (entry.FillLayer is { } fillLayer)
        {
            var fillBoundary = MakeRectangle(x, bottom, swatchW, swatchH, renderLayers[fillLayer]);
            var fillId = btr.AppendEntity(fillBoundary);
            tr.AddNewlyCreatedDBObject(fillBoundary, true);
            rowEntities.Add(fillId);
            rowEntities.Add(AddSolidFill(btr, tr, fillId, renderLayers[fillLayer]));
        }

        // Arcering (A): het patroon uit de brontekening.
        if (entry.HatchLayer is { } hatchLayer)
        {
            analysis.HatchSamples.TryGetValue(hatchLayer, out var sample);
            rowEntities.Add(AddHatch(btr, tr, x, bottom, swatchW, swatchH, renderLayers[hatchLayer], sample,
                s.HatchScaleFactor * (sample?.ScaleForLegend(s.Scale) ?? 1)));
        }

        // Geometrie (G/GD/GS): horizontale lijn; de effectieve bronstijl wordt hieronder vastgezet.
        if (entry.GeometryLayer is { } geoLayer)
        {
            var line = new Polyline();
            line.AddVertexAt(0, new Point2d(x, midY), 0, 0, 0);
            line.AddVertexAt(1, new Point2d(x + swatchW, midY), 0, 0, 0);
            line.Layer = renderLayers[geoLayer];
            rowEntities.Add(btr.AppendEntity(line));
            tr.AddNewlyCreatedDBObject(line, true);
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
                if (entity.Layer == rendered.Value && analysis.LayerStyles.TryGetValue(rendered.Key, out var style))
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

        // Kader per swatch: alleen een zichtbaar vakje op de kaderlaag als de gebruiker dat wil.
        if (s.DrawSwatchFrame)
        {
            double frameHeight = Math.Max(swatchH, prepared?.Height ?? 0);
            var frame = MakeRectangle(x, midY - frameHeight / 2, sampleColumnWidth, frameHeight, s.SwatchFrameLayer);
            btr.AppendEntity(frame);
            tr.AddNewlyCreatedDBObject(frame, true);
        }

        // Omschrijving als MText met vaste breedte: AutoCAD breekt exact op de kolombreedte
        // af (geen horizontale overloop) en de tekst wordt op het bandmidden gecentreerd.
        if (s.IncludeText && !string.IsNullOrWhiteSpace(entry.Description))
        {
            double textColWidth = colWidth - sampleColumnWidth - textGap - TextBearing(tr, styleId, textH)
                - (s.IncludeQuantities ? s.ToModel(s.QuantityColumnWidthMm) : 0.0);
            if (textColWidth < textH * 3)
                textColWidth = textH * 3;

            var mt = CreateDescription(db, s, styleId, entry.DisplayDescription, textColWidth);
            // Gelijke basislijn voor woorden met en zonder onderlengte (g, j, p).
            mt.Attachment = AttachmentPoint.TopLeft;
            mt.Location = new Point3d(x + sampleColumnWidth + textGap + TextBearing(tr, styleId, textH),
                midY + Math.Max(textH * (1 - TemplateDefaults.TextBaselineOffsetRatio), mt.ActualHeight / 2), 0);
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

    private static MText CreateDescription(Database db, LegendSettings s, ObjectId styleId,
        string text, double width)
    {
        var mt = new MText();
        mt.SetDatabaseDefaults(db);
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
            if (!bt.Has(blockName))
            {
                failureReason = $"Symboolblok '{blockName}' ontbreekt in de hostdatabase.";
                return false;
            }
            var defId = imports.Import(bt[blockName]);

            // Explode-to-primitives voorkomt dat annotatieve blokschaal de swatch opblaast.
            primitives = ExplodeToPrimitives(btr, tr, db, layer, defId, created, includeInvisible, sample, out var probeOk);
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
                // Bewaar de exacte niet-uniforme bloktransformatie op een nieuwe,
                // niet-annotatieve hostdefinitie. De bronafhankelijkheden en DBText
                // zijn hierboven al verwijderd; geen losse curve wordt benaderd.
                bt.UpgradeOpen();
                var local = new BlockTableRecord { Name = "NLCS_LEGENDA_SYMBOL_" + Guid.NewGuid().ToString("N") };
                var localId = bt.Add(local); tr.AddNewlyCreatedDBObject(local, true);
                using var ids = new ObjectIdCollection(primitives.ToArray());
                local.AssumeOwnershipOf(ids);
                foreach (var id in primitives) ((Entity)tr.GetObject(id, OpenMode.ForWrite)).Layer = layer;
                using var reference = new BlockReference(Point3d.Origin, localId) { Layer = layer };
                reference.BlockTransform = transform;
                var referenceId = btr.AppendEntity(reference); tr.AddNewlyCreatedDBObject(reference, true);
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

            // Een broninsert is al in modelmaten. Alleen een expliciete NLCS-
            // schaalvariant wordt naar de gekozen legendaschaal omgerekend.
            double fit = sample is null ? modelPerPaperMm
                : sample.SourceScale is > 0 ? modelPerPaperMm * 1000 / sample.SourceScale.Value : 1;

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

    private static void TransformPlanarSymbol(Entity entity, Matrix3d transform)
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

    // Bronkopieën bestaan alleen tijdens deze BuildBlock-aanroep. Geen cache over
    // tekeningwijzigingen heen; iedere nieuwe legenda controleert de bronversie opnieuw.
    private sealed class SymbolImportSession(Database host, Transaction tr) : IDisposable
    {
        private readonly Dictionary<(Database, string), SymbolSnapshot> _sources = new();
        private readonly Dictionary<ObjectId, ObjectId> _definitions = new();
        private readonly List<ObjectId> _importedBlocks = new();

        public ObjectId Import(ObjectId definitionId)
        {
            var original = definitionId.OriginalDatabase;
            if (original == host) return definitionId;
            if (_definitions.TryGetValue(definitionId, out var cached)) return cached;
            var definition = (BlockTableRecord)tr.GetObject(definitionId, OpenMode.ForRead);
            int separator = definition.Name.LastIndexOf('|');
            string xrefPrefix = separator >= 0 ? definition.Name[..(separator + 1)] : string.Empty;
            var key = (original, xrefPrefix);
            if (!_sources.TryGetValue(key, out var snapshot))
                _sources.Add(key, snapshot = new SymbolSnapshot(original, xrefPrefix));
            string localName = LayerNaming.LocalName(definition.Name);
            if (!snapshot.Definitions.TryGetValue(localName, out var sourceId))
                throw new InvalidOperationException($"Symbooldefinitie '{localName}' ontbreekt in de bron.");
            using var ids = new ObjectIdCollection { sourceId };
            using var mapping = new IdMapping();
            snapshot.Database.WblockCloneObjects(ids, host.BlockTableId, mapping, DuplicateRecordCloning.Ignore, false);
            foreach (IdPair pair in mapping)
                if (pair.IsCloned && pair.Value.ObjectClass.IsDerivedFrom(Autodesk.AutoCAD.Runtime.RXObject.GetClass(typeof(BlockTableRecord))))
                    _importedBlocks.Add(pair.Value);
            // Xref-laagoverrides uit de host gelden ook voor onderdelen binnen het symbool.
            var hostLayers = (LayerTable)tr.GetObject(host.LayerTableId, OpenMode.ForRead);
            foreach (IdPair pair in mapping)
                if (snapshot.OriginalLayers.TryGetValue(pair.Key, out var layerName) && layerName != "0" && hostLayers.Has(xrefPrefix + layerName))
                {
                    var from = (LayerTableRecord)tr.GetObject(hostLayers[xrefPrefix + layerName], OpenMode.ForRead);
                    var to = (LayerTableRecord)tr.GetObject(pair.Value, OpenMode.ForWrite);
                    to.Color = from.Color; to.LineWeight = from.LineWeight; to.Transparency = from.Transparency;
                    to.IsOff = from.IsOff; to.IsFrozen = from.IsFrozen;
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

            _definitions.Add(definitionId, mapped.Value);
            return mapped.Value;
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
        public Dictionary<string, ObjectId> Definitions { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<ObjectId, string> OriginalLayers { get; }

        public SymbolSnapshot(Database loaded, string xrefPrefix)
        {
            Database = OpenXrefSnapshot(loaded);
            try
            {
                using var transaction = Database.TransactionManager.StartTransaction();
                var blocks = (BlockTable)transaction.GetObject(Database.BlockTableId, OpenMode.ForRead);
                foreach (ObjectId id in blocks)
                    Definitions.Add(((BlockTableRecord)transaction.GetObject(id, OpenMode.ForRead)).Name, id);
                // Een tweede koppeling naar hetzelfde bestand kan andere laagoverrides
                // hebben. Geef die een eigen namespace in de host.
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
                        // Handles zijn uniek binnen deze broninhoud, ook voor anonieme blokken.
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
        BlockTableRecord btr, Transaction tr, Database db, string layer, ObjectId defId,
        List<ObjectId> created, bool includeInvisible, SymbolSample? sample, out bool ok)
    {
        var result = new List<ObjectId>();
        ok = false;
        using var probe = new BlockReference(Point3d.Origin, defId) { Layer = layer };
        btr.AppendEntity(probe);
        created.Add(probe.ObjectId);
        tr.AddNewlyCreatedDBObject(probe, true);
        var insertionLayer = sample?.InsertionLayerStyle ?? DrawingAnalyzer.EntityLayerStyle(probe, tr);
        if (sample?.InsertionStyle is { } insertionStyle) ApplyEntityStyle(probe, insertionStyle, tr, db);
        var queue = new Queue<(ObjectId Id, LayerDisplayStyle Layer)>();
        queue.Enqueue((probe.ObjectId, insertionLayer));
        int guard = 0;
        while (queue.Count > 0)
        {
            if (++guard > 5000) throw new InvalidOperationException("Het symbool bevat te veel geneste objecten.");
            var (id, inheritedLayer) = queue.Dequeue();
            var entity = (Entity)tr.GetObject(id, OpenMode.ForWrite);
            if (entity is not BlockReference reference) { result.Add(id); continue; }
            using var pieces = new DBObjectCollection();
            try
            {
                reference.Explode(pieces);
                foreach (DBObject obj in pieces)
                {
                    if (obj is not Entity piece) continue;
                    if (piece.LinetypeId.Database != db || piece.LayerId.Database != db)
                        throw new InvalidOperationException("Een symboolafhankelijkheid hoort niet bij de hostdatabase.");
                    if (!includeInvisible && !piece.Visible) continue;
                    var pieceLayer = (LayerTableRecord)tr.GetObject(piece.LayerId, OpenMode.ForRead);
                    if (!includeInvisible && LayerNaming.LocalName(pieceLayer.Name) != "0" && (pieceLayer.IsOff || pieceLayer.IsFrozen)) continue;
                    piece.Visible = true;
                    var pieceLayerStyle = DrawingAnalyzer.EntityLayerStyle(piece, tr, inheritedLayer);
                    var parentStyle = DrawingAnalyzer.EntityStyle(reference, tr, null, inheritedLayer);
                    var pieceStyle = DrawingAnalyzer.EntityStyle(piece, tr, parentStyle, pieceLayerStyle);
                    if (piece.Layer == "0") piece.LayerId = reference.LayerId;
                    ApplyEntityStyle(piece, pieceStyle, tr, db);
                    btr.AppendEntity(piece);
                    created.Add(piece.ObjectId);
                    tr.AddNewlyCreatedDBObject(piece, true);
                    queue.Enqueue((piece.ObjectId, pieceLayerStyle));
                }
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

    // Zet een DBText uit een bronsymbool om naar een gelijkwaardige MText. De tekst wordt op zijn
    // visuele midden geankerd (MiddleCenter, uit de extents — die verrekenen breedtefactor, oblique
    // en rotatie) met dezelfde hoogte, rotatie, normaal, stijl en kleur. Breedtefactor en oblique
    // die van de tekststijl afwijken gaan mee als inline-codes zodat niets stil verloren gaat.
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
        mt.SetDatabaseDefaults();
        mt.Normal = dbt.Normal;
        mt.Layer = dbt.Layer;
        mt.Color = dbt.Color;
        mt.LineWeight = dbt.LineWeight;
        mt.LinetypeId = dbt.LinetypeId;
        mt.LinetypeScale = dbt.LinetypeScale;
        mt.Transparency = dbt.Transparency;
        mt.MaterialId = dbt.MaterialId;
        // Een xref-tekststijl-ID hoort bij de bron-database; alleen de naam mag de grens over.
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
        btr.AppendEntity(mt);
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

    // Alle door de plugin getekende legendatekst is MText. DBText-baseline en MText-attachment
    // hebben andere semantiek; daarom ankeren we op het verticale midden van de regel
    // (baseline + hoogte/2) met een Middle*-attachment, zodat de tekst op dezelfde plek staat.
    private static MText AddMText(
        BlockTableRecord btr, Transaction tr, string text, Point3d anchor,
        AttachmentPoint attach, double height, string layer, ObjectId styleId, bool underline = false)
    {
        var mt = new MText();
        mt.SetDatabaseDefaults();
        mt.Layer = layer;
        if (!styleId.IsNull)
            mt.TextStyleId = styleId;
        mt.TextHeight = height;
        mt.Attachment = attach;
        mt.Width = 0.0; // geen vaste breedte: één regel, geen afbreken
        mt.Location = anchor;
        mt.Contents = underline ? "\\L" + MTextFormat.Escape(text) + "\\l" : MTextFormat.Escape(text);
        btr.AppendEntity(mt);
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

    private static void AddTextRight(
        BlockTableRecord btr, Transaction tr, string text, Point3d alignPoint,
        double height, string layer, ObjectId styleId)
    {
        AddMText(btr, tr, text, new Point3d(alignPoint.X, alignPoint.Y + height / 2.0, alignPoint.Z),
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
            hatch.SetDatabaseDefaults();
            hatch.Layer = layer;
            hatch.SetHatchPattern(HatchPatternType.PreDefined, "SOLID");
            btr.AppendEntity(hatch);
            tr.AddNewlyCreatedDBObject(hatch, true);
            hatch.Associative = false;
            using (var loop = new ObjectIdCollection { boundaryId })
                hatch.AppendLoop(HatchLoopTypes.External, loop);
            hatch.EvaluateHatch(true);
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
        BlockTableRecord btr, Transaction tr, double x, double yBottom, double w, double h,
        string layer, HatchSample? sample, double scaleFactor)
    {
        var hatch = new Hatch();
        try
        {
            hatch.SetDatabaseDefaults();
            hatch.Layer = layer;
            var patternName = sample?.PatternName ?? "SOLID";
            var patternType = sample?.PatternType ?? HatchPatternType.PreDefined;
            hatch.SetHatchPattern(patternType, patternName);
            if (sample is not null)
            {
                var patternScale = sample.IsSolid ? 1.0 : sample.PatternScale * scaleFactor;
                if (!sample.IsSolid && (!double.IsFinite(patternScale) || patternScale <= 0))
                    throw new InvalidOperationException($"Arcering '{patternName}' heeft een ongeldige render-schaal.");
                if (!double.IsFinite(sample.PatternAngle))
                    throw new InvalidOperationException($"Arcering '{patternName}' heeft een ongeldige render-hoek.");
                hatch.PatternScale = patternScale;
                if (sample.PatternType == HatchPatternType.UserDefined)
                {
                    hatch.PatternSpace = sample.PatternSpace * scaleFactor;
                    hatch.PatternDouble = sample.PatternDouble;
                }
                hatch.PatternAngle = sample.IsSolid ? 0.0 : sample.PatternAngle;
                if (sample.Color is not null)
                    hatch.Color = sample.Color;
                hatch.Transparency = sample.Transparency.ToAutoCadValue();
            }

            btr.AppendEntity(hatch);
            tr.AddNewlyCreatedDBObject(hatch, true);
            hatch.Associative = false;
            var pts = new Point2dCollection
            {
                new Point2d(x, yBottom),
                new Point2d(x + w, yBottom),
                new Point2d(x + w, yBottom + h),
                new Point2d(x, yBottom + h)
            };
            var bulges = new DoubleCollection { 0.0, 0.0, 0.0, 0.0 };
            hatch.AppendLoop(HatchLoopTypes.External | HatchLoopTypes.Polyline, pts, bulges);
            hatch.SetHatchPattern(patternType, patternName);
            hatch.EvaluateHatch(true);
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

    // Een bestaande bronlaag blijft ongewijzigd. Bij afwijkend uiterlijk maken we een
    // eigen laag; de uiteindelijke swatches dragen ook expliciete entiteiteigenschappen.
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
            IsPlottable = plottable,
            LineWeight = weight
        };
        lt.Add(ltr);
        tr.AddNewlyCreatedDBObject(ltr, true);
    }

    // De NLCS-template schrijft tekststijl "NLCS-ISO" voor (font NLCS-ISO.ttf). Die krijgt
    // voorrang boven een losse "bevat NLCS"-match; hoogtes zetten we zelf per tekst.
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
            throw new InvalidOperationException($"Tekststijl '{name}' ontbreekt in deze tekening. Kies een bestaande stijl of laat de tekststijl leeg voor de TAUW-opmaak.");
        }

        // De naam alleen garandeert geen template-opmaak: in een bestaande DWG kan
        // NLCS-ISO een ander font, een breedtefactor of schuine tekst bevatten.
        // Laat die gedeelde stijl intact en gebruik dan een eigen correcte stijl.
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

        // Een lege legenda-DWG heeft de tekststijl vaak alleen in de xref. Maak de
        // hoststijl zelfstandig aan; het huidige STANDARD-font is geen templatekeuze.
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
