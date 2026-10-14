using System.IO;
using SimilaritySearchApp.Excel;
using SimilaritySearchApp.Stencil;

namespace SimilaritySearchApp.Tests;

public class StencilTests
{
    [Theory]
    [InlineData("C1", 1, 3)]
    [InlineData("$AT$2", 2, 46)]
    [InlineData("h34", 34, 8)]
    public void CellAddress_parses(string addr, int row, int col)
    {
        Assert.True(CellAddress.TryParse(addr, out var r, out var c));
        Assert.Equal(row, r);
        Assert.Equal(col, c);
    }

    [Fact]
    public void CellAddress_rejects_typo() => Assert.False(CellAddress.TryParse("PJ28x", out _, out _));

    [Theory]
    [InlineData("東京都渋谷区神宮前3丁目1-2", "東京都", "渋谷区", "神宮前3丁目")]
    [InlineData("神奈川県横浜市港北区日吉本町1-1", "神奈川県", "横浜市港北区", "日吉本町")]
    [InlineData("千葉県船橋市本町２－３", "千葉県", "船橋市", "本町")]
    public void SplitJapaneseAddress(string input, string pref, string city, string town) =>
        Assert.Equal((pref, city, town), ProjectInfoExtractor.SplitJapaneseAddress(input));

    [Fact]
    public void ParsePlanText_counts_plans_and_categories_including_1SLDK()
    {
        var c = ProjectInfoExtractor.ParsePlanText("1K-20戸、1LDK 5戸\n１SLDK-2戸、店舗-1、OR-3");
        Assert.Equal(20, c["1K"]);
        Assert.Equal(5, c["1LDK"]);
        Assert.Equal(2, c["1SLDK"]);   // Python 版では 0 になっていた
        Assert.Equal(1, c["店舗"]);
        Assert.Equal(3, c["OR"]);
    }

    [Fact]
    public void SafeInt_handles_text() => Assert.Equal(4, ProjectInfoExtractor.SafeInt("地上4階"));

    private static StencilMapping Mapping()
    {
        var path = Path.Combine(Path.GetTempPath(), $"mapping_test_{Guid.NewGuid():N}.yaml");
        File.WriteAllText(path, """
            実行予算表書:
              sheet: 実行予算表書
              fields:
                現場コード: C1
                物件名: C2
                現場住所: C3
                請負金額: C5
                施工床面積: C10
                階数_地上: C13
                間取: H1
            原価推移表:
              sheet: 原価推移表
              fields:
                形態: J10
            実行予算・引継書:
              sheet: 実行予算・引継書
              fields:
                杭工事: C35
            """);
        try
        {
            return StencilMapping.Load(path);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static Dictionary<string, SheetData> Book()
    {
        var main = new object?[13][];
        for (var i = 0; i < main.Length; i++) main[i] = new object?[8];
        main[0][2] = "J00123"; main[1][2] = "テストマンション"; main[2][2] = "東京都港区芝5丁目1";
        main[4][2] = 300000000d; main[9][2] = 1000d; main[12][2] = "7"; main[0][7] = "1K-10戸、2LDK-4戸";
        var trend = new object?[10][];
        for (var i = 0; i < trend.Length; i++) trend[i] = new object?[10];
        trend[9][9] = "共同住宅";
        var handover = new object?[35][];
        for (var i = 0; i < handover.Length; i++) handover[i] = new object?[3];
        handover[34][2] = "PHC杭";
        var est = new[]
        {
            new object?[] { null, null, null, "工種コード", "査定", "修正" },
            new object?[] { null, null, null, "06-01-10000", 1000d, 950d },
            new object?[] { null, null, null, "01-01-10000", 300d, 280d },
        };
        return new Dictionary<string, SheetData>
        {
            ["実行予算表書"] = new("実行予算表書", main.ToList()),
            ["原価推移表"] = new("原価推移表", trend.ToList()),
            ["実行予算・引継書"] = new("実行予算・引継書", handover.ToList()),
            ["積算予算"] = SheetData.FromRows("積算予算", est),
        };
    }

    [Fact]
    public void Extract_project_info_from_mapping()
    {
        var row = ProjectInfoExtractor.Extract(Book(), Mapping());
        Assert.Equal("J00123", row["project_id"]);
        Assert.Equal("港区", row["現場住所_市区町村"]);
        Assert.Equal(7, row["階数_地上"]);
        Assert.Equal(10, row["1K"]);
        Assert.Equal(4, row["2LDK"]);
        Assert.Equal("共同住宅", row["形態"]);
        Assert.Equal("PHC杭", row["杭工事"]);
        // 施工坪単価が無いので 請負金額 ÷ (施工床面積 ÷ 3.305785)
        Assert.Equal(300000000d / (1000d / 3.305785), (double)row["施工坪単価"]!, 3);
    }

    [Fact]
    public void Extract_fails_when_required_sheet_missing()
    {
        var book = Book();
        book.Remove("原価推移表");
        Assert.Throws<InvalidDataException>(() => ProjectInfoExtractor.Extract(book, Mapping()));
    }

    [Fact]
    public void Unit_prices_follow_template_and_default_to_zero()
    {
        var template = UnitPriceAggregator.ReadTemplate(SheetData.FromRows("単価テーブル",
            new object?[] { "A", "工種コード", "工種項目", "工事項目", "項目" },
            new object?[] { null, "06-01-10000", "内装工事", "内装工事", "ﾎﾞｰﾄﾞ" },
            new object?[] { null, "99-99-99999", "雑", "雑", "雑" },
            new object?[] { null, null, null, null, null },
            new object?[] { null, "無視される", null, null, null }));
        Assert.Equal(2, template.Count);

        var rows = UnitPriceAggregator.Extract(Book(), template, "J00123");
        Assert.Equal(1000d, rows[0].AssessPrice);
        Assert.Equal(950d, rows[0].RevisePrice);
        Assert.True(rows[0].FoundInSource);
        Assert.Equal(0d, rows[1].AssessPrice);
        Assert.False(rows[1].FoundInSource);
    }

    [Fact]
    public void Column_map_resolves_logical_names_without_false_prefix_hits()
    {
        DbColumnInfo C(string n) => new(n, "nvarchar", 50, 0, 0, true, false, false, false, true, n.ToLowerInvariant());
        var cols = new List<DbColumnInfo> { C("Formwork_ReinforcingBarsTruck"), C("Formwork_型枠"), C("Project_id_現場コード") };
        Assert.Equal("Formwork_型枠", StencilColumnMap.Resolve(cols, "Formwork")!.Name);
        Assert.Equal("Project_id_現場コード", StencilColumnMap.Resolve(cols, "Project_id")!.Name);
        Assert.Null(StencilColumnMap.Resolve(cols, "Project"));
    }
}
