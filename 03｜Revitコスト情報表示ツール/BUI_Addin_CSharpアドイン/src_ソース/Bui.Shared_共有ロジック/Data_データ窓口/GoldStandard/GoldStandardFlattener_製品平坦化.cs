// このファイルの目的:
//   正規化された複数の表を結合し、縦持ちの属性を横持ちへ畳んで、
//   既存のProductRecord（1行＝1製品）を作ります。
//
// なぜこの形にするのか:
//   絞り込み・採番・重複検出・新規登録はすべてProductRecordを前提にしています。
//   ここで形を合わせておけば、それらは一切変更せずに済みます。
//   SQL Serverへ移した際も、JOINして1行へ平坦化する形は同じです。
//
// 属性のキー:
//   Spec_Master.display_name_ja（厚さ、防火性能など）を使います。
//   設定のcandidateFilterColumnsが同じ名前を指すため、
//   ここをspec_keyに変えると絞り込みが全滅します。
//
// 修正時の影響:
//   参照切れ（マスタに無いID）でも行を捨てません。捨てると製品が黙って消えます。
//   IDをそのまま表示し、警告に積みます。
//
// 2つの危険な衝突（半日潰した実害あり。値を消さず、必ず警告に積むこと）:
//   1. 項目名の衝突: CsvColumnMapping.GoldStandardDefaultが定義する項目名
//      （製品名・メーカー名・型番・単位・更新日）とSpec_Master.display_name_jaが
//      同じ文字列になることがあります。ProductFieldAccessorはその項目名を
//      対応付け済みの列として先に読むため、AdditionalAttributes側の値は
//      画面のどこからも読まれません。値は消しませんが、この状態は警告に積みます。
//   2. 重複キーのlast-wins: 同じ(product_id, spec_key)が2行あるか、
//      別々のspec_keyが同じdisplay_name_jaを持つと、後に読んだ値が先の値を
//      黙って上書きします。上書き自体は変えませんが、どちらが勝ったか
//      分からなくなるため、こちらも警告に積みます。

using Bui.Products;
using System.Collections.ObjectModel;

namespace Bui.Data.GoldStandard;

/// <summary>表の結合と縦→横変換を行う純粋関数です。</summary>
internal static class GoldStandardFlattener
{
    /// <summary>建材分類を表す項目名です。設定のcandidateFilterColumnsと一致させます。</summary>
    public const string CategoryColumn = "建材分類";

    /// <summary>利用者向けメッセージに並べる警告の最大数です。多すぎると読めなくなります。</summary>
    private const int MaximumReportedProblems = 3;

    public static IReadOnlyList<ProductRecord> Flatten(GoldStandardTableSet set, out IReadOnlyList<string> problems)
    {
        ArgumentNullException.ThrowIfNull(set);

        var collected = new List<string>(set.Problems);
        var manufacturers = Lookup(set, GoldStandardTables.Manufacturers,
            GoldStandardTables.Columns.ManufacturerId, GoldStandardTables.Columns.ManufacturerName);
        var categories = Lookup(set, GoldStandardTables.MaterialCategories,
            GoldStandardTables.Columns.MaterialCategoryId, GoldStandardTables.Columns.CategoryNameJa);
        var specNames = Lookup(set, GoldStandardTables.SpecMaster,
            GoldStandardTables.Columns.SpecKey, GoldStandardTables.Columns.DisplayNameJa);
        var specsByProduct = GroupSpecs(set, specNames);

        var missingReferences = new SortedSet<string>(StringComparer.Ordinal);
        var reservedNameCollisions = new SortedSet<string>(StringComparer.Ordinal);
        var duplicateAttributeNames = new SortedSet<string>(StringComparer.Ordinal);
        var reservedNames = ReservedMappingNames();
        var skippedRows = 0;
        var records = new List<ProductRecord>();

        foreach (var row in set.Rows(GoldStandardTables.Products))
        {
            var productId = SpecValueFormatter.Clean(Value(row, GoldStandardTables.Columns.ProductId));
            if (productId.Length == 0)
            {
                skippedRows++;
                continue;
            }

            var attributes = new Dictionary<string, string>(StringComparer.Ordinal);
            AddAttribute(
                attributes, CategoryColumn,
                Resolve(Value(row, GoldStandardTables.Columns.MaterialCategoryId), categories, missingReferences),
                reservedNames, reservedNameCollisions, duplicateAttributeNames);

            if (specsByProduct.TryGetValue(productId, out var specs))
            {
                foreach (var (name, value) in specs)
                {
                    AddAttribute(attributes, name, value, reservedNames, reservedNameCollisions, duplicateAttributeNames);
                }
            }

            records.Add(new ProductRecord(
                productId,
                SpecValueFormatter.Clean(Value(row, GoldStandardTables.Columns.ProductName)),
                Resolve(Value(row, GoldStandardTables.Columns.ManufacturerId), manufacturers, missingReferences),
                string.Empty,   // 型番はProduct_Variantsにあるが現時点で0件のため常に空
                string.Empty,   // 単位はGoldStandardスキーマに存在しない
                null,           // 製品単位の更新日はスキーマに存在しない
                new ReadOnlyDictionary<string, string>(attributes)));
        }

        if (skippedRows > 0)
        {
            collected.Add($"{skippedRows}行を読み飛ばしました（{GoldStandardTables.Columns.ProductId}が空欄）。");
        }

        if (missingReferences.Count > 0)
        {
            var shown = missingReferences.Take(MaximumReportedProblems);
            collected.Add(
                $"マスタに無いIDが{missingReferences.Count}種類あります（{string.Join(" / ", shown)}）。IDをそのまま表示しています。");
        }

        if (reservedNameCollisions.Count > 0)
        {
            var shown = reservedNameCollisions.Take(MaximumReportedProblems);
            collected.Add(
                $"項目名が{reservedNameCollisions.Count}件、製品名・メーカー名・型番・単位・更新日と衝突しています" +
                $"（{string.Join(" / ", shown)}）。この項目の値は画面に表示されず、固定値に隠れています。");
        }

        if (duplicateAttributeNames.Count > 0)
        {
            var shown = duplicateAttributeNames.Take(MaximumReportedProblems);
            collected.Add(
                $"項目名が{duplicateAttributeNames.Count}件、同じ製品内で重複しています" +
                $"（{string.Join(" / ", shown)}）。後から読み込んだ値が優先され、先の値は消えています。");
        }

        problems = collected;
        return records;
    }

    /// <summary>
    /// 属性を1件書き込みます。項目名がCsvColumnMapping.GoldStandardDefaultの項目名と衝突する場合、
    /// またはこの製品内で既に同じ項目名が使われている場合は、値は書き込みつつ衝突を記録します。
    /// </summary>
    private static void AddAttribute(
        Dictionary<string, string> attributes, string name, string value,
        IReadOnlySet<string> reservedNames, ISet<string> reservedNameCollisions, ISet<string> duplicateAttributeNames)
    {
        if (reservedNames.Contains(name))
        {
            reservedNameCollisions.Add(name);
        }

        if (attributes.ContainsKey(name))
        {
            duplicateAttributeNames.Add(name);
        }

        attributes[name] = value;
    }

    /// <summary>
    /// ProductFieldAccessorがAdditionalAttributesより先に読む項目名の一覧です。
    /// ハードコードで重複させず、CsvColumnMapping.GoldStandardDefaultを直接参照します。
    /// </summary>
    private static IReadOnlySet<string> ReservedMappingNames()
    {
        var mapping = CsvColumnMapping.GoldStandardDefault;
        return new HashSet<string>(StringComparer.Ordinal)
        {
            mapping.ProductIdColumn,
            mapping.ProductNameColumn,
            mapping.ManufacturerColumn,
            mapping.ModelNumberColumn,
            mapping.UnitColumn,
            mapping.UpdatedAtColumn,
        };
    }

    /// <summary>製品IDごとに「表示名 → 値」の一覧を作ります。値が空の属性は項目を作りません。</summary>
    private static Dictionary<string, List<KeyValuePair<string, string>>> GroupSpecs(
        GoldStandardTableSet set, IReadOnlyDictionary<string, string> specNames)
    {
        var grouped = new Dictionary<string, List<KeyValuePair<string, string>>>(StringComparer.Ordinal);
        foreach (var row in set.Rows(GoldStandardTables.ProductSpecs))
        {
            var productId = SpecValueFormatter.Clean(Value(row, GoldStandardTables.Columns.ProductId));
            var specKey = SpecValueFormatter.Clean(Value(row, GoldStandardTables.Columns.SpecKey));
            if (productId.Length == 0 || specKey.Length == 0) continue;

            var value = SpecValueFormatter.SelectValue(
                Value(row, GoldStandardTables.Columns.ValueNumber),
                Value(row, GoldStandardTables.Columns.ValueString));
            // 値が無い属性は項目そのものを作りません。空欄の行を並べても読み手の役に立ちません。
            if (value.Length == 0) continue;

            // Spec_Masterに定義が無いキーは、キーそのものを項目名にします。データを黙って捨てません。
            var name = specNames.TryGetValue(specKey, out var displayName) && displayName.Length > 0
                ? displayName
                : specKey;

            if (!grouped.TryGetValue(productId, out var list))
            {
                list = new List<KeyValuePair<string, string>>();
                grouped[productId] = list;
            }

            list.Add(new KeyValuePair<string, string>(name, value));
        }

        return grouped;
    }

    private static Dictionary<string, string> Lookup(
        GoldStandardTableSet set, string table, string keyColumn, string valueColumn)
    {
        var lookup = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var row in set.Rows(table))
        {
            var key = SpecValueFormatter.Clean(Value(row, keyColumn));
            if (key.Length == 0) continue;
            lookup[key] = SpecValueFormatter.Clean(Value(row, valueColumn));
        }

        return lookup;
    }

    /// <summary>IDを名称へ解決します。解決できない場合はIDをそのまま返し、警告へ積みます。</summary>
    private static string Resolve(
        string? id, IReadOnlyDictionary<string, string> lookup, ISet<string> missingReferences)
    {
        var key = SpecValueFormatter.Clean(id);
        if (key.Length == 0) return string.Empty;
        if (lookup.TryGetValue(key, out var name) && name.Length > 0) return name;

        missingReferences.Add(key);
        return key;
    }

    private static string Value(IReadOnlyDictionary<string, string> row, string column) =>
        row.TryGetValue(column, out var value) ? value ?? string.Empty : string.Empty;
}
