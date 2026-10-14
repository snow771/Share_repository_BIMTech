using System.IO;
using System.Security.Cryptography;
using Microsoft.Data.SqlClient;
using SimilaritySearchApp.Estimate.Models;

namespace SimilaritySearchApp.Estimate.Data;

/// <summary>
/// 資料ファイル（DocumentFile_資料ファイル）の本体を保存・取得する。
///   ・（取込・資料種別・SHA-256）で一意。同じ資料は重複して保存しない（差し替えではなく追加）
///   ・取得時に SHA-256 を照合し、壊れていれば使わない
///   ・画面からはパスではなく資料ID で指定させる
/// </summary>
public sealed class DocumentRepository
{
    public sealed record StoredFile(string Type, string FileName, string ContentType, byte[] Content, byte[] Hash);

    public static string ContentTypeOf(string fileName) => Path.GetExtension(fileName).ToLowerInvariant() switch
    {
        ".pdf" => "application/pdf",
        ".xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        ".xlsm" => "application/vnd.ms-excel.sheet.macroEnabled.12",
        ".json" => "application/json",
        _ => "application/octet-stream",
    };

    public static byte[] Sha256(byte[] content) => SHA256.HashData(content);

    public static string Hex(byte[] hash) => Convert.ToHexString(hash).ToLowerInvariant();

    /// <summary>ファイルを読み込む（上限サイズを超えたら止める）。</summary>
    public static StoredFile Load(string type, string path)
    {
        var info = new FileInfo(path);
        if (!info.Exists) throw new FileNotFoundException($"ファイルが見つかりません: {path}");
        if (info.Length > EstimateDb.MaxFileBytes)
            throw new InvalidOperationException(
                $"{info.Name} は {info.Length / 1024 / 1024:N0}MB あり、保存できる上限（{EstimateDb.MaxFileBytes / 1024 / 1024}MB）を超えています。");
        var bytes = File.ReadAllBytes(path);
        return new StoredFile(type, info.Name, ContentTypeOf(info.Name), bytes, Sha256(bytes));
    }

    /// <summary>まだ無ければ保存する。保存したら true。</summary>
    public async Task<bool> AddIfMissingAsync(SqlConnection conn, SqlTransaction tx, ColumnGuard guard, int batchId, StoredFile f, CancellationToken ct)
    {
        await using (var exists = new SqlCommand(
            $"SELECT COUNT(*) FROM {EstimateDb.T(EstimateDb.Document)} WHERE OcrImportBatch_ID = @b AND DocumentType = @t AND FileHash = @h", conn, tx))
        {
            exists.Parameters.AddWithValue("@b", batchId);
            exists.Parameters.AddWithValue("@t", f.Type);
            exists.Parameters.Add(new SqlParameter("@h", System.Data.SqlDbType.Binary, 32) { Value = f.Hash });
            if (Convert.ToInt32(await exists.ExecuteScalarAsync(ct)) > 0) return false;
        }

        const string table = EstimateDb.Document;
        var where = $"資料「{f.FileName}」";
        await using var cmd = new SqlCommand(
            $"""
            INSERT INTO {EstimateDb.T(table)}
                (OcrImportBatch_ID, DocumentType, OriginalFileName, ContentType, FileSize, FileHash, FileContent, StoredAt, StoredBy)
            VALUES (@b, @t, @n, @c, @s, @h, @f, SYSUTCDATETIME(), @by)
            """, conn, tx) { CommandTimeout = 600 };
        cmd.Parameters.Add(guard.Param("@b", table, "OcrImportBatch_ID", batchId, where));
        cmd.Parameters.Add(guard.Param("@t", table, "DocumentType", f.Type, where));
        cmd.Parameters.Add(guard.Param("@n", table, "OriginalFileName", f.FileName, where));
        cmd.Parameters.Add(guard.Param("@c", table, "ContentType", f.ContentType, where));
        cmd.Parameters.Add(guard.Param("@s", table, "FileSize", (long)f.Content.Length, where));
        cmd.Parameters.Add(guard.Param("@h", table, "FileHash", f.Hash, where));
        cmd.Parameters.Add(guard.Param("@f", table, "FileContent", f.Content, where));
        cmd.Parameters.Add(guard.Param("@by", table, "StoredBy", EstimateDb.Operator, where));
        guard.ThrowIfIssues("資料の保存");
        await cmd.ExecuteNonQueryAsync(ct);
        return true;
    }

    /// <summary>見積に属する資料の一覧（本体は読まない）。</summary>
    public async Task<List<DocumentView>> ListAsync(SqlConnection conn, int headerId, CancellationToken ct)
    {
        await using var cmd = new SqlCommand(
            $"""
            SELECT d.DocumentFile_ID, d.DocumentType, d.OriginalFileName, d.FileSize, d.StoredAt, d.StoredBy, d.FileHash
            FROM {EstimateDb.T(EstimateDb.Document)} d
            JOIN {EstimateDb.T(EstimateDb.Header)} h ON h.OcrImportBatch_ID = d.OcrImportBatch_ID
            WHERE h.OcrEstimateHeader_ID = @id
            ORDER BY d.DocumentFile_ID
            """, conn);
        cmd.Parameters.AddWithValue("@id", headerId);
        var list = new List<DocumentView>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
        {
            list.Add(new DocumentView(r.GetInt64(0), r.GetString(1), r.GetString(2), r.GetInt64(3),
                DateTime.SpecifyKind(r.GetDateTime(4), DateTimeKind.Utc), r.GetString(5), Hex((byte[])r[6])));
        }
        return list;
    }

    /// <summary>
    /// 資料を取り出す。headerId の見積に属する資料だけを返す（他の見積の資料 ID を指定されても渡さない）。
    /// SHA-256 が一致しなければ止める。
    /// </summary>
    public async Task<StoredFile> GetAsync(SqlConnection conn, int headerId, long documentId, CancellationToken ct)
    {
        await using var cmd = new SqlCommand(
            $"""
            SELECT d.DocumentType, d.OriginalFileName, d.ContentType, d.FileContent, d.FileHash
            FROM {EstimateDb.T(EstimateDb.Document)} d
            JOIN {EstimateDb.T(EstimateDb.Header)} h ON h.OcrImportBatch_ID = d.OcrImportBatch_ID
            WHERE h.OcrEstimateHeader_ID = @h AND d.DocumentFile_ID = @d
            """, conn) { CommandTimeout = 600 };
        cmd.Parameters.AddWithValue("@h", headerId);
        cmd.Parameters.AddWithValue("@d", documentId);
        await using var r = await cmd.ExecuteReaderAsync(System.Data.CommandBehavior.SequentialAccess, ct);
        if (!await r.ReadAsync(ct)) throw new InvalidOperationException("資料が見つかりません（この見積の資料ではないか、削除されています）。");
        var type = r.GetString(0);
        var name = r.GetString(1);
        var contentType = r.GetString(2);
        var content = (byte[])r[3];
        var hash = (byte[])r[4];
        if (!Sha256(content).AsSpan().SequenceEqual(hash))
            throw new InvalidOperationException($"資料「{name}」の内容がハッシュと一致しません（破損の可能性）。使用を中止しました。");
        return new StoredFile(type, name, contentType, content, hash);
    }
}
