using System.Text.Json.Nodes;
using ClosedXML.Excel;
using SimilaritySearchApp.Ocr.Structure;

namespace SimilaritySearchApp.Ocr.Output;

/// <summary>
/// OCR そのままの内容を確認する Excel（Python 版 write_check_excel と同じ構成）。
///   Overview      … 元 PDF・ページ数・分割単位・検出した表の数
///   OCR line list … ページごとの OCR 行
///   Table_n       … Azure が検出した表を行×列のまま
/// </summary>
public static class OcrCheckExcelWriter
{
    public static void Write(JsonNode merged, string path)
    {
        using var wb = new XLWorkbook();
        var info = merged["batch_info"];
        var tables = OcrText.Tables(merged).ToList();

        var summary = wb.AddWorksheet("Overview");
        var rows = new (string, object?)[]
        {
            ("項目", "値"),
            ("元PDF", (string?)info?["source_pdf"] ?? ""),
            ("元PDFページ数", OcrText.Int(info?["source_pdf_page_count"])),
            ("Azure取得ページ数", OcrText.Int(info?["ocr_page_count"])),
            ("分割単位", $"{OcrText.Int(info?["chunk_pages"])}ページ"),
            ("検出表数", tables.Count),
        };
        for (var i = 0; i < rows.Length; i++)
        {
            summary.Cell(i + 1, 1).Value = rows[i].Item1;
            StructuredExcelWriter.SetValue(summary.Cell(i + 1, 2), rows[i].Item2);
        }
        StructuredExcelWriter.StyleHeader(summary.Range(1, 1, 1, 2));
        summary.Range(1, 1, rows.Length, 2).Style.Alignment.WrapText = true;
        FitColumns(summary, 40);

        var lines = wb.AddWorksheet("OCR line list");
        lines.Cell(1, 1).Value = "PDFページ";
        lines.Cell(1, 2).Value = "OCR行";
        var r = 2;
        foreach (var page in OcrText.Pages(merged))
        {
            var pageNo = OcrText.Int(page["pageNumber"]);
            foreach (var line in page["lines"] as JsonArray ?? new JsonArray())
            {
                lines.Cell(r, 1).Value = pageNo;
                lines.Cell(r, 2).Value = (string?)line?["content"] ?? "";
                r++;
            }
        }
        StructuredExcelWriter.StyleHeader(lines.Range(1, 1, 1, 2));
        lines.SheetView.FreezeRows(1);
        FitColumns(lines, 80);

        for (var t = 0; t < tables.Count; t++)
        {
            var ws = wb.AddWorksheet($"Table_{t + 1}");
            var matrix = OcrText.TableMatrix(tables[t]);
            var cols = Math.Max(1, matrix.FirstOrDefault()?.Length ?? 1);
            ws.Cell(1, 1).Value = $"PDFページ: {OcrText.PageNumber(tables[t])?.ToString() ?? ""}";
            ws.Cell(1, 1).Style.Fill.BackgroundColor = XLColor.FromHtml("#1F5E4B");
            ws.Cell(1, 1).Style.Font.FontColor = XLColor.White;
            ws.Cell(1, 1).Style.Font.Bold = true;
            for (var i = 0; i < matrix.Count; i++)
            {
                for (var c = 0; c < matrix[i].Length; c++) ws.Cell(i + 2, c + 1).Value = matrix[i][c];
            }
            if (matrix.Count > 0) StructuredExcelWriter.StyleHeader(ws.Range(2, 1, 2, cols));
            ws.SheetView.FreezeRows(2);
            var used = ws.Range(1, 1, matrix.Count + 1, cols);
            used.Style.Alignment.Vertical = XLAlignmentVerticalValues.Top;
            used.Style.Alignment.WrapText = true;
            FitColumns(ws, 40);
        }

        wb.SaveAs(path);
    }

    /// <summary>文字数に合わせて列幅を決める（最小 8・最大 maxWidth）。</summary>
    private static void FitColumns(IXLWorksheet ws, int maxWidth)
    {
        var lastCol = ws.LastColumnUsed()?.ColumnNumber() ?? 0;
        for (var c = 1; c <= lastCol; c++)
        {
            var width = 8;
            foreach (var cell in ws.Column(c).CellsUsed())
            {
                var longest = cell.GetString().Split('\n').Max(l => l.Length);
                width = Math.Max(width, Math.Min(longest + 2, maxWidth));
            }
            ws.Column(c).Width = width;
        }
    }
}
