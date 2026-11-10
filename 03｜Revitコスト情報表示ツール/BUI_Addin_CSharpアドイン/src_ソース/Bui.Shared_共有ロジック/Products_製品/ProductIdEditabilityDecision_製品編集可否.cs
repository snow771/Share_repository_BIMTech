// このファイルはProduct_IDの「永続的な書込み可否」を、Revit APIに依存しない入力から判定します。
// Revit側のDocument/Parameter/Worksharing状態に依存し、規則変更はプレビュー分類とID点検結果の両方に影響します。
namespace Bui.Products;

/// <summary>プレビュー用の書込み可否と、点検で読取専用問題として報告すべきかを分けて返します。</summary>
public sealed record ProductIdEditabilityDecision(
    bool CanWriteParameter,
    bool IsReadOnlyOrNotEditable)
{
    /// <summary>
    /// トランザクション外でも意味が変わらない読取専用状態だけを使います。
    /// パラメータ欠落は専用の点検項目で扱うため、読取専用問題とは重複させません。
    /// </summary>
    public static ProductIdEditabilityDecision Evaluate(
        bool isDocumentReadOnly,
        bool hasParameter,
        bool isParameterReadOnly,
        bool isWorkshared,
        bool isOwnedByOtherUser)
    {
        var ownershipBlocksEditing = isWorkshared && isOwnedByOtherUser;
        var isReadOnlyOrNotEditable = isDocumentReadOnly
            || (hasParameter && isParameterReadOnly)
            || ownershipBlocksEditing;

        return new ProductIdEditabilityDecision(
            CanWriteParameter: hasParameter && !isReadOnlyOrNotEditable,
            IsReadOnlyOrNotEditable: isReadOnlyOrNotEditable);
    }
}
