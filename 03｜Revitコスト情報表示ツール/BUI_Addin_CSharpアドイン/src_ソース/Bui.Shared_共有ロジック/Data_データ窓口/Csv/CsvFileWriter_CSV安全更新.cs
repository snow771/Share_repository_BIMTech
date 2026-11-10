// このファイルはCSVを厳密なUTF-8で読み、同じBOM方針の一時ファイル・バックアップを経由して安全に置換するために存在します。
// 文字コード、CSV構文、競合確認、復元手順を変えると、全CSVリポジトリの互換性と障害時の原本保護に影響します。
using System.IO;
using System.Text;
using System.Collections.Concurrent;
using Microsoft.VisualBasic.FileIO;

namespace Bui.Data.Csv;

/// <summary>CSV置換失敗と、バックアップからの復元結果をまとめて通知します。</summary>
public sealed class CsvReplacementException : IOException
{
    public CsvReplacementException(Exception replacementException, bool restorationAttempted, bool restorationSucceeded)
        : base(
            restorationAttempted
                ? $"CSVの置換に失敗しました。バックアップからの復元結果: {(restorationSucceeded ? "成功" : "失敗")}"
                : "CSVの置換に失敗しました。元ファイルは残っています。",
            replacementException)
    {
        ReplacementException = replacementException;
        RestorationAttempted = restorationAttempted;
        RestorationSucceeded = restorationSucceeded;
    }

    public Exception ReplacementException { get; }

    public bool RestorationAttempted { get; }

    public bool RestorationSucceeded { get; }
}

/// <summary>元CSVを競合から守りながら、バックアップ付きで置換します。</summary>
public sealed class CsvFileWriter
{
    // 同じプロセス内では、同じ正規化パスの確認→置換を一列にします。
    // CSVの種類ごとに別パスなら別Semaphoreになるため、無関係な更新は並行できます。
    private static readonly ConcurrentDictionary<string, PathWriteGate> PathWriteGates =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Action<string, string> replaceFile;
    private readonly Func<string, CancellationToken, Task> afterTemporaryFileWritten;

    /// <summary>通常は既定のファイル置換を使い、テスト時だけ置換処理を差し替えられます。</summary>
    public CsvFileWriter(Action<string, string>? replaceFile = null)
        : this(replaceFile, null)
    {
    }

    /// <summary>temp書込み直後の競合を決定的に検証するための、内部専用の狭い境界です。</summary>
    internal CsvFileWriter(
        Action<string, string>? replaceFile,
        Func<string, CancellationToken, Task>? afterTemporaryFileWritten)
    {
        this.replaceFile = replaceFile ?? ((temporaryPath, sourcePath) => File.Move(temporaryPath, sourcePath, true));
        this.afterTemporaryFileWritten = afterTemporaryFileWritten ?? ((_, _) => Task.CompletedTask);
    }

    /// <summary>期待した版のCSVだけを、BOM方針を保った内容へ安全に置き換えます。</summary>
    public async Task ReplaceWithBackupAsync(
        string csvPath,
        string content,
        DateTime expectedLastWriteTimeUtc,
        bool emitUtf8Bom,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(csvPath);
        ArgumentNullException.ThrowIfNull(content);

        var canonicalPath = Path.GetFullPath(csvPath);
        var writeGate = PathWriteGates.GetOrAdd(canonicalPath, static _ => new PathWriteGate());
        var observedGeneration = Volatile.Read(ref writeGate.Generation);
        await writeGate.Semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // 呼出し開始後に同じプロセスの先行更新が完了した場合、粗いtimestampのファイルシステムでも
            // 古い内容を基にした後続payloadを成功扱いにしません。
            if (observedGeneration != Volatile.Read(ref writeGate.Generation))
            {
                throw new IOException("CSVが同じBUIプロセスの他の更新によって変更されたため、更新を中止しました。");
            }
            await ReplaceOwnedAsync(
                canonicalPath,
                content,
                expectedLastWriteTimeUtc,
                emitUtf8Bom,
                cancellationToken).ConfigureAwait(false);
            Interlocked.Increment(ref writeGate.Generation);
        }
        finally
        {
            writeGate.Semaphore.Release();
        }
    }

    private sealed class PathWriteGate
    {
        public SemaphoreSlim Semaphore { get; } = new(1, 1);
        public long Generation;
    }

    private async Task ReplaceOwnedAsync(
        string csvPath,
        string content,
        DateTime expectedLastWriteTimeUtc,
        bool emitUtf8Bom,
        CancellationToken cancellationToken)
    {

        var observedTimestamp = File.GetLastWriteTimeUtc(csvPath);
        if (observedTimestamp != expectedLastWriteTimeUtc)
        {
            throw new IOException("CSVが他の処理によって変更されたため、更新を中止しました。");
        }

        // 未作成のCSVを初めて書き出す経路です。構成CSVは最初の登録時に作られるため、
        // 置き場のフォルダごと無いことがあります。作らずに進むと一時ファイルの作成で
        // DirectoryNotFoundExceptionになり、利用者には理由が伝わりません。
        var sourceExists = File.Exists(csvPath);
        var parentDirectory = Path.GetDirectoryName(csvPath);
        if (!string.IsNullOrEmpty(parentDirectory))
        {
            Directory.CreateDirectory(parentDirectory);
        }

        // 操作ごとに専有する名前へ分け、並行更新が互いのpayloadを上書きしないようにします。
        var temporaryPath = $"{csvPath}.{Guid.NewGuid():N}.tmp";
        string? backupPath = null;
        try
        {
            var encoding = new UTF8Encoding(emitUtf8Bom, true);
            await using (var stream = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 4096,
                useAsync: true))
            await using (var textWriter = new StreamWriter(stream, encoding))
            {
                await textWriter.WriteAsync(content.AsMemory(), cancellationToken).ConfigureAwait(false);
            }

            await afterTemporaryFileWritten(temporaryPath, cancellationToken).ConfigureAwait(false);

            // 一時ファイルの作成中に原本が変わっていないか、バックアップの直前でもう一度確認します。
            if (File.GetLastWriteTimeUtc(csvPath) != observedTimestamp)
            {
                throw new IOException("CSVが他の処理によって変更されたため、更新を中止しました。");
            }

            // 原本が無い初回作成では、退避するものがありません。
            // File.Copyは元ファイルが無いと例外になるため、ここは飛ばします。
            if (sourceExists)
            {
                backupPath = CreateBackupPath(csvPath);
                File.Copy(csvPath, backupPath, false);
            }

            try
            {
                replaceFile(temporaryPath, csvPath);
            }
            catch (Exception replacementError)
            {
                // backupPathがnullなのは初回作成のときだけで、戻す先の原本もありません。
                var restorablePath = backupPath;
                var restorationAttempted = restorablePath is not null && !File.Exists(csvPath);
                var restorationSucceeded = false;
                if (restorationAttempted)
                {
                    try
                    {
                        // 置換途中で原本が消えた場合だけ、直前に作った完全コピーを元の場所へ戻します。
                        File.Copy(restorablePath!, csvPath, true);
                        restorationSucceeded = true;
                    }
                    catch
                    {
                        restorationSucceeded = false;
                    }
                }

                throw new CsvReplacementException(replacementError, restorationAttempted, restorationSucceeded);
            }
        }
        finally
        {
            // 成功・競合・置換失敗のどの経路でも作業途中のファイルを残しません。
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private static string CreateBackupPath(string csvPath)
    {
        var instant = DateTime.Now;
        string candidate;
        do
        {
            candidate = $"{csvPath}.{instant:yyyyMMddHHmmssfff}.bak";
            instant = instant.AddMilliseconds(1);
        }
        while (File.Exists(candidate));

        return candidate;
    }
}

internal sealed record CsvDocument(
    IReadOnlyList<string> Headers,
    IReadOnlyList<Dictionary<string, string>> Rows,
    bool HasUtf8Bom);

/// <summary>両リポジトリとID対応ローダーで同じ厳密なCSV解釈を共有します。</summary>
internal static class CsvDocumentCodec
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    public static CsvDocument Read(string path)
    {
        byte[] bytes;
        try
        {
            bytes = File.ReadAllBytes(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new InvalidDataException($"CSVファイルを読み取れません: {path}", ex);
        }

        var hasBom = bytes.AsSpan().StartsWith(new byte[] { 0xef, 0xbb, 0xbf });
        string text;
        try
        {
            text = StrictUtf8.GetString(bytes, hasBom ? 3 : 0, bytes.Length - (hasBom ? 3 : 0));
        }
        catch (DecoderFallbackException ex)
        {
            throw new InvalidDataException("CSVを正しいUTF-8として読み取れません。", ex);
        }

        using var parser = new TextFieldParser(new StringReader(text));
        parser.TextFieldType = FieldType.Delimited;
        parser.SetDelimiters(",");
        parser.HasFieldsEnclosedInQuotes = true;
        parser.TrimWhiteSpace = false;

        try
        {
            var headerFields = parser.EndOfData ? null : parser.ReadFields();
            if (headerFields is null || headerFields.Length == 0)
            {
                throw new InvalidDataException("CSVヘッダーがありません。");
            }

            var headers = headerFields.ToList();
            if (headers.Count != headers.Distinct(StringComparer.Ordinal).Count())
            {
                throw new InvalidDataException("CSVヘッダーに重複する列名があります。");
            }

            var rows = new List<Dictionary<string, string>>();
            while (!parser.EndOfData)
            {
                var fields = parser.ReadFields();
                if (fields is null)
                {
                    continue;
                }

                if (fields.Length > headers.Count)
                {
                    throw new InvalidDataException($"CSVの{parser.LineNumber - 1}行目にヘッダーより多い値があります。");
                }

                var row = new Dictionary<string, string>(StringComparer.Ordinal);
                for (var index = 0; index < headers.Count; index++)
                {
                    row[headers[index]] = index < fields.Length ? fields[index] : string.Empty;
                }

                rows.Add(row);
            }

            return new CsvDocument(headers, rows, hasBom);
        }
        catch (MalformedLineException ex)
        {
            throw new InvalidDataException($"CSVの{ex.LineNumber}行目の引用符形式が正しくありません。", ex);
        }
    }

    public static string Write(IReadOnlyList<string> headers, IReadOnlyList<Dictionary<string, string>> rows)
    {
        var builder = new StringBuilder();
        builder.AppendLine(string.Join(',', headers.Select(Escape)));
        foreach (var row in rows)
        {
            builder.AppendLine(string.Join(',', headers.Select(header => Escape(row.GetValueOrDefault(header, string.Empty)))));
        }

        return builder.ToString();
    }

    private static string Escape(string value)
    {
        value ??= string.Empty;
        return value.IndexOfAny([',', '"', '\r', '\n']) >= 0
            ? $"\"{value.Replace("\"", "\"\"")}\""
            : value;
    }
}
