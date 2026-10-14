using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SimilaritySearchApp.Models;

namespace SimilaritySearchApp.Settings;

/// <summary>
/// 設定の読み書き。保存先は %AppData%\SimilaritySearchApp\settings.json。
/// SQL 認証のパスワードは DPAPI（現在の Windows ユーザー）で暗号化して保存する。
/// </summary>
public sealed class SettingsStore
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public static string DefaultPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "SimilaritySearchApp", "settings.json");

    private readonly string _path;

    public SettingsStore(string? path = null) => _path = path ?? DefaultPath;

    /// <summary>設定を読む。ファイルが無い・壊れている場合は既定値。</summary>
    public AppSettings Load()
    {
        try
        {
            if (!File.Exists(_path)) return new AppSettings();
            return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(_path, Encoding.UTF8), Json) ?? new AppSettings();
        }
        catch (Exception)
        {
            return new AppSettings();
        }
    }

    public void Save(AppSettings settings)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        File.WriteAllText(_path, JsonSerializer.Serialize(settings, Json), Encoding.UTF8);
    }

    public static string Protect(string plain)
    {
        if (string.IsNullOrEmpty(plain)) return "";
        var bytes = ProtectedData.Protect(Encoding.UTF8.GetBytes(plain), null, DataProtectionScope.CurrentUser);
        return Convert.ToBase64String(bytes);
    }

    public static string Unprotect(string cipher)
    {
        if (string.IsNullOrEmpty(cipher)) return "";
        try
        {
            var bytes = ProtectedData.Unprotect(Convert.FromBase64String(cipher), null, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(bytes);
        }
        catch (Exception)
        {
            // 別ユーザー・別PCで暗号化された値は復号できない → 未設定扱い
            return "";
        }
    }
}
