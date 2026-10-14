using System.IO;
using System.Text.Json.Nodes;
using SimilaritySearchApp.Ocr.Azure;
using SimilaritySearchApp.Ocr.Structure;
using SimilaritySearchApp.Ocr.Usage;

namespace SimilaritySearchApp.Tests;

/// <summary>
/// 見積AI-OCR のテスト。期待値は Python 版 v6（estimate_ocr_structured_v6.py / estimate_ocr_batch_f0.py）に
/// 同じ入力を与えて得た結果。
/// </summary>
public class OcrTests
{
    private static readonly string Assets = FindAssets();

    /// <summary>同梱ルールの場所（出力フォルダにコピーされていなければ、ソースの Ocr\Assets を探す）。</summary>
    private static string FindAssets()
    {
        var copied = Path.Combine(AppContext.BaseDirectory, "Ocr", "Assets");
        if (Directory.Exists(copied)) return copied;
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d is not null; d = d.Parent)
        {
            var src = Path.Combine(d.FullName, "src", "SimilaritySearchApp", "Ocr", "Assets");
            if (Directory.Exists(src)) return src;
        }
        throw new DirectoryNotFoundException(@"Ocr\Assets が見つかりません。");
    }

    private static OcrRules Rules() => OcrRules.Load(
        Path.Combine(Assets, "RulesCommon_共通ルール.json"),
        Path.Combine(Assets, "RulesSupplier_昇工業_協力業者差分.json"));

    // ── 文字列 ──

    [Theory]
    [InlineData("79. 30", 79.3)]
    [InlineData("2, 000", 2000.0)]
    [InlineData("¥1,200円", 1200.0)]
    [InlineData("１２", 12.0)]
    [InlineData("-3", -3.0)]
    public void ToNumber_OCRの数値を読む(string text, double expected) =>
        Assert.Equal(expected, OcrText.ToNumber(text)!.Value, 6);

    [Theory]
    [InlineData("abc")]
    [InlineData("")]
    [InlineData(null)]
    public void ToNumber_数でなければnull(string? text) => Assert.Null(OcrText.ToNumber(text));

    // ── ルール ──

    [Fact]
    public void MergeRules_配列は重複を除いて連結し差分を後ろに足す()
    {
        var rules = OcrRules.MergeRules(
            OcrRules.LoadJson(Path.Combine(Assets, "RulesCommon_共通ルール.json")),
            OcrRules.LoadJson(Path.Combine(Assets, "RulesSupplier_昇工業_協力業者差分.json")))!;
        Assert.Equal(new[] { "寸法", "材料・寸法", "材料･寸法", "材料・規格", "サイズ", "材料·寸法" },
            rules["column_aliases"]!["dimension"]!.AsArray().Select(x => (string)x!));
        Assert.Equal(new[] { "見積番号", "見積No", "見積NO", "見積書No" },
            rules["header_fields"]!["table_labels"]!["estimate_id"]!.AsArray().Select(x => (string)x!));
        Assert.Equal(4, rules["header_fields"]!["content_patterns"]!["project_name"]!.AsArray().Count);
        Assert.Equal("0.2", (string?)rules["rule_version"]);   // 文字列は差分を優先
    }

    [Fact]
    public void FindColumnMap_備考と数量が1セルにまとまった見出しを補正する()
    {
        var map = DetailRecordExtractor.FindColumnMap(new[] { "記号", "名称", "規格・寸法", "備考 数量", "", "単位", "単価", "金額" }, Rules());
        Assert.Equal(1, map["item_name"]);
        Assert.Equal(2, map["material"]);
        Assert.Equal(2, map["dimension"]);
        Assert.Equal(3, map["remarks"]);
        Assert.Equal(4, map["quantity"]);
        Assert.Equal(5, map["unit"]);
        Assert.Equal(6, map["unit_price"]);
        Assert.Equal(7, map["amount"]);
    }

    // ── 構造化（Python 版と同じ結果になること） ──

    private static JsonObject Table(string[][] rows, int page)
    {
        var cells = new JsonArray();
        for (var r = 0; r < rows.Length; r++)
            for (var c = 0; c < rows[r].Length; c++)
                if (rows[r][c] != "") cells.Add(new JsonObject { ["rowIndex"] = r, ["columnIndex"] = c, ["content"] = rows[r][c] });
        return new JsonObject
        {
            ["rowCount"] = rows.Length, ["columnCount"] = rows[0].Length, ["cells"] = cells,
            ["boundingRegions"] = new JsonArray(new JsonObject { ["pageNumber"] = page }),
        };
    }

    private static JsonObject SampleLayout()
    {
        string[] h = { "名称", "仕様", "寸法", "数量", "単位", "単価", "金額", "備考" };
        string[] R(params string[] v) => v;
        var lines = new[] { "御見積書", "工 事 名 文京区本郷4丁目計画", "スターツCAM株式会社 御中", "株式会社", "昇工業", "見積番号", "M-001", "見積金額", "¥1,432,800" };
        return new JsonObject
        {
            ["content"] = "御見積書\n工 事 名 文京区本郷4丁目計画\n株式会社\n昇工業\n見積番号 M-001",
            ["pages"] = new JsonArray(
                new JsonObject { ["pageNumber"] = 1, ["lines"] = new JsonArray(lines.Select(l => (JsonNode)new JsonObject { ["content"] = l }).ToArray()) },
                new JsonObject { ["pageNumber"] = 2, ["lines"] = new JsonArray() },
                new JsonObject { ["pageNumber"] = 3, ["lines"] = new JsonArray() }),
            ["tables"] = new JsonArray(
                Table(new[]
                {
                    h,
                    R("防水工事", "", "", "", "", "", "", ""),
                    R("(屋上)", "", "", "", "", "", "", ""),
                    R("ウレタン塗膜防水", "X-2", "t=2.0", "128. 5", "m2", "6,800", "873,800", ""),
                    R("ドレン改修", "鋳鉄", "φ100", "6", "ヶ", "18,000", "108,500", "灌"),
                    R("既存撤去", "", "", "", "", "", "", ""),
                    R("養生", "", "", "1", "式", "", "", "数量のみ"),
                    R("", "", "", "", "", "", "12,000", ""),
                    R(":unselected:", "", "", "", "", "", "", ""),
                    R("小計", "", "", "", "", "", "981,800", ""),
                }, 1),
                Table(new[] { h, R("シール", "変成シリコン", "", "410", "m", "1,100", "451,000", ""), R("合計", "", "", "", "", "", "1,432,800", "") }, 2),
                Table(new[] { h, R("シール", "変成シリコン", "10x10", "410", "m", "1,100", "451,000", "") }, 3)),
        };
    }

    [Fact]
    public void Structure_ヘッダを取り出す()
    {
        var header = HeaderFieldExtractor.Extract(SampleLayout(), Rules());
        Assert.Equal("文京区本郷4丁目計画", header["project_name"]);
        Assert.Equal("M-001", header["estimate_id"]);
        Assert.Equal("¥1,432,800", header["header_estimate_amount"]);
        Assert.Equal("株式会社 昇工業", header["supplier_name"]);   // 自社名（スターツCAM）は採用しない
    }

    [Fact]
    public void Structure_金額明細と参考明細と重複候補()
    {
        var r = EstimateStructurer.Structure(SampleLayout(), Rules(), "x.json", "c.json", null);
        var items = r.Items;
        Assert.Equal(5, items.Count);

        var a = items[0];
        Assert.Equal(("金額明細", "防水工事", "屋上", "ウレタン塗膜防水"), (a.RecordType, a.Trade, a.Section, a.ItemName));
        Assert.Equal(128.5, a.Quantity);
        Assert.Equal("㎡", a.Unit);
        Assert.Equal(873800L, a.CalculatedAmount);
        Assert.Equal("OK", a.AmountCheck);
        Assert.Equal("", a.ReviewRequired);

        var b = items[1];
        Assert.Equal("ヶ所", b.Unit);
        Assert.Equal(500L, b.Difference);
        Assert.Equal("要確認", b.AmountCheck);
        Assert.Equal("数量×単価と金額が不一致 / 備考OCRに誤認識候補", b.ReviewReason);

        var c = items[2];
        Assert.Equal(("参考明細", "既存撤去", "養生", 7), (c.RecordType, c.Trade, c.ItemName, c.OcrRowIndex));
        Assert.Equal("対象外", c.AmountCheck);
        Assert.Equal("単価・金額が未記載の参考明細（集計対象外）", c.ReviewReason);

        Assert.All(items.Skip(3), x =>
        {
            Assert.Equal("近似重複候補", x.DuplicateCandidate);
            Assert.Equal("ページ継続時のセル欠落・重複を確認", x.ReviewReason);
        });

        Assert.Equal(new[] { "見出し", "見出し", "採用", "採用", "見出し", "採用", "除外", "空行", "除外", "採用", "除外", "採用" },
            r.AuditRows.Select(x => x.RowStatus));

        var s = r.Summary;
        Assert.Equal((5, 4, 1, 3, 2), (s.DetailCount, s.MoneyDetailCount, s.ReferenceDetailCount, s.AmountCheckOkCount, s.DuplicateCandidateCount));
        Assert.Equal(1432800.0, s.HeaderEstimateAmount);
        Assert.Equal(1884300.0, s.DetailAmountTotal);
        Assert.Equal(-451500L, s.DifferenceToHeaderAmount);
        Assert.Equal((12, 3), (s.AuditRowCount, s.AuditExcludedCount));
    }

    [Fact]
    public void Structure_明細表が無ければ説明付きのエラー()
    {
        var layout = new JsonObject { ["pages"] = new JsonArray(), ["tables"] = new JsonArray() };
        var e = Assert.Throws<InvalidOperationException>(() => EstimateStructurer.Structure(layout, Rules(), "x", "c", null));
        Assert.Contains("明細表を特定できませんでした", e.Message);
    }

    // ── 分割 OCR の統合 ──

    [Fact]
    public void Merge_ページ番号と位置を全体の値に直す()
    {
        static JsonObject Page(int n, int offset, int length) =>
            new() { ["pageNumber"] = n, ["spans"] = new JsonArray(new JsonObject { ["offset"] = offset, ["length"] = length }) };
        var c1 = new JsonObject { ["apiVersion"] = "2024-11-30", ["content"] = "ab", ["pages"] = new JsonArray(Page(1, 0, 1), Page(2, 1, 1)), ["tables"] = new JsonArray() };
        var c2 = new JsonObject
        {
            ["apiVersion"] = "2024-11-30", ["content"] = "cde", ["pages"] = new JsonArray(Page(1, 0, 3)),
            ["tables"] = new JsonArray(new JsonObject { ["rowCount"] = 1, ["columnCount"] = 1, ["cells"] = new JsonArray(), ["boundingRegions"] = new JsonArray(new JsonObject { ["pageNumber"] = 1 }) }),
        };
        var m = LayoutMerger.Merge(new[] { new LayoutMerger.ChunkResult(1, 1, 2, c1), new LayoutMerger.ChunkResult(2, 3, 3, c2) }, "a.pdf", 3, 2);

        Assert.Equal("ab\ncde", (string?)m["content"]);
        Assert.Equal(new[] { 1, 2, 3 }, m["pages"]!.AsArray().Select(p => (int)p!["pageNumber"]!));
        Assert.Equal(new[] { 0, 1, 3 }, m["pages"]!.AsArray().Select(p => (int)p!["spans"]![0]!["offset"]!));
        Assert.Equal(3, (int)m["tables"]![0]!["boundingRegions"]![0]!["pageNumber"]!);
        Assert.Equal(3, (int)m["batch_info"]!["ocr_page_count"]!);
        Assert.Equal("2024-11-30", (string?)m["apiVersion"]);
    }

    // ── 利用枚数 ──

    [Theory]
    [InlineData(300, "safe")]
    [InlineData(301, "caution")]
    [InlineData(400, "caution")]
    [InlineData(401, "danger")]
    [InlineData(500, "over")]
    public void LevelOf_300超で黄_400超で赤_500で上限(int used, string level) =>
        Assert.Equal(level, OcrUsageCounter.LevelOf(used, new Models.OcrSettings()));

    [Fact]
    public void Counter_日ごとに数えて保存し日付が変われば0から()
    {
        var path = Path.Combine(Path.GetTempPath(), $"ocr_usage_test_{Guid.NewGuid():N}.json");
        try
        {
            var now = new DateTime(2026, 10, 9, 10, 0, 0);
            var settings = new Models.OcrSettings();
            var counter = new OcrUsageCounter(path, () => now);
            counter.AddPages(2);
            counter.AddPages(2);
            counter.AddFile();

            var again = new OcrUsageCounter(path, () => now);   // アプリを開き直しても残る
            var today = again.Snapshot(settings);
            Assert.Equal((4, 1, 496), (today.Used, today.Files, today.Remaining));

            now = now.AddDays(1);
            Assert.Equal(0, again.Snapshot(settings).Used);
            settings.Period = "month";
            Assert.Equal(4, again.Snapshot(settings).Used);   // 月で数えれば前日分も入る
        }
        finally
        {
            File.Delete(path);
        }
    }
}
