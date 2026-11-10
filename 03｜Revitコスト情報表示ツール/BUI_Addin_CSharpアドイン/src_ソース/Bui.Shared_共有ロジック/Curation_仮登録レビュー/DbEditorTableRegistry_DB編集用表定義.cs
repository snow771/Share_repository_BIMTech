// このファイルの目的:
//   GoldStandardの全11表について、主キー列と外部キー（列名＋参照先の表）を
//   静的に定義します。DB内容更新機能の「削除前に確認すべき子表の件数」や
//   「親表の実在確認」は、すべてこの定義から導出します。
//
// なぜ子表の一覧を別途持たないのか:
//   各表が「自分の外部キー（親への参照）」だけを持てば、「ある表の子表」は
//   全表を走査して逆引きすれば求まります。子表の一覧を別途手で書くと、
//   親側の定義を直したときに子側の書き忘れが起きるため、導出のみにします。

using Bui.Data.GoldStandard;

namespace Bui.Curation;

/// <summary>ある表の、他表への外部キー1本です。</summary>
public sealed record ForeignKeyReference(string Column, string TargetTable);

/// <summary>1つの表の、主キーと外部キー一式です。</summary>
public sealed record TableDefinition(
    string TableName, string PrimaryKeyColumn, IReadOnlyList<ForeignKeyReference> ForeignKeys);

/// <summary>GoldStandard全11表の、主キー・外部キーの定義一覧です。</summary>
public static class DbEditorTableRegistry
{
    public static IReadOnlyList<TableDefinition> Tables { get; } = new[]
    {
        new TableDefinition(GoldStandardTables.Manufacturers, GoldStandardTables.Columns.ManufacturerId,
            Array.Empty<ForeignKeyReference>()),

        new TableDefinition(GoldStandardTables.StatusMaster, GoldStandardTables.Columns.StatusId,
            Array.Empty<ForeignKeyReference>()),

        new TableDefinition(GoldStandardTables.ReliabilityRanks, GoldStandardTables.Columns.ReliabilityRankId,
            Array.Empty<ForeignKeyReference>()),

        new TableDefinition(GoldStandardTables.MaterialCategories, GoldStandardTables.Columns.MaterialCategoryId,
            new[]
            {
                new ForeignKeyReference(GoldStandardTables.Columns.ParentCategoryId, GoldStandardTables.MaterialCategories),
                new ForeignKeyReference(GoldStandardTables.Columns.StatusId, GoldStandardTables.StatusMaster),
            }),

        new TableDefinition(GoldStandardTables.Sources, GoldStandardTables.Columns.SourceId,
            new[]
            {
                new ForeignKeyReference(GoldStandardTables.Columns.ManufacturerId, GoldStandardTables.Manufacturers),
                new ForeignKeyReference(GoldStandardTables.Columns.ReliabilityRankId, GoldStandardTables.ReliabilityRanks),
                new ForeignKeyReference(GoldStandardTables.Columns.StatusId, GoldStandardTables.StatusMaster),
            }),

        new TableDefinition(GoldStandardTables.Products, GoldStandardTables.Columns.ProductId,
            new[]
            {
                new ForeignKeyReference(GoldStandardTables.Columns.ManufacturerId, GoldStandardTables.Manufacturers),
                new ForeignKeyReference(GoldStandardTables.Columns.MaterialCategoryId, GoldStandardTables.MaterialCategories),
                new ForeignKeyReference(GoldStandardTables.Columns.PrimarySourceId, GoldStandardTables.Sources),
                new ForeignKeyReference(GoldStandardTables.Columns.StatusId, GoldStandardTables.StatusMaster),
            }),

        new TableDefinition(GoldStandardTables.ProductVariants, GoldStandardTables.Columns.VariantId,
            new[]
            {
                new ForeignKeyReference(GoldStandardTables.Columns.ProductId, GoldStandardTables.Products),
                new ForeignKeyReference(GoldStandardTables.Columns.StatusId, GoldStandardTables.StatusMaster),
            }),

        new TableDefinition(GoldStandardTables.DisplayGroups, GoldStandardTables.Columns.DisplayGroupId,
            Array.Empty<ForeignKeyReference>()),

        new TableDefinition(GoldStandardTables.SpecMaster, GoldStandardTables.Columns.SpecKey,
            new[]
            {
                new ForeignKeyReference(GoldStandardTables.Columns.DisplayGroupId, GoldStandardTables.DisplayGroups),
                new ForeignKeyReference(GoldStandardTables.Columns.StatusId, GoldStandardTables.StatusMaster),
            }),

        new TableDefinition(GoldStandardTables.ProductSpecs, GoldStandardTables.Columns.GoldId,
            new[]
            {
                new ForeignKeyReference(GoldStandardTables.Columns.ProductId, GoldStandardTables.Products),
                new ForeignKeyReference(GoldStandardTables.Columns.VariantId, GoldStandardTables.ProductVariants),
                new ForeignKeyReference(GoldStandardTables.Columns.SpecKey, GoldStandardTables.SpecMaster),
                new ForeignKeyReference(GoldStandardTables.Columns.SourceId, GoldStandardTables.Sources),
                new ForeignKeyReference(GoldStandardTables.Columns.ReliabilityRankId, GoldStandardTables.ReliabilityRanks),
                new ForeignKeyReference(GoldStandardTables.Columns.StatusId, GoldStandardTables.StatusMaster),
            }),

        new TableDefinition(GoldStandardTables.ProductImages, GoldStandardTables.Columns.ImageId,
            new[]
            {
                new ForeignKeyReference(GoldStandardTables.Columns.ProductId, GoldStandardTables.Products),
                new ForeignKeyReference(GoldStandardTables.Columns.VariantId, GoldStandardTables.ProductVariants),
                new ForeignKeyReference(GoldStandardTables.Columns.SourceId, GoldStandardTables.Sources),
            }),
    };

    public static TableDefinition Get(string tableName)
    {
        var found = Tables.FirstOrDefault(table => string.Equals(table.TableName, tableName, StringComparison.Ordinal));
        if (found is null)
        {
            throw new InvalidOperationException($"未定義の表です: {tableName}");
        }
        return found;
    }

    /// <summary>tableNameの主キーを外部キーとして参照している、全表を返します。自己参照（Material_Categoriesなど）も含まれます。</summary>
    public static IReadOnlyList<(string ChildTable, string ForeignKeyColumn)> GetChildren(string tableName) =>
        Tables
            .SelectMany(table => table.ForeignKeys
                .Where(fk => string.Equals(fk.TargetTable, tableName, StringComparison.Ordinal))
                .Select(fk => (table.TableName, fk.Column)))
            .ToArray();
}
