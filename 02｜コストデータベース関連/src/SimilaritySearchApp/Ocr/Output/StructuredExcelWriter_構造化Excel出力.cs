using ClosedXML.Excel;
using SimilaritySearchApp.Ocr.Models;

namespace SimilaritySearchApp.Ocr.Output;

/// <summary>
/// 構造化結果を Excel に書く（Python 版 v6 の export_excel と同じ 3 シート）。
///   構造化明細 … 金額明細と参考明細（金額検算 OK=緑・重複候補=黄・要確認=赤 を条件付き書式で色分け）
///   集計・確認 … 見積ヘッダ金額と明細合計の差額など
///   OCR行監査 … OCR 表の各行を 採用／見出し／除外／空行 で一覧化（除外理由つき）
/// </summary>
public static class StructuredExcelWriter
{
    /// <summary>列（項目名, 見出し, 幅）。順番は Python 版と同じ。</summary>
    private static readonly (string Key, string Label, double Width)[] Columns =
    {
        ("estimate_id", "見積ID", 14), ("supplier_name", "協力業者名", 20), ("project_name", "工事名", 22),
        ("source_file", "元JSON", 28), ("record_type", "明細種別", 10), ("pdf_page", "PDFページ", 9),
        ("ocr_table_index", "OCR表番号", 9), ("ocr_row_index", "OCR行番号", 9), ("trade", "工種", 16),
        ("section", "区分", 14), ("item_name", "名称", 32), ("material", "材料・仕様", 18),
        ("dimension", "寸法・規格", 18), ("quantity", "数量", 10), ("unit", "単位", 8),
        ("unit_price", "単価", 12), ("amount", "金額", 14), ("remarks_ocr", "備考_OCR", 20),
        ("calculated_amount", "計算金額", 14), ("difference", "差額", 10), ("amount_check", "金額検算", 10),
        ("duplicate_candidate", "重複候補", 14), ("review_required", "要確認", 8), ("review_reason", "要確認理由", 40),
    };

    private static readonly Dictionary<string, string> NumberFormats = new()
    {
        ["quantity"] = "#,##0.0", ["unit_price"] = "#,##0", ["amount"] = "#,##0", ["calculated_amount"] = "#,##0", ["difference"] = "#,##0",
    };

    private static XLColor C(string hex) => XLColor.FromHtml("#" + hex);

    public static void Write(StructuredResult result, string path)
    {
        using var wb = new XLWorkbook();
        WriteItems(wb.AddWorksheet("構造化明細"), result);
        WriteSummary(wb.AddWorksheet("集計・確認"), result);
        WriteAudit(wb.AddWorksheet("OCR行監査"), result);
        wb.SaveAs(path);
    }

    private static object? ValueOf(EstimateItem r, string key) => key switch
    {
        "estimate_id" => r.EstimateId, "supplier_name" => r.SupplierName, "project_name" => r.ProjectName,
        "source_file" => r.SourceFile, "record_type" => r.RecordType, "pdf_page" => r.PdfPage,
        "ocr_table_index" => r.OcrTableIndex, "ocr_row_index" => r.OcrRowIndex, "trade" => r.Trade,
        "section" => r.Section, "item_name" => r.ItemName, "material" => r.Material, "dimension" => r.Dimension,
        "quantity" => r.Quantity, "unit" => r.Unit, "unit_price" => r.UnitPrice, "amount" => r.Amount,
        "remarks_ocr" => r.RemarksOcr, "calculated_amount" => r.CalculatedAmount, "difference" => r.Difference,
        "amount_check" => r.AmountCheck, "duplicate_candidate" => r.DuplicateCandidate,
        "review_required" => r.ReviewRequired, "review_reason" => r.ReviewReason,
        _ => null,
    };

    private static void WriteItems(IXLWorksheet ws, StructuredResult result)
    {
        var n = Columns.Length;
        ws.Cell(1, 1).Value = "協力業者見積：構造化明細（参考明細・OCR行監査対応）";
        var title = ws.Range(1, 1, 1, n).Merge();
        title.Style.Fill.BackgroundColor = C("1F5E4B");
        title.Style.Font.FontColor = XLColor.White;
        title.Style.Font.Bold = true;
        title.Style.Font.FontSize = 14;
        title.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
        ws.Row(1).Height = 26;

        ws.Cell(2, 1).Value = "Azure Layout JSONを標準列へ変換。差分ルールは協力業者・帳票の例外だけを補完。";
        var note = ws.Range(2, 1, 2, n).Merge();
        note.Style.Fill.BackgroundColor = C("E8F3EE");

        for (var c = 0; c < n; c++) ws.Cell(4, c + 1).Value = Columns[c].Label;
        StyleHeader(ws.Range(4, 1, 4, n));

        var row = 5;
        foreach (var item in result.Items)
        {
            for (var c = 0; c < n; c++) SetValue(ws.Cell(row, c + 1), ValueOf(item, Columns[c].Key));
            row++;
        }

        var last = Math.Max(4, result.Items.Count + 4);
        var body = ws.Range(4, 1, last, n);
        body.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
        body.Style.Border.InsideBorder = XLBorderStyleValues.Thin;
        body.Style.Border.OutsideBorderColor = C("D1D5DB");
        body.Style.Border.InsideBorderColor = C("D1D5DB");
        ws.Range(5, 1, Math.Max(5, last), n).Style.Alignment.Vertical = XLAlignmentVerticalValues.Top;
        ws.Range(5, 1, Math.Max(5, last), n).Style.Alignment.WrapText = true;

        if (last >= 5)
        {
            foreach (var (key, format) in NumberFormats)
            {
                var col = Index(key);
                ws.Range(5, col, last, col).Style.NumberFormat.Format = format;
            }
            Highlight(ws, "amount_check", last, "=\"OK\"", "DCFCE7", "166534");
            Highlight(ws, "duplicate_candidate", last, "<>\"\"", "FEF3C7", "92400E");
            Highlight(ws, "review_required", last, "=\"要確認\"", "FEE2E2", "991B1B");
        }

        for (var c = 0; c < n; c++) ws.Column(c + 1).Width = Columns[c].Width;
        ws.SheetView.FreezeRows(4);
        ws.Range(4, 1, last, n).SetAutoFilter();
    }

    private static int Index(string key) => Array.FindIndex(Columns, c => c.Key == key) + 1;

    /// <summary>その列が条件に合うセルを色付けする（例: 金額検算 = "OK" → 緑）。</summary>
    private static void Highlight(IXLWorksheet ws, string key, int last, string condition, string fill, string font)
    {
        var col = Index(key);
        var letter = XLHelper.GetColumnLetterFromNumber(col);
        var style = ws.Range(5, col, last, col).AddConditionalFormat().WhenIsTrue($"=${letter}5{condition}");
        style.Fill.SetBackgroundColor(C(fill));
        style.Font.SetFontColor(C(font));
        style.Font.SetBold();
    }

    private static void WriteSummary(IXLWorksheet ws, StructuredResult result)
    {
        var m = result.Metadata;
        var s = result.Summary;
        var rows = new (string, object?)[]
        {
            ("協力業者名", m.SupplierName), ("工事名", m.ProjectName), ("見積ID", m.EstimateId),
            ("見積ヘッダ金額", s.HeaderEstimateAmount), ("抽出明細金額合計", s.DetailAmountTotal), ("差額", s.DifferenceToHeaderAmount),
            ("出力明細件数（総数）", s.DetailCount), ("うち金額明細", s.MoneyDetailCount), ("うち参考明細", s.ReferenceDetailCount),
            ("金額検算OK件数", s.AmountCheckOkCount), ("OCR行監査件数", s.AuditRowCount), ("OCR行監査：除外件数", s.AuditExcludedCount),
            ("重複候補件数", s.DuplicateCandidateCount), ("共通ルール", m.CommonRulesFile), ("差分ルール", m.SupplierRulesFile),
        };
        ws.Cell(1, 1).Value = "項目";
        ws.Cell(1, 2).Value = "値";
        for (var i = 0; i < rows.Length; i++)
        {
            ws.Cell(i + 2, 1).Value = rows[i].Item1;
            SetValue(ws.Cell(i + 2, 2), rows[i].Item2);
        }
        ws.Range(5, 2, 7, 2).Style.NumberFormat.Format = "#,##0";
        StyleHeader(ws.Range(1, 1, 1, 2));
        var all = ws.Range(1, 1, rows.Length + 1, 2);
        Bordered(all);
        all.Style.Alignment.WrapText = true;
        all.Style.Alignment.Vertical = XLAlignmentVerticalValues.Top;
        ws.Column(1).Width = 32;
        ws.Column(2).Width = 56;
    }

    private static void WriteAudit(IXLWorksheet ws, StructuredResult result)
    {
        var headers = new[] { "PDFページ", "OCR表番号", "OCR行番号", "判定", "理由", "OCR行原文" };
        for (var c = 0; c < headers.Length; c++) ws.Cell(1, c + 1).Value = headers[c];
        StyleHeader(ws.Range(1, 1, 1, headers.Length));
        var row = 2;
        foreach (var a in result.AuditRows)
        {
            SetValue(ws.Cell(row, 1), a.PdfPage);
            ws.Cell(row, 2).Value = a.OcrTableIndex;
            ws.Cell(row, 3).Value = a.OcrRowIndex;
            ws.Cell(row, 4).Value = a.RowStatus;
            ws.Cell(row, 5).Value = a.Reason;
            ws.Cell(row, 6).Value = a.RowText;
            row++;
        }
        var last = Math.Max(1, result.AuditRows.Count + 1);
        var all = ws.Range(1, 1, last, headers.Length);
        Bordered(all);
        all.Style.Alignment.WrapText = true;
        all.Style.Alignment.Vertical = XLAlignmentVerticalValues.Top;
        var widths = new double[] { 10, 12, 12, 12, 30, 100 };
        for (var c = 0; c < widths.Length; c++) ws.Column(c + 1).Width = widths[c];
        ws.SheetView.FreezeRows(1);
        all.SetAutoFilter();
    }

    internal static void StyleHeader(IXLRange header)
    {
        header.Style.Fill.BackgroundColor = C("2F7D62");
        header.Style.Font.FontColor = XLColor.White;
        header.Style.Font.Bold = true;
        header.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        header.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
        header.Style.Alignment.WrapText = true;
    }

    private static void Bordered(IXLRange range)
    {
        range.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
        range.Style.Border.InsideBorder = XLBorderStyleValues.Thin;
        range.Style.Border.OutsideBorderColor = C("D1D5DB");
        range.Style.Border.InsideBorderColor = C("D1D5DB");
    }

    internal static void SetValue(IXLCell cell, object? v)
    {
        switch (v)
        {
            case null: cell.Value = Blank.Value; break;
            case double d: cell.Value = d; break;
            case int i: cell.Value = i; break;
            case long l: cell.Value = (double)l; break;
            default: cell.Value = v.ToString() ?? ""; break;
        }
    }
}
