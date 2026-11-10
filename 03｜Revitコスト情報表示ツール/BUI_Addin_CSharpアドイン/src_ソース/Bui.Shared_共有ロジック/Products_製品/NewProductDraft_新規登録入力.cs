// このファイルは新規登録の入力値を保持します。
// 列名から値へのただの対応表ですが、必須チェックをここに置くことで
// 画面と登録処理の双方が同じ判断規則を使えます。
namespace Bui.Products;

/// <summary>新規登録の入力内容。キーはCSVの列名です。</summary>
public sealed record NewProductDraft(IReadOnlyDictionary<string, string> Values)
{
    /// <summary>値を取り出します。未入力の項目は空文字を返します。</summary>
    public string Read(string column) =>
        Values.TryGetValue(column, out var value) ? (value ?? string.Empty).Trim() : string.Empty;

    /// <summary>未入力の必須項目を、指定された順序で返します。</summary>
    public IReadOnlyList<string> IsMissingRequired(IReadOnlyList<string> requiredColumns)
    {
        ArgumentNullException.ThrowIfNull(requiredColumns);
        return requiredColumns.Where(column => Read(column).Length == 0).ToArray();
    }
}
