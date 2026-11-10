// このファイルはURLのローカル/LAN検査に成功した場合だけLLMプロバイダーを生成するセキュリティゲートです。
// LocalEndpointPolicy、ピン止めHttpClient、各プロバイダーに依存し、生成順序を変えると未検証URLへの通信防止に影響します。
using System.Net;
using Bui.Common;

namespace Bui.Llm;

/// <summary>接続先検査を必須とするLLMプロバイダー生成窓口です。</summary>
public static class LlmProviderFactory
{
    public static async Task<OperationResult<ILlmProvider>> CreateOllamaAsync(
        Uri baseUri,
        Func<string, CancellationToken, Task<IPAddress[]>> dnsResolver,
        CancellationToken cancellationToken)
    {
        var validation = await LocalEndpointPolicy.ValidateAsync(baseUri, dnsResolver, cancellationToken)
            .ConfigureAwait(false);
        if (!validation.IsAllowed)
        {
            return Denied(validation.Message);
        }

        var httpClient = LlmSecureHttpTransport.Create(baseUri, validation.ValidatedAddresses);
        // 実行時の生成窓口でのみ安全な通信を組立て、検査済みIPと同じDNS境界を後続要求に引き継ぎます。
        return OperationResult<ILlmProvider>.Success(new OllamaProvider(httpClient, baseUri, dnsResolver));
    }

    public static async Task<OperationResult<ILlmProvider>> CreateOpenAiCompatibleAsync(
        Uri baseUri,
        Func<string?> apiKeyProvider,
        Func<string, CancellationToken, Task<IPAddress[]>> dnsResolver,
        CancellationToken cancellationToken)
    {
        var validation = await LocalEndpointPolicy.ValidateAsync(baseUri, dnsResolver, cancellationToken)
            .ConfigureAwait(false);
        if (!validation.IsAllowed)
        {
            return Denied(validation.Message);
        }

        var httpClient = LlmSecureHttpTransport.Create(baseUri, validation.ValidatedAddresses);
        return OperationResult<ILlmProvider>.Success(
            new OpenAiCompatibleProvider(httpClient, baseUri, apiKeyProvider, dnsResolver));
    }

    private static OperationResult<ILlmProvider> Denied(string message) =>
        OperationResult<ILlmProvider>.Failure("LLM_ENDPOINT_DENIED", message, "LLM endpoint policy denied provider creation.");
}
