// このファイルはBUI管理フォルダへ秘匿済みログを保存し、日数と容量で整理します。
// JSONとファイルI/Oに依存し、秘匿ルールや削除範囲の変更は認証情報と調査可能性に影響します。
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Bui.Logging;

public sealed class FileBuiLogger : IBuiLogger
{
    public const long DefaultMaxBytes = 10 * 1024 * 1024;
    public const int DefaultRetentionDays = 30;
    private readonly string _root;
    private readonly string _version;
    private readonly long _maxBytes;
    private readonly int _retentionDays;
    private readonly int _maxDetailCharacters;
    private readonly SemaphoreSlim _writeGate = new(1, 1);

    public FileBuiLogger(string logsDirectory, string version = "0.1.0", long maxBytes = DefaultMaxBytes,
        int retentionDays = DefaultRetentionDays, int maxDetailCharacters = 2048)
    {
        if (maxBytes <= 0) throw new ArgumentOutOfRangeException(nameof(maxBytes));
        if (retentionDays < 0) throw new ArgumentOutOfRangeException(nameof(retentionDays));
        if (maxDetailCharacters <= 0) throw new ArgumentOutOfRangeException(nameof(maxDetailCharacters));
        _root = Path.GetFullPath(logsDirectory);
        _version = version;
        _maxBytes = maxBytes;
        _retentionDays = retentionDays;
        _maxDetailCharacters = maxDetailCharacters;
    }

    public async Task WriteAsync(LogEntry entry, CancellationToken cancellationToken = default)
    {
        try
        {
            await _writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                Directory.CreateDirectory(_root);
                CleanupOwnedLogs();
                var safeDetail = LogSanitizer.Sanitize(entry.SanitizedDetail, _maxDetailCharacters);
                // SensitiveBodyはシリアライズ用オブジェクトへ入れず、文面やCSV行全体を残しません。
                var diskEntry = new
                {
                    timestamp = entry.Timestamp,
                    version = _version,
                    level = SafeField(entry.Level),
                    operation = SafeField(entry.Operation),
                    errorCode = SafeField(entry.ErrorCode),
                    exceptionType = SafeField(entry.ExceptionType),
                    sanitizedDetail = safeDetail
                };
                var fileName = $"bui-{DateTime.UtcNow:yyyyMMdd}.jsonl";
                var path = OwnedPath(fileName);
                await File.AppendAllTextAsync(path, JsonSerializer.Serialize(diskEntry) + Environment.NewLine, cancellationToken).ConfigureAwait(false);
                CleanupOwnedLogs();
            }
            finally { _writeGate.Release(); }
        }
        catch
        {
            // ログは補助機能です。ディスク障害等でRevit本体を停止させません。
        }
    }

    private void CleanupOwnedLogs()
    {
        var files = Directory.EnumerateFiles(_root, "bui-*.jsonl", SearchOption.TopDirectoryOnly)
            .Select(path => new FileInfo(path)).Where(file => IsOwned(file.FullName)).ToList();
        var cutoff = DateTime.UtcNow.AddDays(-_retentionDays);
        foreach (var expired in files.Where(x => x.LastWriteTimeUtc < cutoff).ToArray())
        {
            TryDelete(expired.FullName);
            files.Remove(expired);
        }

        var total = files.Sum(x => x.Exists ? x.Length : 0L);
        foreach (var oldest in files.OrderBy(x => x.LastWriteTimeUtc).ThenBy(x => x.Name, StringComparer.Ordinal))
        {
            if (total <= _maxBytes) break;
            var length = oldest.Exists ? oldest.Length : 0L;
            if (TryDelete(oldest.FullName)) total -= length;
        }
    }

    private string OwnedPath(string fileName)
    {
        if (fileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || Path.GetFileName(fileName) != fileName)
            throw new InvalidOperationException("Invalid BUI log file name.");
        var path = Path.GetFullPath(Path.Combine(_root, fileName));
        if (!IsOwned(path)) throw new InvalidOperationException("Log path escaped BUI Logs directory.");
        return path;
    }

    private bool IsOwned(string path)
    {
        var prefix = _root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return Path.GetFullPath(path).StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryDelete(string path) { try { File.Delete(path); return true; } catch { return false; } }
    private static string SafeField(string value) => LogSanitizer.Sanitize(value ?? string.Empty, 256);
}

internal static partial class LogSanitizer
{
    [GeneratedRegex("(?i)\\b(?:Bearer|Basic)\\s+(?:\"[^\"]*\"|'[^']*'|[^\\s,;}&]+)", RegexOptions.CultureInvariant)]
    private static partial Regex AuthorizationSchemeRegex();
    [GeneratedRegex("(?i)([\"']?(?:api[\\s_-]?key|[a-z0-9_-]*(?:token|password|passwd|pwd|secret|authorization)[a-z0-9_-]*)[\"']?\\s*[:=]\\s*)(?:\"[^\"]*\"|'[^']*'|[^;&,}\\r\\n]+)", RegexOptions.CultureInvariant)]
    private static partial Regex KeyValueRegex();

    public static string Sanitize(string? value, int maxCharacters)
    {
        var safe = value ?? string.Empty;
        // Authorizationスキームは値だけでなくBasic/Bearerの後ろ全体を一度に秘匿します。
        safe = AuthorizationSchemeRegex().Replace(safe, "[AUTHORIZATION REDACTED]");
        safe = KeyValueRegex().Replace(safe, "$1[REDACTED]");
        return safe.Length <= maxCharacters ? safe : safe[..maxCharacters] + "…";
    }
}
