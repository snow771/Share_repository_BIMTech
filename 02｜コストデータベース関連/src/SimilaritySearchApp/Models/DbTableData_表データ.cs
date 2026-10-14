namespace SimilaritySearchApp.Models;

/// <summary>表示用のページ取得条件。</summary>
public sealed class DbQueryRequest
{
    public string Schema { get; set; } = "dbo";
    public string Name { get; set; } = "";
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 200;

    /// <summary>全列を対象にした部分一致検索（空なら絞らない）</summary>
    public string? Search { get; set; }

    public string? SortColumn { get; set; }
    public bool SortDesc { get; set; }
}

/// <summary>
/// 1ページ分の行。値はすべて文字列（null は NULL）。
/// 文字列は書式を固定しており（数値は不変カルチャ、日時は yyyy-MM-dd HH:mm:ss.fffffff）、保存時に行を特定するキーとしてもそのまま使える。
/// </summary>
public sealed record DbPageResult(
    List<DbColumnInfo> Columns, List<string?[]> Rows, int Total, int Page, int PageSize, bool HasPrimaryKey);

/// <summary>行を特定するキー（列名 → 読込時の値）。主キーがあれば主キー列だけ、無ければ全列。</summary>
public sealed class DbRowKey
{
    public Dictionary<string, string?> Key { get; set; } = new();

    /// <summary>画面上の行番号（エラー表示用）</summary>
    public int RowNo { get; set; }
}

public sealed class DbRowUpdate
{
    public Dictionary<string, string?> Key { get; set; } = new();

    /// <summary>変更した列だけ（列名 → 新しい値）</summary>
    public Dictionary<string, string?> Values { get; set; } = new();

    public int RowNo { get; set; }
}

public sealed class DbRowInsert
{
    /// <summary>入力した列（列名 → 値）。省略した列は DB の既定値。</summary>
    public Dictionary<string, string?> Values { get; set; } = new();

    public int RowNo { get; set; }
}

/// <summary>保存する変更一式（1トランザクションで実行）。</summary>
public sealed class DbChangeSet
{
    public string Schema { get; set; } = "dbo";
    public string Name { get; set; } = "";
    public List<DbRowInsert> Inserts { get; set; } = new();
    public List<DbRowUpdate> Updates { get; set; } = new();
    public List<DbRowKey> Deletes { get; set; } = new();

    /// <summary>変更履歴に残す種類（DB編集 / Excel取込）</summary>
    public string? Source { get; set; }
}

/// <summary>物件の削除に伴って、関連テーブルから一緒に削除した行数。</summary>
public sealed record DbRelatedDelete(string Table, string Label, int Rows);

public sealed record DbSaveResult(int Inserted, int Updated, int Deleted)
{
    /// <summary>関連テーブルから一緒に削除した行（物件情報の削除時のみ）</summary>
    public List<DbRelatedDelete> Related { get; init; } = new();
}

/// <summary>物件の削除前の確認用: 削除予定の project_id と、関連テーブルで一緒に消える行数。</summary>
public sealed class DbDeletePreviewRequest
{
    public string Schema { get; set; } = "dbo";
    public string Name { get; set; } = "";
    public List<DbRowKey> Deletes { get; set; } = new();
}
