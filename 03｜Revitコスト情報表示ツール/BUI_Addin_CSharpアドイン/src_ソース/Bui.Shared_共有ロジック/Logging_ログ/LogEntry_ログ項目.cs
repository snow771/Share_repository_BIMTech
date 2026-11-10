// このファイルはエラー境界からロガーへ渡す項目を固定します。
// SensitiveBodyはフィルタ入力専用で、保存対象に追加すると会社情報漏洩に直結します。
namespace Bui.Logging;

public sealed record LogEntry(
    DateTimeOffset Timestamp,
    string Level,
    string Operation,
    string ErrorCode,
    string ExceptionType,
    string SanitizedDetail,
    string? SensitiveBody)
{
    public static LogEntry Error(string operation, string detail, string? sensitiveBody) =>
        new(DateTimeOffset.UtcNow, "Error", operation, "UNEXPECTED", "Unknown", detail, sensitiveBody);
}
