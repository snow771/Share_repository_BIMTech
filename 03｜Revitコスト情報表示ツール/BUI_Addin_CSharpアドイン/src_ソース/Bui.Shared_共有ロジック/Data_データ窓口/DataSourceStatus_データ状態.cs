// このファイルはCSVなどのデータ源が利用可能か、何行あるか、どんな問題があるかを共通の値として伝えるために存在します。
// 状態項目の意味を変えると、診断表示とリポジトリ利用可否の判断に影響します。
namespace Bui.Data;

/// <summary>データ源の現在の読取状態を表します。</summary>
public sealed record DataSourceStatus(
    bool IsAvailable,
    int RowCount,
    DateTimeOffset? LastUpdatedAt,
    IReadOnlyList<string> Problems);
