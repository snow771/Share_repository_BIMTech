# -*- coding: utf-8 -*-
"""
dialogs.py
DBエディタで共通的に使用するダイアログ集
"""

from __future__ import annotations

import tkinter as tk
from tkinter import ttk
from typing import List, Optional, Tuple


# ─── カラーパレット（launcher.py と統一） ────────────────────
BG_DARK        = "#0f1117"
BG_CARD        = "#1a1d27"
BG_CARD2       = "#1e2130"
ACCENT_BLUE    = "#5b8dc0"
ACCENT_TEAL    = "#4aab9a"
ACCENT_PURPLE  = "#9080c8"
TEXT_PRIMARY   = "#e8eaf0"
TEXT_SECONDARY = "#8a8fa8"
TEXT_MUTED     = "#555970"
BORDER_COLOR   = "#2a2d3e"
SUCCESS_COLOR  = "#56a072"
ERROR_COLOR    = "#b06060"
WARN_COLOR     = "#b89040"


# ─── 基底ダイアログ ─────────────────────────────────────────
class BaseDialog(tk.Toplevel):
    """モーダルダイアログの基底クラス"""

    def __init__(self, parent, title: str = "確認", width: int = 460):
        super().__init__(parent)
        self.title(title)
        self.configure(bg=BG_CARD)
        self.resizable(False, False)
        self.result = None

        # サイズと中央配置
        self._width = width
        self._height = 200  # 後で調整
        self.transient(parent)

        # 閉じるボタン無効化（明示的に Yes/No で閉じる）
        self.protocol("WM_DELETE_WINDOW", self._on_cancel)

    def _center_on_parent(self) -> None:
        self.update_idletasks()
        parent = self.master
        pw, ph = parent.winfo_width(), parent.winfo_height()
        px, py = parent.winfo_rootx(), parent.winfo_rooty()
        w, h = self.winfo_width(), self.winfo_height()
        x = px + (pw - w) // 2
        y = py + (ph - h) // 2
        self.geometry(f"+{x}+{y}")

    def _grab(self) -> None:
        self.grab_set()
        self.focus_set()

    def _on_cancel(self):
        self.result = None
        self.destroy()


# ─── 確認ダイアログ（README 仕様） ──────────────────────────
class ConfirmDialog(BaseDialog):
    """
    README 記載の確認ダイアログ:
      ⚠ 確認
      対象：[テーブル名] / [操作内容]
      件数：X 件
      [ Yes ] [ No ]
    """

    def __init__(self, parent, *,
                 title: str = "確認",
                 message: str = "以下の操作を実行しますか？",
                 target: str = "",
                 operation: str = "",
                 count: Optional[int] = None,
                 danger: bool = False):
        super().__init__(parent, title=title, width=480)
        self.danger = danger
        self._build(message, target, operation, count)
        self._center_on_parent()
        self._grab()
        self.wait_window(self)

    def _build(self, message: str, target: str,
               operation: str, count: Optional[int]):
        accent = ERROR_COLOR if self.danger else ACCENT_BLUE
        icon = "⚠" if self.danger else "?"

        # ヘッダー
        hdr = tk.Frame(self, bg=BG_CARD)
        hdr.pack(fill="x", padx=24, pady=(22, 6))
        tk.Label(hdr, text=icon, font=("Yu Gothic UI", 22, "bold"),
                 fg=accent, bg=BG_CARD).pack(side="left", padx=(0, 12))
        tk.Label(hdr, text="確認", font=("Yu Gothic UI", 14, "bold"),
                 fg=TEXT_PRIMARY, bg=BG_CARD).pack(side="left")

        tk.Frame(self, bg=BORDER_COLOR, height=1).pack(
            fill="x", padx=24, pady=(8, 12))

        # メッセージ
        tk.Label(self, text=message, font=("Yu Gothic UI", 10),
                 fg=TEXT_PRIMARY, bg=BG_CARD,
                 wraplength=420, justify="left").pack(
            anchor="w", padx=24, pady=(0, 10))

        # 詳細情報（テーブル名・操作・件数）
        if target or operation or count is not None:
            info = tk.Frame(self, bg=BG_CARD2)
            info.pack(fill="x", padx=24, pady=(0, 14))
            rows: List[Tuple[str, str]] = []
            if target:
                rows.append(("対象", target))
            if operation:
                rows.append(("操作", operation))
            if count is not None:
                rows.append(("件数", f"{count} 件"))
            for i, (k, v) in enumerate(rows):
                tk.Label(info, text=k, font=("Yu Gothic UI", 9),
                         fg=TEXT_MUTED, bg=BG_CARD2, width=8, anchor="w"
                         ).grid(row=i, column=0, padx=(12, 6), pady=4, sticky="w")
                tk.Label(info, text=v, font=("Yu Gothic UI", 9, "bold"),
                         fg=TEXT_PRIMARY, bg=BG_CARD2, anchor="w"
                         ).grid(row=i, column=1, padx=(0, 12), pady=4, sticky="w")

        # ボタン
        btn_row = tk.Frame(self, bg=BG_CARD)
        btn_row.pack(fill="x", padx=24, pady=(0, 22))

        no_btn = tk.Button(
            btn_row, text="No  (キャンセル)",
            font=("Yu Gothic UI", 10),
            fg=TEXT_SECONDARY, bg=BORDER_COLOR,
            activeforeground=TEXT_PRIMARY, activebackground=BG_CARD2,
            relief="flat", bd=0, padx=18, pady=8, cursor="hand2",
            command=self._on_cancel,
        )
        no_btn.pack(side="right")

        yes_btn = tk.Button(
            btn_row, text="Yes  (実行)",
            font=("Yu Gothic UI", 10, "bold"),
            fg="white", bg=accent,
            activeforeground="white", activebackground=accent,
            relief="flat", bd=0, padx=18, pady=8, cursor="hand2",
            command=self._on_ok,
        )
        yes_btn.pack(side="right", padx=(0, 10))

        # キーボード対応
        self.bind("<Return>", lambda e: self._on_ok())
        self.bind("<Escape>", lambda e: self._on_cancel())

    def _on_ok(self):
        self.result = True
        self.destroy()


# ─── 列追加ダイアログ ───────────────────────────────────────
class AddColumnDialog(BaseDialog):
    """列を追加する際の入力ダイアログ"""

    DATA_TYPES = [
        "INT", "BIGINT", "SMALLINT", "BIT",
        "DECIMAL(18,2)", "FLOAT", "MONEY",
        "DATE", "DATETIME", "DATETIME2",
        "NVARCHAR(50)", "NVARCHAR(255)", "NVARCHAR(MAX)",
        "VARCHAR(255)", "CHAR(10)",
        "UNIQUEIDENTIFIER",
    ]

    def __init__(self, parent, table_name: str = ""):
        super().__init__(parent, title="列の追加", width=440)
        self.table_name = table_name
        self._build()
        self._center_on_parent()
        self._grab()
        self.wait_window(self)

    def _build(self):
        # ヘッダー
        tk.Label(self, text="新しい列を追加",
                 font=("Yu Gothic UI", 13, "bold"),
                 fg=TEXT_PRIMARY, bg=BG_CARD).pack(
            anchor="w", padx=24, pady=(22, 4))
        if self.table_name:
            tk.Label(self, text=f"対象テーブル: {self.table_name}",
                     font=("Yu Gothic UI", 9),
                     fg=TEXT_SECONDARY, bg=BG_CARD).pack(
                anchor="w", padx=24)
        tk.Frame(self, bg=BORDER_COLOR, height=1).pack(
            fill="x", padx=24, pady=(12, 16))

        # 列名
        tk.Label(self, text="列名", font=("Yu Gothic UI", 9),
                 fg=TEXT_SECONDARY, bg=BG_CARD).pack(
            anchor="w", padx=24)
        self.var_name = tk.StringVar()
        ent = tk.Entry(self, textvariable=self.var_name,
                       font=("Yu Gothic UI", 10), bg=BG_CARD2,
                       fg=TEXT_PRIMARY, insertbackground=TEXT_PRIMARY,
                       relief="flat", bd=0)
        ent.pack(fill="x", padx=24, pady=(4, 14), ipady=6)
        ent.focus_set()

        # データ型
        tk.Label(self, text="データ型", font=("Yu Gothic UI", 9),
                 fg=TEXT_SECONDARY, bg=BG_CARD).pack(
            anchor="w", padx=24)
        self.var_type = tk.StringVar(value="NVARCHAR(255)")
        cmb = ttk.Combobox(self, textvariable=self.var_type,
                           values=self.DATA_TYPES, state="normal",
                           font=("Yu Gothic UI", 10))
        cmb.pack(fill="x", padx=24, pady=(4, 14))

        # NULL 許可
        self.var_nullable = tk.BooleanVar(value=True)
        chk = tk.Checkbutton(self, text="NULL を許可する",
                             variable=self.var_nullable,
                             font=("Yu Gothic UI", 9),
                             fg=TEXT_PRIMARY, bg=BG_CARD,
                             selectcolor=BG_CARD2,
                             activeforeground=TEXT_PRIMARY,
                             activebackground=BG_CARD)
        chk.pack(anchor="w", padx=22, pady=(0, 16))

        # ボタン
        btn_row = tk.Frame(self, bg=BG_CARD)
        btn_row.pack(fill="x", padx=24, pady=(0, 22))
        tk.Button(btn_row, text="キャンセル",
                  font=("Yu Gothic UI", 10),
                  fg=TEXT_SECONDARY, bg=BORDER_COLOR,
                  activeforeground=TEXT_PRIMARY, activebackground=BG_CARD2,
                  relief="flat", bd=0, padx=18, pady=8, cursor="hand2",
                  command=self._on_cancel).pack(side="right")
        tk.Button(btn_row, text="追加",
                  font=("Yu Gothic UI", 10, "bold"),
                  fg="white", bg=ACCENT_BLUE,
                  activeforeground="white", activebackground=ACCENT_BLUE,
                  relief="flat", bd=0, padx=18, pady=8, cursor="hand2",
                  command=self._on_ok).pack(side="right", padx=(0, 10))

        self.bind("<Return>", lambda e: self._on_ok())
        self.bind("<Escape>", lambda e: self._on_cancel())

    def _on_ok(self):
        name = self.var_name.get().strip()
        dtype = self.var_type.get().strip()
        if not name or not dtype:
            return
        self.result = {
            "name": name,
            "type": dtype,
            "nullable": self.var_nullable.get(),
        }
        self.destroy()


# ─── 行追加ダイアログ ───────────────────────────────────────
class AddRowDialog(BaseDialog):
    """行を追加する際の入力ダイアログ（列ごとに入力欄）"""

    def __init__(self, parent, columns: List[dict], table_name: str = ""):
        # columns: [{"name": ..., "type": ..., "is_pk": ..., "nullable": ...}, ...]
        super().__init__(parent, title="行の追加", width=520)
        self.columns = columns
        self.table_name = table_name
        self.entries: dict = {}
        self._build()
        self._center_on_parent()
        self._grab()
        self.wait_window(self)

    def _build(self):
        tk.Label(self, text="新しい行を追加",
                 font=("Yu Gothic UI", 13, "bold"),
                 fg=TEXT_PRIMARY, bg=BG_CARD).pack(
            anchor="w", padx=24, pady=(22, 4))
        if self.table_name:
            tk.Label(self, text=f"対象テーブル: {self.table_name}",
                     font=("Yu Gothic UI", 9),
                     fg=TEXT_SECONDARY, bg=BG_CARD).pack(
                anchor="w", padx=24)
        tk.Frame(self, bg=BORDER_COLOR, height=1).pack(
            fill="x", padx=24, pady=(12, 12))

        # スクロール可能なエントリー領域
        outer = tk.Frame(self, bg=BG_CARD)
        outer.pack(fill="both", expand=True, padx=24, pady=(0, 12))
        canvas = tk.Canvas(outer, bg=BG_CARD, highlightthickness=0,
                           height=min(360, max(120, len(self.columns) * 56)))
        sb = tk.Scrollbar(outer, orient="vertical", command=canvas.yview,
                          bg=BG_CARD, troughcolor=BG_CARD2, width=8)
        inner = tk.Frame(canvas, bg=BG_CARD)
        inner.bind("<Configure>",
                   lambda e: canvas.configure(scrollregion=canvas.bbox("all")))
        canvas.create_window((0, 0), window=inner, anchor="nw")
        canvas.configure(yscrollcommand=sb.set)
        canvas.pack(side="left", fill="both", expand=True)
        sb.pack(side="right", fill="y")

        # 列ごとの入力欄
        for i, col in enumerate(self.columns):
            row = tk.Frame(inner, bg=BG_CARD)
            row.pack(fill="x", pady=4)
            label_text = col["name"]
            badges = []
            if col.get("is_pk"):
                badges.append("PK")
            if not col.get("nullable", True):
                badges.append("NOT NULL")
            badge_str = f"  [{', '.join(badges)}]" if badges else ""
            tk.Label(row,
                     text=f"{label_text}  ({col.get('type','')}){badge_str}",
                     font=("Yu Gothic UI", 9),
                     fg=TEXT_SECONDARY, bg=BG_CARD).pack(anchor="w")
            var = tk.StringVar()
            ent = tk.Entry(row, textvariable=var,
                           font=("Yu Gothic UI", 10), bg=BG_CARD2,
                           fg=TEXT_PRIMARY, insertbackground=TEXT_PRIMARY,
                           relief="flat", bd=0, width=440)
            ent.pack(fill="x", pady=(2, 0), ipady=5)
            self.entries[col["name"]] = var

        # ボタン
        btn_row = tk.Frame(self, bg=BG_CARD)
        btn_row.pack(fill="x", padx=24, pady=(0, 22))
        tk.Button(btn_row, text="キャンセル",
                  font=("Yu Gothic UI", 10),
                  fg=TEXT_SECONDARY, bg=BORDER_COLOR,
                  activeforeground=TEXT_PRIMARY, activebackground=BG_CARD2,
                  relief="flat", bd=0, padx=18, pady=8, cursor="hand2",
                  command=self._on_cancel).pack(side="right")
        tk.Button(btn_row, text="追加",
                  font=("Yu Gothic UI", 10, "bold"),
                  fg="white", bg=ACCENT_BLUE,
                  activeforeground="white", activebackground=ACCENT_BLUE,
                  relief="flat", bd=0, padx=18, pady=8, cursor="hand2",
                  command=self._on_ok).pack(side="right", padx=(0, 10))

        self.bind("<Escape>", lambda e: self._on_cancel())

    def _on_ok(self):
        data = {}
        for name, var in self.entries.items():
            value = var.get()
            # 空文字は NULL として扱う
            data[name] = value if value != "" else None
        self.result = data
        self.destroy()


# ─── 接続設定ダイアログ ─────────────────────────────────────
class ConnectionDialog(BaseDialog):
    """サーバー名・データベース名を入力させる"""

    def __init__(self, parent,
                 default_server: str = r"localhost\sqlexpress",
                 databases: Optional[List[str]] = None,
                 default_db: Optional[str] = None):
        super().__init__(parent, title="DB 接続設定", width=460)
        self._default_server = default_server
        self._databases = databases or []
        self._default_db = default_db or ""
        self._build()
        self._center_on_parent()
        self._grab()
        self.wait_window(self)

    def _build(self):
        tk.Label(self, text="データベース接続",
                 font=("Yu Gothic UI", 13, "bold"),
                 fg=TEXT_PRIMARY, bg=BG_CARD).pack(
            anchor="w", padx=24, pady=(22, 4))
        tk.Label(self, text="Windows 統合認証で接続します",
                 font=("Yu Gothic UI", 9),
                 fg=TEXT_SECONDARY, bg=BG_CARD).pack(
            anchor="w", padx=24)
        tk.Frame(self, bg=BORDER_COLOR, height=1).pack(
            fill="x", padx=24, pady=(12, 16))

        # サーバー
        tk.Label(self, text="サーバー",
                 font=("Yu Gothic UI", 9), fg=TEXT_SECONDARY, bg=BG_CARD
                 ).pack(anchor="w", padx=24)
        self.var_server = tk.StringVar(value=self._default_server)
        ent_s = tk.Entry(self, textvariable=self.var_server,
                         font=("Yu Gothic UI", 10), bg=BG_CARD2,
                         fg=TEXT_PRIMARY, insertbackground=TEXT_PRIMARY,
                         relief="flat", bd=0)
        ent_s.pack(fill="x", padx=24, pady=(4, 14), ipady=6)

        # データベース
        tk.Label(self, text="データベース（任意 / 後で選択可）",
                 font=("Yu Gothic UI", 9), fg=TEXT_SECONDARY, bg=BG_CARD
                 ).pack(anchor="w", padx=24)
        self.var_db = tk.StringVar(value=self._default_db)
        cmb = ttk.Combobox(self, textvariable=self.var_db,
                           values=self._databases, state="normal",
                           font=("Yu Gothic UI", 10))
        cmb.pack(fill="x", padx=24, pady=(4, 18))

        # ボタン
        btn_row = tk.Frame(self, bg=BG_CARD)
        btn_row.pack(fill="x", padx=24, pady=(0, 22))
        tk.Button(btn_row, text="キャンセル",
                  font=("Yu Gothic UI", 10),
                  fg=TEXT_SECONDARY, bg=BORDER_COLOR,
                  activeforeground=TEXT_PRIMARY, activebackground=BG_CARD2,
                  relief="flat", bd=0, padx=18, pady=8, cursor="hand2",
                  command=self._on_cancel).pack(side="right")
        tk.Button(btn_row, text="接続",
                  font=("Yu Gothic UI", 10, "bold"),
                  fg="white", bg=ACCENT_BLUE,
                  activeforeground="white", activebackground=ACCENT_BLUE,
                  relief="flat", bd=0, padx=18, pady=8, cursor="hand2",
                  command=self._on_ok).pack(side="right", padx=(0, 10))

        self.bind("<Return>", lambda e: self._on_ok())
        self.bind("<Escape>", lambda e: self._on_cancel())

    def _on_ok(self):
        s = self.var_server.get().strip()
        if not s:
            return
        self.result = {
            "server": s,
            "database": self.var_db.get().strip() or None,
        }
        self.destroy()


# ─── 制約管理ダイアログ（PK / FK の設定）────────────────────
class ConstraintManagerDialog(BaseDialog):
    """
    対象テーブルの PK / FK 制約を管理するダイアログ。

    入出力：
      入力:
        - table_name: 対象テーブル名
        - columns:    list[dict]  ({"name", "is_pk", "is_fk"})
        - current_pk: list[str]   現在の PK 列名
        - current_fks: list[dict] 現在の FK 一覧
          [{"fk_name", "column", "ref_table", "ref_column"}, ...]
        - all_tables: list[(schema, table)]  参照先候補
        - get_columns_of: Callable[[str, str], list[str]]  参照先テーブルの列取得

      出力 (self.result):
        {
          "pk_changes":  {"new_pk": list[str] or None},  # None なら PK 削除のみ
          "fk_to_drop":  list[str]   # 削除する FK 制約名
          "fk_to_add":   list[dict]  # 追加する FK
            [{"column", "ref_schema", "ref_table", "ref_column"}, ...]
        }
        変更なしの場合は None
    """

    def __init__(self, parent, *,
                 table_name: str,
                 columns: List[dict],
                 current_pk: List[str],
                 current_fks: List[dict],
                 all_tables: List[Tuple[str, str]],
                 get_columns_of):
        super().__init__(parent, title="制約管理 (PK / FK)", width=620)
        self.table_name = table_name
        self.columns = columns
        self.original_pk = list(current_pk)
        self.original_fks = list(current_fks)
        self.all_tables = all_tables
        self.get_columns_of = get_columns_of

        # 編集中の状態
        self._pk_vars: dict = {}        # column_name -> BooleanVar
        self._fk_to_drop: List[str] = []  # 削除予定 FK 制約名
        self._fk_to_add: List[dict] = []  # 追加予定 FK 定義

        self._build()
        self._center_on_parent()
        self._grab()
        self.wait_window(self)

    def _build(self):
        # ヘッダー
        tk.Label(self, text="制約管理",
                 font=("Yu Gothic UI", 13, "bold"),
                 fg=TEXT_PRIMARY, bg=BG_CARD).pack(
            anchor="w", padx=24, pady=(22, 4))
        tk.Label(self, text=f"対象テーブル: {self.table_name}",
                 font=("Yu Gothic UI", 9),
                 fg=TEXT_SECONDARY, bg=BG_CARD).pack(anchor="w", padx=24)
        tk.Frame(self, bg=BORDER_COLOR, height=1).pack(
            fill="x", padx=24, pady=(12, 12))

        # ── PK セクション ────────────────────────────────────
        pk_frame = tk.LabelFrame(
            self, text=" 主キー (Primary Key) ",
            font=("Yu Gothic UI", 10, "bold"),
            fg=ACCENT_TEAL, bg=BG_CARD, bd=1,
            relief="solid", labelanchor="nw",
            highlightbackground=BORDER_COLOR,
        )
        pk_frame.pack(fill="x", padx=24, pady=(0, 12))

        tk.Label(pk_frame,
                 text="PK にする列をチェックしてください（複合PK可）",
                 font=("Yu Gothic UI", 8),
                 fg=TEXT_MUTED, bg=BG_CARD).pack(anchor="w", padx=12, pady=(6, 4))

        # 列ごとのチェックボックス（スクロール領域）
        pk_box = tk.Frame(pk_frame, bg=BG_CARD)
        pk_box.pack(fill="x", padx=12, pady=(0, 8))
        for col in self.columns:
            name = col["name"]
            var = tk.BooleanVar(value=(name in self.original_pk))
            self._pk_vars[name] = var
            tk.Checkbutton(
                pk_box, text=name, variable=var,
                font=("Yu Gothic UI", 9),
                fg=TEXT_PRIMARY, bg=BG_CARD,
                selectcolor=BG_CARD2,
                activeforeground=TEXT_PRIMARY,
                activebackground=BG_CARD,
            ).pack(anchor="w")

        # ── FK セクション ────────────────────────────────────
        fk_frame = tk.LabelFrame(
            self, text=" 外部キー (Foreign Key) ",
            font=("Yu Gothic UI", 10, "bold"),
            fg=ACCENT_PURPLE, bg=BG_CARD, bd=1,
            relief="solid", labelanchor="nw",
        )
        fk_frame.pack(fill="both", expand=True, padx=24, pady=(0, 12))

        # 既存 FK 一覧
        tk.Label(fk_frame, text="既存の FK 制約:",
                 font=("Yu Gothic UI", 9),
                 fg=TEXT_SECONDARY, bg=BG_CARD).pack(anchor="w", padx=12, pady=(8, 4))

        self.lb_existing = tk.Listbox(
            fk_frame, height=4,
            font=("Yu Gothic UI", 9),
            bg=BG_CARD2, fg=TEXT_PRIMARY,
            selectbackground=ERROR_COLOR, selectforeground="white",
            relief="flat", bd=0, highlightthickness=0,
        )
        self.lb_existing.pack(fill="x", padx=12, pady=(0, 4))
        for fk in self.original_fks:
            self.lb_existing.insert(
                "end",
                f"{fk['fk_name']}:  {fk['column']}  →  {fk['ref_table']}.{fk['ref_column']}"
            )

        tk.Button(
            fk_frame, text="選択した FK を削除予定にする",
            font=("Yu Gothic UI", 9),
            fg="white", bg=ERROR_COLOR,
            activeforeground="white", activebackground=ERROR_COLOR,
            relief="flat", bd=0, padx=12, pady=4, cursor="hand2",
            command=self._mark_fk_drop,
        ).pack(anchor="w", padx=12, pady=(0, 10))

        # 新規 FK 追加
        tk.Frame(fk_frame, bg=BORDER_COLOR, height=1).pack(
            fill="x", padx=12, pady=(4, 8))
        tk.Label(fk_frame, text="新しい FK を追加:",
                 font=("Yu Gothic UI", 9),
                 fg=TEXT_SECONDARY, bg=BG_CARD).pack(anchor="w", padx=12)

        add_grid = tk.Frame(fk_frame, bg=BG_CARD)
        add_grid.pack(fill="x", padx=12, pady=(4, 8))

        # 子側列
        tk.Label(add_grid, text="この列",
                 font=("Yu Gothic UI", 8),
                 fg=TEXT_MUTED, bg=BG_CARD).grid(
            row=0, column=0, sticky="w")
        self.var_child = tk.StringVar()
        cmb_child = ttk.Combobox(
            add_grid, textvariable=self.var_child,
            values=[c["name"] for c in self.columns],
            state="readonly", font=("Yu Gothic UI", 9), width=18,
        )
        cmb_child.grid(row=1, column=0, padx=(0, 6), pady=(2, 4))

        tk.Label(add_grid, text="→ 参照先テーブル",
                 font=("Yu Gothic UI", 8),
                 fg=TEXT_MUTED, bg=BG_CARD).grid(
            row=0, column=1, sticky="w")
        self.var_ref_table = tk.StringVar()
        ref_table_values = [f"{s}.{t}" for s, t in self.all_tables]
        cmb_ref = ttk.Combobox(
            add_grid, textvariable=self.var_ref_table,
            values=ref_table_values, state="readonly",
            font=("Yu Gothic UI", 9), width=24,
        )
        cmb_ref.grid(row=1, column=1, padx=(0, 6), pady=(2, 4))
        cmb_ref.bind("<<ComboboxSelected>>", self._on_ref_table_changed)

        tk.Label(add_grid, text="参照先列",
                 font=("Yu Gothic UI", 8),
                 fg=TEXT_MUTED, bg=BG_CARD).grid(
            row=0, column=2, sticky="w")
        self.var_ref_col = tk.StringVar()
        self.cmb_ref_col = ttk.Combobox(
            add_grid, textvariable=self.var_ref_col,
            values=[], state="readonly",
            font=("Yu Gothic UI", 9), width=18,
        )
        self.cmb_ref_col.grid(row=1, column=2, padx=(0, 6), pady=(2, 4))

        self._add_fk_btn = tk.Button(
            add_grid, text="＋ 追加予定に登録",
            font=("Yu Gothic UI", 9),
            fg="white", bg=ACCENT_BLUE,
            relief="flat", bd=0, padx=10, pady=4, cursor="hand2",
            command=self._add_fk_pending,
        )
        self._add_fk_btn.grid(row=1, column=3, padx=(6, 0))

        # 追加予定 FK 一覧
        tk.Label(fk_frame, text="追加予定:",
                 font=("Yu Gothic UI", 9),
                 fg=TEXT_SECONDARY, bg=BG_CARD).pack(anchor="w", padx=12, pady=(8, 2))
        self.lb_pending = tk.Listbox(
            fk_frame, height=3,
            font=("Yu Gothic UI", 9),
            bg=BG_CARD2, fg=ACCENT_BLUE,
            selectbackground=BORDER_COLOR,
            relief="flat", bd=0, highlightthickness=0,
        )
        self.lb_pending.pack(fill="x", padx=12, pady=(0, 4))
        tk.Button(
            fk_frame, text="選択した予定を取り消し",
            font=("Yu Gothic UI", 9),
            fg=TEXT_SECONDARY, bg=BORDER_COLOR,
            relief="flat", bd=0, padx=10, pady=4, cursor="hand2",
            command=self._remove_fk_pending,
        ).pack(anchor="w", padx=12, pady=(0, 10))

        # ── ボタン ───────────────────────────────────────────
        btn_row = tk.Frame(self, bg=BG_CARD)
        btn_row.pack(fill="x", padx=24, pady=(0, 22))
        tk.Button(
            btn_row, text="キャンセル",
            font=("Yu Gothic UI", 10),
            fg=TEXT_SECONDARY, bg=BORDER_COLOR,
            activeforeground=TEXT_PRIMARY, activebackground=BG_CARD2,
            relief="flat", bd=0, padx=18, pady=8, cursor="hand2",
            command=self._on_cancel,
        ).pack(side="right")
        tk.Button(
            btn_row, text="変更を確定",
            font=("Yu Gothic UI", 10, "bold"),
            fg="white", bg=ACCENT_BLUE,
            activeforeground="white", activebackground=ACCENT_BLUE,
            relief="flat", bd=0, padx=18, pady=8, cursor="hand2",
            command=self._on_ok,
        ).pack(side="right", padx=(0, 10))

    def _on_ref_table_changed(self, _event=None):
        """参照先テーブルが変更されたら、PK/UNIQUE 列のみを取得して反映"""
        sel = self.var_ref_table.get()
        if not sel or "." not in sel:
            self.cmb_ref_col["values"] = []
            self.var_ref_col.set("")
            self._add_fk_btn.config(state="disabled")
            return
        ref_schema, ref_table = sel.split(".", 1)
        try:
            cols = self.get_columns_of(ref_schema, ref_table)
        except Exception:
            cols = []
        if cols:
            self.cmb_ref_col["values"] = cols
            self.var_ref_col.set(cols[0])
            self._add_fk_btn.config(state="normal")
        else:
            self.cmb_ref_col["values"] = ["(有効な参照先列がありません)"]
            self.var_ref_col.set("(有効な参照先列がありません)")
            self._add_fk_btn.config(state="disabled")

    def _mark_fk_drop(self):
        sel = self.lb_existing.curselection()
        if not sel:
            return
        for idx in sel:
            fk = self.original_fks[idx]
            if fk["fk_name"] not in self._fk_to_drop:
                self._fk_to_drop.append(fk["fk_name"])
                self.lb_existing.itemconfig(idx, fg=ERROR_COLOR)

    def _add_fk_pending(self):
        col = self.var_child.get().strip()
        ref_sel = self.var_ref_table.get().strip()
        ref_col = self.var_ref_col.get().strip()
        if not (col and ref_sel and ref_col):
            return
        if ref_col == "(有効な参照先列がありません)":
            return
        ref_schema, ref_table = ref_sel.split(".", 1)
        entry = {
            "column": col,
            "ref_schema": ref_schema,
            "ref_table": ref_table,
            "ref_column": ref_col,
        }
        self._fk_to_add.append(entry)
        self.lb_pending.insert(
            "end",
            f"{col}  →  {ref_table}.{ref_col}"
        )
        # フォームクリア
        self.var_child.set("")
        self.var_ref_table.set("")
        self.var_ref_col.set("")
        self.cmb_ref_col["values"] = []

    def _remove_fk_pending(self):
        sel = self.lb_pending.curselection()
        if not sel:
            return
        for idx in reversed(sel):
            del self._fk_to_add[idx]
            self.lb_pending.delete(idx)

    def _on_ok(self):
        new_pk = [name for name, var in self._pk_vars.items() if var.get()]
        # PK が変更されたかチェック
        pk_changed = sorted(new_pk) != sorted(self.original_pk)

        if not pk_changed and not self._fk_to_drop and not self._fk_to_add:
            # 何も変更がない場合
            self.result = None
            self.destroy()
            return

        self.result = {
            "pk_changed":  pk_changed,
            "new_pk":      new_pk,
            "old_pk":      self.original_pk,
            "fk_to_drop":  list(self._fk_to_drop),
            "fk_to_add":   list(self._fk_to_add),
        }
        self.destroy()


# ─── Excel インポート プレビューダイアログ ──────────────────
class ExcelImportPreviewDialog(BaseDialog):
    """
    Excel から読み込んだデータをプレビューし、列マッピングを設定するダイアログ。

    入力:
      file_path:  読込元 Excel ファイルパス
      table_name: 取込先テーブル名
      table_columns: list[dict]  ({"name", "is_pk", "nullable"})
      excel_headers: list[str]   Excel 1行目の列名
      excel_preview_rows: list[list]   Excel 先頭数行のデータ
      total_rows: int  Excel の総データ行数

    出力 (self.result):
      {
        "mapping": {db_col: excel_col_index or None},  # 列マッピング
        "total_rows": int,
      }
      キャンセル時は None
    """

    def __init__(self, parent, *,
                 file_path: str,
                 table_name: str,
                 table_columns: List[dict],
                 excel_headers: List[str],
                 excel_preview_rows: List[list],
                 total_rows: int):
        super().__init__(parent, title="Excel インポート", width=820)
        self.file_path = file_path
        self.table_name = table_name
        self.table_columns = table_columns
        self.excel_headers = excel_headers
        self.excel_preview_rows = excel_preview_rows
        self.total_rows = total_rows
        self._mapping_vars: dict = {}  # db_col -> StringVar

        self._build()
        self._center_on_parent()
        self._grab()
        self.wait_window(self)

    def _build(self):
        import os
        # ヘッダー
        tk.Label(self, text="Excel インポート",
                 font=("Yu Gothic UI", 13, "bold"),
                 fg=TEXT_PRIMARY, bg=BG_CARD).pack(
            anchor="w", padx=24, pady=(22, 4))
        tk.Label(self, text=f"取込先: {self.table_name}",
                 font=("Yu Gothic UI", 9),
                 fg=TEXT_SECONDARY, bg=BG_CARD).pack(anchor="w", padx=24)
        tk.Label(self,
                 text=f"ファイル: {os.path.basename(self.file_path)}  "
                      f"({self.total_rows} 行)",
                 font=("Yu Gothic UI", 9),
                 fg=TEXT_SECONDARY, bg=BG_CARD).pack(anchor="w", padx=24)
        tk.Frame(self, bg=BORDER_COLOR, height=1).pack(
            fill="x", padx=24, pady=(12, 12))

        # ── 列マッピング ────────────────────────────────────
        tk.Label(self, text="列マッピング（DB列 ← Excel列）",
                 font=("Yu Gothic UI", 10, "bold"),
                 fg=ACCENT_BLUE, bg=BG_CARD).pack(anchor="w", padx=24, pady=(0, 4))
        tk.Label(self,
                 text="Excel 1行目のヘッダーと自動マッチします。「(未使用)」で取込除外。",
                 font=("Yu Gothic UI", 8),
                 fg=TEXT_MUTED, bg=BG_CARD).pack(anchor="w", padx=24, pady=(0, 6))

        # スクロール可能なマッピングエリア
        map_outer = tk.Frame(self, bg=BG_CARD)
        map_outer.pack(fill="x", padx=24, pady=(0, 10))
        canvas = tk.Canvas(map_outer, bg=BG_CARD, highlightthickness=0,
                           height=min(220, 32 * len(self.table_columns) + 8))
        sb = tk.Scrollbar(map_outer, orient="vertical",
                          command=canvas.yview,
                          bg=BG_CARD, troughcolor=BG_CARD2, width=8)
        inner = tk.Frame(canvas, bg=BG_CARD)
        inner.bind("<Configure>",
                   lambda e: canvas.configure(scrollregion=canvas.bbox("all")))
        canvas.create_window((0, 0), window=inner, anchor="nw")
        canvas.configure(yscrollcommand=sb.set)
        canvas.pack(side="left", fill="x", expand=True)
        sb.pack(side="right", fill="y")

        excel_choices = ["(未使用)"] + list(self.excel_headers)

        # 各DB列に対してマッピング選択
        for col in self.table_columns:
            row = tk.Frame(inner, bg=BG_CARD)
            row.pack(fill="x", pady=2)
            badge = ""
            if col.get("is_pk"):
                badge += "  🔑PK"
            if not col.get("nullable", True):
                badge += "  [NOT NULL]"
            tk.Label(row,
                     text=f"{col['name']}  ({col.get('type','')}){badge}",
                     font=("Yu Gothic UI", 9),
                     fg=TEXT_PRIMARY, bg=BG_CARD,
                     width=42, anchor="w").pack(side="left", padx=(8, 6))

            # 自動マッチング（同名 or 大文字小文字を無視して一致）
            default = "(未使用)"
            for h in self.excel_headers:
                if h is None:
                    continue
                if str(h).strip().lower() == col["name"].lower():
                    default = h
                    break

            var = tk.StringVar(value=default)
            self._mapping_vars[col["name"]] = var
            cmb = ttk.Combobox(row, textvariable=var,
                               values=excel_choices, state="readonly",
                               font=("Yu Gothic UI", 9), width=28)
            cmb.pack(side="left", padx=(0, 8))

        # ── プレビュー ───────────────────────────────────────
        tk.Label(self, text="プレビュー（先頭 5 行）",
                 font=("Yu Gothic UI", 10, "bold"),
                 fg=ACCENT_BLUE, bg=BG_CARD).pack(anchor="w", padx=24, pady=(4, 4))

        prev_frame = tk.Frame(self, bg=BG_CARD2,
                              highlightbackground=BORDER_COLOR,
                              highlightthickness=1)
        prev_frame.pack(fill="both", expand=True, padx=24, pady=(0, 10))

        tv = ttk.Treeview(prev_frame, show="headings",
                          height=min(6, len(self.excel_preview_rows) + 1),
                          columns=list(range(len(self.excel_headers))))
        for i, h in enumerate(self.excel_headers):
            tv.heading(i, text=str(h) if h is not None else f"列{i+1}")
            tv.column(i, width=110, anchor="w")
        for row in self.excel_preview_rows[:5]:
            display = ["" if v is None else str(v) for v in row]
            # 列数を揃える
            while len(display) < len(self.excel_headers):
                display.append("")
            tv.insert("", "end", values=display)
        tv.pack(fill="both", expand=True, padx=4, pady=4)

        # ── 注意書き ─────────────────────────────────────────
        tk.Label(
            self,
            text="※ インポートは追加（INSERT）です。既存データとの PK 重複時はエラーで全件ロールバックします。",
            font=("Yu Gothic UI", 8),
            fg=WARN_COLOR, bg=BG_CARD,
            wraplength=760, justify="left",
        ).pack(anchor="w", padx=24, pady=(0, 6))

        # ── ボタン ───────────────────────────────────────────
        btn_row = tk.Frame(self, bg=BG_CARD)
        btn_row.pack(fill="x", padx=24, pady=(0, 22))
        tk.Button(
            btn_row, text="キャンセル",
            font=("Yu Gothic UI", 10),
            fg=TEXT_SECONDARY, bg=BORDER_COLOR,
            activeforeground=TEXT_PRIMARY, activebackground=BG_CARD2,
            relief="flat", bd=0, padx=18, pady=8, cursor="hand2",
            command=self._on_cancel,
        ).pack(side="right")
        tk.Button(
            btn_row, text=f"取込実行 ({self.total_rows} 行)",
            font=("Yu Gothic UI", 10, "bold"),
            fg="white", bg=ACCENT_BLUE,
            activeforeground="white", activebackground=ACCENT_BLUE,
            relief="flat", bd=0, padx=18, pady=8, cursor="hand2",
            command=self._on_ok,
        ).pack(side="right", padx=(0, 10))

    def _on_ok(self):
        # Excel列名 -> インデックスのマップを作成
        header_to_idx = {h: i for i, h in enumerate(self.excel_headers)
                         if h is not None}
        mapping: Dict[str, Optional[int]] = {}
        for db_col, var in self._mapping_vars.items():
            choice = var.get()
            if choice == "(未使用)" or choice not in header_to_idx:
                mapping[db_col] = None
            else:
                mapping[db_col] = header_to_idx[choice]
        # 少なくとも1列マッピングされていることを確認
        if not any(v is not None for v in mapping.values()):
            messagebox_warn = tk.Toplevel(self)
            messagebox_warn.title("警告")
            messagebox_warn.configure(bg=BG_CARD)
            tk.Label(messagebox_warn,
                     text="少なくとも1列のマッピングが必要です。",
                     font=("Yu Gothic UI", 10),
                     fg=WARN_COLOR, bg=BG_CARD).pack(padx=24, pady=20)
            tk.Button(messagebox_warn, text="OK",
                      font=("Yu Gothic UI", 9),
                      fg="white", bg=ACCENT_BLUE, relief="flat", bd=0,
                      padx=20, pady=4,
                      command=messagebox_warn.destroy).pack(pady=(0, 14))
            messagebox_warn.grab_set()
            return

        self.result = {
            "mapping": mapping,
            "total_rows": self.total_rows,
        }
        self.destroy()


# ─── ペースト確認ダイアログ ──────────────────────────────────
class PasteConfirmDialog(BaseDialog):
    """
    クリップボードのペースト内容を確認するダイアログ。

    入力:
      table_name:       対象テーブル名
      target_columns:   list[str]  貼り付け先の列名（左から）
      target_rows_pks:  list[dict] 貼り付け先の行PK（上から）
      paste_matrix:     list[list[str]]  ペースト内容
      skipped_pk_cells: int   PK列に当たっていてスキップされる予定のセル数
      excess_cols:      int   target_columns の右端を超える列数
      excess_rows:      int   target_rows_pks の下端を超える行数

    出力 (self.result):
      True = 実行 / None = キャンセル
    """

    def __init__(self, parent, *,
                 table_name: str,
                 target_columns: List[str],
                 target_rows_count: int,
                 paste_rows: int,
                 paste_cols: int,
                 total_cells: int,
                 skipped_pk_cells: int = 0,
                 excess_cols: int = 0,
                 excess_rows: int = 0):
        super().__init__(parent, title="ペースト確認", width=520)
        self.table_name = table_name
        self.target_columns = target_columns
        self.target_rows_count = target_rows_count
        self.paste_rows = paste_rows
        self.paste_cols = paste_cols
        self.total_cells = total_cells
        self.skipped_pk_cells = skipped_pk_cells
        self.excess_cols = excess_cols
        self.excess_rows = excess_rows
        self._build()
        self._center_on_parent()
        self._grab()
        self.wait_window(self)

    def _build(self):
        # ヘッダー
        hdr = tk.Frame(self, bg=BG_CARD)
        hdr.pack(fill="x", padx=24, pady=(22, 6))
        tk.Label(hdr, text="📋", font=("Yu Gothic UI", 22, "bold"),
                 fg=ACCENT_BLUE, bg=BG_CARD).pack(side="left", padx=(0, 12))
        tk.Label(hdr, text="ペースト確認", font=("Yu Gothic UI", 14, "bold"),
                 fg=TEXT_PRIMARY, bg=BG_CARD).pack(side="left")

        tk.Frame(self, bg=BORDER_COLOR, height=1).pack(
            fill="x", padx=24, pady=(8, 12))

        tk.Label(self,
                 text="以下の内容でセルを一括更新します。よろしいですか？",
                 font=("Yu Gothic UI", 10),
                 fg=TEXT_PRIMARY, bg=BG_CARD,
                 wraplength=460, justify="left").pack(
            anchor="w", padx=24, pady=(0, 10))

        # 詳細
        info = tk.Frame(self, bg=BG_CARD2)
        info.pack(fill="x", padx=24, pady=(0, 12))

        cols_display = ", ".join(self.target_columns[:5])
        if len(self.target_columns) > 5:
            cols_display += f" ... 他 {len(self.target_columns) - 5} 列"

        rows = [
            ("対象テーブル", self.table_name),
            ("貼り付け範囲", f"{self.paste_rows} 行 × {self.paste_cols} 列  ({self.total_cells} セル)"),
            ("対象列",      cols_display),
            ("対象行数",    f"{self.target_rows_count} 行"),
        ]
        if self.skipped_pk_cells > 0:
            rows.append(("PK列スキップ", f"{self.skipped_pk_cells} セル（PK列は更新されません）"))
        if self.excess_cols > 0:
            rows.append(("超過列",     f"{self.excess_cols} 列が範囲外（無視されます）"))
        if self.excess_rows > 0:
            rows.append(("超過行",     f"{self.excess_rows} 行が範囲外（無視されます）"))

        for i, (k, v) in enumerate(rows):
            tk.Label(info, text=k, font=("Yu Gothic UI", 9),
                     fg=TEXT_MUTED, bg=BG_CARD2, width=14, anchor="w"
                     ).grid(row=i, column=0, padx=(12, 6), pady=4, sticky="w")
            tk.Label(info, text=v, font=("Yu Gothic UI", 9, "bold"),
                     fg=TEXT_PRIMARY, bg=BG_CARD2, anchor="w",
                     wraplength=320, justify="left"
                     ).grid(row=i, column=1, padx=(0, 12), pady=4, sticky="w")

        tk.Label(self,
                 text="※ この操作はトランザクション内で実行されます。1セルでも失敗した場合は全件ロールバックされます。",
                 font=("Yu Gothic UI", 8),
                 fg=WARN_COLOR, bg=BG_CARD,
                 wraplength=460, justify="left").pack(
            anchor="w", padx=24, pady=(0, 12))

        # ボタン
        btn_row = tk.Frame(self, bg=BG_CARD)
        btn_row.pack(fill="x", padx=24, pady=(0, 22))
        tk.Button(
            btn_row, text="No  (キャンセル)",
            font=("Yu Gothic UI", 10),
            fg=TEXT_SECONDARY, bg=BORDER_COLOR,
            relief="flat", bd=0, padx=18, pady=8, cursor="hand2",
            command=self._on_cancel,
        ).pack(side="right")
        tk.Button(
            btn_row, text=f"Yes  (一括更新 {self.total_cells - self.skipped_pk_cells} セル)",
            font=("Yu Gothic UI", 10, "bold"),
            fg="white", bg=ACCENT_BLUE,
            relief="flat", bd=0, padx=18, pady=8, cursor="hand2",
            command=self._on_ok,
        ).pack(side="right", padx=(0, 10))

        self.bind("<Return>", lambda e: self._on_ok())
        self.bind("<Escape>", lambda e: self._on_cancel())

    def _on_ok(self):
        self.result = True
        self.destroy()
