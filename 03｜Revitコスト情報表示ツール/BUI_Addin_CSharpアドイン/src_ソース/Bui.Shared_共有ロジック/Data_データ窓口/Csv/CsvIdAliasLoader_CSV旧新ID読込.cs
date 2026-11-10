// このファイルは任意の旧新製品ID CSVを厳密に検証してIdAliasMapへ変換し、元CSVを書き換えずにID移行を可能にするために存在します。
// ヘッダー、重複、空値、循環、未設定時の扱いを変えると、過去IDの解決結果と設定診断に影響します。
using System.IO;
using Bui.Products;

namespace Bui.Data.Csv;

/// <summary>old_id,new_id のCSVを検証して旧新ID対応として読み込みます。</summary>
public sealed class CsvIdAliasLoader
{
    public DataSourceStatus LastStatus { get; private set; } =
        new(true, 0, null, Array.Empty<string>());

    public IdAliasMap Load(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            var warnings = new[] { "旧新ID CSVのパスが未設定です。IDは変更されません。" };
            LastStatus = new DataSourceStatus(true, 0, null, warnings);
            return new IdAliasMap(new Dictionary<string, string>());
        }

        try
        {
            var document = CsvDocumentCodec.Read(path);
            if (document.Headers.Count != 2
                || !string.Equals(document.Headers[0], "old_id", StringComparison.Ordinal)
                || !string.Equals(document.Headers[1], "new_id", StringComparison.Ordinal))
            {
                throw new InvalidDataException("旧新ID CSVのヘッダーは正確に old_id,new_id としてください。");
            }

            var aliases = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var row in document.Rows)
            {
                var oldId = row["old_id"].Trim();
                var newId = row["new_id"].Trim();
                if (oldId.Length == 0)
                {
                    throw new InvalidDataException("旧新ID CSVのold_idが空です。");
                }

                if (newId.Length == 0)
                {
                    throw new InvalidDataException($"旧新ID CSVの移行先が空です: {oldId}");
                }

                if (!aliases.TryAdd(oldId, newId))
                {
                    throw new InvalidDataException($"旧新ID CSVのold_idが重複しています: {oldId}");
                }
            }

            var map = new IdAliasMap(aliases);
            // 全出発点を解決しておき、利用時ではなく設定読込時に循環を報告します。
            foreach (var oldId in aliases.Keys)
            {
                _ = map.Resolve(oldId);
            }

            var timestamp = File.GetLastWriteTimeUtc(path);
            LastStatus = new DataSourceStatus(true, aliases.Count, timestamp, Array.Empty<string>());
            return map;
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            var message = ex.Message.Contains("循環", StringComparison.Ordinal)
                ? ex.Message
                : ex.Message;
            LastStatus = new DataSourceStatus(false, 0, File.Exists(path) ? File.GetLastWriteTimeUtc(path) : null, [message]);
            throw ex is InvalidDataException invalidData ? invalidData : new InvalidDataException(message, ex);
        }
    }
}
