namespace NlcsLegenda.Core;

public enum LegendItemKind
{
    Title,

    Header,

    SubHeader,

    Entry
}

public sealed class LegendPlacement
{
    public required LegendItemKind Kind { get; init; }

    public required double X { get; init; }

    public required double YTop { get; init; }

    public required int Column { get; init; }

    public LegendEntry? Entry { get; init; }

    public NlcsStatus Status { get; init; }

    public string Text { get; init; } = string.Empty;

    public IReadOnlyList<string> Lines { get; init; } = System.Array.Empty<string>();

    public double RowHeight { get; init; }
}

public sealed class LegendLayout
{
    public required IReadOnlyList<LegendPlacement> Items { get; init; }

    public required int Columns { get; init; }

    public required double MinX { get; init; }

    public required double MaxX { get; init; }

    public required double MinY { get; init; }

    public required double MaxY { get; init; }

    public double Width => MaxX - MinX;

    public double Height => MaxY - MinY;
}

public static class LegendLayoutEngine
{
    public static LegendLayout Compute(IReadOnlyList<LegendEntry> entries, LegendSettings s,
        Func<string, double, double>? measureTextHeight = null,
        Func<LegendEntry, double>? minimumRowHeight = null,
        Func<string, double, double>? measureHeadingHeight = null)
    {
        if (s.BalanceColumns && s.Columns == 0)
        {
            var measured = s.Clone(); measured.BalanceColumns = false;
            var limit = Compute(entries, measured, measureTextHeight, minimumRowHeight, measureHeadingHeight);
            if (limit.Columns <= 1) return limit;
            measured.Columns = limit.Columns;
            var balanced = Compute(entries, measured, measureTextHeight, minimumRowHeight, measureHeadingHeight);
            return balanced.Columns <= limit.Columns ? balanced : limit;
        }
        double titleH = s.ToModel(s.TitleTextHeightMm);
        double headerH = s.ToModel(s.HeaderTextHeightMm);
        double subHeaderH = headerH;
        double swatchH = s.ToModel(s.SwatchHeightMm);
        double textH = s.ToModel(s.TextHeightMm);
        double lineHeight = textH * Math.Max(1.25, 5.0 / 3.0 * s.LineSpacingFactor);
        double rowGap = Math.Max(s.ToModel(s.RowPitchMm - s.SwatchHeightMm), textH * 0.3);
        double baselineOffset = textH * TemplateDefaults.TextBaselineOffsetRatio;
        double HeadingHeight(string text, double height) => measureHeadingHeight?.Invoke(text, height) ?? height * 1.2;
        var headerTexts = entries.Select(e => e.CustomStatusName ?? s.StatusLabel(e.Status));
        if (s.IncludeHoofdgroepHeaders)
            headerTexts = headerTexts.Concat(entries.Select(e => StandardTexts.HoofdgroepName(e.Hoofdgroep)));
        double actualHeaderH = headerTexts.Distinct().Select(t => HeadingHeight(t, headerH)).DefaultIfEmpty(headerH).Max();
        double headerBand = Math.Max(actualHeaderH + rowGap, headerH + s.ToModel(s.RowPitchMm) - swatchH / 2 - baselineOffset);
        double subHeaderBand = headerBand;
        double statusGap = Math.Max(0, swatchH / 2 + baselineOffset - headerH);
        // onderstreping en onderlengte meenemen in de kophoogte
        double titleBand = Math.Max(HeadingHeight(s.Title, titleH) + rowGap,
            titleH + s.ToModel(s.HeaderSpacingMm) - headerH);
        double colWidth = s.ToModel(s.ColumnWidthMm);
        double colGap = s.ToModel(s.ColumnGapMm);

        double textColMm = s.ColumnWidthMm - s.SwatchWidthMm - s.TextGapMm
            - (s.IncludeQuantities ? s.QuantityColumnWidthMm : 0.0);
        int maxChars = Math.Max(8, (int)(textColMm / (s.TextHeightMm * 0.72)));

        int maxRows = Math.Max(1, s.MaxRowsPerColumn);
        var entryLines = new List<List<string>>(entries.Count);
        var entryHeights = new List<double>(entries.Count);
        double totalEntryHeight = 0.0;
        foreach (var entry in entries)
        {
            var lines = TextWrap.Wrap(s.IncludeText ? entry.DisplayDescription : string.Empty, maxChars);
            if (lines.Count == 0)
                lines = new List<string> { string.Empty };
            double textBlockH = measureTextHeight is not null && s.IncludeText
                ? measureTextHeight(entry.DisplayDescription, Math.Max(s.ToModel(textColMm), textH * 3))
                : (lines.Count - 1) * lineHeight + textH;
            double rh = Math.Max(Math.Max(swatchH, textBlockH), minimumRowHeight?.Invoke(entry) ?? 0);
            entryLines.Add(lines);
            entryHeights.Add(rh);
            totalEntryHeight += rh + rowGap;
        }

        int CountHeaders(int start, int end, bool subgroup)
        {
            int count = 0; string? status = null, hoofd = null;
            for (int i = start; i < end; i++)
            {
                var row = entries[i];
                if (status != row.StatusGroupId) { status = row.StatusGroupId; hoofd = null; if (!subgroup) count++; }
                if (subgroup && !row.IsBlank && !string.Equals(hoofd, row.Hoofdgroep, StringComparison.OrdinalIgnoreCase))
                { count++; hoofd = row.Hoofdgroep; }
            }
            return count;
        }
        int statusHeaders = s.IncludeGroupHeaders ? CountHeaders(0, entries.Count, false) : 0;
        int groupHeaders = s.IncludeHoofdgroepHeaders ? CountHeaders(0, entries.Count, true) : 0;
        double totalContentHeight = totalEntryHeight + statusHeaders * headerBand + groupHeaders * subHeaderBand
            + Math.Max(0, statusHeaders - 1) * statusGap;
        int columnsNeeded;
        double columnBudget;
        int maxRowsCap;
        double maxColHeight = s.MaxLegendHeightMm > 0 ? s.ToModel(s.MaxLegendHeightMm) : double.MaxValue;
        if (maxColHeight < double.MaxValue)
        {
            columnsNeeded = s.Columns > 0 ? Math.Min(s.Columns, Math.Max(1, entries.Count)) : int.MaxValue;
            columnBudget = s.Columns > 0 ? (totalContentHeight + (columnsNeeded - 1) * (s.IncludeGroupHeaders ? headerBand : 0)) / columnsNeeded : double.MaxValue;
            maxRowsCap = maxRows;
        }
        else if (s.Columns > 0)
        {
            columnsNeeded = Math.Min(s.Columns, Math.Max(1, entries.Count));
            columnBudget = (totalContentHeight + (columnsNeeded - 1) * (s.IncludeGroupHeaders ? headerBand : 0)) / columnsNeeded;
            maxRowsCap = int.MaxValue;
        }
        else if (s.BalanceColumns && entries.Count > maxRows)
        {
            columnsNeeded = (int)Math.Ceiling(entries.Count / (double)maxRows);
            columnBudget = (totalContentHeight + (columnsNeeded - 1) * (s.IncludeGroupHeaders ? headerBand : 0)) / columnsNeeded;
            maxRowsCap = maxRows;
        }
        else
        {
            columnsNeeded = int.MaxValue;
            columnBudget = double.MaxValue;
            maxRowsCap = maxRows;
        }

        var items = new List<LegendPlacement>();
        const double maxY = 0.0;
        double minY = 0.0;

        double titleBandUsed = 0.0;
        if (s.IncludeTitle && !string.IsNullOrWhiteSpace(s.Title))
        {
            items.Add(new LegendPlacement
            {
                Kind = LegendItemKind.Title, X = 0, YTop = 0, Column = 0, Text = s.Title
            });
            minY = Math.Min(minY, -titleH);
            titleBandUsed = titleBand;
        }

        double colTop = -titleBandUsed;
        int col = 0;
        double y = colTop;
        int rows = 0;
        bool keepCurrentGroup = false;
        double colHeight = 0.0;
        NlcsStatus? curStatus = null;
        string? curGroupId = null;
        string curHeaderText = string.Empty;
        string? curHoofd = null;

        double ColX(int c) => c * (colWidth + colGap);

        for (int i = 0; i < entries.Count; i++)
        {
            var entry = entries[i];

            bool needStatusHeader = false;
            bool statusChanged = curGroupId != entry.StatusGroupId;
            double groupHeight = 0;
            if (statusChanged)
            {
                curGroupId = entry.StatusGroupId;
                curStatus = entry.Status;
                curHeaderText = entry.CustomStatusName ?? s.StatusLabel(entry.Status);
                curHoofd = null;
                needStatusHeader = s.IncludeGroupHeaders;
            }

            if (statusChanged)
            {
                int end = i;
                while (end < entries.Count && entries[end].StatusGroupId == entry.StatusGroupId) end++;
                groupHeight = entryHeights.Skip(i).Take(end - i).Sum() + (end - i) * rowGap
                    + (s.IncludeGroupHeaders ? headerBand : 0);
                if (s.IncludeHoofdgroepHeaders)
                    groupHeight += CountHeaders(i, end, true) * subHeaderBand;
                keepCurrentGroup = s.KeepStatusGroupsTogether && end - i <= maxRowsCap
                    && groupHeight + titleBandUsed <= maxColHeight;
            }
            bool columnBroke = false;
            bool reserveColumns = columnsNeeded != int.MaxValue && entries.Count - i <= columnsNeeded - col - 1;
            bool groupNeedsColumn = rows > 0 && statusChanged && keepCurrentGroup && -y + statusGap + groupHeight > maxColHeight;
            double extraHeader = needStatusHeader ? headerBand + (rows > 0 ? statusGap : 0) : 0;
            if (s.IncludeHoofdgroepHeaders && !entry.IsBlank
                && !string.Equals(curHoofd, entry.Hoofdgroep, StringComparison.OrdinalIgnoreCase))
                extraHeader += subHeaderBand;
            bool budgetReached = rows >= 1 && col < columnsNeeded - 1 && (!keepCurrentGroup || statusChanged)
                && (reserveColumns || colHeight + (keepCurrentGroup ? groupHeight : extraHeader + entryHeights[i] + rowGap) * 0.5 >= columnBudget);
            bool heightExceeded = rows >= 1 && maxColHeight < double.MaxValue
                && -y + extraHeader + entryHeights[i] > maxColHeight;
            if (rows >= maxRowsCap || budgetReached || heightExceeded || groupNeedsColumn)
            {
                col++;
                y = colTop;
                rows = 0;
                colHeight = 0.0;
                columnBroke = true;
            }

            if (needStatusHeader || (columnBroke && s.IncludeGroupHeaders))
            {
                if (rows > 0)
                { y -= statusGap; colHeight += statusGap; }
                items.Add(new LegendPlacement
                {
                    Kind = LegendItemKind.Header, X = ColX(col), YTop = y,
                    Column = col, Status = curStatus!.Value, Text = curHeaderText
                });
                minY = Math.Min(minY, y - headerH);
                y -= headerBand;
                colHeight += headerBand;
            }

            // blanco regels zonder hoofdgroep of subkop
            bool hoofdChanged = !entry.IsBlank
                && !string.Equals(curHoofd, entry.Hoofdgroep, StringComparison.OrdinalIgnoreCase);
            bool needSubHeader = s.IncludeHoofdgroepHeaders && !entry.IsBlank && (hoofdChanged || columnBroke);
            if (hoofdChanged)
                curHoofd = entry.Hoofdgroep;
            if (needSubHeader && curHoofd is not null)
            {
                items.Add(new LegendPlacement
                {
                    Kind = LegendItemKind.SubHeader, X = ColX(col), YTop = y,
                    Column = col, Status = curStatus!.Value,
                    Text = StandardTexts.HoofdgroepName(curHoofd)
                });
                minY = Math.Min(minY, y - subHeaderH);
                y -= subHeaderBand;
                colHeight += subHeaderBand;
            }

            var entryRowLines = entryLines[i];
            double rowHeight = entryHeights[i];

            items.Add(new LegendPlacement
            {
                Kind = LegendItemKind.Entry, X = ColX(col), YTop = y, Column = col, Entry = entry,
                Lines = entryRowLines, RowHeight = rowHeight
            });
            minY = Math.Min(minY, y - rowHeight);
            y -= rowHeight + rowGap;
            colHeight += rowHeight + rowGap;
            rows++;
        }

        int columns = col + 1;
        double maxX = (columns - 1) * (colWidth + colGap) + colWidth;

        return new LegendLayout
        {
            Items = items,
            Columns = columns,
            MinX = 0,
            MaxX = maxX,
            MinY = minY,
            MaxY = maxY
        };
    }
}
