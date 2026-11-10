// このファイルはローカル/LAN上のOllama APIをILlmProviderとして利用するために存在します。
// HttpClient、LocalEndpointPolicy、LLM共通契約に依存し、経路・JSON項目・失敗変換を変えるとOllamaサーバーと画面の互換性に影響します。
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using Bui.Common;

namespace Bui.Llm;

/// <summary>Ollamaのモデル一覧と非ストリームチャットを提供します。</summary>
public sealed class OllamaProvider : ILlmProvider
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly HttpClient httpClient;
    private readonly Uri serverRoot;
    private readonly Func<string, CancellationToken, Task<IPAddress[]>> dnsResolver;

    public OllamaProvider(HttpClient httpClient, Uri baseUri)
        : this(httpClient, baseUri, LocalEndpointPolicy.ResolveHostAddressesAsync)
    {
    }

    internal OllamaProvider(
        HttpClient httpClient,
        Uri baseUri,
        Func<string, CancellationToken, Task<IPAddress[]>> dnsResolver)
    {
        this.httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        serverRoot = LlmProviderSupport.NormalizeServerRoot(baseUri);
        this.dnsResolver = dnsResolver ?? throw new ArgumentNullException(nameof(dnsResolver));
    }

    public string ProviderId => "ollama";

    public async Task<OperationResult<IReadOnlyList<LlmModelInfo>>> GetModelsAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            using var requestCancellation = LlmProviderSupport.CreateRequestCancellation(
                LlmProviderSupport.ModelListTimeout,
                cancellationToken);
            var endpointError = await LlmProviderSupport.ValidateEndpointAsync(
                serverRoot,
                dnsResolver,
                requestCancellation.Token).ConfigureAwait(false);
            if (requestCancellation.IsCancellationRequested)
            {
                return LlmProviderSupport.CancellationFailure<IReadOnlyList<LlmModelInfo>>(cancellationToken);
            }

            if (endpointError is not null)
            {
                return LlmProviderSupport.EndpointFailure<IReadOnlyList<LlmModelInfo>>(endpointError);
            }

            using var request = new HttpRequestMessage(
                HttpMethod.Get,
                LlmProviderSupport.BuildRoute(serverRoot, "/api/tags"));
            using var response = await httpClient.SendAsync(request, requestCancellation.Token).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            await using var stream = await response.Content.ReadAsStreamAsync(requestCancellation.Token).ConfigureAwait(false);
            using var json = await JsonDocument.ParseAsync(stream, cancellationToken: requestCancellation.Token)
                .ConfigureAwait(false);

            var models = json.RootElement.GetProperty("models")
                .EnumerateArray()
                .Select(item => item.GetProperty("name").GetString() ?? string.Empty)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Select(name => new LlmModelInfo(name, name))
                .ToArray();
            return OperationResult<IReadOnlyList<LlmModelInfo>>.Success(models);
        }
        catch (OperationCanceledException)
        {
            return LlmProviderSupport.CancellationFailure<IReadOnlyList<LlmModelInfo>>(cancellationToken);
        }
        catch (Exception ex) when (LlmProviderSupport.IsResponseFormatException(ex))
        {
            return LlmProviderSupport.InvalidResponse<IReadOnlyList<LlmModelInfo>>(ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException)
        {
            return LlmProviderSupport.RequestFailure<IReadOnlyList<LlmModelInfo>>(ex);
        }
    }

    public async Task<OperationResult<LlmChatResponse>> ChatAsync(
        LlmChatRequest request,
        CancellationToken cancellationToken)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.Model) || request.Timeout <= TimeSpan.Zero)
        {
            return OperationResult<LlmChatResponse>.Failure(
                "LLM_REQUEST_INVALID",
                "LLMへの依頼内容またはタイムアウト設定が不正です。",
                "Chat request was null, model was blank, or timeout was not positive.");
        }

        try
        {
            using var requestCancellation = LlmProviderSupport.CreateRequestCancellation(request.Timeout, cancellationToken);
            var endpointError = await LlmProviderSupport.ValidateEndpointAsync(
                serverRoot,
                dnsResolver,
                requestCancellation.Token).ConfigureAwait(false);
            if (requestCancellation.IsCancellationRequested)
            {
                return LlmProviderSupport.CancellationFailure<LlmChatResponse>(cancellationToken);
            }

            if (endpointError is not null)
            {
                return LlmProviderSupport.EndpointFailure<LlmChatResponse>(endpointError);
            }

            var payload = new
            {
                model = request.Model,
                messages = request.Messages.Select(message => new { role = message.Role, content = message.Content }),
                stream = false
            };
            using var httpRequest = new HttpRequestMessage(
                HttpMethod.Post,
                LlmProviderSupport.BuildRoute(serverRoot, "/api/chat"))
            {
                Content = new StringContent(
                    JsonSerializer.Serialize(payload, JsonOptions),
                    Encoding.UTF8,
                    "application/json")
            };
            using var response = await httpClient.SendAsync(httpRequest, requestCancellation.Token).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            await using var stream = await response.Content.ReadAsStreamAsync(requestCancellation.Token).ConfigureAwait(false);
            using var json = await JsonDocument.ParseAsync(stream, cancellationToken: requestCancellation.Token)
                .ConfigureAwait(false);
            var content = json.RootElement.GetProperty("message").GetProperty("content").GetString() ?? string.Empty;
            return OperationResult<LlmChatResponse>.Success(new LlmChatResponse(content));
        }
        catch (OperationCanceledException)
        {
            return LlmProviderSupport.CancellationFailure<LlmChatResponse>(cancellationToken);
        }
        catch (Exception ex) when (LlmProviderSupport.IsResponseFormatException(ex))
        {
            return LlmProviderSupport.InvalidResponse<LlmChatResponse>(ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException)
        {
            return LlmProviderSupport.RequestFailure<LlmChatResponse>(ex);
        }
    }
}
