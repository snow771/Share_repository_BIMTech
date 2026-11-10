// このファイルは編集可能なJSONファイルからアドイン情報を読み込み、利用者の設定をアドインで使える値に変換します。
// 文字コード、項目名の照合、例外の扱いを変えると、既存設定JSONと後続の表示・エラー処理に影響します。
using System.IO;
using System.Text;
using System.Text.Json;

namespace Bui.Metadata;

/// <summary>
/// アドイン情報のJSONファイルを読み込む機能を提供します。
/// </summary>
public static class MetadataLoader
{
    /// <summary>
    /// 指定されたJSONファイルをアドイン情報へ変換します。
    /// </summary>
    public static AddinMetadata Load(string path)
    {
        // UTF-8を明示し、利用者が編集したJSONをそのまま読み込みます。ここを変えると日本語を含む既存設定の読込み結果に影響します。
        var json = File.ReadAllText(path, Encoding.UTF8);
        // 小文字のJSON項目も受け入れます。照合規則や失敗時の例外を変える場合は、設定ファイルと呼出側のエラー処理への影響を確認します。
        return JsonSerializer.Deserialize<AddinMetadata>(json,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidDataException("アドイン情報を読み込めませんでした。");
    }
}
