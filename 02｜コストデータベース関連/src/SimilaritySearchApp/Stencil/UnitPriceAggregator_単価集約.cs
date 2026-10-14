using System.IO;
using System.Globalization;
using SimilaritySearchApp.Excel;

namespace SimilaritySearchApp.Stencil;

/// <summary>テンプレートの1行（工種コードと名称）。</summary>
public sealed record UnitPriceTemplateRow(string WorkTypeCode, string? Trade, string? Work, string? Item);

/// <summary>集約結果の1行（物件 × 工種コード）。</summary>
public sealed record UnitPriceRow(
    string ProjectId, string WorkTypeCode, string? Trade, string? Work, string? Item,
    double AssessPrice, double RevisePrice, bool FoundInSource);

/// <summary>
/// ② 査定・修正単価の集約（Python「stencile単価集約ツール.py」を移植・整理）。
///   テンプレート（B列=工種コード, C=工種項目, D=工事項目, E=項目）の工種コードごとに、
///   各 Stencile の「積算予算」シートの D列（工種コード）と照合し、E列=査定単価・F列=修正単価を取り出す。
///   見つからない工種コードは 0（Python 版と同じ）。
///
/// Python 版からの修正:
///   ・テンプレートの行数（373行）・積算予算の行範囲（2〜938行）を固定せず、空行まで／最終行まで読む
/// </summary>
public static class UnitPriceAggregator
{
    public const string TemplateSheetName = "単価テーブル";
    public const string EstimateSheet = "積算予算";
    public const string MainSheet = "実行予算表書";

    /// <summary>テンプレート Excel の工種コード一覧（「単価テーブル」シート、無ければ先頭シート。2行目から B列が空になるまで）。</summary>
    public static List<UnitPriceTemplateRow> ReadTemplate(string path)
    {
        var book = ExcelWorkbookReader.Read(path);
        if (book.Count == 0) throw new InvalidDataException("テンプレートにシートがありません。");
        var sheet = book.TryGetValue(TemplateSheetName, out var s) ? s : book.Values.First();
        return ReadTemplate(sheet);
    }

    public static List<UnitPriceTemplateRow> ReadTemplate(SheetData sheet)
    {
        var rows = new List<UnitPriceTemplateRow>();
        for (var r = 2; r <= sheet.RowCount; r++)
        {
            var code = Text(sheet[r, 2]);
            if (code is null) break;
            rows.Add(new UnitPriceTemplateRow(code, Text(sheet[r, 3]), Text(sheet[r, 4]), Text(sheet[r, 5])));
        }
        if (rows.Count == 0) throw new InvalidDataException("テンプレートの B列（2行目以降）に工種コードがありません。");
        return rows;
    }

    /// <summary>1ブック分。project_id は「実行予算表書」C1（マッピングがあればその番地）。</summary>
    public static List<UnitPriceRow> Extract(
        IReadOnlyDictionary<string, SheetData> book, IReadOnlyList<UnitPriceTemplateRow> template, string? projectId)
    {
        if (string.IsNullOrWhiteSpace(projectId)) throw new InvalidDataException("project_id（実行予算表書 C1）が空です。");
        if (!book.TryGetValue(EstimateSheet, out var est)) throw new InvalidDataException($"「{EstimateSheet}」シートがありません。");

        // 積算予算: D列=工種コード → (E列=査定, F列=修正)。同じコードが複数あれば後の行を使う（Python 版と同じ）
        var prices = new Dictionary<string, (object? Assess, object? Revise)>(StringComparer.Ordinal);
        for (var r = 2; r <= est.RowCount; r++)
        {
            var code = Text(est[r, 4]);
            if (code is not null) prices[code] = (est[r, 5], est[r, 6]);
        }

        return template.Select(t =>
        {
            var hit = prices.TryGetValue(t.WorkTypeCode, out var p);
            return new UnitPriceRow(projectId!, t.WorkTypeCode, t.Trade, t.Work, t.Item,
                hit ? ProjectInfoExtractor.Number(p.Assess) ?? 0 : 0,
                hit ? ProjectInfoExtractor.Number(p.Revise) ?? 0 : 0,
                hit);
        }).ToList();
    }

    private static string? Text(object? v)
    {
        var s = v is null ? "" : Convert.ToString(v, CultureInfo.InvariantCulture)?.Trim() ?? "";
        return s.Length == 0 ? null : s;
    }
}
