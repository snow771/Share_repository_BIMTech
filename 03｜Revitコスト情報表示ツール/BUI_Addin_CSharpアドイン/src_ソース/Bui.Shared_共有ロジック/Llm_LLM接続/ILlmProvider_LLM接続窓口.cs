// このファイルはOllamaとOpenAI互換サーバーを同じ方法で呼び出す接続窓口を定義します。
// OperationResultとLLM共通データに依存し、この契約を変えると設定画面・検索補助・全プロバイダーに影響します。
using Bui.Common;

namespace Bui.Llm;

/// <summary>LLM方式ごとのモデル取得とチャット呼出しを抽象化します。</summary>
public interface ILlmProvider
{
    string ProviderId { get; }

    Task<OperationResult<IReadOnlyList<LlmModelInfo>>> GetModelsAsync(CancellationToken cancellationToken);

    Task<OperationResult<LlmChatResponse>> ChatAsync(
        LlmChatRequest request,
        CancellationToken cancellationToken);
}
