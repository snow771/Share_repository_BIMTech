using System.IO;
using ClosedXML.Excel;

namespace SimilaritySearchApp.Stencil;

/// <summary>
/// 読込結果の Excel 出力（Python 版と同じファイル名・列構成）。
///   ① 物件諸情報一覧.xlsx（62列、見出しは水色・游ゴシック、数値・日付の表示形式付き）
///   ② 単価テーブル_集約結果.xlsx（シート「単価テーブル_査定」「単価テーブル_修正」）
/// </summary>
public static class StencilExcelWriter
{
    public const string ProjectInfoFileName = "物件諸情報一覧.xlsx";
    public const string UnitPriceFileName = "単価テーブル_集約結果.xlsx";

    private static readonly XLColor HeaderFill = XLColor.FromHtml("#ADD8E6");
    private const string FontName = "游ゴシック";

    private static readonly HashSet<string> IntColumns = new() { "請負金額", "契約時予定利益" };
    private static readonly HashSet<string> OneDecimalColumns = new() { "敷地面積", "建築面積", "法廷床面積", "施工床面積", "施工坪単価" };
    private static readonly HashSet<string> DateColumns = new() { "工期(着工)", "工期(竣工)" };

    /// <summary>保存したファイルのパス一覧を返す。</summary>
    public static List<string> Write(StencilScanResult scan, IEnumerable<StencilFileResult> files, string outputFolder)
    {
        Directory.CreateDirectory(outputFolder);
        var list = files.Where(f => f.IsValid).OrderBy(f => f.ProjectId, StringComparer.Ordinal).ThenBy(f => f.ProjectName, StringComparer.Ordinal).ToList();
        var saved = new List<string>();

        if (scan.DoProjectInfo && list.Any(f => f.ProjectInfo is not null))
        {
            var path = Path.Combine(outputFolder, ProjectInfoFileName);
            WriteProjectInfo(list.Where(f => f.ProjectInfo is not null).Select(f => f.ProjectInfo!).ToList(), path);
            saved.Add(path);
        }
        if (scan.DoUnitPrice && list.Any(f => f.UnitPrices is not null))
        {
            var path = Path.Combine(outputFolder, UnitPriceFileName);
            WriteUnitPrices(list.Where(f => f.UnitPrices is not null).SelectMany(f => f.UnitPrices!).ToList(), path);
            saved.Add(path);
        }
        if (saved.Count == 0) throw new InvalidOperationException("出力できるデータがありません。");
        return saved;
    }

    public static void WriteProjectInfo(IReadOnlyList<Dictionary<string, object?>> rows, string path)
    {
        using var wb = new XLWorkbook();
        var ws = wb.AddWorksheet(SafeSheetName(Path.GetFileNameWithoutExtension(path)));
        var cols = ProjectInfoExtractor.OutputColumns;

        for (var c = 0; c < cols.Length; c++) ws.Cell(1, c + 1).Value = cols[c];
        for (var r = 0; r < rows.Count; r++)
        {
            for (var c = 0; c < cols.Length; c++)
            {
                var cell = ws.Cell(r + 2, c + 1);
                SetValue(cell, rows[r].TryGetValue(cols[c], out var v) ? v : null);
                if (IntColumns.Contains(cols[c])) cell.Style.NumberFormat.Format = "#,##0";
                else if (OneDecimalColumns.Contains(cols[c])) cell.Style.NumberFormat.Format = "#,##0.0";
                else if (cols[c] == "利益率") cell.Style.NumberFormat.Format = "0.00000_);[Red]\\(0.00000\\)";
                else if (DateColumns.Contains(cols[c])) cell.Style.NumberFormat.Format = "yyyy/m/d";
            }
        }

        StyleTable(ws, rows.Count + 1, cols.Length);
        ws.SheetView.FreezeRows(1);
        ws.SheetView.FreezeColumns(2);   // project_id・物件名を固定（Python 版は AT2。横スクロールしやすいよう変更）
        ws.Columns(1, cols.Length).AdjustToContents(1, Math.Min(rows.Count + 1, 200), 6, 50);
        wb.SaveAs(path);
    }

    public static void WriteUnitPrices(IReadOnlyList<UnitPriceRow> rows, string path)
    {
        using var wb = new XLWorkbook();
        AddPriceSheet(wb, "単価テーブル_査定", "査定単価", rows, r => r.AssessPrice);
        AddPriceSheet(wb, "単価テーブル_修正", "修正単価", rows, r => r.RevisePrice);
        wb.SaveAs(path);
    }

    private static void AddPriceSheet(XLWorkbook wb, string name, string priceHeader, IReadOnlyList<UnitPriceRow> rows, Func<UnitPriceRow, double> price)
    {
        var ws = wb.AddWorksheet(name);
        string[] headers = { "project_id", "工種コード", "工種項目", "工事項目", "項目", priceHeader };
        for (var c = 0; c < headers.Length; c++) ws.Cell(1, c + 1).Value = headers[c];
        for (var i = 0; i < rows.Count; i++)
        {
            var r = rows[i];
            var n = i + 2;
            ws.Cell(n, 1).Value = r.ProjectId;
            ws.Cell(n, 2).Value = r.WorkTypeCode;
            ws.Cell(n, 3).Value = r.Trade ?? "";
            ws.Cell(n, 4).Value = r.Work ?? "";
            ws.Cell(n, 5).Value = r.Item ?? "";
            ws.Cell(n, 6).Value = price(r);
            ws.Cell(n, 6).Style.NumberFormat.Format = "#,##0";
        }
        StyleTable(ws, rows.Count + 1, headers.Length);
        ws.SheetView.FreezeRows(1);
        ws.Columns(1, headers.Length).AdjustToContents(1, Math.Min(rows.Count + 1, 500), 8, 50);
    }

    private static void StyleTable(IXLWorksheet ws, int lastRow, int lastCol)
    {
        var all = ws.Range(1, 1, Math.Max(1, lastRow), lastCol);
        all.Style.Font.FontName = FontName;
        all.Style.Font.FontSize = 11;
        all.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
        all.Style.Border.InsideBorder = XLBorderStyleValues.Thin;

        var header = ws.Range(1, 1, 1, lastCol);
        header.Style.Fill.BackgroundColor = HeaderFill;
        header.Style.Font.Bold = true;
        header.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        header.Style.Alignment.Vertical = XLAlignmentVerticalValues.Top;
    }

    private static void SetValue(IXLCell cell, object? v)
    {
        switch (v)
        {
            case null: break;
            case double d: cell.Value = d; break;
            case int i: cell.Value = i; break;
            case long l: cell.Value = (double)l; break;
            case decimal m: cell.Value = (double)m; break;
            case DateTime dt: cell.Value = dt; break;
            case bool b: cell.Value = b; break;
            default: cell.Value = Convert.ToString(v, System.Globalization.CultureInfo.InvariantCulture) ?? ""; break;
        }
    }

    private static string SafeSheetName(string name)
    {
        var s = new string(name.Where(ch => !"[]:*?/\\".Contains(ch)).ToArray());
        if (s.Length == 0) s = "物件諸情報一覧";
        return s.Length > 31 ? s[..31] : s;
    }
}
