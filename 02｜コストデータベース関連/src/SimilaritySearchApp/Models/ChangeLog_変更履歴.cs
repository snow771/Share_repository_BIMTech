namespace SimilaritySearchApp.Models;

/// <summary>変更1行分（変更前・変更後の全列。値は DbValueConverter.Format の文字列）。</summary>
/// <param name="Operation">INSERT / UPDATE / DELETE</param>
public sealed record ChangeRowRecord(
    string Schema, string Table, string Operation,
    Dictionary<string, string?>? Before, Dictionary<string, string?>? After);

/// <summary>変更操作（保存1回分）の一覧表示用。</summary>
/// <param name="Source">DB編集 / Excel取込 / Stencile登録 / 取り消し</param>
/// <param name="RevertsBatchIds">この操作が取り消した操作の番号（取り消しの操作のみ）</param>
/// <param name="RevertedByBatchId">この操作を取り消した操作の番号（取り消し済みなら）</param>
public sealed record ChangeBatchInfo(
    int BatchId, DateTime ExecutedAt, string UserName, string? MachineName, string? DbLogin,
    string Source, string? Summary, string? Tables, int RowTotal,
    string? RevertsBatchIds, int? RevertedByBatchId);

/// <summary>変更1行の表示用（変わった列の 前 → 後）。</summary>
public sealed record ChangeRowView(int SeqNo, string Table, string Operation, string KeyText, List<ChangeDiff> Changes);

public sealed record ChangeDiff(string Column, string? Before, string? After);

public sealed class HistoryQuery
{
    public DateTime? From { get; set; }
    public DateTime? To { get; set; }
    public string? User { get; set; }
    public string? Table { get; set; }
    public string? Keyword { get; set; }
    public int Limit { get; set; } = 300;
}

public sealed record HistoryListResult(List<ChangeBatchInfo> Batches, bool Truncated, string BatchTable, string RowTable);

public sealed class HistoryDetailRequest
{
    public int BatchId { get; set; }
    public int Limit { get; set; } = 1000;
}

public sealed record HistoryDetailResult(ChangeBatchInfo Batch, List<ChangeRowView> Rows, int TotalRows);

/// <summary>取り消しの要求。Mode: one（この操作だけ）/ since（この操作以降をすべて）</summary>
public sealed class RevertRequest
{
    public int BatchId { get; set; }
    public string Mode { get; set; } = "one";

    /// <summary>true: その後に他で変更されていても戻す（衝突を無視）</summary>
    public bool Force { get; set; }
}

/// <summary>取り消せない行（その後に別の変更が入っている等）。</summary>
public sealed record RevertConflict(int BatchId, string Table, string Operation, string KeyText, string Reason);

public sealed record RevertResult(bool Done, int? NewBatchId, List<int> RevertedBatchIds, int Rows, List<RevertConflict> Conflicts, List<string> Warnings);

public sealed class PurgeRequest
{
    /// <summary>この日時より前の履歴を削除</summary>
    public DateTime Before { get; set; }
}
