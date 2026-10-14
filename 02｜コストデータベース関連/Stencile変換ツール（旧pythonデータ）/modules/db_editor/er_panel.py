# -*- coding: utf-8 -*-
"""
er_panel.py
ER図パネル

主な機能：
  - DB 内の全テーブルをノード（矩形）として表示
  - 主キー（🔑）・外部キー（FK）を列名に表示
  - 外部キー制約を `sys.foreign_keys` から取得しテーブル間を線で結ぶ
  - 編集中の列が絡むリレーション線をアクセントカラーで強調表示
  - ノードのドラッグ移動、ズーム、レイアウト保存（er_layout.json）
  - ノードのクリックでテーブル選択コールバックを発火
"""

from __future__ import annotations

import json
import math
from dataclasses import dataclass, field
from pathlib import Path
from typing import Callable, Dict, List, Optional, Tuple

import tkinter as tk

from .db_connection import (
    DatabaseConnection, ForeignKey, TableSchema,
)
from .dialogs import (
    BG_DARK, BG_CARD, BG_CARD2, ACCENT_BLUE, ACCENT_TEAL, ACCENT_PURPLE,
    TEXT_PRIMARY, TEXT_SECONDARY, TEXT_MUTED, BORDER_COLOR,
    SUCCESS_COLOR, ERROR_COLOR, WARN_COLOR,
)


# ─── レイアウト定数 ─────────────────────────────────────────
NODE_WIDTH       = 200
NODE_HEADER_H    = 30
ROW_H            = 18
NODE_PAD_X       = 30
NODE_PAD_Y       = 30
NODES_PER_ROW    = 4

LAYOUT_FILE      = "er_layout.json"


# ─── ノード ─────────────────────────────────────────────────
@dataclass
class TableNode:
    """ER図上のテーブルノード"""
    table_name: str
    columns: List[dict] = field(default_factory=list)
    # 列: {"name": str, "type": str, "is_pk": bool, "is_fk": bool}
    x: float = 0.0
    y: float = 0.0
    # Canvas 上の図形ID
    rect_id: int = 0
    header_id: int = 0
    title_id: int = 0
    column_ids: List[int] = field(default_factory=list)

    @property
    def height(self) -> int:
        return NODE_HEADER_H + ROW_H * max(1, len(self.columns))


# ─── ER 図パネル本体 ────────────────────────────────────────
class ErPanel(tk.Frame):
    """ER図パネル"""

    def __init__(self, parent,
                 db: DatabaseConnection,
                 on_table_click: Optional[Callable[[str, str], None]] = None,
                 layout_path: Optional[Path] = None,
                 **kwargs):
        super().__init__(parent, bg=BG_CARD, **kwargs)
        self.db = db
        self.on_table_click = on_table_click  # (schema, table) -> None
        self.layout_path = Path(layout_path or LAYOUT_FILE)

        self.nodes: Dict[str, TableNode] = {}
        self.fks: List[ForeignKey] = []
        # FK 用の Canvas ID リスト：{(parent_table, parent_column) -> [line_ids]}
        self.relation_ids: Dict[Tuple[str, str], List[int]] = {}
        # FK 全体の参照テーブル_列の対応も保持
        self.relations: List[dict] = []  # 描画情報の保持

        self._zoom = 1.0
        self._drag = {"node": None, "dx": 0, "dy": 0}
        self._highlight_table: Optional[str] = None
        self._highlight_column: Optional[str] = None

        self.configure(highlightbackground=BORDER_COLOR, highlightthickness=1)
        self._build()

    # ── UI 構築 ─────────────────────────────────────────────
    def _build(self):
        # ヘッダー
        header = tk.Frame(self, bg=BG_CARD)
        header.pack(fill="x", padx=14, pady=(12, 8))

        tk.Label(header, text="ER 図",
                 font=("Yu Gothic UI", 12, "bold"),
                 fg=TEXT_PRIMARY, bg=BG_CARD).pack(side="left")

        self.lbl_info = tk.Label(header, text="",
                                 font=("Yu Gothic UI", 9),
                                 fg=TEXT_SECONDARY, bg=BG_CARD)
        self.lbl_info.pack(side="left", padx=(14, 0))

        # ツールボタン
        tk.Button(header, text="↺ リセット",
                  font=("Yu Gothic UI", 9),
                  fg=TEXT_SECONDARY, bg=BORDER_COLOR,
                  activeforeground=TEXT_PRIMARY, activebackground=BG_CARD2,
                  relief="flat", bd=0, padx=10, pady=4, cursor="hand2",
                  command=self.reset_layout).pack(side="right", padx=2)
        tk.Button(header, text="🔍－",
                  font=("Yu Gothic UI", 9),
                  fg=TEXT_SECONDARY, bg=BORDER_COLOR,
                  relief="flat", bd=0, padx=8, pady=4, cursor="hand2",
                  command=lambda: self.zoom(0.9)).pack(side="right", padx=2)
        tk.Button(header, text="🔍＋",
                  font=("Yu Gothic UI", 9),
                  fg=TEXT_SECONDARY, bg=BORDER_COLOR,
                  relief="flat", bd=0, padx=8, pady=4, cursor="hand2",
                  command=lambda: self.zoom(1.1)).pack(side="right", padx=2)

        # Canvas + スクロール
        body = tk.Frame(self, bg=BG_DARK)
        body.pack(fill="both", expand=True, padx=14, pady=(0, 14))
        self.canvas = tk.Canvas(body, bg=BG_DARK, highlightthickness=0,
                                bd=0, scrollregion=(0, 0, 2400, 1600))
        ysb = tk.Scrollbar(body, orient="vertical",
                           command=self.canvas.yview,
                           bg=BG_CARD, troughcolor=BG_DARK, width=8)
        xsb = tk.Scrollbar(body, orient="horizontal",
                           command=self.canvas.xview,
                           bg=BG_CARD, troughcolor=BG_DARK, width=8)
        self.canvas.configure(yscrollcommand=ysb.set, xscrollcommand=xsb.set)
        self.canvas.grid(row=0, column=0, sticky="nsew")
        ysb.grid(row=0, column=1, sticky="ns")
        xsb.grid(row=1, column=0, sticky="ew")
        body.rowconfigure(0, weight=1)
        body.columnconfigure(0, weight=1)

        # マウス操作
        self.canvas.bind("<MouseWheel>", self._on_wheel)        # Windows
        self.canvas.bind("<Button-4>", lambda e: self.zoom(1.1)) # Linux
        self.canvas.bind("<Button-5>", lambda e: self.zoom(0.9))
        self.canvas.bind("<ButtonPress-2>", self._on_pan_start)
        self.canvas.bind("<B2-Motion>", self._on_pan_drag)

    # ── ロード ──────────────────────────────────────────────
    def load_schema(self) -> None:
        """DB 全体のスキーマを取得して描画する"""
        if not self.db.is_connected():
            return
        try:
            tables = self.db.list_tables()
            self.fks = self.db.get_foreign_keys()
        except Exception as e:
            self.lbl_info.config(text=f"ロード失敗: {e}", fg=ERROR_COLOR)
            return

        # ノード構築
        self.nodes.clear()
        # FK 参照元・参照先の列セット
        fk_parent_cols = {(fk.parent_table, fk.parent_column) for fk in self.fks}
        fk_ref_cols    = {(fk.ref_table, fk.ref_column)       for fk in self.fks}

        for schema, table in tables:
            try:
                ts = self.db.get_table_schema(schema, table)
            except Exception:
                continue
            cols_info = []
            for c in ts.columns:
                cols_info.append({
                    "name": c.name,
                    "type": c.data_type,
                    "is_pk": c.is_primary_key,
                    "is_fk": (table, c.name) in fk_parent_cols,
                    "is_ref": (table, c.name) in fk_ref_cols,
                })
            self.nodes[table] = TableNode(table_name=table, columns=cols_info)

        # 配置を復元 or 自動配置
        self._auto_layout()
        self._load_layout()

        # 描画
        self._redraw_all()
        self.lbl_info.config(
            text=f"{len(self.nodes)} テーブル / {len(self.fks)} FK",
            fg=TEXT_SECONDARY,
        )

    # ── 外部からの再ロード ──────────────────────────────────
    def refresh(self) -> None:
        """制約変更後などに再ロードする（既存のノード配置はそのまま保持）"""
        # 現在のハイライト状態を退避
        prev_table = self._highlight_table
        prev_column = self._highlight_column
        # 再取得・再描画
        self.load_schema()
        # ハイライトを復元
        if prev_table:
            self.highlight_column(prev_table, prev_column)

    # ── 自動配置（格子）────────────────────────────────────
    def _auto_layout(self):
        for i, (name, node) in enumerate(self.nodes.items()):
            row = i // NODES_PER_ROW
            col = i % NODES_PER_ROW
            node.x = NODE_PAD_X + col * (NODE_WIDTH + NODE_PAD_X * 1.4)
            node.y = NODE_PAD_Y + row * 240

    # ── レイアウト保存・復元 ────────────────────────────────
    def _save_layout(self):
        data = {name: {"x": n.x, "y": n.y}
                for name, n in self.nodes.items()}
        try:
            with open(self.layout_path, "w", encoding="utf-8") as f:
                json.dump(data, f, ensure_ascii=False, indent=2)
        except Exception:
            pass  # 保存失敗は致命的でないので無視

    def _load_layout(self):
        if not self.layout_path.exists():
            return
        try:
            with open(self.layout_path, "r", encoding="utf-8") as f:
                data = json.load(f)
            for name, pos in data.items():
                if name in self.nodes:
                    self.nodes[name].x = float(pos.get("x", self.nodes[name].x))
                    self.nodes[name].y = float(pos.get("y", self.nodes[name].y))
        except Exception:
            pass

    def reset_layout(self):
        """配置をリセット"""
        try:
            if self.layout_path.exists():
                self.layout_path.unlink()
        except Exception:
            pass
        self._auto_layout()
        self._redraw_all()

    # ── 描画 ────────────────────────────────────────────────
    def _redraw_all(self):
        self.canvas.delete("all")
        self.relation_ids.clear()
        self.relations.clear()

        # まずノードを描画
        for node in self.nodes.values():
            self._draw_node(node)

        # 次に FK 線を描画（ノードの上に出ないよう先に線→後でノードが理想だが
        # ここでは線をノードの後で描いて矢印が見えるようにし、ノードを raise する）
        for fk in self.fks:
            self._draw_relation(fk)

        # ノードを最前面に
        for node in self.nodes.values():
            self.canvas.tag_raise(f"node:{node.table_name}")

        # スクロール領域更新
        bbox = self.canvas.bbox("all")
        if bbox:
            pad = 80
            self.canvas.configure(
                scrollregion=(bbox[0] - pad, bbox[1] - pad,
                              bbox[2] + pad, bbox[3] + pad)
            )

        # ハイライト復元
        if self._highlight_table:
            self.highlight_column(self._highlight_table, self._highlight_column)

    def _draw_node(self, node: TableNode):
        tag = f"node:{node.table_name}"
        x, y = node.x, node.y
        w = NODE_WIDTH
        h = node.height

        # 影
        self.canvas.create_rectangle(
            x + 3, y + 3, x + w + 3, y + h + 3,
            fill="#000000", stipple="gray25", outline="",
            tags=(tag, f"shadow:{node.table_name}"),
        )

        # 本体
        node.rect_id = self.canvas.create_rectangle(
            x, y, x + w, y + h,
            fill=BG_CARD, outline=BORDER_COLOR, width=1,
            tags=(tag, f"rect:{node.table_name}"),
        )

        # ヘッダー（テーブル名）
        node.header_id = self.canvas.create_rectangle(
            x, y, x + w, y + NODE_HEADER_H,
            fill=BG_CARD2, outline=BORDER_COLOR, width=1,
            tags=(tag, f"header:{node.table_name}"),
        )
        node.title_id = self.canvas.create_text(
            x + 12, y + NODE_HEADER_H / 2,
            text=node.table_name,
            font=("Yu Gothic UI", 10, "bold"),
            fill=TEXT_PRIMARY, anchor="w",
            tags=(tag, f"title:{node.table_name}"),
        )

        # 列
        node.column_ids = []
        for i, col in enumerate(node.columns):
            cy = y + NODE_HEADER_H + ROW_H * (i + 0.5)
            marks = []
            if col["is_pk"]:
                marks.append("🔑")
            if col["is_fk"]:
                marks.append("FK")
            mark_str = " ".join(marks)
            text = f"{mark_str} {col['name']}" if mark_str else col["name"]
            color = ACCENT_TEAL if col["is_pk"] else (
                ACCENT_PURPLE if col["is_fk"] else TEXT_SECONDARY)
            cid = self.canvas.create_text(
                x + 12, cy,
                text=text,
                font=("Yu Gothic UI", 9),
                fill=color, anchor="w",
                tags=(tag, f"col:{node.table_name}:{col['name']}"),
            )
            node.column_ids.append(cid)

        # イベント
        self.canvas.tag_bind(tag, "<ButtonPress-1>",
                             lambda e, n=node: self._on_node_press(e, n))
        self.canvas.tag_bind(tag, "<B1-Motion>",
                             lambda e, n=node: self._on_node_drag(e, n))
        self.canvas.tag_bind(tag, "<ButtonRelease-1>",
                             lambda e, n=node: self._on_node_release(e, n))
        self.canvas.tag_bind(f"header:{node.table_name}", "<Double-Button-1>",
                             lambda e, n=node: self._on_node_double_click(n))
        self.canvas.tag_bind(f"title:{node.table_name}", "<Double-Button-1>",
                             lambda e, n=node: self._on_node_double_click(n))

    def _draw_relation(self, fk: ForeignKey):
        """FK 線を描画（参照元 → 参照先）"""
        p_node = self.nodes.get(fk.parent_table)
        r_node = self.nodes.get(fk.ref_table)
        if not p_node or not r_node:
            return

        # 接続点：親側は右端 or 左端、参照側も近い方
        p_y = self._column_y(p_node, fk.parent_column)
        r_y = self._column_y(r_node, fk.ref_column)

        # 親が左にいるか右にいるかで接続点決定
        if p_node.x + NODE_WIDTH / 2 < r_node.x + NODE_WIDTH / 2:
            p_x = p_node.x + NODE_WIDTH
            r_x = r_node.x
        else:
            p_x = p_node.x
            r_x = r_node.x + NODE_WIDTH

        # ベジェ風の中継点（横方向に伸ばしてカーブ感を出す）
        dx = (r_x - p_x) * 0.5
        c1 = (p_x + dx, p_y)
        c2 = (r_x - dx, r_y)
        line_id = self.canvas.create_line(
            p_x, p_y, c1[0], c1[1], c2[0], c2[1], r_x, r_y,
            smooth=True, splinesteps=24,
            fill=TEXT_MUTED, width=1.4,
            arrow="last", arrowshape=(10, 12, 4),
            tags=("relation", f"rel:{fk.parent_table}:{fk.parent_column}",
                  f"refrel:{fk.ref_table}:{fk.ref_column}"),
        )
        self.relations.append({
            "fk": fk,
            "line_id": line_id,
        })
        # ハイライト解決用にマップに登録
        key = (fk.parent_table, fk.parent_column)
        self.relation_ids.setdefault(key, []).append(line_id)
        key2 = (fk.ref_table, fk.ref_column)
        self.relation_ids.setdefault(key2, []).append(line_id)

        # 線を裏側へ
        self.canvas.tag_lower(line_id)

    def _column_y(self, node: TableNode, column_name: str) -> float:
        for i, col in enumerate(node.columns):
            if col["name"] == column_name:
                return node.y + NODE_HEADER_H + ROW_H * (i + 0.5)
        # 見つからない場合はヘッダー直下
        return node.y + NODE_HEADER_H + ROW_H / 2

    # ── ハイライト ──────────────────────────────────────────
    def highlight_column(self, table: str,
                         column: Optional[str]) -> None:
        """指定列に関係する FK 線を強調表示"""
        self._highlight_table = table
        self._highlight_column = column

        # 全リレーション線を通常色に戻す
        for rel in self.relations:
            self.canvas.itemconfig(rel["line_id"],
                                   fill=TEXT_MUTED, width=1.4)

        # 関係する線を強調
        if column is None:
            return
        accent = ACCENT_BLUE
        for rel in self.relations:
            fk: ForeignKey = rel["fk"]
            is_related = (
                (fk.parent_table == table and fk.parent_column == column) or
                (fk.ref_table == table and fk.ref_column == column)
            )
            if is_related:
                self.canvas.itemconfig(rel["line_id"],
                                       fill=accent, width=2.6)
                self.canvas.tag_raise(rel["line_id"])

        # ノードのテーブル名背景を強調
        for name, node in self.nodes.items():
            if name == table:
                self.canvas.itemconfig(node.header_id,
                                       fill=ACCENT_BLUE, outline=ACCENT_BLUE)
            else:
                self.canvas.itemconfig(node.header_id,
                                       fill=BG_CARD2, outline=BORDER_COLOR)

    # ── ノード操作 ──────────────────────────────────────────
    def _on_node_press(self, event, node: TableNode):
        cx = self.canvas.canvasx(event.x)
        cy = self.canvas.canvasy(event.y)
        self._drag["node"] = node
        self._drag["dx"] = cx - node.x
        self._drag["dy"] = cy - node.y

    def _on_node_drag(self, event, node: TableNode):
        if self._drag["node"] is not node:
            return
        cx = self.canvas.canvasx(event.x)
        cy = self.canvas.canvasy(event.y)
        new_x = cx - self._drag["dx"]
        new_y = cy - self._drag["dy"]
        dx = new_x - node.x
        dy = new_y - node.y
        node.x = new_x
        node.y = new_y
        # ノード全体を移動
        self.canvas.move(f"node:{node.table_name}", dx, dy)
        # FK 線を再描画
        self._redraw_relations_for(node.table_name)

    def _on_node_release(self, event, node: TableNode):
        if self._drag["node"] is node:
            self._drag["node"] = None
            self._save_layout()

    def _on_node_double_click(self, node: TableNode):
        if self.on_table_click:
            # スキーマは dbo を仮定（list_tables の最初に出てくる schema を使ってもよい）
            schema = self._guess_schema(node.table_name)
            self.on_table_click(schema, node.table_name)

    def _guess_schema(self, table: str) -> str:
        # シンプルに dbo を返す（複数スキーマある場合は要拡張）
        return "dbo"

    def _redraw_relations_for(self, table: str):
        """指定テーブルが絡む FK 線を再描画する"""
        to_redraw = [rel for rel in self.relations
                     if rel["fk"].parent_table == table
                     or rel["fk"].ref_table == table]
        if not to_redraw:
            return
        # 既存線を削除
        for rel in to_redraw:
            self.canvas.delete(rel["line_id"])

        # マップから削除
        for key in list(self.relation_ids.keys()):
            if key[0] == table or any(
                rel["fk"].parent_table == key[0] and
                rel["fk"].parent_column == key[1]
                for rel in to_redraw
            ):
                pass  # 後でクリーンアップ
        # 単純化のため、全マップを再構築する
        self.relations = [rel for rel in self.relations if rel not in to_redraw]
        self.relation_ids.clear()
        # 関係のないものを先に登録し直す
        for rel in self.relations:
            fk = rel["fk"]
            self.relation_ids.setdefault((fk.parent_table, fk.parent_column),
                                         []).append(rel["line_id"])
            self.relation_ids.setdefault((fk.ref_table, fk.ref_column),
                                         []).append(rel["line_id"])

        # 再描画
        for rel in to_redraw:
            self._draw_relation(rel["fk"])

        # ハイライトを再適用
        if self._highlight_table:
            self.highlight_column(self._highlight_table, self._highlight_column)

    # ── ズーム・パン ────────────────────────────────────────
    def zoom(self, factor: float):
        new_zoom = self._zoom * factor
        if not (0.4 <= new_zoom <= 2.5):
            return
        self.canvas.scale("all",
                          self.canvas.canvasx(0), self.canvas.canvasy(0),
                          factor, factor)
        self._zoom = new_zoom
        bbox = self.canvas.bbox("all")
        if bbox:
            pad = 80
            self.canvas.configure(
                scrollregion=(bbox[0] - pad, bbox[1] - pad,
                              bbox[2] + pad, bbox[3] + pad))

    def _on_wheel(self, event):
        factor = 1.1 if event.delta > 0 else 0.9
        self.zoom(factor)

    def _on_pan_start(self, event):
        self.canvas.scan_mark(event.x, event.y)

    def _on_pan_drag(self, event):
        self.canvas.scan_dragto(event.x, event.y, gain=1)
