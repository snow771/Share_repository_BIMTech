// このファイルはLLM方式に依存しないモデル・メッセージ・応答の不変データを共有するために存在します。
// 両プロバイダーと検索補助に依存され、項目名や型を変えると設定画面とリクエスト生成の互換性に影響します。
namespace Bui.Llm;

/// <summary>LLMサーバーが公開する選択可能モデルを表します。</summary>
public sealed record LlmModelInfo(string Id, string DisplayName);

/// <summary>LLMに渡すロール付きの1メッセージを表します。</summary>
public sealed record LlmMessage(string Role, string Content);

/// <summary>モデル、会話、1回の制限時間を持つチャット要求を表します。</summary>
public sealed record LlmChatRequest(string Model, IReadOnlyList<LlmMessage> Messages, TimeSpan Timeout);

/// <summary>LLMが返したテキストだけをプロバイダー差なく表します。</summary>
public sealed record LlmChatResponse(string Content);
