// このファイルの目的:
//   概要タブの製品カードに出す内容を、画面から独立した不変の値として表します。
//   画面はこの形だけを見るため、CSVからSQLへ実装を差し替えても変わりません。
//
// なぜProductRecordと分けるのか:
//   ProductRecordは「1行＝1製品」の平坦な入れ物で、絞り込みと採番のための形です。
//   カードは表示グループ・信頼度・情報源という構造を必要とします。
//   同じ型に両方を詰めると、絞り込み側がカード用の項目を無視する作りになり、
//   どちらの用途にも中途半端になります。
//
// 修正時の影響:
//   項目を増やすと、詳細取得の実装と概要タブのXAMLの両方に反映が要ります。

namespace Bui.Products;

/// <summary>概要タブの製品カード1枚分の内容です。</summary>
public sealed record ProductDetail(
    string ProductId,
    string ProductName,
    string ManufacturerName,
    string CategoryName,
    /// <summary>ローカルの画像ファイルの絶対パス。無ければ空文字。外部URLは入りません。</summary>
    string ImagePath,
    string ProductUrl,
    IReadOnlyList<SpecGroup> Groups,
    IReadOnlyList<SourceReference> Sources,
    IReadOnlyList<RelatedResource> Related);

/// <summary>カードのタブ1枚に相当する属性のまとまりです。</summary>
public sealed record SpecGroup(string GroupId, string GroupName, IReadOnlyList<SpecLine> Lines);

/// <summary>属性1行です。値と単位は表示できる形に整形済みです。</summary>
public sealed record SpecLine(
    string DisplayName, string Value, string Unit, string ReliabilityRank, string SourceName);

/// <summary>この製品の属性が根拠にしている資料1件です。</summary>
public sealed record SourceReference(
    string SourceName, string SourceType, string Publisher,
    DateTimeOffset? PublishedOn, string Url, string ReliabilityRank);

/// <summary>関連資料タブに並べる1件です。</summary>
public sealed record RelatedResource(string Title, string Kind, string LocalImagePath, string Url);
