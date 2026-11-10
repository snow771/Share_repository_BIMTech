// このファイルは3層のJSON設定を安全に読み、非秘密のPC個別設定だけを原子的に保存します。
// System.Text.JsonとOperationResultに依存し、保存方式の変更は既存設定の保護と秘密情報の境界に影響します。
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Bui.Common;

namespace Bui.Configuration;

public sealed class SettingsService : ISettingsEditorService
{
    private static readonly JsonSerializerOptions ReadOptions = new() { PropertyNameCaseInsensitive = true };
    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        // 初心者が設定ファイルを開いたとき、日本語パスや列名をそのまま読めるようにします。
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };
    private readonly string _defaultsPath;
    private readonly string _localPath;
    private readonly string _secretsPath;
    private readonly Func<string, string, Task> _commit;
    private string? _currentApiKey;

    public SettingsService(string defaultsPath, string localPath, string secretsPath, Func<string, string, Task>? commit = null)
    {
        _defaultsPath = Path.GetFullPath(defaultsPath);
        _localPath = Path.GetFullPath(localPath);
        _secretsPath = Path.GetFullPath(secretsPath);
        _commit = commit ?? CommitAsync;
    }

    public static SettingsService CreateForCurrentUser(string defaultsPath)
    {
        var root = BuiLocalPaths.Root;
        return new SettingsService(defaultsPath, Path.Combine(root, "Settings.Local_個別設定.json"), Path.Combine(root, "Secrets.Local_認証情報.json"));
    }

    public OperationResult<SettingsLoadResult> Load() => LoadAsync().GetAwaiter().GetResult();

    public async Task<OperationResult<SettingsLoadResult>> LoadAsync(CancellationToken cancellationToken = default)
    {
        BuiSettings defaults;
        try
        {
            defaults = await ReadRequiredSettingsAsync(_defaultsPath, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or JsonException or InvalidDataException)
        {
            return OperationResult<SettingsLoadResult>.Failure("SETTINGS_DEFAULT_INVALID",
                "BUIの出荷設定を読み込めません。再配置してください。", exception.GetType().Name);
        }

        var warnings = new List<string>();
        var local = await TryReadOptionalAsync<BuiSettings>(_localPath, "PC個別設定", warnings, cancellationToken).ConfigureAwait(false);
        if (local is not null && !IsComplete(local))
        {
            warnings.Add("PC個別設定の必須項目が不足しているため、出荷設定を使用しました。");
            local = null;
        }

        var secrets = await TryReadSecretsAsync(warnings, cancellationToken).ConfigureAwait(false);
        var merged = SettingsMerger.Merge(defaults, local, secrets);
        // 再読込で認証キーが更新された場合、既存のLLM providerも次の要求から現在値を取得できるよう原子的に公開します。
        Volatile.Write(ref _currentApiKey, merged.Settings.Llm.ApiKey);
        // 秘密値はSettingsServiceから外へBuiSettingsとして返さず、専用provider経由に限定します。
        var publicSettings = merged.Settings with { Llm = merged.Settings.Llm with { ApiKey = null } };
        return OperationResult<SettingsLoadResult>.Success(new SettingsLoadResult(publicSettings, warnings.Concat(merged.Warnings).ToArray()));
    }

    internal string? GetCurrentApiKey() => Volatile.Read(ref _currentApiKey);

    public async Task<OperationResult<bool>> SaveLocalAsync(BuiSettings settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var directory = Path.GetDirectoryName(_localPath)!;
        var tempPath = Path.Combine(directory, $".{Path.GetFileName(_localPath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            Directory.CreateDirectory(directory);
            // API keyをnullに固定し、シリアライズ前から秘密値を保存対象外にします。
            // パスは保存時点で整え、引用符付きの値をファイルへ残さないようにします。
            var safe = new
            {
                data = CsvPathNormalizer.Normalize(settings.Data),
                llm = new
                {
                    settings.Llm.Enabled,
                    settings.Llm.Provider,
                    settings.Llm.BaseUrl,
                    settings.Llm.Model,
                    settings.Llm.TimeoutSeconds
                }
            };
            var json = JsonSerializer.Serialize(safe, WriteOptions);
            await File.WriteAllTextAsync(tempPath, json, new UTF8Encoding(false), cancellationToken).ConfigureAwait(false);
            await _commit(tempPath, _localPath).ConfigureAwait(false);
            return OperationResult<bool>.Success(true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return OperationResult<bool>.Failure("SETTINGS_SAVE_FAILED", "設定を保存できませんでした。元の設定は変更されていません。", exception.GetType().Name);
        }
        finally
        {
            try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { }
        }
    }

    private static async Task<BuiSettings> ReadRequiredSettingsAsync(string path, CancellationToken token)
    {
        var json = await File.ReadAllTextAsync(path, Encoding.UTF8, token).ConfigureAwait(false);
        var value = JsonSerializer.Deserialize<BuiSettings>(json, ReadOptions);
        if (value is null || !IsComplete(value)) throw new InvalidDataException("Required settings shape is invalid.");
        return value;
    }

    private static async Task<T?> TryReadOptionalAsync<T>(string path, string label, ICollection<string> warnings, CancellationToken token) where T : class
    {
        if (!File.Exists(path)) return null;
        try
        {
            var json = await File.ReadAllTextAsync(path, Encoding.UTF8, token).ConfigureAwait(false);
            return JsonSerializer.Deserialize<T>(json, ReadOptions);
        }
        catch (Exception exception) when (exception is IOException or JsonException)
        {
            // 内容やパスを警告に含めず、秘密値が画面/ログへ出ることを避けます。
            warnings.Add($"{label}を読み込めなかったため、安全な設定で続行しました。");
            return null;
        }
    }

    private async Task<SecretSettings?> TryReadSecretsAsync(ICollection<string> warnings, CancellationToken token)
    {
        if (!File.Exists(_secretsPath)) return null;
        try
        {
            var json = await File.ReadAllTextAsync(_secretsPath, Encoding.UTF8, token).ConfigureAwait(false);
            // 完全なBuiSettingsではなく、API keyだけを許可するpartial DTOで読みます。
            var dto = JsonSerializer.Deserialize<SecretFileDto>(json, ReadOptions) ?? throw new JsonException();
            var hasIgnoredFields = dto.IgnoredRootFields?.Count > 0 || dto.Llm?.IgnoredLlmFields?.Count > 0;
            return new SecretSettings(dto.Llm?.ApiKey, hasIgnoredFields);
        }
        catch (Exception exception) when (exception is IOException or JsonException or InvalidOperationException)
        {
            warnings.Add("認証情報ファイルを読み込めなかったため、認証情報なしで続行しました。");
            return null;
        }
    }

    private static bool IsComplete(BuiSettings value) => value.Data is not null && value.Llm is not null &&
        value.Data.ProductColumns is not null && value.Data.PriceColumns is not null &&
        value.Data.Provider is not null && value.Llm.Provider is not null && value.Llm.BaseUrl is not null && value.Llm.Model is not null;

    private static Task CommitAsync(string tempPath, string targetPath)
    {
        // 同じフォルダー内のmoveにし、書込中の半端なJSONが正式ファイルにならないようにします。
        File.Move(tempPath, targetPath, true);
        return Task.CompletedTask;
    }
}

public static class DataProviderPolicy
{
    /// <summary>
    /// PoCで実装済みのデータ方式です。
    /// 旧設定の "Csv" はSettingsMergerが "GoldStandard" へ読み替えるため、ここには到達しません。
    /// </summary>
    public static OperationResult<string> Validate(string provider)
    {
        if (string.Equals(provider, "GoldStandard", StringComparison.OrdinalIgnoreCase))
            return OperationResult<string>.Success("GoldStandard");
        if (string.Equals(provider, "SqlServer", StringComparison.OrdinalIgnoreCase))
            return OperationResult<string>.Success("SqlServer");

        return OperationResult<string>.Failure("DATA_PROVIDER_NOT_IMPLEMENTED",
            $"データ方式「{provider}」はまだ利用できません。GoldStandard（CSV）かSqlServerを選択してください。",
            "Only GoldStandard and SqlServer are implemented.");
    }
}
