namespace SimilaritySearchApp.Models;

/// <summary>DB のテーブル1件（DB編集画面の一覧・関連図のカード）。</summary>
/// <param name="Schema">スキーマ名（通常 dbo）</param>
/// <param name="Name">テーブル名（例: MaterialMaster_建築材料マスタ）</param>
/// <param name="BaseName">英字部分（例: MaterialMaster）。関連の推定に使う</param>
/// <param name="Label">日本語部分（例: 建築材料マスタ）。無ければテーブル名</param>
/// <param name="RowCount">行数（概算）</param>
/// <param name="AppRole">このアプリが使っているテーブルなら役割名（例: 物件情報）。使っていなければ null</param>
public sealed record DbTableInfo(string Schema, string Name, string BaseName, string Label, long RowCount, string? AppRole)
{
    public string FullName => $"{Schema}.{Name}";
}

/// <summary>DB の列1件。</summary>
/// <param name="TypeName">SQL Server の型名（nvarchar, int など）</param>
/// <param name="MaxLength">文字数の上限（-1 は max、文字列以外は null）</param>
/// <param name="IsEditable">画面から編集できるか（IDENTITY・計算列・バイナリ等は不可）</param>
/// <param name="KeyName">関連推定用に正規化した列名（例: Work_Type_Code_工種コード → work_type_code）</param>
public sealed record DbColumnInfo(
    string Name, string TypeName, int? MaxLength, int Precision, int Scale,
    bool IsNullable, bool IsIdentity, bool IsComputed, bool IsPrimaryKey, bool IsEditable, string KeyName);

/// <summary>全テーブルの列（関連推定用）。</summary>
public sealed record ColumnRef(string Schema, string Table, string Column, bool IsPrimaryKey);

/// <summary>外部キー制約1本（列単位）。From が参照する側（子）、To が参照される側（親）。</summary>
public sealed record ForeignKeyRef(string FromSchema, string FromTable, string FromColumn, string ToSchema, string ToTable, string ToColumn);

/// <summary>関連図の隣接テーブル。</summary>
/// <param name="Direction">parent（選択テーブルが参照する親）/ child（選択テーブルを参照する子）/ peer（同名キー列を共有）</param>
/// <param name="IsForeignKey">true: DB の外部キー制約 / false: 同名キー列からの推定</param>
/// <param name="Links">結んでいる列（例: "Work_Type_Code → Work_Type_Code_工種コード"）</param>
public sealed record DbNeighbor(DbTableInfo Table, string Direction, bool IsForeignKey, List<string> Links);

/// <summary>選択テーブルと、その隣接テーブル。</summary>
public sealed record DbNeighborhood(DbTableInfo Center, List<DbNeighbor> Neighbors);

/// <summary>dbTable コマンドの結果（列定義＋関連図）。</summary>
public sealed record DbTableDetail(DbTableInfo Table, List<DbColumnInfo> Columns, bool HasPrimaryKey, DbNeighborhood Neighborhood);

/// <summary>テーブル一覧（接続先の情報付き）。</summary>
public sealed record DbTablesResult(string Server, string Database, List<DbTableInfo> Tables);
