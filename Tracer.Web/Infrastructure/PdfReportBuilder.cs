using System.Globalization;
using System.Text;

namespace Tracer.Web.Infrastructure;

internal static class PdfReportBuilder
{
    private const double PageWidth = 595;
    private const double PageHeight = 842;
    private const double LeftMargin = 42;
    private const double RightMargin = 42;
    private const double TopMargin = 42;
    private const double BottomMargin = 42;
    private const double TitleFontSize = 15;
    private const double MetaFontSize = 9;
    private const double BodyFontSize = 9;
    private const double TitleLineHeight = 20;
    private const double MetaLineHeight = 12;
    private const double TextLineHeight = 11;
    private const double ParagraphGap = 8;
    private const double BlockGap = 10;
    private const double CellPaddingX = 4;
    private const double CellPaddingY = 4;
    private const double HeaderRowFillGray = 0.92;
    private const double BorderGray = 0.68;
    private const double CharWidthFactor = 0.56;
    private const int MinColumnChars = 6;

    public static byte[] Build(string title, IReadOnlyList<PdfBlock> blocks)
    {
        var generatedAt = $"Generated {DateTimeOffset.Now:yyyy-MM-dd HH:mm}";
        var pages = BuildPages(title, generatedAt, blocks);
        var objects = new List<string>
        {
            "<< /Type /Catalog /Pages 2 0 R >>"
        };

        var pageObjectIds = Enumerable.Range(0, pages.Count)
            .Select(index => 5 + (index * 2))
            .ToList();

        objects.Add($"<< /Type /Pages /Count {pageObjectIds.Count} /Kids [{string.Join(" ", pageObjectIds.Select(id => $"{id} 0 R"))}] >>");
        objects.Add("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica-Bold >>");
        objects.Add("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>");

        for (var i = 0; i < pages.Count; i++)
        {
            var pageObjectId = pageObjectIds[i];
            var contentObjectId = pageObjectId + 1;
            objects.Add($"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {PageWidth.ToString(CultureInfo.InvariantCulture)} {PageHeight.ToString(CultureInfo.InvariantCulture)}] /Resources << /Font << /F1 3 0 R /F2 4 0 R >> >> /Contents {contentObjectId} 0 R >>");

            var content = pages[i];
            objects.Add($"<< /Length {Encoding.ASCII.GetByteCount(content)} >>\nstream\n{content}\nendstream");
        }

        return WriteDocument(objects);
    }

    private static List<string> BuildPages(string title, string generatedAt, IReadOnlyList<PdfBlock> blocks)
    {
        var pages = new List<PageCanvas>();
        var page = CreatePage(title, generatedAt);
        pages.Add(page);

        foreach (var block in blocks)
        {
            switch (block)
            {
                case PdfParagraph paragraph:
                    RenderParagraphAcrossPages(pages, title, generatedAt, paragraph.Text);
                    break;

                case PdfTable table:
                    RenderTableAcrossPages(pages, title, generatedAt, table);
                    break;
            }
        }

        return pages.Select(x => x.Builder.ToString()).ToList();
    }

    private static void RenderParagraphAcrossPages(List<PageCanvas> pages, string title, string generatedAt, string text)
    {
        var maxChars = GetMaxChars(UsableWidth);
        var lines = WrapText(text, maxChars);
        var index = 0;

        while (index < lines.Count)
        {
            var page = pages[^1];
            var remainingLines = Math.Max(1, (int)Math.Floor((page.CurrentY - BottomMargin) / TextLineHeight));
            if (remainingLines == 0)
            {
                page = CreatePage(title, generatedAt);
                pages.Add(page);
                remainingLines = Math.Max(1, (int)Math.Floor((page.CurrentY - BottomMargin) / TextLineHeight));
            }

            var batchSize = Math.Min(remainingLines, lines.Count - index);
            for (var i = 0; i < batchSize; i++)
            {
                page.DrawText(lines[index + i], LeftMargin, page.CurrentY, BodyFontSize, bold: false);
                page.CurrentY -= TextLineHeight;
            }

            index += batchSize;

            if (index < lines.Count)
            {
                var nextPage = CreatePage(title, generatedAt);
                pages.Add(nextPage);
            }
        }

        pages[^1].CurrentY -= ParagraphGap;
    }

    private static void RenderTableAcrossPages(List<PageCanvas> pages, string title, string generatedAt, PdfTable table)
    {
        if (table.Headers.Length == 0)
        {
            return;
        }

        var widths = CalculateColumnWidths(table);
        var currentPage = pages[^1];

        EnsureSpaceForRow(pages, title, generatedAt, currentPage, GetRowHeight(table.Headers, widths) + BlockGap);
        currentPage = pages[^1];
        DrawTableHeader(currentPage, table.Headers, widths);

        foreach (var row in table.Rows)
        {
            var normalized = NormalizeRow(row, table.Headers.Length);
            var rowHeight = GetRowHeight(normalized, widths);

            if (pages[^1].CurrentY - rowHeight < BottomMargin)
            {
                currentPage = CreatePage(title, generatedAt);
                pages.Add(currentPage);
                DrawTableHeader(currentPage, table.Headers, widths);
            }

            DrawTableRow(pages[^1], normalized, widths, rowHeight, isHeader: false);
        }

        pages[^1].CurrentY -= BlockGap;
    }

    private static void EnsureSpaceForRow(
        List<PageCanvas> pages,
        string title,
        string generatedAt,
        PageCanvas page,
        double requiredHeight)
    {
        if (page.CurrentY - requiredHeight >= BottomMargin)
        {
            return;
        }

        var nextPage = CreatePage(title, generatedAt);
        pages.Add(nextPage);
    }

    private static void DrawTableHeader(PageCanvas page, IReadOnlyList<string> headers, IReadOnlyList<double> widths)
    {
        var rowHeight = GetRowHeight(headers, widths);
        DrawTableRow(page, headers, widths, rowHeight, isHeader: true);
    }

    private static void DrawTableRow(PageCanvas page, IReadOnlyList<string> row, IReadOnlyList<double> widths, double rowHeight, bool isHeader)
    {
        var wrappedCells = row
            .Select((cell, index) => WrapText(cell, GetMaxChars(widths[index] - (CellPaddingX * 2))))
            .ToList();

        var rowTop = page.CurrentY;
        var rowBottom = rowTop - rowHeight;
        var x = LeftMargin;

        if (isHeader)
        {
            page.DrawFilledRectangle(x, rowBottom, widths.Sum(), rowHeight, HeaderRowFillGray);
        }

        for (var i = 0; i < widths.Count; i++)
        {
            page.DrawRectangle(x, rowBottom, widths[i], rowHeight, BorderGray);

            var textY = rowTop - CellPaddingY - BodyFontSize;
            foreach (var line in wrappedCells[i])
            {
                page.DrawText(line, x + CellPaddingX, textY, BodyFontSize, bold: isHeader);
                textY -= TextLineHeight;
            }

            x += widths[i];
        }

        page.CurrentY = rowBottom;
    }

    private static double GetRowHeight(IReadOnlyList<string> row, IReadOnlyList<double> widths)
    {
        var maxLines = row
            .Select((cell, index) => WrapText(cell, GetMaxChars(widths[index] - (CellPaddingX * 2))).Count)
            .DefaultIfEmpty(1)
            .Max();

        return (maxLines * TextLineHeight) + (CellPaddingY * 2) + 2;
    }

    private static List<string> WrapText(string? text, int maxChars)
    {
        var input = (text ?? string.Empty).Trim();
        if (input.Length == 0)
        {
            return new List<string> { string.Empty };
        }

        var lines = new List<string>();
        var remaining = input;

        while (remaining.Length > maxChars)
        {
            var splitAt = remaining.LastIndexOf(' ', maxChars);
            if (splitAt <= 0)
            {
                splitAt = maxChars;
            }

            lines.Add(remaining[..splitAt].TrimEnd());
            remaining = remaining[splitAt..].TrimStart();
        }

        lines.Add(remaining);
        return lines;
    }

    private static double[] CalculateColumnWidths(PdfTable table)
    {
        var availableWidth = UsableWidth;
        var weights = new double[table.Headers.Length];

        for (var i = 0; i < table.Headers.Length; i++)
        {
            var headerWeight = Math.Max(MinColumnChars, Math.Min(18, table.Headers[i].Length));
            var dataWeight = table.Rows
                .Where(row => i < row.Length)
                .Select(row => Math.Min(36, (row[i] ?? string.Empty).Length))
                .DefaultIfEmpty(MinColumnChars)
                .Max();

            weights[i] = Math.Max(headerWeight, dataWeight);
        }

        var totalWeight = weights.Sum();
        var widths = weights
            .Select(weight => Math.Round((weight / totalWeight) * availableWidth, 2, MidpointRounding.AwayFromZero))
            .ToArray();

        var difference = availableWidth - widths.Sum();
        widths[^1] += difference;

        foreach (ref var width in widths.AsSpan())
        {
            var minimumWidth = (MinColumnChars * BodyFontSize * CharWidthFactor) + (CellPaddingX * 2);
            if (width < minimumWidth)
            {
                width = minimumWidth;
            }
        }

        var overflow = widths.Sum() - availableWidth;
        if (overflow > 0)
        {
            widths[^1] -= overflow;
        }

        return widths;
    }

    private static string[] NormalizeRow(string[] row, int columnCount)
    {
        if (row.Length == columnCount)
        {
            return row;
        }

        var normalized = new string[columnCount];
        for (var i = 0; i < columnCount; i++)
        {
            normalized[i] = i < row.Length ? row[i] ?? string.Empty : string.Empty;
        }

        return normalized;
    }

    private static int GetMaxChars(double width)
        => Math.Max(MinColumnChars, (int)Math.Floor(width / (BodyFontSize * CharWidthFactor)));

    private static double UsableWidth => PageWidth - LeftMargin - RightMargin;

    private static PageCanvas CreatePage(string title, string generatedAt)
    {
        var page = new PageCanvas();
        var titleY = PageHeight - TopMargin;
        page.DrawText(title, LeftMargin, titleY, TitleFontSize, bold: true);
        var metaY = titleY - TitleLineHeight;
        page.DrawText(generatedAt, LeftMargin, metaY, MetaFontSize, bold: false);
        page.CurrentY = metaY - MetaLineHeight - 8;
        return page;
    }

    private static byte[] WriteDocument(IReadOnlyList<string> objects)
    {
        var builder = new StringBuilder();
        builder.AppendLine("%PDF-1.4");

        var offsets = new List<int> { 0 };

        for (var i = 0; i < objects.Count; i++)
        {
            offsets.Add(Encoding.ASCII.GetByteCount(builder.ToString()));
            builder.AppendLine($"{i + 1} 0 obj");
            builder.AppendLine(objects[i]);
            builder.AppendLine("endobj");
        }

        var xrefPosition = Encoding.ASCII.GetByteCount(builder.ToString());
        builder.AppendLine("xref");
        builder.AppendLine($"0 {objects.Count + 1}");
        builder.AppendLine("0000000000 65535 f ");

        foreach (var offset in offsets.Skip(1))
        {
            builder.AppendLine($"{offset.ToString("D10", CultureInfo.InvariantCulture)} 00000 n ");
        }

        builder.AppendLine("trailer");
        builder.AppendLine($"<< /Size {objects.Count + 1} /Root 1 0 R >>");
        builder.AppendLine("startxref");
        builder.AppendLine(xrefPosition.ToString(CultureInfo.InvariantCulture));
        builder.Append("%%EOF");

        return Encoding.ASCII.GetBytes(builder.ToString());
    }

    private static string EscapePdfText(string value)
        => value
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("(", "\\(", StringComparison.Ordinal)
            .Replace(")", "\\)", StringComparison.Ordinal);

    private sealed class PageCanvas
    {
        public StringBuilder Builder { get; } = new();
        public double CurrentY { get; set; }

        public void DrawText(string text, double x, double y, double fontSize, bool bold)
        {
            Builder.AppendLine("BT");
            Builder.AppendLine($"/{(bold ? "F1" : "F2")} {fontSize.ToString("0.##", CultureInfo.InvariantCulture)} Tf");
            Builder.AppendLine($"1 0 0 1 {x.ToString("0.##", CultureInfo.InvariantCulture)} {y.ToString("0.##", CultureInfo.InvariantCulture)} Tm");
            Builder.AppendLine($"({EscapePdfText(text)}) Tj");
            Builder.AppendLine("ET");
        }

        public void DrawRectangle(double x, double y, double width, double height, double gray)
        {
            Builder.AppendLine($"{gray.ToString("0.##", CultureInfo.InvariantCulture)} G");
            Builder.AppendLine("0.6 w");
            Builder.AppendLine($"{x.ToString("0.##", CultureInfo.InvariantCulture)} {y.ToString("0.##", CultureInfo.InvariantCulture)} {width.ToString("0.##", CultureInfo.InvariantCulture)} {height.ToString("0.##", CultureInfo.InvariantCulture)} re S");
            Builder.AppendLine("0 G");
        }

        public void DrawFilledRectangle(double x, double y, double width, double height, double gray)
        {
            Builder.AppendLine($"{gray.ToString("0.##", CultureInfo.InvariantCulture)} g");
            Builder.AppendLine($"{x.ToString("0.##", CultureInfo.InvariantCulture)} {y.ToString("0.##", CultureInfo.InvariantCulture)} {width.ToString("0.##", CultureInfo.InvariantCulture)} {height.ToString("0.##", CultureInfo.InvariantCulture)} re f");
            Builder.AppendLine("0 g");
        }
    }
}

internal abstract record PdfBlock;

internal sealed record PdfParagraph(string Text) : PdfBlock;

internal sealed record PdfTable(string[] Headers, IReadOnlyList<string[]> Rows) : PdfBlock;
