// このファイルの目的:
//   仮登録レビューの作業用データ(queue.json)を読み書きします。
//   SQLite等を持ち込まず、BUI全体の「CSV/JSON中心」という方針に合わせます。
//
// なぜ二重追加を防ぐのか:
//   同じ現場フォルダを何度も取り込む運用を想定しています。
//   毎回全件追加すると、レビュー中の内容が上書きされて消えます。

using System.Text.Json;

namespace Bui.Curation;

/// <summary>作業用データ(queue.json)の読み書きです。</summary>
public static class CurationQueueStore
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    /// <summary>ファイルが無ければ空の一覧を返します（初回起動時に例外にしないため）。</summary>
    public static List<CurationQueueItem> Load(string filePath)
    {
        if (!File.Exists(filePath)) return new List<CurationQueueItem>();

        var json = File.ReadAllText(filePath);
        return JsonSerializer.Deserialize<List<CurationQueueItem>>(json, Options) ?? new List<CurationQueueItem>();
    }

    public static void Save(string filePath, IReadOnlyList<CurationQueueItem> items)
    {
        var directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

        var json = JsonSerializer.Serialize(items, Options);
        File.WriteAllText(filePath, json);
    }

    /// <summary>
    /// スキャン結果のうち、(出どころフォルダ+元の仮ID)がまだ一覧に無いものだけを追加します。
    /// 追加した件数を返します。
    /// </summary>
    public static int AddIfMissing(List<CurationQueueItem> items, IReadOnlyList<ScannedProvisionalRecord> scanned)
    {
        var existingKeys = items
            .Select(item => (item.SourceFolder, item.ProvisionalId))
            .ToHashSet();

        var added = 0;
        foreach (var record in scanned)
        {
            var key = (record.SourceFolder, record.ProvisionalId);
            if (existingKeys.Contains(key)) continue;

            items.Add(new CurationQueueItem
            {
                SourceFolder = record.SourceFolder,
                ProvisionalId = record.ProvisionalId,
                Values = new Dictionary<string, string>(record.Values),
                SpecRows = record.SpecRows.Select(specRow => new Dictionary<string, string>(specRow)).ToList()
            });
            existingKeys.Add(key);
            added++;
        }

        return added;
    }
}
