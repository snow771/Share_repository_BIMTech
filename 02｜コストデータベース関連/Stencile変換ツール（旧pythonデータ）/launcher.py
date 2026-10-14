# -*- coding: utf-8 -*-
"""
Stencile ツールランチャー  v1.2
ステンシルデータ変換 & 査定・修正単価マスタ作成 & コストDB編集 の統合起動GUI

v1.2 変更点:
  - ③ コストDB編集ツール（modules.db_editor）を追加
  - 3カード構成に対応してウィンドウ高さを拡張
  - DBエディタ用のラッパーを追加

v1.1 から継続:
  - wmf UserWarning を子プロセス側で抑制
  - ① ツールのダイアログ操作をステップログとして表示
  - ② ツールの文字化け修正（PYTHONIOENCODING=utf-8 強制）
  - ログの色分け（OK=緑 / ERROR=赤 / WARN=黄 / STEP=青 / DIALOG=ティール）
  - exec() による元スクリプト二重起動を防止
"""

import tkinter as tk
import subprocess
import sys
import os
import threading
import queue
from pathlib import Path
import time
import re
import tempfile


# ─── カラーパレット ───────────────────────────────────────────
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


# ─────────────────────────────────────────────────────────────
#  ラッパースクリプトのテンプレート
#  {script_dir} は実行時に置換される
# ─────────────────────────────────────────────────────────────

# ① stencilデータ変換_完成版.py 用
TOOL1_WRAPPER = '''\
import warnings
warnings.filterwarnings("ignore", message=".*wmf.*",  category=UserWarning)
warnings.filterwarnings("ignore", message=".*image.*", category=UserWarning)

import sys, os
from pathlib import Path

SCRIPT_DIR = Path(r"{script_dir}")
sys.path.insert(0, str(SCRIPT_DIR))

def log(msg):
    print(msg, flush=True)

log("[STEP] モジュールを読み込んでいます...")

_src = open(str(SCRIPT_DIR / "stencilデータ変換_完成版.py"), encoding="utf-8").read()
_module_globals = {
    "__name__": "tool1_loaded_module",
    "__file__": str(SCRIPT_DIR / "stencilデータ変換_完成版.py"),
}
exec(compile(_src, "stencilデータ変換_完成版.py", "exec"), _module_globals)
globals().update(_module_globals)

import tkinter as _tk
from tkinter import filedialog as _fd, messagebox as _mb

def choose_inputs():
    global root
    if 'root' not in globals():
        globals()['root'] = None

    if root is None:
        root = _tk.Tk()
        root.withdraw()

    log("[DIALOG] フォルダ選択ダイアログを開いています...")
    folder = _fd.askdirectory(title="元データ Excel が入っているフォルダを選択してください")
    if not folder:
        log("[CANCEL] フォルダが選択されませんでした")
        sys.exit(0)
    log(f"[OK] フォルダ選択完了: {folder}")

    folder_path = Path(folder)
    script_dir2 = SCRIPT_DIR
    yaml_path = find_mapping_yaml(script_dir2, folder_path)

    if yaml_path is None:
        log("[DIALOG] マッピング YAML を選択してください...")
        selected = _fd.askopenfilename(
            title="マッピング YAML を選択してください",
            filetypes=[("YAML", "*.yaml *.yml"), ("All files", "*.*")],
        )
        if not selected:
            log("[CANCEL] YAML が選択されませんでした")
            sys.exit(0)
        yaml_path = Path(selected)
        log(f"[OK] YAML 選択完了: {yaml_path.name}")
    else:
        log(f"[OK] マッピングYAML 自動検出: {yaml_path.name}")

    log("[DIALOG] 出力ファイルの保存先を選択してください...")
    output_path = _fd.asksaveasfilename(
        title="出力先ファイル名を指定してください",
        defaultextension=".xlsx",
        filetypes=[("Excel", "*.xlsx")],
        initialfile="物件諸情報一覧.xlsx",
    )
    if not output_path:
        log("[CANCEL] 保存先が選択されませんでした")
        sys.exit(0)
    log(f"[OK] 保存先確定: {output_path}")

    return Path(folder), yaml_path, Path(output_path)

from openpyxl import Workbook as _Workbook

def main():
    folder_path, yaml_path, output_path = choose_inputs()

    log("[STEP] マッピングYAMLを読み込んでいます...")
    mapping = load_yaml_mapping(yaml_path)
    log("[OK] YAML 読み込み完了")

    log("[STEP] Excelファイルを検索しています...")
    excel_files = collect_excel_files(folder_path)
    total = len(excel_files)
    if not total:
        log("[ERROR] 対象となるExcelファイルが見つかりませんでした")
        return
    log(f"[OK] {total} 件のファイルを検出しました")

    rows = []
    skipped = []
    for i, excel_path in enumerate(excel_files, 1):
        log(f"[FILE] ファイル {i}/{total}: {excel_path.name}")
        try:
            row = extract_row_from_workbook(excel_path, mapping)
            if not row.get("project_id") and not row.get("物件名"):
                log(f"  → スキップ（抽出対象データなし）")
                skipped.append(f"{excel_path.name} : 抽出対象データなし")
            else:
                rows.append(row)
                log(f"  → 抽出完了")
        except Exception as e:
            log(f"  → エラー: {e}")
            skipped.append(f"{excel_path.name} : {e}")

    if not rows:
        log("[ERROR] 処理できたファイルがありませんでした")
        return

    log("[STEP] データを整理して出力ファイルを生成しています...")
    rows.sort(key=lambda x: (str(x.get("project_id") or ""), str(x.get("物件名") or "")))

    wb = _Workbook()
    ws = wb.active
    ws.title = output_path.stem[:31]
    apply_output_format(ws)
    for r_idx, row_data in enumerate(rows, start=2):
        for c_idx, header in enumerate(OUTPUT_COLUMNS, start=1):
            ws.cell(row=r_idx, column=c_idx, value=row_data.get(header))
    apply_output_format(ws)
    wb.save(output_path)

    log(f"[DONE] 出力完了: {output_path}")
    if skipped:
        log(f"[WARN] スキップ件数: {len(skipped)}")
        for s in skipped[:10]:
            log(f"  - {s}")

main()
'''


# ② stencile単価集約ツール.py 用
TOOL2_WRAPPER = '''\
import warnings
warnings.filterwarnings("ignore", message=".*wmf.*",  category=UserWarning)
warnings.filterwarnings("ignore", message=".*image.*", category=UserWarning)

import sys
from pathlib import Path

SCRIPT_DIR = Path(r"{script_dir}")
sys.path.insert(0, str(SCRIPT_DIR))

_src = open(str(SCRIPT_DIR / "stencile単価集約ツール.py"), encoding="utf-8").read()
_module_globals = {
    "__name__": "tool2_loaded_module",
    "__file__": str(SCRIPT_DIR / "stencile単価集約ツール.py"),
}
exec(compile(_src, "stencile単価集約ツール.py", "exec"), _module_globals)
globals().update(_module_globals)

main()
'''


# ③ modules.db_editor 用
#   - DB エディタは対話的UI なので、ログ出力は少ない
#   - 起動・接続成功・終了のみログを流す
TOOL3_WRAPPER = '''\
import warnings
warnings.filterwarnings("ignore", message=".*wmf.*",  category=UserWarning)
warnings.filterwarnings("ignore", message=".*image.*", category=UserWarning)

import sys
from pathlib import Path

SCRIPT_DIR = Path(r"{script_dir}")
sys.path.insert(0, str(SCRIPT_DIR))

def log(msg):
    print(msg, flush=True)

log("[STEP] DBエディタを起動しています...")

try:
    from modules.db_editor.db_editor import main as _db_main
    log("[OK] モジュール読み込み完了")
except ImportError as e:
    log(f"[ERROR] モジュール読み込み失敗: {e}")
    log("[ERROR] pyodbc・pandas のインストール、")
    log("[ERROR] および modules/db_editor/ ディレクトリの配置を確認してください。")
    sys.exit(1)

log("[STEP] DBエディタウィンドウを開きます")
log("[DIALOG] 起動後の接続ダイアログでサーバー・データベースを指定してください")
log("[DIALOG] サーバー初期値: localhost\\\\sqlexpress  (Windows統合認証)")

try:
    _db_main()
except Exception as e:
    log(f"[ERROR] DBエディタの実行中にエラー: {e}")
    sys.exit(1)

log("[DONE] DBエディタを終了しました")
'''


# ─────────────────────────────────────────────────────────────

class AnimatedProgressBar(tk.Frame):
    """
    tk.Frame に tk.Canvas を子として持つ方式。
    - _w/_h は tkinter 内部属性と衝突するため _bw/_bh を使用
    - Frame.__init__ に width/height を渡さない
    - create_rectangle は after_idle で遅延実行
    """
    def __init__(self, parent, bar_width=460, bar_height=5, accent=ACCENT_BLUE, **kwargs):
        for key in ("width", "height"):
            kwargs.pop(key, None)
        super().__init__(parent, bg=BG_CARD, **kwargs)

        self._bw     = int(bar_width)
        self._bh     = int(bar_height)
        self._accent = accent
        self._anim   = False
        self._pos    = 0.0
        self._dir    = 1
        self._bar_id = None
        self._gl_id  = None

        self._cv = tk.Canvas(self,
                             width=self._bw, height=self._bh,
                             bg=BG_CARD, highlightthickness=0, bd=0)
        self._cv.pack()
        self.after_idle(self._init_shapes)

    def _init_shapes(self):
        self._cv.create_rectangle(0, 0, self._bw, self._bh,
                                  fill="#2a2d3e", outline="")
        self._bar_id = self._cv.create_rectangle(
            0, 0, 0, self._bh, fill=self._accent, outline="")
        glow = self._lighten(self._accent)
        self._gl_id = self._cv.create_rectangle(
            0, 0, 0, self._bh, fill=glow, outline="", stipple="gray50")

    @staticmethod
    def _lighten(h):
        r = min(255, int(h[1:3], 16) + 60)
        g = min(255, int(h[3:5], 16) + 60)
        b = min(255, int(h[5:7], 16) + 60)
        return f"#{r:02x}{g:02x}{b:02x}"

    def _sc(self, item_id, *coords):
        """安全な coords 更新（None や初期化前は無視）"""
        if item_id is not None:
            try:
                self._cv.coords(item_id, *coords)
            except Exception:
                pass

    def set_progress(self, v):
        self._anim = False
        w = int(self._bw * max(0.0, min(1.0, v)))
        self._sc(self._bar_id, 0, 0, w, self._bh)
        self._sc(self._gl_id,  max(0, w - 30), 0, w, self._bh)

    def start_indeterminate(self):
        self._anim = True
        self._pos  = 0.0
        self._dir  = 1
        self._tick()

    def stop_indeterminate(self):
        self._anim = False

    def _tick(self):
        if not self._anim:
            return
        self._pos += 0.015 * self._dir
        if self._pos >= 1.0:
            self._pos, self._dir = 1.0, -1
        elif self._pos <= 0.0:
            self._pos, self._dir = 0.0, 1
        seg = int(self._bw * 0.35)
        s   = int(self._bw * self._pos * 0.65)
        self._sc(self._bar_id, s, 0, s + seg, self._bh)
        self._sc(self._gl_id,  s + seg - 15, 0, s + seg, self._bh)
        self.after(16, self._tick)

    def reset(self):
        self._anim = False
        self._sc(self._bar_id, 0, 0, 0, self._bh)
        self._sc(self._gl_id,  0, 0, 0, self._bh)


class ToolCard(tk.Frame):
    def __init__(self, parent, number, title, subtitle,
                 accent_color, tool_id, **kwargs):
        super().__init__(parent, bg=BG_CARD, **kwargs)
        self.accent   = accent_color
        self.tool_id  = tool_id
        self._proc    = None
        self._q       = queue.Queue()
        self._running = False
        self._total   = 0

        self.config(highlightbackground=BORDER_COLOR, highlightthickness=1)
        self._build(number, title, subtitle)

    # ── UI ─────────────────────────────────────────────────────
    def _build(self, number, title, subtitle):
        hdr = tk.Frame(self, bg=BG_CARD)
        hdr.pack(fill="x", padx=24, pady=(22, 0))
        tk.Label(hdr, text=number, font=("Courier New", 12, "bold"),
                 fg=self.accent, bg=BG_CARD, width=3).pack(side="left")
        tk.Label(hdr, text=title, font=("Yu Gothic UI", 14, "bold"),
                 fg=TEXT_PRIMARY, bg=BG_CARD).pack(side="left", padx=(6, 0))
        tk.Label(self, text=subtitle, font=("Yu Gothic UI", 9),
                 fg=TEXT_SECONDARY, bg=BG_CARD,
                 wraplength=480, justify="left").pack(anchor="w", padx=24)

        tk.Frame(self, bg=BORDER_COLOR, height=1).pack(
            fill="x", padx=24, pady=(14, 10))

        # ステータス行
        sr = tk.Frame(self, bg=BG_CARD)
        sr.pack(fill="x", padx=24, pady=(0, 6))
        self.dot = tk.Label(sr, text="●", font=("Arial", 8),
                            fg=TEXT_MUTED, bg=BG_CARD)
        self.dot.pack(side="left")
        self.slbl = tk.Label(sr, text="待機中", font=("Yu Gothic UI", 9),
                             fg=TEXT_MUTED, bg=BG_CARD)
        self.slbl.pack(side="left", padx=(5, 0))
        self.tlbl = tk.Label(sr, text="", font=("Courier New", 8),
                             fg=TEXT_MUTED, bg=BG_CARD)
        self.tlbl.pack(side="right")

        # プログレスバー
        self.bar = AnimatedProgressBar(self, bar_width=460, bar_height=5, accent=self.accent)
        self.bar.pack(padx=24, pady=(0, 4))

        # カウンター
        self.clbl = tk.Label(self, text="", font=("Yu Gothic UI", 8),
                             fg=TEXT_MUTED, bg=BG_CARD)
        self.clbl.pack(anchor="e", padx=26, pady=(0, 6))

        # ログ
        lf = tk.Frame(self, bg="#13151f")
        lf.pack(fill="both", expand=True, padx=24, pady=(0, 12))
        self.log = tk.Text(lf, height=6, bg="#13151f", fg=TEXT_SECONDARY,
                           font=("Yu Gothic UI", 9), relief="flat", bd=0,
                           insertbackground=TEXT_PRIMARY, wrap="word",
                           state="disabled", selectbackground=ACCENT_BLUE)
        sb = tk.Scrollbar(lf, orient="vertical", command=self.log.yview,
                          bg=BG_CARD, troughcolor="#13151f", width=7)
        self.log.config(yscrollcommand=sb.set)
        for tag, col in [("ok",   SUCCESS_COLOR), ("err",  ERROR_COLOR),
                         ("warn", WARN_COLOR),    ("step", ACCENT_BLUE),
                         ("file", TEXT_PRIMARY),  ("done", SUCCESS_COLOR),
                         ("dlg",  ACCENT_TEAL)]:
            self.log.tag_config(tag, foreground=col)
        sb.pack(side="right", fill="y")
        self.log.pack(side="left", fill="both", expand=True, padx=4, pady=4)

        # ボタン
        br = tk.Frame(self, bg=BG_CARD)
        br.pack(fill="x", padx=24, pady=(0, 20))
        self.run_btn = tk.Button(br, text="▶  起動する",
            font=("Yu Gothic UI", 10, "bold"), fg="white", bg=self.accent,
            activeforeground="white", activebackground=self.accent,
            relief="flat", bd=0, padx=20, pady=8, cursor="hand2",
            command=self._on_run)
        self.run_btn.pack(side="left")
        self.stop_btn = tk.Button(br, text="■  停止",
            font=("Yu Gothic UI", 9), fg=TEXT_SECONDARY, bg=BORDER_COLOR,
            activeforeground=TEXT_PRIMARY, activebackground=BG_CARD2,
            relief="flat", bd=0, padx=14, pady=8, cursor="hand2",
            state="disabled", command=self._on_stop)
        self.stop_btn.pack(side="left", padx=(8, 0))
        tk.Button(br, text="ログクリア",
            font=("Yu Gothic UI", 9), fg=TEXT_MUTED, bg=BG_CARD,
            activeforeground=TEXT_SECONDARY, activebackground=BG_CARD,
            relief="flat", bd=0, padx=14, pady=8, cursor="hand2",
            command=self._clear).pack(side="right")

    # ── ロジック ────────────────────────────────────────────────
    def _on_run(self):
        if self._running:
            return
        self._running = True
        self._t0      = time.time()
        self._total   = 0
        self.run_btn.config(state="disabled", text="実行中…")
        self.stop_btn.config(state="normal")
        self.bar.start_indeterminate()
        self._status("running", "起動しています…")
        self._write("▶ ツールを起動しました\n", "step")

        threading.Thread(target=self._worker, daemon=True).start()
        self.after(100, self._poll)
        self.after(500, self._timer)

    def _select_wrapper(self):
        """tool_id ごとにラッパースクリプトを返す"""
        if self.tool_id == 1:
            return TOOL1_WRAPPER
        if self.tool_id == 2:
            return TOOL2_WRAPPER
        if self.tool_id == 3:
            return TOOL3_WRAPPER
        raise ValueError(f"未知の tool_id: {self.tool_id}")

    def _worker(self):
        script_dir = str(Path(__file__).parent)
        esc = script_dir.replace("\\", "\\\\")
        wrapper = self._select_wrapper()
        code = wrapper.replace("{script_dir}", esc)

        tmp = tempfile.NamedTemporaryFile(
            mode="w", suffix=".py", encoding="utf-8",
            delete=False, prefix="stencile_tmp_")
        tmp.write(code)
        tmp.close()

        env = os.environ.copy()
        env["PYTHONIOENCODING"] = "utf-8"
        env["PYTHONWARNINGS"]   = "ignore::UserWarning"

        try:
            self._proc = subprocess.Popen(
                [sys.executable, "-u", tmp.name],
                stdout=subprocess.PIPE, stderr=subprocess.STDOUT,
                env=env, text=True, encoding="utf-8", errors="replace",
                creationflags=(subprocess.CREATE_NO_WINDOW
                               if sys.platform == "win32" else 0),
            )
            for line in self._proc.stdout:
                self._q.put(("log", line.rstrip("\n")))
            self._proc.wait()
            rc = self._proc.returncode
            if rc == 0:
                self._q.put(("done", "✓ 処理が正常に完了しました"))
            else:
                self._q.put(("error", f"✗ 終了コード {rc} で終了しました"))
        except Exception as e:
            self._q.put(("error", f"✗ 起動エラー: {e}"))
        finally:
            try:
                os.unlink(tmp.name)
            except Exception:
                pass

    def _poll(self):
        try:
            while True:
                kind, msg = self._q.get_nowait()
                if kind == "log":
                    self._handle(msg)
                elif kind == "done":
                    self._write(f"\n{msg}\n", "done")
                    self._finish(True)
                    return
                elif kind == "error":
                    self._write(f"\n{msg}\n", "err")
                    self._finish(False)
                    return
        except queue.Empty:
            pass
        if self._running:
            self.after(100, self._poll)

    def _handle(self, line: str):
        """ログ行を解析してタグ付き表示＋進捗更新"""
        if line.startswith("[DONE]"):
            tag = "done"
        elif line.startswith("[OK]"):
            tag = "ok"
        elif line.startswith("[ERROR]"):
            tag = "err"
        elif line.startswith("[WARN]") or "警告" in line:
            tag = "warn"
        elif line.startswith("[STEP]") or line.startswith("【"):
            tag = "step"
        elif line.startswith("[DIALOG]") or line.startswith("[CANCEL]"):
            tag = "dlg"
        elif line.startswith("[FILE]") or re.search(r"処理中.*xlsm|処理中.*xlsx", line):
            tag = "file"
        elif line.startswith("  →") or line.startswith("  -"):
            tag = "warn" if ("エラー" in line or "スキップ" in line) else "ok"
        elif "完了" in line or "正常" in line:
            tag = "done"
        else:
            tag = ""
        self._write(line + "\n", tag)

        # 進捗抽出パターン
        m = re.search(r"ファイル\s+(\d+)/(\d+)", line)
        if m:
            cur, tot = int(m.group(1)), int(m.group(2))
            if tot > 0:
                self._total = tot
                self.bar.stop_indeterminate()
                self.bar.set_progress(cur / tot)
                self.clbl.config(text=f"{cur} / {tot} ファイル処理済み",
                                 fg=TEXT_SECONDARY)
                self._status("running", f"処理中… ({cur}/{tot})")
            return

        m2 = re.search(r"見つかったファイル数[：:]\s*(\d+)", line)
        if m2:
            self._total = int(m2.group(1))
            self.clbl.config(text=f"0 / {self._total} ファイル処理済み",
                             fg=TEXT_SECONDARY)
            return

        if "[DIALOG]" in line:
            self._status("running", "ダイアログの操作を待っています…")
        elif "[OK]" in line and "フォルダ" in line:
            self._status("running", "フォルダ確定 — 処理準備中…")
        elif "[OK]" in line and "保存先" in line:
            self._status("running", "保存先確定 — 処理中…")

    def _timer(self):
        if not self._running:
            return
        self.tlbl.config(text=f"{int(time.time()-self._t0)}s")
        self.after(500, self._timer)

    def _finish(self, ok: bool):
        self._running = False
        self.bar.stop_indeterminate()
        elapsed = time.time() - self._t0
        self.tlbl.config(text=f"{elapsed:.1f}s")
        if ok:
            self.bar.set_progress(1.0)
            if self._total:
                self.clbl.config(
                    text=f"{self._total} / {self._total} ファイル完了",
                    fg=SUCCESS_COLOR)
            self._status("done", "処理完了")
        else:
            self._status("error", "エラーが発生しました")
        self.run_btn.config(state="normal", text="▶  起動する")
        self.stop_btn.config(state="disabled")

    def _on_stop(self):
        if self._proc and self._proc.poll() is None:
            self._proc.terminate()
        self._running = False
        self.bar.stop_indeterminate()
        self._write("\n■ ユーザーによって停止されました\n", "warn")
        self._status("warn", "停止しました")
        self.run_btn.config(state="normal", text="▶  起動する")
        self.stop_btn.config(state="disabled")

    def _status(self, kind, text):
        col = {"running": ACCENT_BLUE, "done": SUCCESS_COLOR,
               "error": ERROR_COLOR, "warn": WARN_COLOR}.get(kind, TEXT_MUTED)
        self.dot.config(fg=col)
        self.slbl.config(fg=col, text=text)

    def _write(self, text, tag=""):
        self.log.config(state="normal")
        self.log.insert("end", text, tag) if tag else self.log.insert("end", text)
        self.log.see("end")
        self.log.config(state="disabled")

    def _clear(self):
        self.log.config(state="normal")
        self.log.delete("1.0", "end")
        self.log.config(state="disabled")
        self.bar.reset()
        self.clbl.config(text="", fg=TEXT_MUTED)
        self.dot.config(fg=TEXT_MUTED)
        self.slbl.config(fg=TEXT_MUTED, text="待機中")
        self.tlbl.config(text="")


class App(tk.Tk):
    def __init__(self):
        super().__init__()
        self.title("Stencile ツールランチャー  v1.2")
        self.configure(bg=BG_DARK)
        self.resizable(True, True)
        self.minsize(560, 900)
        self._build()
        self._center()

    def _build(self):
        # スクロール可能な領域を作成（3カード分の高さに対応）
        outer = tk.Frame(self, bg=BG_DARK)
        outer.pack(fill="both", expand=True)

        canvas = tk.Canvas(outer, bg=BG_DARK, highlightthickness=0, bd=0)
        sb = tk.Scrollbar(outer, orient="vertical", command=canvas.yview,
                          bg=BG_DARK, troughcolor=BG_DARK, width=8)
        canvas.configure(yscrollcommand=sb.set)
        canvas.pack(side="left", fill="both", expand=True)
        sb.pack(side="right", fill="y")

        content = tk.Frame(canvas, bg=BG_DARK)
        canvas.create_window((0, 0), window=content, anchor="nw",
                             tags=("content",))
        # ウィンドウ幅変化に追従
        def _on_canvas_resize(event):
            canvas.itemconfig("content", width=event.width)
        canvas.bind("<Configure>", _on_canvas_resize)
        content.bind("<Configure>",
                     lambda e: canvas.configure(scrollregion=canvas.bbox("all")))
        # マウスホイール
        def _on_wheel(event):
            canvas.yview_scroll(int(-1 * (event.delta / 120)), "units")
        canvas.bind_all("<MouseWheel>", _on_wheel)

        # ─ ヘッダー ─
        hdr = tk.Frame(content, bg=BG_DARK)
        hdr.pack(fill="x", padx=30, pady=(28, 0))
        tk.Label(hdr, text="STENCILE", font=("Courier New", 10, "bold"),
                 fg=ACCENT_BLUE, bg=BG_DARK).pack(anchor="w")
        tk.Label(hdr, text="ツールランチャー",
                 font=("Yu Gothic UI", 22, "bold"),
                 fg=TEXT_PRIMARY, bg=BG_DARK).pack(anchor="w")
        tk.Label(hdr, text="各種データ処理ツールを統合管理します",
                 font=("Yu Gothic UI", 10), fg=TEXT_SECONDARY, bg=BG_DARK
                 ).pack(anchor="w", pady=(2, 0))

        tk.Frame(content, bg=BORDER_COLOR, height=1).pack(
            fill="x", padx=30, pady=(18, 20))

        # ─ ① 物件諸情報変換 ─
        ToolCard(content,
                 number="①",
                 title="Stencile 情報変換",
                 subtitle="フォルダ内の複数Excelから物件諸情報を抽出し、一覧ファイルへ出力します",
                 accent_color=ACCENT_BLUE,
                 tool_id=1,
                 ).pack(fill="x", padx=30, pady=(0, 16))

        # ─ ② 査定・修正単価集約 ─
        ToolCard(content,
                 number="②",
                 title="査定・修正 単価マスタ作成",
                 subtitle="複数ExcelからE/F列の単価データを集約し、テンプレートに基づいた単価テーブルを生成します",
                 accent_color=ACCENT_TEAL,
                 tool_id=2,
                 ).pack(fill="x", padx=30, pady=(0, 16))

        # ─ ③ コストDB編集 ─
        ToolCard(content,
                 number="③",
                 title="コストデータベース編集",
                 subtitle=(
                    "SQL Server (localhost\\sqlexpress) に Excel ライクなGUIで接続し、"
                    "テーブルの編集・行/列の追加削除を行います。"
                    "ER図でリレーションも視覚化されます"
                 ),
                 accent_color=ACCENT_PURPLE,
                 tool_id=3,
                 ).pack(fill="x", padx=30, pady=(0, 16))

        tk.Label(content,
                 text="※ WMF画像形式の警告は自動的に非表示にしています（処理に影響なし）",
                 font=("Yu Gothic UI", 8), fg=TEXT_MUTED, bg=BG_DARK
                 ).pack(anchor="w", padx=30, pady=(0, 14))

    def _center(self):
        self.update_idletasks()
        w, h = 600, 940
        sw, sh = self.winfo_screenwidth(), self.winfo_screenheight()
        # 画面の高さを超えないように調整
        h = min(h, sh - 60)
        self.geometry(f"{w}x{h}+{(sw-w)//2}+{max(0,(sh-h)//2)}")


if __name__ == "__main__":
    App().mainloop()
