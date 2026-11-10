// このファイルの目的:
//   管理者が手入力した正式Product_IDが、DB上の既存値およびキュー内の
//   他のドラフトの正式IDと重複しないことを確認します。
//   自動採番は行いません（決まった採番ルールが無いため）。

namespace Bui.Curation;

/// <summary>正式Product_IDの重複を確認します。</summary>
public static class OfficialIdValidator
{
    /// <summary>問題が無ければnull、問題があれば理由を返します。</summary>
    public static string? Validate(
        string? officialId, IReadOnlyCollection<string> existingDatabaseIds, IReadOnlyCollection<string> pendingQueueIds)
    {
        var trimmed = (officialId ?? string.Empty).Trim();
        if (trimmed.Length == 0)
        {
            return "正式なProduct_IDを入力してください。";
        }

        if (existingDatabaseIds.Any(id => string.Equals(id.Trim(), trimmed, StringComparison.OrdinalIgnoreCase)))
        {
            return $"正式ID「{trimmed}」はすでにデータベースに存在します。";
        }

        if (pendingQueueIds.Any(id => string.Equals(id.Trim(), trimmed, StringComparison.OrdinalIgnoreCase)))
        {
            return $"正式ID「{trimmed}」は、まだ登録していない他の仮登録データと重複しています。";
        }

        return null;
    }
}
