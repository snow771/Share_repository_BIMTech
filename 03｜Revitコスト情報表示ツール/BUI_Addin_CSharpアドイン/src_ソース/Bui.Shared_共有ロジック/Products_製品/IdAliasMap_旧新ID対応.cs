// このファイルは旧製品IDを最終的な新製品IDへ解決し、元CSVにある不変のProduct_IDを書き換えずに移行を扱うために存在します。
// 連鎖の追跡、循環検出、空の移行先の扱いを変えると、過去データと現行製品の照合結果に影響します。
using System.Collections.ObjectModel;
using System.IO;

namespace Bui.Products;

/// <summary>
/// 旧IDから新IDへの対応を安全にたどる機能を提供します。
/// </summary>
public sealed class IdAliasMap
{
    private readonly IReadOnlyDictionary<string, string> aliases;

    /// <summary>
    /// 指定した旧新ID対応のスナップショットを作成します。
    /// </summary>
    public IdAliasMap(IReadOnlyDictionary<string, string> aliases)
    {
        ArgumentNullException.ThrowIfNull(aliases);

        // 呼出元の辞書が後から変更されても移行規則が変わらないよう、読取専用の複製を保持します。
        this.aliases = new ReadOnlyDictionary<string, string>(
            new Dictionary<string, string>(aliases, StringComparer.Ordinal));
    }

    /// <summary>
    /// 読取専用の旧新ID対応そのものです。点検の移行候補表示など、解決前の対応表が必要な箇所で使います。
    /// </summary>
    public IReadOnlyDictionary<string, string> Aliases => aliases;

    /// <summary>
    /// 指定IDを対応表に従って最終IDまで解決します。
    /// </summary>
    public string Resolve(string id)
    {
        ArgumentNullException.ThrowIfNull(id);

        var currentId = id;
        var visited = new HashSet<string>(StringComparer.Ordinal);
        while (aliases.TryGetValue(currentId, out var targetId))
        {
            // 同じIDを再訪した場合は終点がないため、誤った移行規則を明示して停止します。
            if (!visited.Add(currentId))
            {
                throw new InvalidDataException($"製品ID対応に循環があります: {currentId}");
            }

            if (string.IsNullOrWhiteSpace(targetId))
            {
                throw new InvalidDataException($"製品ID対応の移行先が空です: {currentId}");
            }

            currentId = targetId;
        }

        return currentId;
    }
}
