// このファイルの目的:
//   DB内容更新画面・仮登録レビュー画面で共通の接続設定
//   （サーバー名・データベース名・旧新ID CSVのパス）を、
//   次回起動時に自動復元できるようJSONファイルへ保存・読込します。
//   CurationQueueStore（queue.json）と同じ、単純なSystem.Text.Json方式です。

using System.Text.Json;

namespace Bui.Curation;

/// <summary>画面上部の接続設定です。</summary>
public sealed class CurationUiSettings
{
    public string SqlServerName { get; set; } = string.Empty;
    public string SqlDatabaseName { get; set; } = string.Empty;
    public string IdAliasCsvPath { get; set; } = string.Empty;
}

/// <summary>接続設定(ui_settings.json)の読み書きです。</summary>
public static class CurationUiSettingsStore
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    /// <summary>ファイルが無い・壊れている場合は既定値を返します（例外を投げません）。</summary>
    public static CurationUiSettings Load(string filePath)
    {
        if (!File.Exists(filePath)) return new CurationUiSettings();

        try
        {
            var json = File.ReadAllText(filePath);
            return JsonSerializer.Deserialize<CurationUiSettings>(json, Options) ?? new CurationUiSettings();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return new CurationUiSettings();
        }
    }

    public static void Save(string filePath, CurationUiSettings settings)
    {
        var directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

        var json = JsonSerializer.Serialize(settings, Options);
        File.WriteAllText(filePath, json);
    }
}
