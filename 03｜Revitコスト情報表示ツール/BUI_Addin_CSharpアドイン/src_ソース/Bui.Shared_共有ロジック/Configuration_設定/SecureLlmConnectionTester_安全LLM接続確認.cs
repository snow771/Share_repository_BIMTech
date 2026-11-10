// このファイルは設定中のURLをTask 5の安全factoryだけで接続確認します。
// LlmProviderFactoryとDNS解決に依存し、この経路を迂回する変更はクラウド誤接続防止に影響します。
using System.Net;
using Bui.Common;
using Bui.Llm;

namespace Bui.Configuration;

public sealed class SecureLlmConnectionTester : ILlmConnectionTester
{
    private readonly Func<string, CancellationToken, Task<IPAddress[]>> _dnsResolver;
    private readonly Func<string?> _apiKeyProvider;

    public SecureLlmConnectionTester(Func<string, CancellationToken, Task<IPAddress[]>>? dnsResolver = null,
        Func<string?>? apiKeyProvider = null)
    {
        _dnsResolver = dnsResolver ?? ((host, token) => Dns.GetHostAddressesAsync(host, token));
        _apiKeyProvider = apiKeyProvider ?? (() => null);
    }

    public async Task<OperationResult<IReadOnlyList<LlmModelInfo>>> TestAsync(LlmSettings settings, CancellationToken cancellationToken = default)
    {
        if (!settings.Enabled)
            return OperationResult<IReadOnlyList<LlmModelInfo>>.Failure("LLM_DISABLED", "LLMはOFFです。ONにしてから接続を確認してください。", "LLM disabled.");
        if (!Uri.TryCreate(settings.BaseUrl, UriKind.Absolute, out var uri))
            return OperationResult<IReadOnlyList<LlmModelInfo>>.Failure("LLM_URL_INVALID", "LLM接続URLの形式が正しくありません。", "Invalid absolute URI.");

        var created = await CreateProviderAsync(settings, uri, cancellationToken).ConfigureAwait(false);
        if (!created.IsSuccess)
            return OperationResult<IReadOnlyList<LlmModelInfo>>.Failure(created.Error!.Code, created.Error.UserMessage, created.Error.TechnicalDetail);
        return await created.Value!.GetModelsAsync(cancellationToken).ConfigureAwait(false);
    }

    internal Task<OperationResult<ILlmProvider>> CreateProviderAsync(LlmSettings settings, Uri uri, CancellationToken token) =>
        settings.Provider.Trim().Equals("Ollama", StringComparison.OrdinalIgnoreCase)
            ? LlmProviderFactory.CreateOllamaAsync(uri, _dnsResolver, token)
            : IsOpenAiCompatible(settings.Provider)
                // 設定再読込後のキーを毎回取得し、起動時スナップショットへ固定しません。
                ? LlmProviderFactory.CreateOpenAiCompatibleAsync(uri, ResolveApiKey, _dnsResolver, token)
                : Task.FromResult(OperationResult<ILlmProvider>.Failure("LLM_PROVIDER_UNKNOWN", "選択されたLLMプロバイダーは利用できません。", "Unknown provider."));

    // 認証providerのnullは「現在はキーなし」であり、過去のsettingsへは回帰しません。
    internal string? ResolveApiKey() => _apiKeyProvider();

    private static bool IsOpenAiCompatible(string provider) =>
        provider.Trim().Equals("OpenAICompatible", StringComparison.OrdinalIgnoreCase) ||
        provider.Trim().Equals("OpenAI互換API", StringComparison.OrdinalIgnoreCase);
}
