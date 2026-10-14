namespace SimilaritySearchApp.Tests;

public class DbValueConverterTests
{
    private static DbColumnInfo Col(string type, bool nullable = true, int? max = null) =>
        new("C", type, max, 18, 4, nullable, false, false, false, true, "c");

    [Theory]
    [InlineData("int", "1,234", 1234)]
    [InlineData("int", "１２３", 123)]          // 全角数字
    public void Parse_int(string type, string text, int expected) =>
        Assert.Equal(expected, DbValueConverter.Parse(text, Col(type)));

    [Fact]
    public void Parse_decimal_and_date()
    {
        Assert.Equal(1500.25m, DbValueConverter.Parse("¥1,500.25", Col("decimal")));
        Assert.Equal(new DateTime(2024, 4, 1), DbValueConverter.Parse("2024/4/1", Col("date")));
    }

    [Fact]
    public void Empty_is_null_for_nullable_and_error_for_required_number()
    {
        Assert.Equal(DBNull.Value, DbValueConverter.Parse("", Col("int")));
        Assert.Throws<FormatException>(() => DbValueConverter.Parse("", Col("int", nullable: false)));
        Assert.Equal("", DbValueConverter.Parse("", Col("nvarchar", nullable: false)));
    }

    [Fact]
    public void Text_length_is_checked() =>
        Assert.Throws<FormatException>(() => DbValueConverter.Parse("あいうえお", Col("nvarchar", max: 3)));

    [Fact]
    public void Bad_number_has_japanese_message()
    {
        var e = Assert.Throws<FormatException>(() => DbValueConverter.Parse("abc", Col("int")));
        Assert.Contains("入力できません", e.Message);
    }

    [Fact]
    public void Format_round_trips()
    {
        var dt = new DateTime(2024, 4, 1, 10, 20, 30, 500);
        Assert.Equal(dt, DbValueConverter.Parse(DbValueConverter.Format(dt), Col("datetime2")));
        Assert.Equal("2024-04-01", DbValueConverter.Format(new DateTime(2024, 4, 1)));
        Assert.Equal(0.1, DbValueConverter.Parse(DbValueConverter.Format(0.1), Col("float")));
        Assert.Equal("100", DbValueConverter.Format(100.0000m));
        Assert.Null(DbValueConverter.Format(DBNull.Value));
    }
}

public class RelationInferenceServiceTests
{
    private static DbTableInfo T(string name) =>
        new("dbo", name, RelationInferenceService.BaseName(name), RelationInferenceService.JapaneseLabel(name), 0, null);

    private static readonly List<DbTableInfo> Tables = new()
    {
        T("Category_カテゴリマスタ"), T("MaterialMaster_建築材料マスタ"), T("Appraisal_Unit_Price_査定単価"),
        T("ProjectInformation_物件情報"), T("Index_指数マスタ"), T("Index_Allocation_指数割当マスタ"),
    };

    private static readonly List<ColumnRef> Columns = new()
    {
        new("dbo", "Category_カテゴリマスタ", "ID", true),
        new("dbo", "Category_カテゴリマスタ", "Work_Type_Code", false),
        new("dbo", "MaterialMaster_建築材料マスタ", "MaterialMaster_ID", true),
        new("dbo", "MaterialMaster_建築材料マスタ", "Work_Type_Code_工種コード", false),
        new("dbo", "MaterialMaster_建築材料マスタ", "Material_Name_名称", false),
        new("dbo", "Appraisal_Unit_Price_査定単価", "Project_id", false),
        new("dbo", "Appraisal_Unit_Price_査定単価", "Work_Type_Code", false),
        new("dbo", "ProjectInformation_物件情報", "Project_id", false),
    };

    [Theory]
    [InlineData("MaterialMaster_建築材料マスタ", "MaterialMaster", "建築材料マスタ")]
    [InlineData("Index_Allocation_指数割当マスタ", "Index_Allocation", "指数割当マスタ")]
    [InlineData("SupplierMaster", "SupplierMaster", "")]
    public void Names_split(string name, string baseName, string label)
    {
        Assert.Equal(baseName, RelationInferenceService.BaseName(name));
        Assert.Equal(label, RelationInferenceService.JapaneseLabel(name));
    }

    [Fact]
    public void Key_columns_are_normalized_and_filtered()
    {
        Assert.Equal("work_type_code", RelationInferenceService.KeyOf("Work_Type_Code_工種コード"));
        Assert.True(RelationInferenceService.IsJoinKey("work_type_code"));
        Assert.False(RelationInferenceService.IsJoinKey("id"));
        Assert.False(RelationInferenceService.IsJoinKey("material_name"));
    }

    [Fact]
    public void Material_master_links_to_category_as_parent_without_fk()
    {
        var hood = RelationInferenceService.Build(Tables[1], Tables, Columns, new List<ForeignKeyRef>());
        var n = Assert.Single(hood.Neighbors);
        Assert.Equal("Category_カテゴリマスタ", n.Table.Name);
        Assert.Equal(RelationInferenceService.Parent, n.Direction);
        Assert.False(n.IsForeignKey);
    }

    [Fact]
    public void Category_sees_children_and_fk_wins_over_inference()
    {
        var fks = new List<ForeignKeyRef>
        {
            new("dbo", "Appraisal_Unit_Price_査定単価", "Work_Type_Code", "dbo", "Category_カテゴリマスタ", "Work_Type_Code"),
        };
        var hood = RelationInferenceService.Build(Tables[0], Tables, Columns, fks);
        Assert.Equal(2, hood.Neighbors.Count);
        Assert.All(hood.Neighbors, n => Assert.Equal(RelationInferenceService.Child, n.Direction));
        Assert.True(hood.Neighbors.Single(n => n.Table.BaseName == "Appraisal_Unit_Price").IsForeignKey);
    }

    [Fact]
    public void Cost_table_links_to_project_and_category()
    {
        var hood = RelationInferenceService.Build(Tables[2], Tables, Columns, new List<ForeignKeyRef>());
        Assert.Equal(new[] { "Category", "ProjectInformation" }, hood.Neighbors.Select(n => n.Table.BaseName).OrderBy(x => x));
        Assert.All(hood.Neighbors, n => Assert.Equal(RelationInferenceService.Parent, n.Direction));
    }
}
