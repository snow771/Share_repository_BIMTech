// このファイルは各LLMプロバイダーのURL組立て、接続ゲート、キャンセル結果を同じ規則に保つために存在します。
// LocalEndpointPolicyとOperationResultに依存し、この共通処理を変えるとOllamaとOpenAI互換の両方の安全性・エラー表示に影響します。
using System.Net;
using System.Text.Json;
using Bui.Common;

namespace Bui.Llm;

internal static class LlmProviderSupport
{
    internal static readonly TimeSpan ModelListTimeout = TimeSpan.FromSeconds(10);

    /// <summary>
    /// LLMへ送るユーザープロンプトの上限文字数です。呼び出し①（LlmQueryTranslator）と
    /// 呼び出し②（LlmSearchAssistant）で共有し、数値を1つに保ちます。
    /// 上限を超えると小さいローカルモデルのコンテキストを溢れさせ、壊れたJSON応答という
    /// 「モデルのせい」に見える失敗を起こすため、どちらの呼び出しも必ずこれを守ってください。
    /// </summary>
    internal const int MaximumPromptLength = 16_000;

    /// <summary>
    /// 呼び出し側のCancellationTokenで中止された（タイムアウトではない）ことを表す結果コードです。
    /// OperationCanceledExceptionを投げないプロバイダー（OllamaProvider等）はこの失敗結果を返すため、
    /// TranslateAsync/AnswerWithCandidatesAsyncはこのコードを見て「中断」として扱う必要があります。
    /// ここを見落とすと、利用者が中断したはずの操作がキーワード検索の劣化回答へ静かに続いてしまいます。
    /// </summary>
    internal const string CancelledErrorCode = "LLM_CANCELLED";

    internal static Uri NormalizeServerRoot(Uri baseUri)
    {
        ArgumentNullException.ThrowIfNull(baseUri);
        if (!baseUri.IsAbsoluteUri)
        {
            throw new ArgumentException("LLM接続URLは絶対URLで指定してください。", nameof(baseUri));
        }

        // 設定値に /v1 等が含まれてもサーバールートへ正規化し、規定経路を1回だけ付与します。
        return new Uri(baseUri.GetLeftPart(UriPartial.Authority) + "/", UriKind.Absolute);
    }

    internal static Uri BuildRoute(Uri serverRoot, string route) =>
        new(serverRoot, route.TrimStart('/'));

    internal static async Task<OperationError?> ValidateEndpointAsync(
        Uri endpoint,
        Func<string, CancellationToken, Task<IPAddress[]>> dnsResolver,
        CancellationToken cancellationToken)
    {
        var validation = await LocalEndpointPolicy.ValidateAsync(endpoint, dnsResolver, cancellationToken)
            .ConfigureAwait(false);
        return validation.IsAllowed
            ? null
            : new OperationError(
                "LLM_ENDPOINT_DENIED",
                validation.Message,
                "LLM endpoint policy denied the request before HTTP dispatch.");
    }

    internal static CancellationTokenSource CreateRequestCancellation(
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        linked.CancelAfter(timeout);
        return linked;
    }

    internal static OperationResult<T> CancellationFailure<T>(CancellationToken callerToken) =>
        callerToken.IsCancellationRequested
            ? OperationResult<T>.Failure(
                CancelledErrorCode,
                "LLMへの接続を中止しました。",
                "The caller cancellation token was cancelled.")
            : OperationResult<T>.Failure(
                "LLM_TIMEOUT",
                "LLMへの接続がタイムアウトしました。通常検索の結果を利用します。",
                "The per-request timeout elapsed.");

    internal static OperationResult<T> EndpointFailure<T>(OperationError error) => new(default, error);

    internal static OperationResult<T> RequestFailure<T>(Exception exception) =>
        OperationResult<T>.Failure(
            "LLM_REQUEST_FAILED",
            "LLMと通信できませんでした。通常検索の結果を利用します。",
            $"{exception.GetType().Name}: {exception.Message}");

    internal static OperationResult<T> InvalidResponse<T>(Exception exception) =>
        OperationResult<T>.Failure(
            "LLM_RESPONSE_INVALID",
            "LLMからの応答を読み取れませんでした。通常検索の結果を利用します。",
            $"{exception.GetType().Name}: {exception.Message}");

    internal static bool IsResponseFormatException(Exception exception) =>
        exception is JsonException
            or KeyNotFoundException
            or InvalidOperationException
            or IndexOutOfRangeException;
}
