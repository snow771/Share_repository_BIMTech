# -*- coding: utf-8 -*-
"""
db_editor.py
コストデータベース編集ツール - メインウィンドウ

このスクリプトはランチャー（launcher.py）から起動されるか、
単独で `python -m modules.db_editor.db_editor` として起動できます。

接続先：localhost\\sqlexpress（Windows 統合認証）

主な機能：
  - テーブル一覧表示・選択
  - ER図パネル（テーブル間のリレーション可視化）
  - Excel ライクなデータグリッド編集
  - 行/列の追加・削除（すべて確認ダイアログ経由）
  - 検索フィルタ
"""

from __future__ import annotations

import sys
import traceback
from pathlib import Path
from tkinter import messagebox
import tkinter as tk

# パッケージとしてもスクリプトとしても起動できるよう sys.path を調整
if __package__ in (None, ""):
    sys.path.insert(0, str(Path(__file__).resolve().parents[2]))
    from modules.db_editor.db_connection import DatabaseConnection
    from modules.db_editor.grid_panel import GridPanel
    from modules.db_editor.er_panel import ErPanel
    from modules.db_editor.dialogs import (
        BG_DARK, BG_CARD, BG_CARD2, ACCENT_BLUE, ACCENT_TEAL, ACCENT_PURPLE,
        TEXT_PRIMARY, TEXT_SECONDARY, TEXT_MUTED, BORDER_COLOR,
        SUCCESS_COLOR, ERROR_COLOR, WARN_COLOR,
        ConnectionDialog,
    )
else:
    from .db_connection import DatabaseConnection
    from .grid_panel import GridPanel
    from .er_panel import ErPanel
    from .dialogs import (
        BG_DARK, BG_CARD, BG_CARD2, ACCENT_BLUE, ACCENT_TEAL, ACCENT_PURPLE,
        TEXT_PRIMARY, TEXT_SECONDARY, TEXT_MUTED, BORDER_COLOR,
        SUCCESS_COLOR, ERROR_COLOR, WARN_COLOR,
        ConnectionDialog,
    )


# ─── メインアプリ ───────────────────────────────────────────
class DbEditorApp(tk.Tk):
    """DBエディタのメインアプリケーション"""

    def __init__(self):
        super().__init__()
        self.title("③ コストデータベース編集ツール")
        self.configure(bg=BG_DARK)
        self.minsize(1200, 720)

        self.db = DatabaseConnection()
        self._build()
        self._center()

        # 起動時に接続ダイアログを開く
        self.after(200, self._on_connect)

    # ── UI 構築 ─────────────────────────────────────────────
    def _build(self):
        # ヘッダー
        hdr = tk.Frame(self, bg=BG_DARK)
        hdr.pack(fill="x", padx=24, pady=(20, 0))
        tk.Label(hdr, text="COST DB EDITOR",
                 font=("Courier New", 10, "bold"),
                 fg=ACCENT_PURPLE, bg=BG_DARK).pack(anchor="w")
        tk.Label(hdr, text="コストデータベース編集ツール",
                 font=("Yu Gothic UI", 18, "bold"),
                 fg=TEXT_PRIMARY, bg=BG_DARK).pack(anchor="w")
        sub = tk.Frame(hdr, bg=BG_DARK)
        sub.pack(fill="x", pady=(2, 0))
        self.lbl_connection = tk.Label(
            sub, text="未接続",
            font=("Yu Gothic UI", 9),
            fg=TEXT_MUTED, bg=BG_DARK,
        )
        self.lbl_connection.pack(side="left")
        tk.Button(sub, text="接続設定",
                  font=("Yu Gothic UI", 9),
                  fg=TEXT_SECONDARY, bg=BORDER_COLOR,
                  activeforeground=TEXT_PRIMARY, activebackground=BG_CARD2,
                  relief="flat", bd=0, padx=10, pady=3, cursor="hand2",
                  command=self._on_connect).pack(side="right")

        tk.Frame(self, bg=BORDER_COLOR, height=1).pack(
            fill="x", padx=24, pady=(14, 16))

        # 本体（3カラム: テーブル一覧 / ER図 / グリッド）
        # 配置：左=テーブル一覧（240px）、中=ER図（柔軟）、右=グリッド（柔軟）
        paned = tk.PanedWindow(
            self, orient="horizontal",
            bg=BORDER_COLOR,
            sashwidth=4, sashpad=0, sashrelief="flat", showhandle=False,
        )
        paned.pack(fill="both", expand=True, padx=24, pady=(0, 20))

        # ─ テーブル一覧 ─
        list_frame = tk.Frame(paned, bg=BG_CARD,
                              highlightbackground=BORDER_COLOR,
                              highlightthickness=1)

        tk.Label(list_frame, text="テーブル一覧",
                 font=("Yu Gothic UI", 11, "bold"),
                 fg=TEXT_PRIMARY, bg=BG_CARD).pack(
            anchor="w", padx=14, pady=(12, 8))

        tk.Frame(list_frame, bg=BORDER_COLOR, height=1).pack(
            fill="x", padx=14, pady=(0, 8))

        lb_box = tk.Frame(list_frame, bg=BG_CARD)
        lb_box.pack(fill="both", expand=True, padx=14, pady=(0, 12))

        self.listbox = tk.Listbox(
            lb_box,
            font=("Yu Gothic UI", 9),
            bg=BG_CARD2, fg=TEXT_PRIMARY,
            selectbackground=ACCENT_BLUE,
            selectforeground="white",
            relief="flat", bd=0,
            highlightthickness=0,
            activestyle="none",
        )
        sb = tk.Scrollbar(lb_box, orient="vertical",
                          command=self.listbox.yview,
                          bg=BG_CARD, troughcolor=BG_CARD2, width=8)
        self.listbox.configure(yscrollcommand=sb.set)
        self.listbox.pack(side="left", fill="both", expand=True)
        sb.pack(side="right", fill="y")
        self.listbox.bind("<<ListboxSelect>>", self._on_table_select)
        self.listbox.bind("<Double-Button-1>", self._on_table_select)

        # ─ ER 図パネル ─
        self.er_panel = ErPanel(
            paned, db=self.db,
            on_table_click=self._on_er_table_click,
        )

        # ─ グリッドパネル ─
        self.grid_panel = GridPanel(
            paned, db=self.db,
            on_column_selected=self._on_column_selected,
            on_status=self._on_grid_status,
            on_schema_changed=self._on_schema_changed,
        )
        paned.add(list_frame,       minsize=160, width=240)
        paned.add(self.er_panel,    minsize=300)
        paned.add(self.grid_panel,  minsize=400)

        # ステータスバー
        self.lbl_global_status = tk.Label(
            self, text="準備完了",
            font=("Yu Gothic UI", 8),
            fg=TEXT_MUTED, bg=BG_DARK, anchor="w",
        )
        self.lbl_global_status.pack(fill="x", padx=24, pady=(0, 10))

    def _center(self):
        self.update_idletasks()
        w, h = 1480, 860
        sw, sh = self.winfo_screenwidth(), self.winfo_screenheight()
        x = max(0, (sw - w) // 2)
        y = max(0, (sh - h) // 2)
        self.geometry(f"{w}x{h}+{x}+{y}")

    # ── 接続 ────────────────────────────────────────────────
    def _on_connect(self):
        # 既存接続を切断
        if self.db.is_connected():
            self.db.disconnect()
            self.lbl_connection.config(text="未接続", fg=TEXT_MUTED)

        # 接続先入力（最初は DB 一覧なし）
        dlg = ConnectionDialog(self,
                               default_server=r"localhost\sqlexpress")
        if not dlg.result:
            return

        server = dlg.result["server"]
        database = dlg.result["database"]
        try:
            self.db.connect(server=server, database=database)
        except Exception as e:
            messagebox.showerror(
                "接続エラー",
                f"接続に失敗しました:\n{e}\n\n"
                "ODBC ドライバーのインストールと、"
                "SQL Server サービスの起動を確認してください。",
                parent=self,
            )
            self._global_status("error", f"接続失敗: {e}")
            return

        # DB 未指定の場合は DB 選択を促す
        if not database:
            try:
                dbs = self.db.list_databases()
            except Exception as e:
                messagebox.showerror("エラー", f"DB 一覧取得失敗:\n{e}",
                                     parent=self)
                return
            dlg2 = ConnectionDialog(self,
                                    default_server=server,
                                    databases=dbs)
            if not dlg2.result or not dlg2.result["database"]:
                messagebox.showinfo("情報",
                                    "データベースが選択されていません。\n"
                                    "再度「接続設定」から選択してください。",
                                    parent=self)
                return
            try:
                self.db.use_database(dlg2.result["database"])
                database = dlg2.result["database"]
            except Exception as e:
                messagebox.showerror("エラー", f"DB 切替失敗:\n{e}",
                                     parent=self)
                return

        # 接続成功 → ラベル更新・テーブル一覧取得・ER 図ロード
        self.lbl_connection.config(
            text=f"接続中: {server}  /  DB: {database}",
            fg=SUCCESS_COLOR,
        )
        self._global_status("ok", f"{server} / {database} に接続しました")
        self._reload_tables()
        self.er_panel.load_schema()

    # ── テーブル一覧再読込 ──────────────────────────────────
    def _reload_tables(self):
        self.listbox.delete(0, "end")
        try:
            tables = self.db.list_tables()
        except Exception as e:
            messagebox.showerror("エラー", f"テーブル一覧取得失敗:\n{e}",
                                 parent=self)
            return
        self._tables_cache = tables
        for schema, name in tables:
            self.listbox.insert("end", f"{schema}.{name}")

    # ── テーブル選択 ────────────────────────────────────────
    def _on_table_select(self, _event=None):
        sel = self.listbox.curselection()
        if not sel:
            return
        idx = sel[0]
        if not hasattr(self, "_tables_cache"):
            return
        schema, name = self._tables_cache[idx]
        self.grid_panel.load_table(schema, name)
        # ER 図のテーブル名ハイライトもクリア
        self.er_panel.highlight_column(name, None)

    def _on_er_table_click(self, schema: str, table: str):
        """ER 図でテーブルがダブルクリックされた"""
        # listbox の該当行を選択
        target = f"{schema}.{table}"
        for i in range(self.listbox.size()):
            if self.listbox.get(i) == target:
                self.listbox.selection_clear(0, "end")
                self.listbox.selection_set(i)
                self.listbox.see(i)
                break
        self.grid_panel.load_table(schema, table)

    # ── 列選択時のER図ハイライト ────────────────────────────
    def _on_column_selected(self, table: str, column: str):
        """グリッドで列を選択 → ER図でリレーションをハイライト"""
        self.er_panel.highlight_column(table, column)

    # ── スキーマ変更時のER図再ロード ────────────────────────
    def _on_schema_changed(self):
        """PK/FK 追加・削除・列追加・列削除などの後にER図を再ロード"""
        self.er_panel.refresh()
        self._global_status("info", "ER図を最新の状態に更新しました")

    # ── ステータス通知 ──────────────────────────────────────
    def _on_grid_status(self, level: str, message: str):
        self._global_status(level, message)

    def _global_status(self, level: str, message: str):
        colors = {
            "ok":    SUCCESS_COLOR,
            "info":  TEXT_SECONDARY,
            "warn":  WARN_COLOR,
            "error": ERROR_COLOR,
        }
        self.lbl_global_status.config(text=message,
                                       fg=colors.get(level, TEXT_MUTED))

    # ── 終了処理 ────────────────────────────────────────────
    def destroy(self):
        try:
            if self.db.is_connected():
                self.db.disconnect()
        except Exception:
            pass
        super().destroy()


# ─── エントリーポイント ─────────────────────────────────────
def main():
    try:
        app = DbEditorApp()
        app.mainloop()
    except Exception as e:
        traceback.print_exc()
        try:
            messagebox.showerror("起動エラー", str(e))
        except Exception:
            pass
        sys.exit(1)


if __name__ == "__main__":
    main()
