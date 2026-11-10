// このファイルは処理の成功値または利用者向けエラーを同じ形式で返し、呼出側の例外依存を減らすために存在します。
// 成功判定やエラー項目を変えると、CSV取込などの呼出側が結果を表示・記録する方法に影響します。
namespace Bui.Common;

/// <summary>
/// 利用者へ通知するエラー情報と、技術調査用の詳細を保持します。
/// </summary>
public sealed record OperationError(string Code, string UserMessage, string TechnicalDetail);

/// <summary>
/// 成功時の値または失敗時のエラー情報を表します。
/// </summary>
public sealed record OperationResult<T>(T? Value, OperationError? Error)
{
    /// <summary>
    /// エラーがなく、値を利用できる状態かを返します。
    /// </summary>
    public bool IsSuccess => Error is null;

    /// <summary>
    /// 指定した値を持つ成功結果を作成します。
    /// </summary>
    public static OperationResult<T> Success(T value) => new(value, null);

    /// <summary>
    /// コード、利用者向け文言、技術詳細を持つ失敗結果を作成します。
    /// </summary>
    public static OperationResult<T> Failure(string code, string userMessage, string detail) =>
        new(default, new OperationError(code, userMessage, detail));
}
