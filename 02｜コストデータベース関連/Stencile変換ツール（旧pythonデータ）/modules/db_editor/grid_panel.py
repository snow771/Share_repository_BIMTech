# -*- coding: utf-8 -*-
"""
grid_panel.py
Excelライクなデータグリッドパネル

主な機能：
  - Treeview ベースの表示
  - セルをダブルクリックでインライン編集
  - 行/列の追加・削除
  - PK/FK 制約の管理
  - 検索フィルタ
  - Ctrl+C / Ctrl+V によるコピー・ペースト
  - Excel ファイルからの一括インポート
  - 選択列を ER図パネルへ通知（コールバック）
  - すべての変更操作の前に確認ダイアログを表示
"""

from __future__ import annotations

import tkinter as tk
from tkinter import ttk, filedialog, messagebox
from pathlib import Path
from typing import Any, Callable, Dict, List, Optional, Tuple

from .db_connection import DatabaseConnection, TableSchema
from .dialogs import (
    BG_DARK, BG_CARD, BG_CARD2, ACCENT_BLUE, ACCENT_TEAL, ACCENT_PURPLE,
    TEXT_PRIMARY, TEXT_SECONDARY, TEXT_MUTED, BORDER_COLOR,
    SUCCESS_COLOR, ERROR_COLOR, WARN_COLOR,
    ConfirmDialog, AddColumnDialog, AddRowDialog,
    ConstraintManagerDialog, ExcelImportPreviewDialog, PasteConfirmDialog,
)


class GridPanel(tk.Frame):
    """データグリッドパネル"""

    def __init__(self, parent,
                 db: DatabaseConnection,
                 on_column_selected: Optional[Callable[[str, str], None]] = None,
                 on_status: Optional[Callable[[str, str], None]] = None,
                 on_schema_changed: Optional[Callable[[], None]] = None,
                 **kwargs):
        super().__init__(parent, bg=BG_CARD, **kwargs)
        self.db = db
        self.on_column_selected = on_column_selected   # (table, column) -> None
        self.on_status = on_status                     # (level, message) -> None
        self.on_schema_changed = on_schema_changed     # () -> None  (PK/FK 変更時)

        self.current_schema: Optional[TableSchema] = None
        self.current_columns: List[str] = []
        self.current_rows: List[List[Any]] = []
        self._filtered_iids: List[str] = []
        self._row_pk_map: Dict[str, Dict[str, Any]] = {}  # iid -> PK dict
        self._editor: Optional[tk.Entry] = None
        # 選択中のセル（最後にクリックされたセル）
        self._last_clicked_iid: Optional[str] = None
        self._last_clicked_col_idx: Optional[int] = None

        self.configure(highlightbackground=BORDER_COLOR, highlightthickness=1)
        self._build()

    # ── UI 構築 ─────────────────────────────────────────────
    def _build(self):
        # ─ ヘッダー（テーブル名 + 操作ボタン）─
        header = tk.Frame(self, bg=BG_CARD)
        header.pack(fill="x", padx=14, pady=(12, 8))

        self.lbl_table = tk.Label(
            header, text="（テーブル未選択）",
            font=("Yu Gothic UI", 12, "bold"),
            fg=TEXT_PRIMARY, bg=BG_CARD,
        )
        self.lbl_table.pack(side="left")

        self.lbl_meta = tk.Label(
            header, text="",
            font=("Yu Gothic UI", 9),
            fg=TEXT_SECONDARY, bg=BG_CARD,
        )
        self.lbl_meta.pack(side="left", padx=(14, 0))

        # ─ 検索バー + ボタン群 ─
        tools = tk.Frame(self, bg=BG_CARD)
        tools.pack(fill="x", padx=14, pady=(0, 8))

        tk.Label(tools, text="🔍", font=("Yu Gothic UI", 10),
                 fg=TEXT_MUTED, bg=BG_CARD).pack(side="left")
        self.var_search = tk.StringVar()
        self.var_search.trace_add("write", lambda *a: self._apply_filter())
        ent = tk.Entry(tools, textvariable=self.var_search,
                       font=("Yu Gothic UI", 9), bg=BG_CARD2,
                       fg=TEXT_PRIMARY, insertbackground=TEXT_PRIMARY,
                       relief="flat", bd=0, width=20)
        ent.pack(side="left", padx=(6, 10), ipady=4)

        # 操作ボタン（左から右へ：基本操作→構造変更→取込）
        self._make_btn(tools, "＋ 行",       self._on_add_row,      ACCENT_BLUE).pack(side="left", padx=2)
        self._make_btn(tools, "－ 行",       self._on_delete_rows,  ERROR_COLOR).pack(side="left", padx=2)
        self._make_btn(tools, "＋ 列",       self._on_add_column,   ACCENT_TEAL).pack(side="left", padx=2)
        self._make_btn(tools, "－ 列",       self._on_drop_column,  ERROR_COLOR).pack(side="left", padx=2)
        self._make_btn(tools, "🔑 制約",     self._on_manage_constraints, ACCENT_PURPLE).pack(side="left", padx=2)
        self._make_btn(tools, "📥 Excel取込", self._on_import_excel, ACCENT_TEAL).pack(side="left", padx=2)
        self._make_btn(tools, "↻ 更新",     self._on_refresh,      BORDER_COLOR,
                       fg=TEXT_SECONDARY).pack(side="right", padx=2)

        # ─ Treeview 本体 ─
        body = tk.Frame(self, bg=BG_CARD2)
        body.pack(fill="both", expand=True, padx=14, pady=(0, 12))

        # スタイル
        style = ttk.Style()
        try:
            style.theme_use("clam")
        except tk.TclError:
            pass
        style.configure("DBEditor.Treeview",
                        background=BG_CARD2,
                        foreground=TEXT_PRIMARY,
                        fieldbackground=BG_CARD2,
                        bordercolor=BORDER_COLOR,
                        rowheight=24,
                        font=("Yu Gothic UI", 9))
        style.configure("DBEditor.Treeview.Heading",
                        background=BG_CARD,
                        foreground=TEXT_SECONDARY,
                        font=("Yu Gothic UI", 9, "bold"),
                        relief="flat")
        style.map("DBEditor.Treeview",
                  background=[("selected", ACCENT_BLUE)],
                  foreground=[("selected", "white")])
        style.map("DBEditor.Treeview.Heading",
                  background=[("active", BG_CARD2)])

        self.tree = ttk.Treeview(body, show="headings",
                                 style="DBEditor.Treeview",
                                 selectmode="extended")
        ysb = ttk.Scrollbar(body, orient="vertical", command=self.tree.yview)
        xsb = ttk.Scrollbar(body, orient="horizontal", command=self.tree.xview)
        self.tree.configure(yscrollcommand=ysb.set, xscrollcommand=xsb.set)
        self.tree.grid(row=0, column=0, sticky="nsew")
        ysb.grid(row=0, column=1, sticky="ns")
        xsb.grid(row=1, column=0, sticky="ew")
        body.rowconfigure(0, weight=1)
        body.columnconfigure(0, weight=1)

        # イベント
        self.tree.bind("<Double-1>", self._on_double_click)
        self.tree.bind("<Button-1>", self._on_single_click, add="+")
        self.tree.bind("<<TreeviewSelect>>", self._on_select, add="+")
        # コピー・ペースト
        self.tree.bind("<Control-c>", self._on_copy)
        self.tree.bind("<Control-C>", self._on_copy)
        self.tree.bind("<Control-v>", self._on_paste)
        self.tree.bind("<Control-V>", self._on_paste)
        # Delete キーで選択行削除
        self.tree.bind("<Delete>", lambda e: self._on_delete_rows())

        # ステータスバー
        self.lbl_status = tk.Label(self, text="",
                                   font=("Yu Gothic UI", 8),
                                   fg=TEXT_MUTED, bg=BG_CARD, anchor="w")
        self.lbl_status.pack(fill="x", padx=14, pady=(0, 8))

    def _make_btn(self, parent, text, command, bg, fg="white"):
        return tk.Button(parent, text=text,
                         font=("Yu Gothic UI", 9),
                         fg=fg, bg=bg,
                         activeforeground=fg, activebackground=bg,
                         relief="flat", bd=0, padx=10, pady=4,
                         cursor="hand2", command=command)

    # ── テーブルロード ──────────────────────────────────────
    def load_table(self, schema_name: str, table_name: str) -> None:
        """テーブルを読み込んで表示"""
        try:
            ts = self.db.get_table_schema(schema_name, table_name)
            cols, rows = self.db.fetch_data(schema_name, table_name, limit=1000)
        except Exception as e:
            self._status("error", f"テーブル読み込みエラー: {e}")
            messagebox.showerror("エラー", f"テーブルを読み込めませんでした:\n{e}",
                                 parent=self)
            return

        self.current_schema = ts
        self.current_columns = cols
        self.current_rows = rows

        self._refresh_tree()
        pk_str = ", ".join(ts.primary_keys) if ts.primary_keys else "なし"
        self.lbl_table.config(text=ts.full_name)
        self.lbl_meta.config(
            text=f"{len(rows)} 行 / {len(cols)} 列  ｜  PK: {pk_str}")
        self._status("ok", f"{ts.full_name} を読み込みました")

    def _refresh_tree(self):
        # 既存項目クリア
        for iid in self.tree.get_children():
            self.tree.delete(iid)
        self._row_pk_map.clear()

        # 列定義
        self.tree.configure(columns=self.current_columns)
        for col in self.current_columns:
            heading = col
            # PK/FK バッジ
            if self.current_schema:
                for c in self.current_schema.columns:
                    if c.name == col:
                        marks = []
                        if c.is_primary_key:
                            marks.append("🔑")
                        if c.is_foreign_key:
                            marks.append("FK")
                        if marks:
                            heading = f"{col} {' '.join(marks)}"
                        break
            self.tree.heading(col, text=heading,
                              command=lambda c=col: self._on_header_click(c))
            self.tree.column(col, width=140, anchor="w", stretch=False)

        # 行データ
        for i, row in enumerate(self.current_rows):
            iid = f"row_{i}"
            display = ["" if v is None else str(v) for v in row]
            self.tree.insert("", "end", iid=iid, values=display)
            # PK 辞書を構築
            if self.current_schema and self.current_schema.primary_keys:
                pk = {}
                for pk_col in self.current_schema.primary_keys:
                    idx = self.current_columns.index(pk_col)
                    pk[pk_col] = row[idx]
                self._row_pk_map[iid] = pk

        self._apply_filter()

    # ── フィルタ ─────────────────────────────────────────────
    def _apply_filter(self):
        query = self.var_search.get().strip().lower()
        if not query:
            # 全表示
            for iid in self.tree.get_children():
                # detach されている場合に備えて reattach
                self.tree.reattach(iid, "", "end")
            return
        # 該当しないものを detach
        for iid in list(self._row_pk_map.keys()):
            values = self.tree.item(iid, "values")
            if any(query in str(v).lower() for v in values):
                self.tree.reattach(iid, "", "end")
            else:
                self.tree.detach(iid)

    # ── ヘッダクリック（列選択）─────────────────────────────
    def _on_header_click(self, column: str):
        if not self.current_schema:
            return
        if self.on_column_selected:
            self.on_column_selected(self.current_schema.name, column)
        self._status("info", f"列を選択: {column}（ER図でリレーションを表示）")

    # ── セルクリック（列選択検出 + ペースト起点記録）─────
    def _on_single_click(self, event):
        if not self.current_schema:
            return
        region = self.tree.identify("region", event.x, event.y)
        if region in ("cell", "heading"):
            col_id = self.tree.identify_column(event.x)
            if col_id and col_id.startswith("#"):
                try:
                    idx = int(col_id[1:]) - 1
                    if 0 <= idx < len(self.current_columns):
                        column_name = self.current_columns[idx]
                        # ペースト起点として記録
                        if region == "cell":
                            iid = self.tree.identify_row(event.y)
                            if iid:
                                self._last_clicked_iid = iid
                                self._last_clicked_col_idx = idx
                        if self.on_column_selected:
                            self.on_column_selected(
                                self.current_schema.name, column_name)
                except ValueError:
                    pass

    def _on_select(self, event):
        # 選択行数をステータスに表示
        sel = self.tree.selection()
        if sel:
            self._status("info", f"{len(sel)} 行 選択中")

    # ── セル編集 ─────────────────────────────────────────────
    def _on_double_click(self, event):
        if not self.current_schema:
            return
        region = self.tree.identify("region", event.x, event.y)
        if region != "cell":
            return
        iid = self.tree.identify_row(event.y)
        col_id = self.tree.identify_column(event.x)
        if not iid or not col_id:
            return

        # PK が無いテーブルは編集不可
        if not self.current_schema.primary_keys:
            messagebox.showwarning(
                "編集不可",
                "このテーブルには主キーがないため、安全に編集できません。",
                parent=self,
            )
            return

        col_idx = int(col_id[1:]) - 1
        column_name = self.current_columns[col_idx]

        # PK 列は編集禁止
        if column_name in self.current_schema.primary_keys:
            messagebox.showwarning(
                "編集不可",
                f"列 '{column_name}' は主キーのため、編集できません。",
                parent=self,
            )
            return

        self._open_cell_editor(iid, col_id, col_idx, column_name)

    def _open_cell_editor(self, iid: str, col_id: str,
                          col_idx: int, column_name: str):
        # 編集中のセルの座標を取得
        bbox = self.tree.bbox(iid, col_id)
        if not bbox:
            return
        x, y, w, h = bbox
        current_value = self.tree.item(iid, "values")[col_idx]

        # Entry を Treeview 上に重ねて配置
        if self._editor is not None:
            self._editor.destroy()
        self._editor = tk.Entry(self.tree,
                                bg="white", fg="black",
                                relief="flat", bd=0,
                                font=("Yu Gothic UI", 9))
        self._editor.place(x=x, y=y, width=w, height=h)
        self._editor.insert(0, current_value)
        self._editor.select_range(0, "end")
        self._editor.focus_set()

        def commit(_event=None):
            new_value = self._editor.get()
            self._editor.destroy()
            self._editor = None
            if str(new_value) == str(current_value):
                return  # 変更なし
            self._commit_cell_edit(iid, col_idx, column_name,
                                   current_value, new_value)

        def cancel(_event=None):
            self._editor.destroy()
            self._editor = None

        self._editor.bind("<Return>", commit)
        self._editor.bind("<FocusOut>", commit)
        self._editor.bind("<Escape>", cancel)

    def _commit_cell_edit(self, iid: str, col_idx: int,
                          column_name: str, old_value: Any, new_value: Any):
        pk = self._row_pk_map.get(iid)
        if not pk:
            messagebox.showerror("エラー", "対象行の主キーが特定できません。",
                                 parent=self)
            return

        # 確認ダイアログ
        dlg = ConfirmDialog(
            self,
            title="確認",
            message=f"以下のセルを更新します。よろしいですか？",
            target=(f"{self.current_schema.full_name} / 列: {column_name}\n"
                    f"変更前: {old_value}\n"
                    f"変更後: {new_value}"),
            operation="UPDATE",
            count=1,
        )
        if not dlg.result:
            self._status("warn", "セル更新をキャンセルしました")
            return

        # 実行
        try:
            self.db.update_cell(
                self.current_schema.schema,
                self.current_schema.name,
                pk, column_name, new_value if new_value != "" else None,
            )
            self.db.commit()
        except Exception as e:
            self.db.rollback()
            self._status("error", f"更新失敗: {e}")
            messagebox.showerror("更新エラー", f"更新に失敗しました:\n{e}",
                                 parent=self)
            return

        # 表示更新
        values = list(self.tree.item(iid, "values"))
        values[col_idx] = new_value
        self.tree.item(iid, values=values)
        # メモリ上の current_rows も更新
        row_index = int(iid.split("_")[1])
        if 0 <= row_index < len(self.current_rows):
            self.current_rows[row_index][col_idx] = new_value
        self._status("ok", f"更新成功: {column_name} を {new_value} に変更")

    # ── 行追加 ───────────────────────────────────────────────
    def _on_add_row(self):
        if not self.current_schema:
            return
        col_specs = []
        for c in self.current_schema.columns:
            col_specs.append({
                "name": c.name,
                "type": c.data_type,
                "is_pk": c.is_primary_key,
                "nullable": c.is_nullable,
            })
        dlg = AddRowDialog(self, col_specs,
                           table_name=self.current_schema.full_name)
        if not dlg.result:
            return
        data = dlg.result

        # 確認ダイアログ
        preview = ", ".join(f"{k}={v}" for k, v in data.items() if v is not None)
        cdlg = ConfirmDialog(
            self,
            title="確認",
            message="以下の行を追加します。よろしいですか？",
            target=f"{self.current_schema.full_name}\n{preview[:200]}",
            operation="INSERT",
            count=1,
        )
        if not cdlg.result:
            self._status("warn", "行追加をキャンセルしました")
            return

        # 実行
        try:
            # 空欄は NULL（nullable=False かつ未入力はエラーになる）
            non_null_data = {k: v for k, v in data.items() if v is not None}
            self.db.insert_row(self.current_schema.schema,
                               self.current_schema.name,
                               non_null_data)
            self.db.commit()
        except Exception as e:
            self.db.rollback()
            self._status("error", f"追加失敗: {e}")
            messagebox.showerror("追加エラー", f"追加に失敗しました:\n{e}",
                                 parent=self)
            return

        self._status("ok", "行を追加しました")
        self._on_refresh()  # 全件再読み込み

    # ── 行削除 ───────────────────────────────────────────────
    def _on_delete_rows(self):
        if not self.current_schema:
            return
        sel = self.tree.selection()
        if not sel:
            messagebox.showinfo("情報", "削除する行を選択してください。",
                                parent=self)
            return
        if not self.current_schema.primary_keys:
            messagebox.showwarning(
                "削除不可",
                "このテーブルには主キーがないため、安全に削除できません。",
                parent=self,
            )
            return

        pk_dicts = [self._row_pk_map[iid] for iid in sel
                    if iid in self._row_pk_map]
        if not pk_dicts:
            return

        # 確認ダイアログ（削除は赤系）
        dlg = ConfirmDialog(
            self,
            title="削除確認",
            message="以下の行を削除します。\nこの操作は取り消せません。",
            target=self.current_schema.full_name,
            operation="DELETE",
            count=len(pk_dicts),
            danger=True,
        )
        if not dlg.result:
            self._status("warn", "削除をキャンセルしました")
            return

        try:
            self.db.delete_rows(self.current_schema.schema,
                                self.current_schema.name,
                                pk_dicts)
            self.db.commit()
        except Exception as e:
            self.db.rollback()
            self._status("error", f"削除失敗: {e}")
            messagebox.showerror("削除エラー", f"削除に失敗しました:\n{e}",
                                 parent=self)
            return

        self._status("ok", f"{len(pk_dicts)} 行を削除しました")
        self._on_refresh()

    # ── 列追加 ───────────────────────────────────────────────
    def _on_add_column(self):
        if not self.current_schema:
            return
        dlg = AddColumnDialog(self,
                              table_name=self.current_schema.full_name)
        if not dlg.result:
            return
        info = dlg.result

        cdlg = ConfirmDialog(
            self,
            title="確認",
            message="以下の列を追加します。よろしいですか？",
            target=(f"{self.current_schema.full_name}\n"
                    f"列名: {info['name']}\n"
                    f"型:   {info['type']}\n"
                    f"NULL: {'許可' if info['nullable'] else '不可'}"),
            operation="ALTER TABLE ADD COLUMN",
            count=1,
        )
        if not cdlg.result:
            self._status("warn", "列追加をキャンセルしました")
            return

        try:
            self.db.add_column(self.current_schema.schema,
                               self.current_schema.name,
                               info["name"], info["type"],
                               nullable=info["nullable"])
            self.db.commit()
        except Exception as e:
            self.db.rollback()
            self._status("error", f"列追加失敗: {e}")
            messagebox.showerror("列追加エラー",
                                 f"列追加に失敗しました:\n{e}",
                                 parent=self)
            return

        self._status("ok", f"列 '{info['name']}' を追加しました")
        self._on_refresh()

    # ── 列削除 ───────────────────────────────────────────────
    def _on_drop_column(self):
        if not self.current_schema:
            return
        # 列選択用の小ダイアログ
        col_list = [c.name for c in self.current_schema.columns
                    if not c.is_primary_key]
        if not col_list:
            messagebox.showinfo("情報", "削除可能な列がありません（PK列は削除不可）。",
                                parent=self)
            return
        # シンプルに、selection ダイアログ代わりに askstring 風UIを構築
        sel = _SimpleSelectDialog(self, "削除する列を選択", col_list)
        if not sel.result:
            return
        column = sel.result

        cdlg = ConfirmDialog(
            self,
            title="列削除確認",
            message="以下の列を削除します。\nこの列のデータはすべて失われます。",
            target=f"{self.current_schema.full_name} / 列: {column}",
            operation="ALTER TABLE DROP COLUMN",
            count=1,
            danger=True,
        )
        if not cdlg.result:
            self._status("warn", "列削除をキャンセルしました")
            return

        try:
            self.db.drop_column(self.current_schema.schema,
                                self.current_schema.name, column)
            self.db.commit()
        except Exception as e:
            self.db.rollback()
            self._status("error", f"列削除失敗: {e}")
            messagebox.showerror("列削除エラー",
                                 f"列削除に失敗しました:\n{e}",
                                 parent=self)
            return

        self._status("ok", f"列 '{column}' を削除しました")
        self._on_refresh()

    # ── 再読み込み ───────────────────────────────────────────
    def _on_refresh(self):
        if self.current_schema:
            self.load_table(self.current_schema.schema,
                            self.current_schema.name)

    # ── ステータス通知 ──────────────────────────────────────
    def _status(self, level: str, message: str):
        colors = {
            "ok":    SUCCESS_COLOR,
            "info":  TEXT_SECONDARY,
            "warn":  WARN_COLOR,
            "error": ERROR_COLOR,
        }
        self.lbl_status.config(text=message,
                               fg=colors.get(level, TEXT_MUTED))
        if self.on_status:
            self.on_status(level, message)

    # ──────────────────────────────────────────────────────────
    # PK / FK 制約管理
    # ──────────────────────────────────────────────────────────
    def _on_manage_constraints(self):
        """PK / FK 制約管理ダイアログを開く"""
        if not self.current_schema:
            messagebox.showinfo("情報", "テーブルを選択してください。", parent=self)
            return

        schema = self.current_schema.schema
        table = self.current_schema.name

        # 現在の FK 一覧（このテーブルが参照元のもの）
        try:
            all_fks = self.db.get_foreign_keys()
            current_fks = [
                {
                    "fk_name":   fk.fk_name,
                    "column":    fk.parent_column,
                    "ref_table": fk.ref_table,
                    "ref_column": fk.ref_column,
                }
                for fk in all_fks if fk.parent_table == table
            ]
            all_tables = self.db.list_tables()
        except Exception as e:
            self._status("error", f"制約情報取得失敗: {e}")
            messagebox.showerror("エラー", f"制約情報の取得に失敗しました:\n{e}",
                                 parent=self)
            return

        # 参照先候補テーブルの列を取得する関数
        def get_columns_of(ref_schema: str, ref_table: str) -> List[str]:
            try:
                return self.db.get_pk_and_unique_columns(ref_schema, ref_table)
            except Exception:
                return []

        cols_info = [{
            "name":  c.name,
            "is_pk": c.is_primary_key,
            "is_fk": c.is_foreign_key,
        } for c in self.current_schema.columns]

        dlg = ConstraintManagerDialog(
            self,
            table_name=self.current_schema.full_name,
            columns=cols_info,
            current_pk=self.current_schema.primary_keys,
            current_fks=current_fks,
            all_tables=all_tables,
            get_columns_of=get_columns_of,
        )
        if not dlg.result:
            self._status("info", "制約変更はありません")
            return

        plan = dlg.result

        # 変更プランをまとめて確認ダイアログに表示
        summary_lines = []
        if plan["pk_changed"]:
            if plan["old_pk"]:
                summary_lines.append(
                    f"PK 削除: {', '.join(plan['old_pk'])}")
            if plan["new_pk"]:
                summary_lines.append(
                    f"PK 新規: {', '.join(plan['new_pk'])}")
        for fk_name in plan["fk_to_drop"]:
            summary_lines.append(f"FK 削除: {fk_name}")
        for fk in plan["fk_to_add"]:
            summary_lines.append(
                f"FK 追加: {fk['column']} → "
                f"{fk['ref_table']}.{fk['ref_column']}"
            )

        # 件数 = 変更件数
        change_count = (
            (2 if plan["pk_changed"] and plan["old_pk"] and plan["new_pk"]
             else 1 if plan["pk_changed"] else 0)
            + len(plan["fk_to_drop"]) + len(plan["fk_to_add"])
        )

        cdlg = ConfirmDialog(
            self,
            title="制約変更確認",
            message=("以下の制約変更を実行します。\n"
                     "この操作は取り消せません（手動で再設定が必要）。"),
            target=f"{self.current_schema.full_name}\n" + "\n".join(summary_lines),
            operation="ALTER CONSTRAINT",
            count=change_count,
            danger=True,
        )
        if not cdlg.result:
            self._status("warn", "制約変更をキャンセルしました")
            return

        # ── 実行（同一トランザクション内）─────────────────
        try:
            # ① 既存 PK を削除（PK 変更時のみ）
            if plan["pk_changed"] and plan["old_pk"]:
                self.db.drop_primary_key(schema, table)
            # ② FK を削除
            for fk_name in plan["fk_to_drop"]:
                self.db.drop_foreign_key(schema, table, fk_name)
            # ③ 新しい PK を追加
            if plan["pk_changed"] and plan["new_pk"]:
                self.db.add_primary_key(schema, table, plan["new_pk"])
            # ④ 新しい FK を追加
            for fk in plan["fk_to_add"]:
                self.db.add_foreign_key(
                    schema, table, fk["column"],
                    fk["ref_schema"], fk["ref_table"], fk["ref_column"],
                )
            self.db.commit()
        except Exception as e:
            self.db.rollback()
            self._status("error", f"制約変更失敗: {e}")
            messagebox.showerror(
                "制約変更エラー",
                f"制約変更に失敗しました:\n{e}\n\n"
                "全変更をロールバックしました。",
                parent=self,
            )
            return

        self._status("ok", "制約を更新しました")
        # ER 図とグリッドを再読み込み
        if self.on_schema_changed:
            self.on_schema_changed()
        self._on_refresh()

    # ──────────────────────────────────────────────────────────
    # コピー & ペースト
    # ──────────────────────────────────────────────────────────
    def _on_copy(self, event=None):
        """選択中の行データをタブ区切りでクリップボードへコピー"""
        if not self.current_schema:
            return "break"
        sel = self.tree.selection()
        if not sel:
            return "break"

        lines = []
        # ヘッダー行も含める（任意：Excel 貼り付け時に便利）
        # → 今回は値のみ
        for iid in sel:
            values = self.tree.item(iid, "values")
            lines.append("\t".join("" if v is None else str(v)
                                   for v in values))
        text = "\n".join(lines)
        try:
            self.clipboard_clear()
            self.clipboard_append(text)
            self._status("ok", f"{len(sel)} 行をコピーしました")
        except tk.TclError as e:
            self._status("error", f"コピー失敗: {e}")
        return "break"

    def _on_paste(self, event=None):
        """
        クリップボードの内容をペースト。
        起点：最後にクリックされたセル（無ければ選択中の最初の行・先頭列）
        """
        if not self.current_schema:
            return "break"
        if not self.current_schema.primary_keys:
            messagebox.showwarning(
                "ペースト不可",
                "このテーブルには主キーがないため、ペーストできません。",
                parent=self,
            )
            return "break"

        # クリップボード取得
        try:
            clipboard = self.clipboard_get()
        except tk.TclError:
            self._status("warn", "クリップボードが空です")
            return "break"

        if not clipboard.strip():
            return "break"

        # タブ・改行で分割
        # Excel コピー時の改行は \r\n、Mac は \r
        clipboard = clipboard.replace("\r\n", "\n").replace("\r", "\n")
        # 末尾改行を除去
        clipboard = clipboard.rstrip("\n")
        paste_matrix = [line.split("\t") for line in clipboard.split("\n")]
        if not paste_matrix or not paste_matrix[0]:
            return "break"

        paste_rows = len(paste_matrix)
        paste_cols = max(len(r) for r in paste_matrix)

        # 起点セルを決定
        if self._last_clicked_iid and self._last_clicked_col_idx is not None:
            anchor_iid = self._last_clicked_iid
            anchor_col = self._last_clicked_col_idx
        else:
            sel = self.tree.selection()
            if not sel:
                messagebox.showinfo(
                    "情報",
                    "ペースト先のセルをクリックしてから貼り付けてください。",
                    parent=self,
                )
                return "break"
            anchor_iid = sel[0]
            anchor_col = 0

        # 起点から下方向に paste_rows 件の行を確定
        all_iids = list(self.tree.get_children())
        if anchor_iid not in all_iids:
            return "break"
        anchor_row_idx = all_iids.index(anchor_iid)

        target_iids = all_iids[anchor_row_idx : anchor_row_idx + paste_rows]
        target_col_indices = list(range(
            anchor_col,
            min(anchor_col + paste_cols, len(self.current_columns))
        ))
        target_columns = [self.current_columns[i] for i in target_col_indices]

        # PK 列はスキップ対象としてカウント
        pk_set = set(self.current_schema.primary_keys)
        skipped_pk_cells = 0
        for ci in target_col_indices:
            col_name = self.current_columns[ci]
            if col_name in pk_set:
                skipped_pk_cells += len(target_iids)

        excess_cols = max(0, (anchor_col + paste_cols) - len(self.current_columns))
        excess_rows = max(0, (anchor_row_idx + paste_rows) - len(all_iids))
        total_cells = len(target_iids) * len(target_col_indices)

        # 確認ダイアログ
        dlg = PasteConfirmDialog(
            self,
            table_name=self.current_schema.full_name,
            target_columns=target_columns,
            target_rows_count=len(target_iids),
            paste_rows=paste_rows,
            paste_cols=paste_cols,
            total_cells=total_cells,
            skipped_pk_cells=skipped_pk_cells,
            excess_cols=excess_cols,
            excess_rows=excess_rows,
        )
        if not dlg.result:
            self._status("warn", "ペーストをキャンセルしました")
            return "break"

        # ── 更新リストを構築 ─────────────────────────────────
        updates = []
        for r_offset, iid in enumerate(target_iids):
            pk = self._row_pk_map.get(iid)
            if not pk:
                continue
            paste_row = paste_matrix[r_offset]
            for c_offset, ci in enumerate(target_col_indices):
                col_name = self.current_columns[ci]
                if col_name in pk_set:
                    continue  # PK スキップ
                if c_offset >= len(paste_row):
                    continue
                new_val = paste_row[c_offset]
                # 空文字は NULL として扱う
                final_val = new_val if new_val != "" else None
                updates.append({
                    "pk":     pk,
                    "column": col_name,
                    "value":  final_val,
                })

        if not updates:
            self._status("warn", "更新対象セルがありません")
            return "break"

        # ── 一括 UPDATE ──────────────────────────────────────
        try:
            self.db.bulk_update_cells(
                self.current_schema.schema,
                self.current_schema.name,
                updates,
            )
            self.db.commit()
        except Exception as e:
            self.db.rollback()
            self._status("error", f"ペースト失敗: {e}")
            messagebox.showerror(
                "ペーストエラー",
                f"ペーストに失敗しました:\n{e}\n\n"
                "全変更をロールバックしました。",
                parent=self,
            )
            return "break"

        self._status("ok", f"{len(updates)} セルを更新しました")
        self._on_refresh()
        return "break"

    # ──────────────────────────────────────────────────────────
    # Excel インポート
    # ──────────────────────────────────────────────────────────
    def _on_import_excel(self):
        """Excel ファイルを取り込んでテーブルに一括 INSERT する"""
        if not self.current_schema:
            messagebox.showinfo("情報", "テーブルを選択してください。", parent=self)
            return

        # ── ファイル選択 ─────────────────────────────────────
        path = filedialog.askopenfilename(
            title="取り込む Excel ファイルを選択",
            filetypes=[("Excel", "*.xlsx *.xlsm"), ("すべてのファイル", "*.*")],
            parent=self,
        )
        if not path:
            return

        # ── Excel を読み込む ─────────────────────────────────
        try:
            import openpyxl
        except ImportError:
            messagebox.showerror(
                "エラー",
                "openpyxl がインストールされていません。\n"
                "pip install openpyxl を実行してください。",
                parent=self,
            )
            return

        try:
            wb = openpyxl.load_workbook(path, data_only=True, read_only=True)
            ws = wb.active
            rows = list(ws.iter_rows(values_only=True))
            wb.close()
        except Exception as e:
            self._status("error", f"Excel 読み込み失敗: {e}")
            messagebox.showerror("エラー",
                                 f"Excel の読み込みに失敗しました:\n{e}",
                                 parent=self)
            return

        if not rows or len(rows) < 2:
            messagebox.showinfo("情報",
                                "Excel にデータが含まれていません。",
                                parent=self)
            return

        # ヘッダー（1行目）とデータ（2行目以降）
        headers = [h for h in rows[0]]
        data_rows = [list(r) for r in rows[1:]]
        # 完全に空の行を除外
        data_rows = [r for r in data_rows
                     if any(v is not None and v != "" for v in r)]
        if not data_rows:
            messagebox.showinfo("情報", "データ行がありません。", parent=self)
            return

        # 列情報を整形
        cols_info = [{
            "name":     c.name,
            "type":     c.data_type,
            "is_pk":    c.is_primary_key,
            "nullable": c.is_nullable,
        } for c in self.current_schema.columns]

        # ── プレビュー＆マッピングダイアログ ────────────────
        dlg = ExcelImportPreviewDialog(
            self,
            file_path=path,
            table_name=self.current_schema.full_name,
            table_columns=cols_info,
            excel_headers=[str(h) if h is not None else "" for h in headers],
            excel_preview_rows=data_rows[:5],
            total_rows=len(data_rows),
        )
        if not dlg.result:
            self._status("warn", "Excel 取込をキャンセルしました")
            return

        mapping: Dict[str, Optional[int]] = dlg.result["mapping"]
        # 取込対象列（マッピングされている DB 列）
        target_cols = [c for c, idx in mapping.items() if idx is not None]
        if not target_cols:
            return

        # ── 取込データ構築 ───────────────────────────────────
        insert_rows: List[List[Any]] = []
        for r in data_rows:
            row_values = []
            for c in target_cols:
                excel_idx = mapping[c]
                if excel_idx is None or excel_idx >= len(r):
                    row_values.append(None)
                else:
                    v = r[excel_idx]
                    # 空文字は NULL として扱う
                    row_values.append(v if v != "" else None)
            insert_rows.append(row_values)

        # ── 最終確認 ─────────────────────────────────────────
        cdlg = ConfirmDialog(
            self,
            title="Excel 取込 最終確認",
            message=("以下の内容で一括 INSERT を実行します。\n"
                     "1行でも失敗した場合は全件ロールバックされます。"),
            target=(f"{self.current_schema.full_name}\n"
                    f"取込列: {', '.join(target_cols[:8])}"
                    f"{' ...' if len(target_cols) > 8 else ''}"),
            operation="BULK INSERT",
            count=len(insert_rows),
        )
        if not cdlg.result:
            self._status("warn", "Excel 取込をキャンセルしました")
            return

        # ── 実行 ────────────────────────────────────────────
        try:
            inserted = self.db.bulk_insert(
                self.current_schema.schema,
                self.current_schema.name,
                target_cols,
                insert_rows,
            )
            self.db.commit()
        except Exception as e:
            self.db.rollback()
            self._status("error", f"取込失敗: {e}")
            messagebox.showerror(
                "取込エラー",
                f"Excel 取込に失敗しました:\n{e}\n\n"
                "全変更をロールバックしました。",
                parent=self,
            )
            return

        self._status("ok", f"{inserted} 行を取り込みました")
        self._on_refresh()


# ─── 列選択用 小ダイアログ ──────────────────────────────────
class _SimpleSelectDialog(tk.Toplevel):
    """選択肢からひとつ選ぶシンプルなダイアログ"""

    def __init__(self, parent, title: str, options: List[str]):
        super().__init__(parent)
        self.title(title)
        self.configure(bg=BG_CARD)
        self.resizable(False, False)
        self.result: Optional[str] = None

        tk.Label(self, text=title, font=("Yu Gothic UI", 11, "bold"),
                 fg=TEXT_PRIMARY, bg=BG_CARD).pack(
            anchor="w", padx=22, pady=(20, 10))

        self.var = tk.StringVar(value=options[0] if options else "")
        cmb = ttk.Combobox(self, textvariable=self.var,
                           values=options, state="readonly",
                           font=("Yu Gothic UI", 10), width=36)
        cmb.pack(padx=22, pady=(0, 16))

        btn_row = tk.Frame(self, bg=BG_CARD)
        btn_row.pack(fill="x", padx=22, pady=(0, 20))
        tk.Button(btn_row, text="キャンセル", font=("Yu Gothic UI", 10),
                  fg=TEXT_SECONDARY, bg=BORDER_COLOR,
                  relief="flat", bd=0, padx=14, pady=6, cursor="hand2",
                  command=self._cancel).pack(side="right")
        tk.Button(btn_row, text="OK", font=("Yu Gothic UI", 10, "bold"),
                  fg="white", bg=ACCENT_BLUE,
                  relief="flat", bd=0, padx=14, pady=6, cursor="hand2",
                  command=self._ok).pack(side="right", padx=(0, 8))

        self.transient(parent)
        self.update_idletasks()
        pw, ph = parent.winfo_width(), parent.winfo_height()
        px, py = parent.winfo_rootx(), parent.winfo_rooty()
        w, h = self.winfo_width(), self.winfo_height()
        self.geometry(f"+{px+(pw-w)//2}+{py+(ph-h)//2}")
        self.grab_set()
        self.wait_window(self)

    def _ok(self):
        self.result = self.var.get()
        self.destroy()

    def _cancel(self):
        self.result = None
        self.destroy()
