// このファイルの目的:
//   GoldStandardスキーマの表名・ファイル名・列名を1か所に集めます。
//   文字列リテラルを各所に散らすと、列名の変更時に直し漏れが起きます。
//
// 修正時の影響:
//   ここの表名がそのままCSVのファイル名（<表名>.csv）になります。
//   SQL Serverへ移す際も、この名前がテーブル名に対応します。

namespace Bui.Data.GoldStandard;

/// <summary>GoldStandardスキーマの表名と列名です。</summary>
internal static class GoldStandardTables
{
    public const string Products = "Products";
    public const string Manufacturers = "Manufacturers";
    public const string MaterialCategories = "Material_Categories";
    public const string SpecMaster = "Spec_Master";
    public const string DisplayGroups = "Display_Groups";
    public const string ProductSpecs = "Product_Specs";
    public const string Sources = "Sources";
    public const string ReliabilityRanks = "Reliability_Ranks";
    public const string ProductVariants = "Product_Variants";
    public const string StatusMaster = "Status_Master";
    public const string ProductImages = "Product_Images";

    /// <summary>読み込みを試みる表の全体です。Productsだけが必須で、他は無くても動きます。</summary>
    public static IReadOnlyList<string> All { get; } = new[]
    {
        Products, Manufacturers, MaterialCategories, SpecMaster, DisplayGroups,
        ProductSpecs, Sources, ReliabilityRanks, ProductVariants, ProductImages
    };

    /// <summary>各表で参照する列名です。ER図（GoldStandard_ER_v1.dbml）と一致させます。</summary>
    public static class Columns
    {
        public const string ProductId = "product_id";
        public const string ProductName = "product_name";
        public const string ManufacturerId = "manufacturer_id";
        public const string ManufacturerName = "manufacturer_name";
        public const string MaterialCategoryId = "material_category_id";
        public const string CategoryNameJa = "category_name_ja";
        public const string PrimarySourceId = "primary_source_id";
        public const string ProductUrl = "product_url";
        public const string StatusId = "status_id";
        public const string Note = "note";
        public const string VariantId = "variant_id";
        public const string ParentCategoryId = "parent_category_id";

        public const string GoldId = "gold_id";
        public const string SpecKey = "spec_key";
        public const string DisplayNameJa = "display_name_ja";
        public const string DisplayGroupId = "display_group_id";
        public const string DisplayGroupNameJa = "display_group_name_ja";
        public const string DisplayOrder = "display_order";
        public const string DataType = "data_type";
        public const string StandardUnit = "standard_unit";

        public const string ValueType = "value_type";
        public const string ValueNumber = "value_number";
        public const string ValueString = "value_string";
        public const string Unit = "unit";
        public const string SourceId = "source_id";
        public const string ReliabilityRankId = "reliability_rank_id";

        public const string SourceName = "source_name";
        public const string SourceType = "source_type";
        public const string Publisher = "publisher";
        public const string Url = "url";
        public const string FileName = "file_name";
        public const string PublicationDate = "publication_date";

        public const string RankCode = "rank_code";
        public const string RankNameJa = "rank_name_ja";

        public const string ImageId = "image_id";
        public const string ImageUrl = "image_url";
        public const string IsPrimary = "is_primary";
    }
}
