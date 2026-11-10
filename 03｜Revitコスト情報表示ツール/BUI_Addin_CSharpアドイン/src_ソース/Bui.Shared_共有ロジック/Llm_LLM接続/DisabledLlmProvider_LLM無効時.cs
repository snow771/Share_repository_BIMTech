// このファイルは、LLMが未設定のPCで通信を確実に発生させないための空実装です。
// ILlmProviderのみに依存し、Task 11で設定済みプロバイダーへ差し替えるときに使用箇所が影響を受けます。
using Bui.Common;

namespace Bui.Llm;

public sealed class DisabledLlmProvider : ILlmProvider
{
    public string ProviderId => "Disabled";

    public Task<OperationResult<IReadOnlyList<LlmModelInfo>>> GetModelsAsync(CancellationToken cancellationToken) =>
        Task.FromResult(OperationResult<IReadOnlyList<LlmModelInfo>>.Failure(
            "LLM-DISABLED", "LLMはOFFまたは未設定です。", "No network request was made."));

    public Task<OperationResult<LlmChatResponse>> ChatAsync(LlmChatRequest request, CancellationToken cancellationToken) =>
        Task.FromResult(OperationResult<LlmChatResponse>.Failure(
            "LLM-DISABLED", "LLMはOFFまたは未設定です。", "No network request was made."));
}
