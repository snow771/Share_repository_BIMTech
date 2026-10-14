using System.Text;
using SimilaritySearchApp.Estimate;
using SimilaritySearchApp.Estimate.Agreement;
using SimilaritySearchApp.Estimate.Data;
using SimilaritySearchApp.Estimate.Import;
using SimilaritySearchApp.Estimate.Models;

namespace SimilaritySearchApp.Tests;

/// <summary>見積DB連携（取決め計算・構造化JSONの decimal 読取・列定義の検査・名称照合）のテスト。DB には接続しない。</summary>
public class EstimateDbTests
{
    // ── テスト用のデータ ──

    private static HeaderView Header(decimal? headerAmount = null, string? project = "J00001", int? supplier = 1) => new(
        1, 1, project, "物件", null, supplier, "000001", "業者", null, "工事名OCR", "業者OCR", null, null,
        headerAmount, null, null, null, false, null, null, null, null, "AAAAAAAAAAA=");

    private static int _id;

    private static LineView Line(decimal? unitPrice, decimal? amount, decimal? qty = 1, string type = "金額明細", bool? target = null,
        string? amountCheck = "OK", string? dup = null, string? reviewRequired = null, string? human = "未確認", decimal? correctedAmount = null) =>
        new(++_id, _id, type, 1, 1, _id, null, null, $"明細{_id}", null, null, qty, "m", unitPrice, amount, null, null, null,
            amountCheck, dup, reviewRequired, null, human, null, null, null, target, target == false ? "対象外" : null,
            null, null, null, correctedAmount, null, "AAAAAAAAAAA=");

    private static readonly EvidenceInfo Pdf = new(true, true, 6);

    private static AgreementInput Input(decimal? baseAmount, decimal? agreed, string source = EstimateDb.BaseFromInput, string? baseReason = "PDFで確認") => new()
    {
        HeaderId = 1, BaseSource = source, BaseAmount = baseAmount, BaseReason = baseReason, TaxBasis = EstimateDb.TaxExcluded,
        AgreedAmount = agreed, EvidenceDocumentId = 10, EvidencePage = 1, HeaderRowVersion = "AAAAAAAAAAA=",
    };

    // ── 取決め計算 ──

    [Fact]
    public void Calc_1000円を970円で取決め_掛率097_単価は10円単位で100は100_200は190()
    {
        var lines = new List<LineView> { Line(100, 400, 4), Line(200, 600, 3) };
        var p = AgreementCalculator.Compute(Input(1000, 970), Header(), lines, Pdf);

        Assert.True(p.CanConfirm, string.Join(" / ", p.Blocking));
        Assert.Equal(0.97m, p.Rate);
        Assert.Equal(100m, p.Lines[0].ConvertedUnitPrice);   // 97 → 10 円単位で 100
        Assert.Equal(190m, p.Lines[1].ConvertedUnitPrice);   // 194 → 190
        Assert.Equal(388m, p.Lines[0].ConvertedAmount);      // 金額は 1 円単位のまま（400 × 0.97）
        Assert.Equal(582m, p.Lines[1].ConvertedAmount);
        Assert.Equal(970m, p.ConvertedTotal);
        Assert.Equal(0m, p.Residual);
    }

    [Fact]
    public void Calc_端数の残差は配賦せず理由がなければ確定しない()
    {
        var lines = new List<LineView> { Line(100, 100), Line(100, 100), Line(100, 100) };
        var p = AgreementCalculator.Compute(Input(300, 200), Header(), lines, Pdf);
        Assert.Equal(0.666666666667m, p.Rate);
        Assert.All(p.Lines, l => Assert.Equal(67m, l.ConvertedAmount));   // 66.67 → 67（四捨五入）
        Assert.Equal(-1m, p.Residual);
        Assert.False(p.CanConfirm);
        Assert.Contains(p.Blocking, b => b.Contains("残差"));

        var withReason = Input(300, 200);
        withReason.ResidualReason = "端数";
        Assert.True(AgreementCalculator.Compute(withReason, Header(), lines, Pdf).CanConfirm);
    }

    [Fact]
    public void Calc_四捨五入は0から遠い側()
    {
        Assert.Equal(3m, AgreementCalculator.Round(2.5m, 0));
        Assert.Equal(-3m, AgreementCalculator.Round(-2.5m, 0));
        Assert.Equal(1.0001m, AgreementCalculator.Round(1.00005m, 4));
        Assert.Equal(1070m, AgreementCalculator.RoundUnitPrice(1071.9755m));
        Assert.Equal(1080m, AgreementCalculator.RoundUnitPrice(1075m));
        Assert.Equal(-1080m, AgreementCalculator.RoundUnitPrice(-1075m));
    }

    [Fact]
    public void Calc_取決め0円は確認がなければ確定しない()
    {
        var lines = new List<LineView> { Line(100, 100) };
        var p = AgreementCalculator.Compute(Input(100, 0), Header(), lines, Pdf);
        Assert.Contains(p.Blocking, b => b.Contains("0 円"));
        var zero = Input(100, 0);
        zero.ZeroConfirmed = true;
        var ok = AgreementCalculator.Compute(zero, Header(), lines, Pdf);
        Assert.True(ok.CanConfirm, string.Join(" / ", ok.Blocking));
        Assert.Equal(0m, ok.Rate);
        Assert.Equal(0m, ok.Lines[0].ConvertedAmount);   // 0 は欠損（null）にしない
    }

    [Fact]
    public void Calc_基準総額0や未入力は確定しない()
    {
        var lines = new List<LineView> { Line(100, 100) };
        Assert.Contains(AgreementCalculator.Compute(Input(0, 0), Header(), lines, Pdf).Blocking, b => b.Contains("基準総額は 0 より大きい"));
        Assert.Null(AgreementCalculator.Compute(Input(null, 100), Header(), lines, Pdf).Rate);
    }

    [Fact]
    public void Calc_負の値引き行も換算し_金額欠損の対象行は止める()
    {
        var discount = new List<LineView> { Line(1100, 1100), Line(null, -100, null) };
        var p = AgreementCalculator.Compute(Input(1000, 970), Header(), discount, Pdf);
        Assert.Equal(-97m, p.Lines[1].ConvertedAmount);
        Assert.Null(p.Lines[1].ConvertedUnitPrice);   // 単価が無い行は金額だけ換算
        Assert.Contains(p.Warnings, w => w.Contains("単価が無い"));

        var missing = new List<LineView> { Line(100, 100), Line(100, null) };
        Assert.Contains(AgreementCalculator.Compute(Input(100, 97), Header(), missing, Pdf).Blocking, b => b.Contains("金額が無い"));
    }

    [Fact]
    public void Calc_参考明細と対象外は換算しない()
    {
        var lines = new List<LineView> { Line(100, 1000, 10), Line(null, null, null, "参考明細"), Line(50, 50, 1, target: false) };
        var p = AgreementCalculator.Compute(Input(1000, 970), Header(), lines, Pdf);
        Assert.Equal(1, p.TargetCount);
        Assert.Null(p.Lines[1].ConvertedAmount);
        Assert.Null(p.Lines[2].ConvertedAmount);
        Assert.Equal(970m, p.ConvertedTotal);
    }

    [Fact]
    public void Calc_増額は理由が必要()
    {
        var lines = new List<LineView> { Line(100, 100) };
        var p = AgreementCalculator.Compute(Input(100, 110), Header(), lines, Pdf);
        Assert.Contains(p.Blocking, b => b.Contains("増額"));
        var i = Input(100, 110);
        i.IncreaseReason = "追加工事";
        Assert.True(AgreementCalculator.Compute(i, Header(), lines, Pdf).CanConfirm);
    }

    [Fact]
    public void Calc_要確認行は人の確認が必要_補正値を使う()
    {
        var pending = new List<LineView> { Line(100, 100, dup: "同一明細候補") };
        Assert.Contains(AgreementCalculator.Compute(Input(100, 97), Header(), pending, Pdf).Blocking, b => b.Contains("未確認"));

        var corrected = new List<LineView> { Line(100, 100, amountCheck: "要確認", human: "確認済", correctedAmount: 200) };
        var p = AgreementCalculator.Compute(Input(200, 194), Header(), corrected, Pdf);
        Assert.True(p.CanConfirm, string.Join(" / ", p.Blocking));
        Assert.Equal(194m, p.Lines[0].ConvertedAmount);
        Assert.True(p.Lines[0].Corrected);
    }

    [Fact]
    public void Calc_紐づけ_PDF_根拠ページ_税区分_基準の理由を確認する()
    {
        var lines = new List<LineView> { Line(100, 100) };
        var noLink = AgreementCalculator.Compute(Input(100, 97), Header(project: null, supplier: null), lines, Pdf);
        Assert.Contains(noLink.Blocking, b => b.Contains("物件"));
        Assert.Contains(noLink.Blocking, b => b.Contains("業者"));

        Assert.Contains(AgreementCalculator.Compute(Input(100, 97), Header(), lines, new EvidenceInfo(false, false, null)).Blocking, b => b.Contains("元PDF"));
        var page = Input(100, 97);
        page.EvidencePage = 9;
        Assert.Contains(AgreementCalculator.Compute(page, Header(), lines, Pdf).Blocking, b => b.Contains("ページ数"));
        var tax = Input(100, 97);
        tax.TaxBasis = null;
        Assert.Contains(AgreementCalculator.Compute(tax, Header(), lines, Pdf).Blocking, b => b.Contains("税区分"));

        // 表紙金額と明細合計が違えば、表紙金額を選んでも理由が必要
        var cover = Input(1000, 970, EstimateDb.BaseFromHeader, baseReason: null);
        Assert.Contains(AgreementCalculator.Compute(cover, Header(headerAmount: 1000), new List<LineView> { Line(100, 900, 9) }, Pdf).Blocking,
            b => b.Contains("理由"));
        // 表紙金額を選んだのに値が違えば止める
        Assert.Contains(AgreementCalculator.Compute(Input(999, 970, EstimateDb.BaseFromHeader), Header(headerAmount: 1000), lines, Pdf).Blocking,
            b => b.Contains("一致しません"));
    }

    // ── 諸経費（内訳合計＋諸経費＝総合計）──

    [Fact]
    public void Overhead_明細の諸経費を単価へ配分してから掛率をかける()
    {
        // 内訳 1,000 円（単価 100 ×6・200 ×2）＋ 諸経費 200 円 ＝ 1,200 円 を 1,140 円で取決め
        var a = Line(100, 600, 6);
        var b = Line(200, 400, 2);
        var oh = Line(200, 200, 1);
        var input = Input(1200, 1140);
        input.OverheadSource = AgreementCalculator.OverheadLines;
        input.OverheadLineIds = new List<int> { oh.LineId };

        var p = AgreementCalculator.Compute(input, Header(), new List<LineView> { a, b, oh }, Pdf);

        Assert.True(p.CanConfirm, string.Join(" / ", p.Blocking));
        Assert.Equal(1000m, p.ItemsTotal);
        Assert.Equal(200m, p.OverheadAmount);
        Assert.Equal(1.2m, p.OverheadRate);          // (1,000＋200) ÷ 1,000
        Assert.Equal(0.95m, p.Rate);                 // 1,140 ÷ 1,200
        Assert.Equal(120m, p.Lines[0].OverheadIncludedUnitPrice);
        Assert.Equal(110m, p.Lines[0].ConvertedUnitPrice);   // 100 × 1.2 × 0.95 = 114 → 10 円単位で 110
        Assert.Equal(230m, p.Lines[1].ConvertedUnitPrice);   // 200 × 1.2 × 0.95 = 228 → 230
        Assert.Equal(684m, p.Lines[0].ConvertedAmount);
        Assert.Equal(456m, p.Lines[1].ConvertedAmount);
        Assert.True(p.Lines[2].IsOverhead);
        Assert.False(p.Lines[2].IsTarget);
        Assert.Null(p.Lines[2].ConvertedAmount);
        Assert.Equal(1140m, p.ConvertedTotal);
        Assert.Equal(0m, p.Residual);
        Assert.Equal(AgreementCalculator.RuleV2, AgreementCalculator.RuleVersion(input));
    }

    [Fact]
    public void Overhead_手入力の諸経費は明細合計に加える()
    {
        var lines = new List<LineView> { Line(100, 600, 6), Line(200, 400, 2) };
        var input = Input(1200, 1140, EstimateDb.BaseFromDetail, baseReason: null);
        input.OverheadSource = AgreementCalculator.OverheadInput;
        input.OverheadAmount = 200;
        var p = AgreementCalculator.Compute(input, Header(), lines, Pdf);
        Assert.True(p.CanConfirm, string.Join(" / ", p.Blocking));
        Assert.Equal(110m, p.Lines[0].ConvertedUnitPrice);

        var missing = Input(1000, 950);
        missing.OverheadSource = AgreementCalculator.OverheadInput;
        Assert.Contains(AgreementCalculator.Compute(missing, Header(), lines, Pdf).Blocking, x => x.Contains("諸経費の金額"));
        var noLines = Input(1000, 950);
        noLines.OverheadSource = AgreementCalculator.OverheadLines;
        Assert.Contains(AgreementCalculator.Compute(noLines, Header(), lines, Pdf).Blocking, x => x.Contains("諸経費として扱う"));
    }

    [Fact]
    public void Overhead_諸経費なしは従来どおり_v1()
    {
        var input = Input(1000, 970);
        var p = AgreementCalculator.Compute(input, Header(), new List<LineView> { Line(100, 1000, 10) }, Pdf);
        Assert.Null(p.OverheadRate);
        Assert.Equal(100m, p.Lines[0].ConvertedUnitPrice);
        Assert.Equal(AgreementCalculator.RuleV1, AgreementCalculator.RuleVersion(input));
        Assert.Equal(new[] { 3, 12 }, AgreementCalculator.SplitIds(AgreementCalculator.JoinIds(new[] { 12, 3, 12 })));
    }

    [Fact]
    public void Notes_残差と増額の理由をメモに入れて取り出せる()
    {
        var stored = AgreementCalculator.ComposeNotes("メモ", "端数", "追加");
        Assert.Equal(("メモ", "端数", "追加"), AgreementCalculator.SplitNotes(stored));
        Assert.Null(AgreementCalculator.ComposeNotes(" ", null, ""));
    }

    // ── 構造化JSON ──

    [Fact]
    public void Json_数値は_decimal_で読み_桁がずれない()
    {
        var json = """
            {"metadata":{"supplier_name":"株式会社昇 工","project_name":"Mill押上","estimate_id":""},
             "summary":{"detail_count":2,"header_estimate_amount":1000000,"detail_amount_total":1082138.1,"difference_to_header_amount":null},
             "items":[{"record_type":"金額明細","pdf_page":1,"quantity":0.1,"unit_price":"1,050","amount":14700,"item_name":"目地"},
                      {"record_type":"参考明細","pdf_page":2,"quantity":null,"unit_price":null,"amount":null,"item_name":"見出し"}],
             "audit_rows":[{"pdf_page":1,"ocr_table_index":2,"ocr_row_index":4,"row_status":"採用","reason":"金額明細","row_text":"目地 | 14"}]}
            """.Replace("\"1,050\"", "\"1050\"");
        var p = StructuredJsonReader.Read(Encoding.UTF8.GetBytes(json));
        Assert.Equal("株式会社昇 工", p.Metadata.SupplierName);
        Assert.Equal(1082138.1m, p.Summary.DetailAmountTotal);
        Assert.Null(p.Summary.DifferenceToHeaderAmount);
        Assert.Equal(0.1m, p.Items[0].Quantity);
        Assert.Equal(1050m, p.Items[0].UnitPrice);   // 文字列の数値も読む
        Assert.Null(p.Items[1].Amount);
        Assert.Single(p.AuditRows);
    }

    [Fact]
    public void Json_数値でない値は行と項目を示して止める()
    {
        var json = """{"items":[{"record_type":"金額明細","amount":"千円"}]}""";
        var e = Assert.Throws<InvalidOperationException>(() => StructuredJsonReader.Read(Encoding.UTF8.GetBytes(json)));
        Assert.Contains("明細 1 行目", e.Message);
        Assert.Contains("amount", e.Message);
        Assert.Throws<InvalidOperationException>(() => StructuredJsonReader.Read(Encoding.UTF8.GetBytes("""{"metadata":{}}""")));
    }

    // ── 列定義の検査（丸めない・切り詰めない）──

    private static DbColumnInfo Col(string type, int? max = null, int precision = 0, int scale = 0, bool nullable = true) =>
        new("Col", type, max, precision, scale, nullable, false, false, false, true, "col");

    [Fact]
    public void Guard_旧型_decimal18_2_に入らない桁は止める()
    {
        Assert.Null(ColumnGuard.Check(Col("decimal", precision: 18, scale: 2), 1050.5m));
        Assert.Null(ColumnGuard.Check(Col("decimal", precision: 18, scale: 2), 1.50m));       // 末尾の 0 は桁に数えない
        Assert.Contains("小数 2 桁", ColumnGuard.Check(Col("decimal", precision: 18, scale: 2), 1.555m));
        Assert.Contains("整数部", ColumnGuard.Check(Col("decimal", precision: 5, scale: 2), 1234.5m));
        Assert.Null(ColumnGuard.Check(Col("decimal", precision: 19, scale: 4), 1.5555m));
        Assert.Null(ColumnGuard.Check(Col("decimal", precision: 18, scale: 2), -0.01m));
    }

    [Fact]
    public void Guard_文字数と必須を確かめる()
    {
        Assert.Null(ColumnGuard.Check(Col("nvarchar", max: 5), "あいうえお"));
        Assert.Contains("最大 5 文字", ColumnGuard.Check(Col("nvarchar", max: 5), "あいうえおか"));
        Assert.Null(ColumnGuard.Check(Col("nvarchar", max: -1), new string('a', 10000)));   // nvarchar(max)
        Assert.Contains("必須", ColumnGuard.Check(Col("nvarchar", max: 5, nullable: false), null));
        Assert.Contains("整数", ColumnGuard.Check(Col("int"), 1.5m));
    }

    // ── 名称照合・取込識別 ──

    [Fact]
    public void Names_正規化は全角半角と空白だけ_法人格は削らない()
    {
        Assert.Equal("株式会社昇工", NameMatcher.Normalize("株式会社昇 工"));
        Assert.Equal("ABC株式会社", NameMatcher.Normalize("ＡＢＣ　株式会社"));

        var suppliers = new[] { "中川インテリア株式会社", "株式会社昇工業", "株式会社八千代工業", "株式会社八千代工業" };
        var exact = NameMatcher.Rank(suppliers, s => new[] { s }, "中川インテリア株式会社", false).ToList();
        Assert.Equal("正規化一致", exact.Single().Match);

        var yachiyo = NameMatcher.Rank(suppliers, s => new[] { s }, "株式会社八千代工業", false).ToList();
        Assert.Equal(2, yachiyo.Count);   // 同名 2 社はどちらも候補（自動で 1 つにしない）

        var noboru = NameMatcher.Rank(suppliers, s => new[] { s }, "株式会社昇 工", false).ToList();
        Assert.DoesNotContain(noboru, x => x.Match == "正規化一致");
    }

    [Fact]
    public void Identity_同じ内容とルールなら同じキー()
    {
        var hash = DocumentRepository.Sha256(Encoding.UTF8.GetBytes("{}"));
        var a = EstimateImportService.IdentityKey(hash, "common:A|supplier:");
        Assert.Equal(64, a.Length);
        Assert.Equal(a, EstimateImportService.IdentityKey(hash, "common:A|supplier:"));
        Assert.NotEqual(a, EstimateImportService.IdentityKey(hash, "common:B|supplier:"));
    }
}
