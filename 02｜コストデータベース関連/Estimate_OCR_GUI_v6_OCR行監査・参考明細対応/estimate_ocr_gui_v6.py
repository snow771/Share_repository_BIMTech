"""
協力業者見積 AI-OCR GUI v3
2ページずつ全ページをOCRして、1つのJSON・Excelへ統合する画面。
"""

from __future__ import annotations
import json
import os
import queue
import subprocess
import sys
import threading
from datetime import datetime
from pathlib import Path
from tkinter import Tk, StringVar, BooleanVar, END, filedialog, messagebox
from tkinter import ttk
from tkinter.scrolledtext import ScrolledText

try:
    from pypdf import PdfReader
    PYPDF_READY = True
except Exception:
    PYPDF_READY = False

APP_TITLE = "協力業者見積 AI-OCR v6（OCR行監査対応）"
SCRIPT_DIR = Path(__file__).resolve().parent
BATCH_SCRIPT = SCRIPT_DIR / "estimate_ocr_batch_f0.py"
STRUCTURED_SCRIPT = SCRIPT_DIR / "estimate_ocr_structured_v6.py"
DEFAULT_COMMON_RULES = SCRIPT_DIR / "rules_common_v3.json"


class App:
    def __init__(self, root: Tk):
        self.root = root
        self.root.title(APP_TITLE)
        self.root.geometry("1050x810")
        self.root.minsize(950, 700)

        self.pdfs: list[Path] = []
        self.page_counts: dict[str, int | None] = {}
        self.queue: queue.Queue = queue.Queue()
        self.running = False

        self.output_dir = StringVar()
        self.common_rules = StringVar(value=str(DEFAULT_COMMON_RULES) if DEFAULT_COMMON_RULES.exists() else "")
        self.supplier_rules = StringVar()
        self.do_structure = BooleanVar(value=True)
        self.keep_raw_json = BooleanVar(value=True)
        self.keep_raw_excel = BooleanVar(value=True)
        self.keep_chunks = BooleanVar(value=False)
        self.status = StringVar(value="PDFを選択してください。")

        self.build()
        self.check_environment()
        self.root.after(150, self.flush)

    def build(self):
        style = ttk.Style()
        try:
            style.theme_use("clam")
        except Exception:
            pass
        style.configure("Header.TLabel", font=("", 13, "bold"))
        style.configure("Hint.TLabel", foreground="#4B5563")
        style.configure("Warn.TLabel", foreground="#B45309")
        style.configure("Run.TButton", font=("", 11, "bold"), padding=(16, 9))

        outer = ttk.Frame(self.root, padding=14)
        outer.pack(fill="both", expand=True)
        ttk.Label(outer, text=APP_TITLE, style="Header.TLabel").pack(anchor="w")
        ttk.Label(
            outer,
            text="PDFを2ページずつ自動分割してAzure OCRを実行し、PDF全体を1つのJSON・Excelへ統合します。",
            style="Hint.TLabel",
        ).pack(anchor="w", pady=(2, 2))
        ttk.Label(
            outer,
            text="無料枠向け：6ページPDFなら「1-2 / 3-4 / 5-6」の3回OCR後、全ページ結果を統合します。",
            style="Warn.TLabel",
        ).pack(anchor="w", pady=(0, 12))

        f = ttk.LabelFrame(outer, text="1. 見積PDFの選択", padding=10)
        f.pack(fill="both")
        row = ttk.Frame(f); row.pack(fill="x")
        ttk.Button(row, text="PDFを追加...", command=self.choose_pdfs).pack(side="left")
        ttk.Button(row, text="選択をクリア", command=self.clear_pdfs).pack(side="left", padx=(8,0))
        self.count_label = ttk.Label(row, text="選択件数：0件", style="Hint.TLabel")
        self.count_label.pack(side="right")

        box = ttk.Frame(f); box.pack(fill="both", expand=True, pady=(8,0))
        self.tree = ttk.Treeview(box, columns=("name", "pages", "path"), show="headings", height=6)
        self.tree.heading("name", text="ファイル名")
        self.tree.heading("pages", text="PDF頁")
        self.tree.heading("path", text="フルパス")
        self.tree.column("name", width=260)
        self.tree.column("pages", width=70, anchor="center")
        self.tree.column("path", width=650)
        self.tree.pack(side="left", fill="both", expand=True)
        scroll = ttk.Scrollbar(box, orient="vertical", command=self.tree.yview)
        scroll.pack(side="right", fill="y")
        self.tree.configure(yscrollcommand=scroll.set)

        f = ttk.LabelFrame(outer, text="2. 保存先", padding=10)
        f.pack(fill="x", pady=(10,0))
        self.path_row(f, "出力フォルダ", self.output_dir, self.choose_output, folder=True)

        f = ttk.LabelFrame(outer, text="3. 構造化ルール", padding=10)
        f.pack(fill="x", pady=(10,0))
        self.path_row(f, "共通ルールJSON（必須）", self.common_rules, self.choose_common)
        self.path_row(f, "協力業者差分ルールJSON（任意）", self.supplier_rules, self.choose_supplier, clear=True)

        f = ttk.LabelFrame(outer, text="4. 実行", padding=10)
        f.pack(fill="x", pady=(10,0))
        options = ttk.Frame(f); options.pack(side="left", fill="x", expand=True)
        ttk.Checkbutton(options, text="構造化Excel・JSONを作成する", variable=self.do_structure).pack(anchor="w")
        ttk.Checkbutton(options, text="統合したAzure生JSONを保存する", variable=self.keep_raw_json).pack(anchor="w")
        ttk.Checkbutton(options, text="統合したOCR確認用Excelを保存する", variable=self.keep_raw_excel).pack(anchor="w")
        ttk.Checkbutton(options, text="2ページ分割PDFも残す（通常は不要）", variable=self.keep_chunks).pack(anchor="w")
        self.run_button = ttk.Button(f, text="全ページOCR・構造化を実行", style="Run.TButton", command=self.start)
        self.run_button.pack(side="right", padx=(14,0))

        ttk.Label(outer, textvariable=self.status, style="Hint.TLabel").pack(anchor="w", pady=(12,4))
        f = ttk.LabelFrame(outer, text="処理ログ", padding=6)
        f.pack(fill="both", expand=True)
        self.log_box = ScrolledText(f, height=14, wrap="word", state="disabled")
        self.log_box.pack(fill="both", expand=True)

    def path_row(self, parent, label, var, command, folder=False, clear=False):
        r = ttk.Frame(parent); r.pack(fill="x", pady=2)
        ttk.Label(r, text=label, width=31).pack(side="left")
        ttk.Entry(r, textvariable=var).pack(side="left", fill="x", expand=True, padx=(4,8))
        ttk.Button(r, text="フォルダ選択..." if folder else "ファイル選択...", command=command).pack(side="left")
        if clear:
            ttk.Button(r, text="解除", command=lambda: var.set("")).pack(side="left", padx=(6,0))

    def check_environment(self):
        missing = [p.name for p in [BATCH_SCRIPT, STRUCTURED_SCRIPT] if not p.exists()]
        if missing:
            self.log("必要なスクリプトが見つかりません: " + ", ".join(missing))
        if not PYPDF_READY:
            self.log("注意：pypdf が未導入です。PowerShellで python -m pip install pypdf を実行してください。")
        if os.getenv("DI_ENDPOINT") and os.getenv("DI_KEY"):
            self.log("Azure接続用環境変数を確認しました。")
        else:
            self.log("注意：DI_ENDPOINT または DI_KEY が見つかりません。")

    def pdf_pages(self, path: Path):
        if not PYPDF_READY:
            return None
        try:
            return len(PdfReader(str(path)).pages)
        except Exception:
            return None

    def choose_pdfs(self):
        names = filedialog.askopenfilenames(title="見積PDFを選択（複数選択可）", filetypes=[("PDF", "*.pdf")])
        known = {str(x).lower() for x in self.pdfs}
        for name in names:
            p = Path(name)
            if str(p).lower() not in known:
                self.pdfs.append(p)
                self.page_counts[str(p)] = self.pdf_pages(p)
                known.add(str(p).lower())
        self.refresh()
        if self.pdfs and not self.output_dir.get():
            self.output_dir.set(str(self.pdfs[0].parent / "OCR_結果"))

    def clear_pdfs(self):
        self.pdfs = []
        self.page_counts = {}
        self.refresh()

    def refresh(self):
        for item in self.tree.get_children():
            self.tree.delete(item)
        for p in self.pdfs:
            pages = self.page_counts.get(str(p))
            self.tree.insert("", "end", values=(p.name, pages if pages else "不明", str(p)))
        self.count_label.config(text=f"選択件数：{len(self.pdfs)}件")

    def choose_output(self):
        value = filedialog.askdirectory(title="保存先フォルダを選択")
        if value:
            self.output_dir.set(value)

    def choose_common(self):
        value = filedialog.askopenfilename(title="共通ルールJSON", filetypes=[("JSON", "*.json")])
        if value:
            self.common_rules.set(value)

    def choose_supplier(self):
        value = filedialog.askopenfilename(title="協力業者差分ルールJSON", filetypes=[("JSON", "*.json")])
        if value:
            self.supplier_rules.set(value)

    def start(self):
        if self.running:
            return
        if not self.pdfs:
            messagebox.showwarning(APP_TITLE, "PDFを選択してください。")
            return
        if not self.output_dir.get():
            messagebox.showwarning(APP_TITLE, "保存先フォルダを選択してください。")
            return
        if not BATCH_SCRIPT.exists():
            messagebox.showerror(APP_TITLE, f"{BATCH_SCRIPT.name} が見つかりません。")
            return
        if self.do_structure.get() and (not STRUCTURED_SCRIPT.exists() or not Path(self.common_rules.get()).exists()):
            messagebox.showwarning(APP_TITLE, "構造化スクリプトまたは共通ルールJSONを確認してください。")
            return
        if not os.getenv("DI_ENDPOINT") or not os.getenv("DI_KEY"):
            messagebox.showerror(APP_TITLE, "DI_ENDPOINT / DI_KEY の環境変数が見つかりません。")
            return
        Path(self.output_dir.get()).mkdir(parents=True, exist_ok=True)
        self.running = True
        self.run_button.config(state="disabled")
        self.log("=" * 70)
        self.log(f"開始: {datetime.now():%Y-%m-%d %H:%M:%S}")
        threading.Thread(target=self.run_batch, daemon=True).start()

    def run_batch(self):
        success, failures, warnings = 0, [], []
        try:
            for n, pdf in enumerate(self.pdfs, 1):
                self.put_status(f"処理中 {n}/{len(self.pdfs)}：{pdf.name}")
                self.put_log(f"\n[{n}/{len(self.pdfs)}] {pdf.name}")
                job = Path(self.output_dir.get()) / f"{pdf.stem}_{datetime.now():%Y%m%d_%H%M%S}"
                raw = job / "01_OCR抽出"
                structured = job / "02_構造化"
                raw.mkdir(parents=True, exist_ok=True)

                cmd = [sys.executable, str(BATCH_SCRIPT), str(pdf), "--output-dir", str(raw), "--chunk-pages", "2"]
                if self.keep_chunks.get():
                    cmd.append("--keep-chunks")
                if not self.command(cmd, "全ページ分割OCR・統合"):
                    failures.append(pdf.name); continue

                files = sorted(raw.glob("*_layout_raw_full.json"), key=lambda x: x.stat().st_mtime, reverse=True)
                if not files:
                    self.put_log("  エラー：統合OCR JSONが見つかりません。")
                    failures.append(pdf.name); continue
                raw_json = files[0]

                info = {}
                try:
                    info = json.loads(raw_json.read_text(encoding="utf-8")).get("batch_info", {})
                except Exception:
                    pass
                pdf_pages = self.page_counts.get(str(pdf))
                ocr_pages = info.get("ocr_page_count")
                self.put_log(f"  ページ照合：PDF {pdf_pages}ページ / Azure取得 {ocr_pages}ページ")
                if pdf_pages and ocr_pages and pdf_pages == ocr_pages:
                    self.put_log("  ページ照合：OK（PDF全ページを統合済み）")
                else:
                    warnings.append(pdf.name)
                    (job / "00_ページ数警告.txt").write_text(
                        f"PDFページ数: {pdf_pages}\nAzure取得ページ数: {ocr_pages}\n",
                        encoding="utf-8"
                    )
                    self.put_log("  【警告】PDF全ページが取得できていない可能性があります。")

                if self.do_structure.get():
                    structured.mkdir(parents=True, exist_ok=True)
                    cmd = [sys.executable, str(STRUCTURED_SCRIPT), str(raw_json), "--common-rules", self.common_rules.get(), "--output-dir", str(structured)]
                    if self.supplier_rules.get():
                        cmd += ["--supplier-rules", self.supplier_rules.get()]
                    if not self.command(cmd, "構造化"):
                        failures.append(pdf.name); continue

                if not self.keep_raw_json.get():
                    raw_json.unlink(missing_ok=True)
                if not self.keep_raw_excel.get():
                    for x in raw.glob("*_layout_check_full.xlsx"):
                        x.unlink(missing_ok=True)
                success += 1
                self.put_log(f"  完了：{job}")
        except Exception as exc:
            failures.append("バッチ処理")
            self.put_log(f"予期しないエラー：{exc}")
        finally:
            self.put_status(f"完了：成功 {success}件 / 失敗 {len(failures)}件 / ページ警告 {len(warnings)}件")
            self.root.after(0, self.finish, success, failures, warnings)

    def command(self, cmd, label):
        self.put_log(f"  {label}を開始...")
        try:
            p = subprocess.Popen(
                cmd, stdout=subprocess.PIPE, stderr=subprocess.STDOUT,
                text=True, encoding="utf-8", errors="replace",
                creationflags=getattr(subprocess, "CREATE_NO_WINDOW", 0)
            )
            assert p.stdout
            for line in p.stdout:
                self.put_log("    " + line.rstrip())
            code = p.wait()
        except Exception as exc:
            self.put_log(f"  起動失敗：{exc}")
            return False
        if code:
            self.put_log(f"  {label}に失敗しました（終了コード {code}）。")
            return False
        self.put_log(f"  {label}が完了しました。")
        return True

    def finish(self, success, failures, warnings):
        self.running = False
        self.run_button.config(state="normal")
        text = f"処理が終了しました。\n成功：{success}件\n失敗：{len(failures)}件"
        if warnings:
            text += f"\nページ警告：{len(warnings)}件"
            messagebox.showwarning(APP_TITLE, text)
        elif failures:
            messagebox.showwarning(APP_TITLE, text)
        else:
            messagebox.showinfo(APP_TITLE, text)

    def put_log(self, text): self.queue.put(text)
    def put_status(self, text): self.queue.put(("STATUS", text))

    def flush(self):
        try:
            while True:
                item = self.queue.get_nowait()
                if isinstance(item, tuple):
                    self.status.set(item[1])
                else:
                    self.log(str(item))
        except queue.Empty:
            pass
        self.root.after(150, self.flush)

    def log(self, text):
        self.log_box.configure(state="normal")
        self.log_box.insert(END, text + "\n")
        self.log_box.see(END)
        self.log_box.configure(state="disabled")


if __name__ == "__main__":
    root = Tk()
    App(root)
    root.mainloop()
