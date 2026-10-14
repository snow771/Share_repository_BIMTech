using System.Text.Json.Nodes;

namespace SimilaritySearchApp.Tests;

public class DbLookupTests
{
    private static DbColumnInfo Col(string name, string type) =>
        new(name, type, null, 0, 0, true, false, false, name.EndsWith("_ID"), true, name.ToLowerInvariant());

    private static readonly List<DbTableInfo> Tables = new()
    {
        new DbTableInfo("dbo", "MaterialMaster_建築材料マスタ", "MaterialMaster", "建築材料マスタ", 120, "標準単価"),
        new DbTableInfo("dbo", "ProjectInformation_物件情報", "ProjectInformation", "物件情報", 64, "物件情報"),
    };

    [Fact]
    public void KeywordVariants_全角カナと半角カナの両方で探す()
    {
        var v = DbLookupService.KeywordVariants("ボード");
        Assert.Contains("ボード", v);
        Assert.Contains("ﾎﾞｰﾄﾞ", v);
        Assert.Contains("ボード", DbLookupService.KeywordVariants("ﾎﾞｰﾄﾞ"));   // 半角で入力しても全角を探す
        Assert.Contains("PB", DbLookupService.KeywordVariants("ＰＢ"));
        Assert.Equal("ﾀｲﾙ", DbLookupService.ToHalfKana("タイル"));
        Assert.Equal("ﾊﾟﾈﾙ", DbLookupService.ToHalfKana("パネル"));
    }

    [Fact]
    public void ParsePlan_テーブルは実在するものだけ_キーワードは3つまで()
    {
        var plan = DbLookupService.ParsePlan(new JsonObject
        {
            ["table"] = "dbo.MaterialMaster_建築材料マスタ",
            ["keywords"] = new JsonArray("タイル", " ", "タイル", "床", "外装", "磁器"),
            ["search_columns"] = new JsonArray("Material_Name_名称"),
            ["show_columns"] = new JsonArray(),
            ["reason"] = "標準単価",
        }, Tables);
        Assert.Equal(("dbo", "MaterialMaster_建築材料マスタ"), (plan.Schema, plan.Table));
        Assert.Equal(new[] { "タイル", "床", "外装" }, plan.Keywords);

        Assert.Throws<InvalidOperationException>(() => DbLookupService.ParsePlan(new JsonObject { ["table"] = "dbo.Users" }, Tables));
    }

    [Fact]
    public void Columns_存在しない列は使わず_文字の列だけを探す()
    {
        var cols = new List<DbColumnInfo>
        {
            Col("MaterialMaster_ID", "int"), Col("Material_Name_名称", "nvarchar"), Col("Unit_単位", "nvarchar"),
            Col("Appraisal_Unit_Price_査定単価", "decimal"), Col("Photo", "varbinary"),
        };
        Assert.Equal(new[] { "Material_Name_名称" },
            DbLookupService.SearchColumns(cols, new[] { "Material_Name_名称", "DROP TABLE x" }).Select(c => c.Name));
        Assert.Equal(new[] { "Material_Name_名称", "Unit_単位" }, DbLookupService.SearchColumns(cols, Array.Empty<string>()).Select(c => c.Name));
        Assert.DoesNotContain("Photo", DbLookupService.ShowColumns(cols, Array.Empty<string>()).Select(c => c.Name));
        Assert.Equal(new[] { "Appraisal_Unit_Price_査定単価" },
            DbLookupService.ShowColumns(cols, new[] { "appraisal_unit_price_査定単価", "無い列" }).Select(c => c.Name));
    }

    [Fact]
    public void Catalog_変更履歴テーブルは除外できる()
    {
        var names = new TableNames();
        Assert.True(DbLookupService.IsExcluded(new DbTableInfo("dbo", names.ChangeRow, "ChangeRow", "変更明細", 0, null), names));
        var catalog = DbLookupService.BuildCatalog(Tables, new[] { new ColumnRef("dbo", "MaterialMaster_建築材料マスタ", "Material_Name_名称", false) });
        Assert.Contains("dbo.MaterialMaster_建築材料マスタ（建築材料マスタ・120行・アプリでの役割: 標準単価）列: Material_Name_名称", catalog);
    }
}
