// このファイルの目的:
//   製品カードの中身を、どのタブでも同じ「見出し／値／補足」の3列へ落とします。
//
// なぜ形を揃えるのか:
//   タブごとに違う形を渡すと、XAML側でテンプレートを出し分ける仕組みが必要になり、
//   画面の定義が読めなくなります。形を1つにすればテンプレートは1つで済み、
//   表示内容そのものをRevit無しでテストできます。
//
// タブの数:
//   表示グループの数だけ作り、そこへ「情報源」「関連資料」を足します。
//   固定の4つにすると、Display_Groupsに行を足しても画面へ出ません。
//
// 修正時の影響:
//   ここを変えると概要タブの見え方が変わります。XAMLは3列を前提にしています。

using System.Globalization;
using Bui.Products;

namespace Bui.UI;

/// <summary>カードの1行です。どのタブでも同じ形です。</summary>
public sealed record ProductCardRow(string Label, string Value, string Note, string Url);

/// <summary>カードのタブ1枚です。</summary>
public sealed record ProductCardTab(string Header, IReadOnlyList<ProductCardRow> Rows, string EmptyMessage);

/// <summary>製品カードの内容をタブの一覧へ組み立てます。</summary>
public static class ProductCardTabBuilder
{
    private const string SourcesHeader = "情報源";
    private const string RelatedHeader = "関連資料";

    public static IReadOnlyList<ProductCardTab> Build(ProductDetail? detail)
    {
        if (detail is null) return Array.Empty<ProductCardTab>();

        var tabs = new List<ProductCardTab>();
        foreach (var group in detail.Groups)
        {
            tabs.Add(new ProductCardTab(
                group.GroupName,
                group.Lines.Select(line => ToRow(line)).ToArray(),
                "この分類の情報は登録されていません。"));
        }

        tabs.Add(new ProductCardTab(
            SourcesHeader,
            detail.Sources.Select(source => ToRow(source)).ToArray(),
            "この製品の属性に情報源が登録されていません。"));

        tabs.Add(new ProductCardTab(
            RelatedHeader,
            detail.Related.Select(resource => ToRow(resource)).ToArray(),
            "関連資料は登録されていません。"));

        return tabs;
    }

    /// <summary>
    /// 値が無いことを空欄で表すと、読み手には「まだ調べていない」のか
    /// 「画面が壊れている」のか区別できません。必ずこの記号を置きます。
    /// </summary>
    private const string NoValue = "－";

    private static ProductCardRow ToRow(SpecLine line)
    {
        var value = line.Value.Length == 0
            ? NoValue
            : line.Unit.Length > 0 ? $"{line.Value} {line.Unit}" : line.Value;
        var notes = new List<string>();
        if (line.ReliabilityRank.Length > 0) notes.Add($"信頼度 {line.ReliabilityRank}");
        if (line.SourceName.Length > 0) notes.Add(line.SourceName);
        return new ProductCardRow(line.DisplayName, value, string.Join(" / ", notes), string.Empty);
    }

    private static ProductCardRow ToRow(SourceReference source)
    {
        var parts = new List<string>();
        if (source.Publisher.Length > 0) parts.Add(source.Publisher);
        // 発行日が無い資料もあります。既定値の0001年を表示すると誤解を招くため出しません。
        if (source.PublishedOn is { } published)
        {
            parts.Add(published.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        }

        var notes = new List<string>();
        if (source.SourceType.Length > 0) notes.Add(source.SourceType);
        if (source.ReliabilityRank.Length > 0) notes.Add($"信頼度 {source.ReliabilityRank}");

        var value = parts.Count > 0 ? string.Join(" / ", parts) : NoValue;
        return new ProductCardRow(source.SourceName, value, string.Join(" / ", notes), source.Url);
    }

    private static ProductCardRow ToRow(RelatedResource resource)
    {
        // 関連資料は「何であるか」が値、リンク先やファイルの場所が補足です。
        var value = resource.Kind.Length > 0 ? resource.Kind : NoValue;
        var note = resource.Url.Length > 0 ? resource.Url : resource.LocalImagePath;
        return new ProductCardRow(resource.Title, value, note, resource.Url);
    }
}
