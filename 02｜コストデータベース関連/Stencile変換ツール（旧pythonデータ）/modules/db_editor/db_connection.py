# -*- coding: utf-8 -*-
"""
db_connection.py
SQL Server 接続・スキーマ取得・CRUD 操作

接続先：localhost\\sqlexpress（Windows 統合認証）
"""

from __future__ import annotations

import re
from dataclasses import dataclass, field
from typing import Any, Dict, List, Optional, Tuple

try:
    import pyodbc  # type: ignore
except ImportError:
    pyodbc = None  # 起動時にチェックする


# ─── データクラス ───────────────────────────────────────────
@dataclass
class ColumnInfo:
    """列のメタ情報"""
    name: str
    data_type: str
    is_nullable: bool
    is_primary_key: bool
    is_foreign_key: bool = False
    max_length: Optional[int] = None
    ordinal: int = 0


@dataclass
class ForeignKey:
    """外部キー定義"""
    fk_name: str
    parent_table: str
    parent_column: str
    ref_table: str
    ref_column: str


@dataclass
class TableSchema:
    """テーブルのスキーマ情報"""
    schema: str
    name: str
    columns: List[ColumnInfo] = field(default_factory=list)

    @property
    def full_name(self) -> str:
        return f"{self.schema}.{self.name}"

    @property
    def primary_keys(self) -> List[str]:
        return [c.name for c in self.columns if c.is_primary_key]


# ─── 接続文字列ビルダー ─────────────────────────────────────
def build_connection_string(server: str = r"localhost\sqlexpress",
                            database: Optional[str] = None,
                            driver: str = "ODBC Driver 17 for SQL Server") -> str:
    """README 記載の接続文字列を構築する"""
    parts = [
        f"Driver={{{driver}}}",
        f"Server={server}",
        "Trusted_Connection=yes",
        "Encrypt=yes",
        "TrustServerCertificate=yes",
        "Connection Timeout=30",
    ]
    if database:
        parts.append(f"Database={database}")
    return ";".join(parts)


# ─── 接続クラス ─────────────────────────────────────────────
class DatabaseConnection:
    """SQL Server への接続と各種操作を提供"""

    def __init__(self):
        self.conn = None
        self.server: str = r"localhost\sqlexpress"
        self.database: Optional[str] = None

    # ── 接続管理 ────────────────────────────────────────────
    def ensure_pyodbc(self) -> None:
        if pyodbc is None:
            raise RuntimeError(
                "pyodbc がインストールされていません。\n"
                "pip install pyodbc を実行してください。"
            )

    def connect(self, server: Optional[str] = None,
                database: Optional[str] = None,
                driver: Optional[str] = None) -> None:
        """DB へ接続する"""
        self.ensure_pyodbc()
        if server:
            self.server = server
        if database is not None:
            self.database = database

        # 利用可能なドライバを自動検出
        used_driver = driver or self._pick_driver()
        cs = build_connection_string(self.server, self.database, used_driver)
        # autocommit を無効にしてトランザクション制御する
        self.conn = pyodbc.connect(cs, autocommit=False)

    def disconnect(self) -> None:
        if self.conn is not None:
            try:
                self.conn.close()
            except Exception:
                pass
            self.conn = None

    def is_connected(self) -> bool:
        return self.conn is not None

    @staticmethod
    def _pick_driver() -> str:
        """環境にインストールされているドライバから優先順で選択する"""
        if pyodbc is None:
            return "ODBC Driver 17 for SQL Server"
        drivers = [d for d in pyodbc.drivers() if "SQL Server" in d]
        # 18 > 17 > その他の順で優先
        for prefer in ("ODBC Driver 18 for SQL Server",
                       "ODBC Driver 17 for SQL Server"):
            if prefer in drivers:
                return prefer
        return drivers[0] if drivers else "ODBC Driver 17 for SQL Server"

    # ── データベース一覧 ────────────────────────────────────
    def list_databases(self) -> List[str]:
        """システムDB を除いた利用可能DB の一覧を返す"""
        self._require_conn()
        cur = self.conn.cursor()
        cur.execute(
            "SELECT name FROM sys.databases "
            "WHERE name NOT IN ('master', 'tempdb', 'model', 'msdb') "
            "ORDER BY name"
        )
        return [row[0] for row in cur.fetchall()]

    def use_database(self, database: str) -> None:
        """USE で対象DB を切り替える"""
        self._require_conn()
        cur = self.conn.cursor()
        cur.execute(f"USE [{database}]")
        self.database = database

    # ── テーブル一覧・スキーマ取得 ──────────────────────────
    def list_tables(self) -> List[Tuple[str, str]]:
        """(schema, table_name) のリストを返す（ユーザーテーブルのみ）"""
        self._require_conn()
        cur = self.conn.cursor()
        cur.execute(
            "SELECT TABLE_SCHEMA, TABLE_NAME "
            "FROM INFORMATION_SCHEMA.TABLES "
            "WHERE TABLE_TYPE = 'BASE TABLE' "
            "ORDER BY TABLE_SCHEMA, TABLE_NAME"
        )
        return [(r[0], r[1]) for r in cur.fetchall()]

    def get_table_schema(self, schema: str, table: str) -> TableSchema:
        """テーブルの列情報・PK・FK を取得"""
        self._require_conn()
        cur = self.conn.cursor()

        # 列情報
        cur.execute(
            """
            SELECT COLUMN_NAME, DATA_TYPE, IS_NULLABLE,
                   CHARACTER_MAXIMUM_LENGTH, ORDINAL_POSITION
            FROM INFORMATION_SCHEMA.COLUMNS
            WHERE TABLE_SCHEMA = ? AND TABLE_NAME = ?
            ORDER BY ORDINAL_POSITION
            """,
            schema, table,
        )
        columns: List[ColumnInfo] = []
        for name, dtype, nullable, maxlen, ordinal in cur.fetchall():
            columns.append(ColumnInfo(
                name=name,
                data_type=dtype,
                is_nullable=(nullable == "YES"),
                is_primary_key=False,
                max_length=maxlen,
                ordinal=ordinal,
            ))

        # PK 取得
        pks = self._get_primary_keys(schema, table)
        for c in columns:
            if c.name in pks:
                c.is_primary_key = True

        # FK（このテーブルが参照元）
        fks = self.get_foreign_keys()
        fk_parent_cols = {fk.parent_column for fk in fks
                          if fk.parent_table == table}
        for c in columns:
            if c.name in fk_parent_cols:
                c.is_foreign_key = True

        return TableSchema(schema=schema, name=table, columns=columns)

    def _get_primary_keys(self, schema: str, table: str) -> List[str]:
        cur = self.conn.cursor()
        cur.execute(
            """
            SELECT kcu.COLUMN_NAME
            FROM INFORMATION_SCHEMA.TABLE_CONSTRAINTS tc
            JOIN INFORMATION_SCHEMA.KEY_COLUMN_USAGE kcu
              ON tc.CONSTRAINT_NAME = kcu.CONSTRAINT_NAME
             AND tc.TABLE_SCHEMA   = kcu.TABLE_SCHEMA
            WHERE tc.CONSTRAINT_TYPE = 'PRIMARY KEY'
              AND tc.TABLE_SCHEMA = ?
              AND tc.TABLE_NAME   = ?
            ORDER BY kcu.ORDINAL_POSITION
            """,
            schema, table,
        )
        return [r[0] for r in cur.fetchall()]

    def get_foreign_keys(self) -> List[ForeignKey]:
        """DB 全体の外部キー一覧を取得（README 参考クエリ）"""
        self._require_conn()
        cur = self.conn.cursor()
        cur.execute(
            """
            SELECT
                fk.name                          AS fk_name,
                tp.name                          AS parent_table,
                cp.name                          AS parent_column,
                tr.name                          AS ref_table,
                cr.name                          AS ref_column
            FROM sys.foreign_keys AS fk
            JOIN sys.foreign_key_columns AS fkc
                ON fk.object_id = fkc.constraint_object_id
            JOIN sys.tables  AS tp ON fkc.parent_object_id  = tp.object_id
            JOIN sys.columns AS cp ON fkc.parent_object_id  = cp.object_id
                                   AND fkc.parent_column_id  = cp.column_id
            JOIN sys.tables  AS tr ON fkc.referenced_object_id  = tr.object_id
            JOIN sys.columns AS cr ON fkc.referenced_object_id  = cr.object_id
                                   AND fkc.referenced_column_id  = cr.column_id
            ORDER BY tp.name, fk.name
            """
        )
        return [ForeignKey(*r) for r in cur.fetchall()]

    # ── データ取得・編集 ────────────────────────────────────
    def fetch_data(self, schema: str, table: str,
                   limit: Optional[int] = 1000) -> Tuple[List[str], List[List[Any]]]:
        """テーブルから (列名リスト, データ行リスト) を取得"""
        self._require_conn()
        cur = self.conn.cursor()
        top = f"TOP {int(limit)}" if limit else ""
        sql = f"SELECT {top} * FROM [{schema}].[{table}]"
        cur.execute(sql)
        cols = [c[0] for c in cur.description]
        rows = [list(r) for r in cur.fetchall()]
        return cols, rows

    def update_cell(self, schema: str, table: str,
                    pk_dict: Dict[str, Any],
                    column: str, new_value: Any) -> int:
        """1セルを UPDATE する（PK で行を特定）"""
        self._require_conn()
        if not pk_dict:
            raise ValueError("主キーが定義されていないテーブルは更新できません。")
        self._validate_identifier(column)

        where_clauses = " AND ".join(
            [f"[{k}] = ?" for k in pk_dict.keys()]
        )
        sql = (f"UPDATE [{schema}].[{table}] "
               f"SET [{column}] = ? WHERE {where_clauses}")
        params = [new_value, *pk_dict.values()]
        cur = self.conn.cursor()
        cur.execute(sql, *params)
        rowcount = cur.rowcount
        return rowcount

    def insert_row(self, schema: str, table: str,
                   data: Dict[str, Any]) -> int:
        """1行 INSERT する"""
        self._require_conn()
        if not data:
            raise ValueError("挿入するデータが空です。")
        cols = list(data.keys())
        for c in cols:
            self._validate_identifier(c)
        placeholders = ", ".join(["?"] * len(cols))
        col_list = ", ".join([f"[{c}]" for c in cols])
        sql = (f"INSERT INTO [{schema}].[{table}] ({col_list}) "
               f"VALUES ({placeholders})")
        cur = self.conn.cursor()
        cur.execute(sql, *data.values())
        return cur.rowcount

    def delete_rows(self, schema: str, table: str,
                    pk_dicts: List[Dict[str, Any]]) -> int:
        """複数行を DELETE する（PK で特定）"""
        self._require_conn()
        if not pk_dicts:
            return 0
        cur = self.conn.cursor()
        total = 0
        for pk in pk_dicts:
            where_clauses = " AND ".join(
                [f"[{k}] = ?" for k in pk.keys()]
            )
            sql = f"DELETE FROM [{schema}].[{table}] WHERE {where_clauses}"
            cur.execute(sql, *pk.values())
            total += cur.rowcount
        return total

    def add_column(self, schema: str, table: str,
                   column: str, sql_type: str,
                   nullable: bool = True) -> None:
        """列を追加する"""
        self._require_conn()
        self._validate_identifier(column)
        self._validate_sql_type(sql_type)
        null_clause = "NULL" if nullable else "NOT NULL"
        sql = (f"ALTER TABLE [{schema}].[{table}] "
               f"ADD [{column}] {sql_type} {null_clause}")
        cur = self.conn.cursor()
        cur.execute(sql)

    def drop_column(self, schema: str, table: str, column: str) -> None:
        """列を削除する"""
        self._require_conn()
        self._validate_identifier(column)
        sql = f"ALTER TABLE [{schema}].[{table}] DROP COLUMN [{column}]"
        cur = self.conn.cursor()
        cur.execute(sql)

    # ── PK 制約管理 ─────────────────────────────────────────
    def list_table_constraints(self, schema: str, table: str) -> Dict[str, Any]:
        """テーブルの制約一覧を取得（UI 用の統合メソッド）

        Returns:
            {
                "primary_key": {"name": str, "columns": [str, ...]} or None,
                "foreign_keys": [
                    {"name": str, "columns": [str, ...],
                     "ref_schema": str, "ref_table": str,
                     "ref_columns": [str, ...]},
                    ...
                ]
            }
        """
        self._require_conn()
        cur = self.conn.cursor()

        # PK（複合PK対応）
        cur.execute(
            """
            SELECT tc.CONSTRAINT_NAME, kcu.COLUMN_NAME
            FROM INFORMATION_SCHEMA.TABLE_CONSTRAINTS tc
            JOIN INFORMATION_SCHEMA.KEY_COLUMN_USAGE kcu
              ON tc.CONSTRAINT_NAME = kcu.CONSTRAINT_NAME
             AND tc.TABLE_SCHEMA   = kcu.TABLE_SCHEMA
            WHERE tc.CONSTRAINT_TYPE = 'PRIMARY KEY'
              AND tc.TABLE_SCHEMA = ? AND tc.TABLE_NAME = ?
            ORDER BY kcu.ORDINAL_POSITION
            """,
            schema, table,
        )
        pk_rows = cur.fetchall()
        pk_info = None
        if pk_rows:
            pk_info = {
                "name": pk_rows[0][0],
                "columns": [r[1] for r in pk_rows],
            }

        # FK（複合FK対応）
        cur.execute(
            """
            SELECT
                fk.name AS fk_name,
                cp.name AS parent_column,
                sr.name AS ref_schema,
                tr.name AS ref_table,
                cr.name AS ref_column,
                fkc.constraint_column_id AS col_order
            FROM sys.foreign_keys AS fk
            JOIN sys.foreign_key_columns AS fkc
              ON fk.object_id = fkc.constraint_object_id
            JOIN sys.tables  AS tp ON fkc.parent_object_id = tp.object_id
            JOIN sys.schemas AS sp ON tp.schema_id = sp.schema_id
            JOIN sys.columns AS cp ON fkc.parent_object_id = cp.object_id
                                   AND fkc.parent_column_id = cp.column_id
            JOIN sys.tables  AS tr ON fkc.referenced_object_id = tr.object_id
            JOIN sys.schemas AS sr ON tr.schema_id = sr.schema_id
            JOIN sys.columns AS cr ON fkc.referenced_object_id = cr.object_id
                                   AND fkc.referenced_column_id = cr.column_id
            WHERE sp.name = ? AND tp.name = ?
            ORDER BY fk.name, fkc.constraint_column_id
            """,
            schema, table,
        )
        fk_dict: Dict[str, Dict[str, Any]] = {}
        for fk_name, p_col, r_schema, r_table, r_col, _ord in cur.fetchall():
            if fk_name not in fk_dict:
                fk_dict[fk_name] = {
                    "name": fk_name,
                    "columns": [],
                    "ref_schema": r_schema,
                    "ref_table": r_table,
                    "ref_columns": [],
                }
            fk_dict[fk_name]["columns"].append(p_col)
            fk_dict[fk_name]["ref_columns"].append(r_col)

        return {
            "primary_key": pk_info,
            "foreign_keys": list(fk_dict.values()),
        }

    def get_primary_key_constraint_name(self, schema: str,
                                        table: str) -> Optional[str]:
        """対象テーブルの PK 制約名を取得（無ければ None）"""
        self._require_conn()
        cur = self.conn.cursor()
        cur.execute(
            """
            SELECT CONSTRAINT_NAME
            FROM INFORMATION_SCHEMA.TABLE_CONSTRAINTS
            WHERE CONSTRAINT_TYPE = 'PRIMARY KEY'
              AND TABLE_SCHEMA = ?
              AND TABLE_NAME   = ?
            """,
            schema, table,
        )
        row = cur.fetchone()
        return row[0] if row else None

    def add_primary_key(self, schema: str, table: str,
                        columns: List[str],
                        constraint_name: Optional[str] = None) -> None:
        """PK 制約を追加する（既存 PK は事前に削除しておくこと）"""
        self._require_conn()
        if not columns:
            raise ValueError("PK 対象列が指定されていません。")
        for c in columns:
            self._validate_identifier(c)

        cn = constraint_name or f"PK_{table}"
        self._validate_identifier(cn)
        col_list = ", ".join(f"[{c}]" for c in columns)
        sql = (f"ALTER TABLE [{schema}].[{table}] "
               f"ADD CONSTRAINT [{cn}] PRIMARY KEY ({col_list})")
        cur = self.conn.cursor()
        cur.execute(sql)

    def drop_primary_key(self, schema: str, table: str) -> None:
        """既存の PK 制約を削除する"""
        self._require_conn()
        cn = self.get_primary_key_constraint_name(schema, table)
        if not cn:
            raise ValueError("削除対象の PK 制約が存在しません。")
        self._validate_identifier(cn)
        sql = f"ALTER TABLE [{schema}].[{table}] DROP CONSTRAINT [{cn}]"
        cur = self.conn.cursor()
        cur.execute(sql)

    # ── FK 制約管理 ─────────────────────────────────────────
    def get_foreign_keys_of_table(self, table: str) -> List[ForeignKey]:
        """指定テーブルが参照元となっている FK 一覧を返す"""
        return [fk for fk in self.get_foreign_keys()
                if fk.parent_table == table]

    def add_foreign_key(self, schema: str, table: str,
                        column: str,
                        ref_schema: str, ref_table: str, ref_column: str,
                        constraint_name: Optional[str] = None) -> None:
        """FK 制約を追加する"""
        self._require_conn()
        for ident in (column, ref_table, ref_column):
            self._validate_identifier(ident)

        cn = constraint_name or f"FK_{table}_{column}_{ref_table}"
        # 制約名は長すぎる可能性があるため切り詰め
        if len(cn) > 120:
            cn = cn[:120]
        self._validate_identifier(cn)
        sql = (
            f"ALTER TABLE [{schema}].[{table}] "
            f"ADD CONSTRAINT [{cn}] FOREIGN KEY ([{column}]) "
            f"REFERENCES [{ref_schema}].[{ref_table}] ([{ref_column}])"
        )
        cur = self.conn.cursor()
        cur.execute(sql)

    def drop_foreign_key(self, schema: str, table: str,
                         constraint_name: str) -> None:
        """FK 制約を削除する"""
        self._require_conn()
        self._validate_identifier(constraint_name)
        sql = (f"ALTER TABLE [{schema}].[{table}] "
               f"DROP CONSTRAINT [{constraint_name}]")
        cur = self.conn.cursor()
        cur.execute(sql)

    def get_pk_and_unique_columns(self, schema: str, table: str) -> List[str]:
        """PK または UNIQUE 制約を持つ列名のリストを返す（FK参照先として有効な列）

        単一列の PK/UNIQUE インデックスに属する列のみを返す。
        複合インデックスの構成列は SQL Server の FK 参照先として無効なため除外する。
        """
        self._require_conn()
        cur = self.conn.cursor()
        cur.execute(
            """
            SELECT DISTINCT c.name
            FROM sys.indexes i
            JOIN sys.index_columns ic ON i.object_id = ic.object_id
                                      AND i.index_id = ic.index_id
            JOIN sys.columns c ON ic.object_id = c.object_id
                               AND ic.column_id = c.column_id
            JOIN sys.tables t ON i.object_id = t.object_id
            JOIN sys.schemas s ON t.schema_id = s.schema_id
            WHERE (i.is_primary_key = 1 OR i.is_unique = 1)
              AND (i.has_filter = 0 OR i.is_primary_key = 1)
              AND s.name = ?
              AND t.name = ?
              AND (
                  SELECT COUNT(*)
                  FROM sys.index_columns ic2
                  WHERE ic2.object_id = i.object_id
                    AND ic2.index_id  = i.index_id
              ) = 1
            ORDER BY c.name
            """,
            schema, table,
        )
        return [row[0] for row in cur.fetchall()]

    # ── 一括 INSERT（Excel インポート用）─────────────────────
    def bulk_insert(self, schema: str, table: str,
                    columns: List[str],
                    rows: List[List[Any]]) -> int:
        """
        複数行を一括 INSERT する（fast_executemany 使用）。

        Args:
            columns: 挿入対象の列名リスト
            rows:    [[v1, v2, ...], [v1, v2, ...], ...] 形式の値リスト
                     各行の値数は columns と一致していること

        Returns:
            挿入された行数
        """
        self._require_conn()
        if not columns:
            raise ValueError("挿入対象の列が指定されていません。")
        if not rows:
            return 0
        for c in columns:
            self._validate_identifier(c)
        for i, row in enumerate(rows):
            if len(row) != len(columns):
                raise ValueError(
                    f"行 {i+1} の値数 ({len(row)}) が列数 ({len(columns)}) と一致しません。"
                )

        placeholders = ", ".join(["?"] * len(columns))
        col_list = ", ".join(f"[{c}]" for c in columns)
        sql = (f"INSERT INTO [{schema}].[{table}] ({col_list}) "
               f"VALUES ({placeholders})")

        cur = self.conn.cursor()
        # fast_executemany を有効化（数千行の取り込みで大幅高速化）
        try:
            cur.fast_executemany = True
        except AttributeError:
            pass

        # rows をタプルのリストに変換（pyodbc 推奨形式）
        param_rows = [tuple(r) for r in rows]
        cur.executemany(sql, param_rows)
        return len(param_rows)

    # ── 一括 UPDATE（コピペ貼り付け用）──────────────────────
    def bulk_update_cells(self, schema: str, table: str,
                          updates: List[Dict[str, Any]]) -> int:
        """
        複数セルを一括更新する（同一トランザクション内）。

        Args:
            updates: [
              {
                "pk":     {pk_col: value, ...},   # 行特定用
                "column": "列名",                  # 更新対象列
                "value":  新しい値,
              },
              ...
            ]

        Returns:
            更新成功行数
        """
        self._require_conn()
        if not updates:
            return 0
        cur = self.conn.cursor()
        total = 0
        for upd in updates:
            pk = upd["pk"]
            col = upd["column"]
            val = upd["value"]
            if not pk:
                raise ValueError("PK が空の更新があります。")
            self._validate_identifier(col)
            where_clauses = " AND ".join(f"[{k}] = ?" for k in pk.keys())
            sql = (f"UPDATE [{schema}].[{table}] "
                   f"SET [{col}] = ? WHERE {where_clauses}")
            params = [val, *pk.values()]
            cur.execute(sql, *params)
            total += cur.rowcount
        return total

    # ── トランザクション制御 ────────────────────────────────
    def commit(self) -> None:
        if self.conn is not None:
            self.conn.commit()

    def rollback(self) -> None:
        if self.conn is not None:
            self.conn.rollback()

    # ── 内部ユーティリティ ──────────────────────────────────
    def _require_conn(self) -> None:
        if self.conn is None:
            raise RuntimeError("データベースに接続されていません。")

    @staticmethod
    def _validate_identifier(name: str) -> None:
        """SQL インジェクション対策：識別子は英数字・アンダースコア・日本語のみ許可"""
        if not name:
            raise ValueError("識別子が空です。")
        # ブラケット・セミコロン・引用符を含むものは拒否
        if re.search(r"[\[\];'\"`\\\x00]", name):
            raise ValueError(f"不正な識別子: {name}")
        if len(name) > 128:
            raise ValueError(f"識別子が長すぎます（128文字以内）: {name}")

    @staticmethod
    def _validate_sql_type(sql_type: str) -> None:
        """データ型指定の簡易バリデーション"""
        allowed_pattern = re.compile(
            r"^(INT|BIGINT|SMALLINT|TINYINT|BIT|"
            r"DECIMAL(\(\s*\d+\s*,\s*\d+\s*\))?|"
            r"NUMERIC(\(\s*\d+\s*,\s*\d+\s*\))?|"
            r"FLOAT|REAL|MONEY|"
            r"DATE|TIME|DATETIME|DATETIME2|SMALLDATETIME|"
            r"CHAR(\(\s*\d+\s*\))?|"
            r"VARCHAR(\(\s*(\d+|MAX)\s*\))?|"
            r"NCHAR(\(\s*\d+\s*\))?|"
            r"NVARCHAR(\(\s*(\d+|MAX)\s*\))?|"
            r"TEXT|NTEXT|"
            r"UNIQUEIDENTIFIER|"
            r"VARBINARY(\(\s*(\d+|MAX)\s*\))?)$",
            re.IGNORECASE,
        )
        if not allowed_pattern.match(sql_type.strip()):
            raise ValueError(f"未対応または不正なデータ型: {sql_type}")
