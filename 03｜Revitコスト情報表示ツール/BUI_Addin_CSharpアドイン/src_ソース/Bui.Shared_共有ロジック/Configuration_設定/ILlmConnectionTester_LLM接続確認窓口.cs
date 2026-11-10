// このファイルは設定画面の接続確認を、安全なLLM生成処理の実装から分離します。
// LlmSettingsとLLMモデル契約に依存し、変更は設定タブの非同期接続確認に影響します。
using Bui.Common;
using Bui.Llm;

namespace Bui.Configuration;

public interface ILlmConnectionTester
{
    Task<OperationResult<IReadOnlyList<LlmModelInfo>>> TestAsync(LlmSettings settings, CancellationToken cancellationToken = default);
}
