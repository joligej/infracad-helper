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
    public static LegendLayout Compute(IReadOnlyList<LegendEntry> entries, LegendSettings s)
    {
        double titleH = s.ToModel(s.TitleTextHeightMm);
        double headerH = s.ToModel(s.HeaderTextHeightMm);
        double subHeaderH = headerH * 0.85;
        double swatchH = s.ToModel(s.SwatchHeightMm);
        double textH = s.ToModel(s.TextHeightMm);
        double lineHeight = textH * Math.Max(1.0, s.LineSpacingFactor);
        double rowGap = Math.Max(s.ToModel(s.RowPitchMm - s.SwatchHeightMm), textH * 0.3);
        double headerBand = headerH + s.ToModel(s.HeaderSpacingMm) * 0.6;
        double subHeaderBand = subHeaderH + s.ToModel(s.HeaderSpacingMm) * 0.4;
        double titleBand = titleH + s.ToModel(s.HeaderSpacingMm);
        double colWidth = s.ToModel(s.ColumnWidthMm);
        double colGap = s.ToModel(s.ColumnGapMm);

        // Ruime tekstbreedte-inschatting: liever iets te veel hoogte dan MText-overlap.
        double textColMm = s.ColumnWidthMm - s.SwatchWidthMm - s.TextGapMm
            - (s.IncludeQuantities ? s.QuantityColumnWidthMm : 0.0);
        int maxChars = Math.Max(8, (int)(textColMm / (s.TextHeightMm * 0.72)));

        // Kolommen balanceren op geschatte teksthoogte, niet alleen op regelaantal.
        int maxRows = Math.Max(1, s.MaxRowsPerColumn);
        var entryLines = new List<List<string>>(entries.Count);
        var entryHeights = new List<double>(entries.Count);
        double totalEntryHeight = 0.0;
        foreach (var entry in entries)
        {
            var lines = TextWrap.Wrap(s.IncludeText ? entry.Description : string.Empty, maxChars);
            if (lines.Count == 0)
                lines = new List<string> { string.Empty };
            double textBlockH = (lines.Count - 1) * lineHeight + textH;
            double rh = Math.Max(swatchH, textBlockH);
            entryLines.Add(lines);
            entryHeights.Add(rh);
            totalEntryHeight += rh + rowGap;
        }

        int columnsNeeded;
        double columnBudget;
        int maxRowsCap;
        double maxColHeight = s.MaxLegendHeightMm > 0 ? s.ToModel(s.MaxLegendHeightMm) : double.MaxValue;
        if (maxColHeight < double.MaxValue)
        {
            columnsNeeded = int.MaxValue;
            columnBudget = double.MaxValue;
            maxRowsCap = int.MaxValue;
        }
        else if (s.Columns > 0)
        {
            columnsNeeded = s.Columns;
            columnBudget = totalEntryHeight / columnsNeeded;
            maxRowsCap = int.MaxValue;
        }
        else if (s.BalanceColumns && entries.Count > maxRows)
        {
            columnsNeeded = (int)Math.Ceiling(entries.Count / (double)maxRows);
            columnBudget = totalEntryHeight / columnsNeeded;
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
            if (curGroupId != entry.StatusGroupId)
            {
                curGroupId = entry.StatusGroupId;
                curStatus = entry.Status;
                curHeaderText = entry.CustomStatusName ?? s.StatusLabel(entry.Status);
                curHoofd = null;
                needStatusHeader = s.IncludeGroupHeaders;
            }

            bool columnBroke = false;
            bool budgetReached = rows >= 1 && col < columnsNeeded - 1
                && colHeight + entryHeights[i] * 0.5 >= columnBudget;
            bool heightExceeded = rows >= 1 && maxColHeight < double.MaxValue
                && colHeight + entryHeights[i] > maxColHeight;
            if (rows >= maxRowsCap || budgetReached || heightExceeded)
            {
                col++;
                y = colTop;
                rows = 0;
                colHeight = 0.0;
                columnBroke = true;
            }

            if (needStatusHeader || (columnBroke && s.IncludeGroupHeaders))
            {
                items.Add(new LegendPlacement
                {
                    Kind = LegendItemKind.Header, X = ColX(col), YTop = y,
                    Column = col, Status = curStatus!.Value, Text = curHeaderText
                });
                minY = Math.Min(minY, y - headerH);
                y -= headerBand;
            }

            bool hoofdChanged = !string.Equals(curHoofd, entry.Hoofdgroep, StringComparison.OrdinalIgnoreCase);
            bool needSubHeader = s.IncludeHoofdgroepHeaders && (hoofdChanged || columnBroke);
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
