// このファイルの目的:
//   仮ID→正式IDの対応を「旧新ID CSV」（old_id,new_id）へ追記します。
//   このファイルは既存のCsvIdAliasLoader（Bui本体側）が読み、
//   Revitモデルに書き込み済みの旧IDから新IDへの移行候補を示します。
//
// なぜ重複old_idを拒否するのか:
//   CsvIdAliasLoaderはold_idの重複を「壊れたCSV」として扱い、
//   ファイル全体の読み込みに失敗させます（意図的な安全策）。
//   ここで重複を作ると、Bui本体の起動時警告やチャットの移行候補機能
//   まで巻き込んで壊れます。

using System.Text;

namespace Bui.Curation;

/// <summary>旧新ID CSVへ1行追記します。</summary>
public static class OldNewIdAliasWriter
{
    private static readonly UTF8Encoding Encoding = new(true);

    /// <summary>成功すればnull、失敗すれば理由を返します。例外は投げません。</summary>
    public static string? Append(string filePath, string oldId, string newId)
    {
        var trimmedOldId = (oldId ?? string.Empty).Trim();
        var trimmedNewId = (newId ?? string.Empty).Trim();
        if (trimmedOldId.Length == 0) return "旧ID(仮ID)が空です。";
        if (trimmedNewId.Length == 0) return "新ID(正式ID)が空です。";

        try
        {
            if (!File.Exists(filePath))
            {
                var directory = Path.GetDirectoryName(filePath);
                if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
                File.WriteAllText(filePath, "old_id,new_id\r\n", Encoding);
            }

            var existingLines = File.ReadAllLines(filePath, Encoding);
            var alreadyExists = existingLines
                .Skip(1)
                .Select(line => line.Split(',').FirstOrDefault()?.Trim())
                .Any(existingOldId => string.Equals(existingOldId, trimmedOldId, StringComparison.OrdinalIgnoreCase));
            if (alreadyExists)
            {
                return $"旧新ID CSVには既に old_id「{trimmedOldId}」の行があります。追記しませんでした。";
            }

            File.AppendAllText(filePath, $"{EscapeCsvField(trimmedOldId)},{EscapeCsvField(trimmedNewId)}\r\n", Encoding);
            return null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return $"旧新ID CSVへの書き込みに失敗しました。{exception.Message}";
        }
    }

    // CsvDocumentCodec（Bui.Data.Csv、internal）が使うエスケープ規則と同じものを適用します。
    // old_id/new_idにカンマ・ダブルクォート・改行が含まれても、CsvIdAliasLoaderが
    // 使うTextFieldParserで正しく1列として読み戻せるようにするためです。
    private static string EscapeCsvField(string value)
    {
        return value.IndexOfAny([',', '"', '\r', '\n']) >= 0
            ? $"\"{value.Replace("\"", "\"\"")}\""
            : value;
    }
}
