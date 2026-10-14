"""
Excelファイルから単価データを集約するプログラム

機能：
1. 複数の大本Excelファイルから工種コード別の単価データを抽出
2. テンプレートファイルに基づいて2つのシートを作成
   - 単価テーブル_査定：積算予算シートのE列の値を使用
   - 単価テーブル_修正：積算予算シートのF列の値を使用
"""

import openpyxl
from openpyxl.utils.cell import get_column_letter
import tkinter as tk
from tkinter import filedialog
import os
from pathlib import Path
from copy import copy


def select_folder():
    """ユーザーにフォルダを選択させる"""
    root = tk.Tk()
    root.withdraw()
    folder_path = filedialog.askdirectory(title="元のStencileデータ(Excelファイル)が入っているフォルダを選択してください")
    root.destroy()
    return folder_path


def select_template_file():
    """ユーザーにテンプレートファイルを選択させる"""
    root = tk.Tk()
    root.withdraw()
    file_path = filedialog.askopenfilename(
        title="テンプレートファイル（〇〇期RC造_〇〇単価.xlsx）を選択してください",
        filetypes=[("Excelファイル", "*.xlsx *.xlsm"), ("すべてのファイル", "*.*")]
    )
    root.destroy()
    return file_path


def select_output_folder():
    """ユーザーに出力先フォルダを選択させる"""
    root = tk.Tk()
    root.withdraw()
    folder_path = filedialog.askdirectory(title="結果ファイルの保存先フォルダを選択してください")
    root.destroy()
    return folder_path


def get_excel_files(folder_path):
    """フォルダ内のExcelファイル（.xlsx, .xlsm）を取得"""
    excel_files = []
    # .xlsxファイルを取得
    for file in Path(folder_path).glob("*.xlsx"):
        # 一時ファイル（~$で始まる）と出力ファイルを除外
        if not file.name.startswith("~$") and file.name != "査定・修正単価出力_集約結果.xlsx":
            excel_files.append(str(file))
    # .xlsmファイルを取得
    for file in Path(folder_path).glob("*.xlsm"):
        # 一時ファイル（~$で始まる）を除外
        if not file.name.startswith("~$"):
            excel_files.append(str(file))
    return sorted(excel_files)


def copy_cell_style(source_cell, target_cell):
    """セルのスタイルをコピー"""
    if source_cell.has_style:
        target_cell.font = copy(source_cell.font)
        target_cell.border = copy(source_cell.border)
        target_cell.fill = copy(source_cell.fill)
        target_cell.number_format = copy(source_cell.number_format)
        target_cell.protection = copy(source_cell.protection)
        target_cell.alignment = copy(source_cell.alignment)


def create_template_sheet(wb, template_ws, sheet_name, title_column_f):
    """テンプレートをコピーして新しいシートを作成"""
    from openpyxl.styles import Border, Side
    
    # 新しいシートを作成
    new_ws = wb.create_sheet(title=sheet_name)
    
    # 列幅をコピー
    for col in range(1, template_ws.max_column + 1):
        col_letter = get_column_letter(col)
        new_ws.column_dimensions[col_letter].width = template_ws.column_dimensions[col_letter].width
    
    # 1行目（ヘッダー）をコピー
    for col in range(1, template_ws.max_column + 1):
        source_cell = template_ws.cell(row=1, column=col)
        target_cell = new_ws.cell(row=1, column=col)
        
        # F列のタイトルは変更
        if col == 6:  # F列
            target_cell.value = title_column_f
        else:
            target_cell.value = source_cell.value
        
        copy_cell_style(source_cell, target_cell)
    
    # B列～E列の工種コードと項目をコピー（2行目以降）
    for row in range(2, 400):
        # B列（工種コード）
        source_cell_b = template_ws.cell(row=row, column=2)
        if source_cell_b.value:
            target_cell_b = new_ws.cell(row=row, column=2)
            target_cell_b.value = source_cell_b.value
            copy_cell_style(source_cell_b, target_cell_b)
            
            # C列（工種項目）
            source_cell_c = template_ws.cell(row=row, column=3)
            target_cell_c = new_ws.cell(row=row, column=3)
            target_cell_c.value = source_cell_c.value
            copy_cell_style(source_cell_c, target_cell_c)
            
            # D列（工事項目）
            source_cell_d = template_ws.cell(row=row, column=4)
            target_cell_d = new_ws.cell(row=row, column=4)
            target_cell_d.value = source_cell_d.value
            copy_cell_style(source_cell_d, target_cell_d)
            
            # E列（項目）
            source_cell_e = template_ws.cell(row=row, column=5)
            target_cell_e = new_ws.cell(row=row, column=5)
            target_cell_e.value = source_cell_e.value
            copy_cell_style(source_cell_e, target_cell_e)
        else:
            break
    
    return new_ws


def process_excel_file(file_path, target_ws, start_row, column_to_read):
    """
    1つのExcelファイルを処理
    
    Args:
        file_path: 大本のExcelファイルパス
        target_ws: 書き込み先のワークシート
        start_row: 書き込み開始行
        column_to_read: 読み込む列（'E'または'F'）
    
    Returns:
        処理した行数
    """
    from openpyxl.styles import Border, Side
    
    print(f"  処理中: {os.path.basename(file_path)}")
    
    # ファイルの存在確認
    if not os.path.exists(file_path):
        print(f"    エラー: ファイルが見つかりません: {file_path}")
        return 0
    
    # 大本ファイルを開く
    try:
        source_wb = openpyxl.load_workbook(file_path, data_only=True)
    except Exception as e:
        print(f"    エラー: ファイルを開けませんでした: {e}")
        return 0
    
    # 実行予算表書シートからproject_idを取得
    if "実行予算表書" not in source_wb.sheetnames:
        print(f"    警告: '実行予算表書'シートが見つかりません")
        source_wb.close()
        return 0
    
    execution_ws = source_wb["実行予算表書"]
    project_id = execution_ws["C1"].value
    
    if not project_id:
        print(f"    警告: project_id（C1）が空です")
        source_wb.close()
        return 0
    
    # 積算予算シートからデータを取得
    if "積算予算" not in source_wb.sheetnames:
        print(f"    警告: '積算予算'シートが見つかりません")
        source_wb.close()
        return 0
    
    estimation_ws = source_wb["積算予算"]
    
    # 積算予算シートのD列（工種コード）とE列またはF列（単価）の対応表を作成
    code_to_price = {}
    for row in range(2, 939):  # 2行目～938行目
        code = estimation_ws[f"D{row}"].value
        price = estimation_ws[f"{column_to_read}{row}"].value
        
        if code:
            code_to_price[str(code).strip()] = price
    
    # 罫線のスタイルを定義（格子状）
    thin_border = Border(
        left=Side(style='thin'),
        right=Side(style='thin'),
        top=Side(style='thin'),
        bottom=Side(style='thin')
    )
    
    # テンプレートのB列と照合してF列に値を入力
    rows_written = 0
    current_row = start_row
    
    # テンプレートのB列をスキャン（373行分：2行目～374行目）
    for offset in range(373):
        template_row = 2 + offset  # テンプレートの行（常に2～374）
        target_row = current_row + offset  # 実際に書き込む行
        
        # B列の工種コードを取得（テンプレートの2～374行目から）
        code = target_ws[f"B{template_row}"].value
        
        if code:
            code_str = str(code).strip()
            
            # A列にproject_idを入力（最初の行には値、それ以降は数式）
            if offset == 0:
                target_ws[f"A{target_row}"].value = project_id
            else:
                target_ws[f"A{target_row}"].value = f"=A{target_row - 1}"
            
            # B列～E列は既にテンプレートからコピーされているが、
            # 375行目以降にも同じ値をコピーする必要がある
            if target_row > 374:
                # テンプレート（2～374行目）の値を375行目以降にもコピー
                target_ws[f"B{target_row}"].value = target_ws[f"B{template_row}"].value
                target_ws[f"C{target_row}"].value = target_ws[f"C{template_row}"].value
                target_ws[f"D{target_row}"].value = target_ws[f"D{template_row}"].value
                target_ws[f"E{target_row}"].value = target_ws[f"E{template_row}"].value
                
                # スタイルもコピー
                copy_cell_style(target_ws[f"B{template_row}"], target_ws[f"B{target_row}"])
                copy_cell_style(target_ws[f"C{template_row}"], target_ws[f"C{target_row}"])
                copy_cell_style(target_ws[f"D{template_row}"], target_ws[f"D{target_row}"])
                copy_cell_style(target_ws[f"E{template_row}"], target_ws[f"E{target_row}"])
            
            # F列に単価を入力（マッチした場合）
            if code_str in code_to_price:
                target_ws[f"F{target_row}"].value = code_to_price[code_str]
            else:
                target_ws[f"F{target_row}"].value = 0
            
            # F列に数値フォーマット（カンマ区切り）を設定
            target_ws[f"F{target_row}"].number_format = '#,##0'
            
            # A列～F列に罫線を設定
            for col in ['A', 'B', 'C', 'D', 'E', 'F']:
                target_ws[f"{col}{target_row}"].border = thin_border
            
            rows_written += 1
    
    source_wb.close()
    print(f"    完了: {rows_written}行を処理しました")
    
    return rows_written


def main():
    """メイン処理"""
    print("=" * 60)
    print("Excel単価データ集約プログラム")
    print("=" * 60)
    print()
    
    # テンプレートファイル選択
    print("【ステップ1】テンプレートファイル選択")
    template_path = select_template_file()
    
    if not template_path:
        print("テンプレートファイルが選択されませんでした。プログラムを終了します。")
        return
    
    if not os.path.exists(template_path):
        print(f"エラー: テンプレートファイルが見つかりません: {template_path}")
        return
    
    print(f"選択されたテンプレート: {os.path.basename(template_path)}")
    print()
    
    # フォルダ選択
    print("【ステップ2】大本ファイルのフォルダ選択")
    folder_path = select_folder()
    
    if not folder_path:
        print("フォルダが選択されませんでした。プログラムを終了します。")
        return
    
    print(f"選択されたフォルダ: {folder_path}")
    print()
    
    # Excelファイルを取得
    excel_files = get_excel_files(folder_path)
    
    if not excel_files:
        print("フォルダ内にExcelファイルが見つかりませんでした。")
        return
    
    print(f"見つかったファイル数: {len(excel_files)}件")
    for i, file in enumerate(excel_files, 1):
        print(f"  {i}. {os.path.basename(file)}")
    print()
    
    # 出力先フォルダ選択
    print("【ステップ3】出力先フォルダ選択")
    output_folder = select_output_folder()
    
    if not output_folder:
        print("出力先フォルダが選択されませんでした。プログラムを終了します。")
        return
    
    print(f"選択された出力先: {output_folder}")
    print()
    
    # テンプレートファイルを開く
    print("【ステップ4】テンプレートファイル読み込み")
    print(f"テンプレート: {os.path.basename(template_path)}")
    
    template_wb = openpyxl.load_workbook(template_path)
    template_ws = template_wb.active
    print()
    
    # ===== シート1: 単価テーブル_査定 =====
    print("【ステップ5】単価テーブル_査定シート作成")
    sheet1 = create_template_sheet(template_wb, template_ws, "単価テーブル_査定", "査定単価")
    
    current_row = 2
    for i, file_path in enumerate(excel_files, 1):
        print(f"  ファイル {i}/{len(excel_files)}")
        rows = process_excel_file(file_path, sheet1, current_row, "E")
        current_row += rows
    
    print(f"  → 合計 {current_row - 2} 行のデータを書き込みました")
    print()
    
    # ===== シート2: 単価テーブル_修正 =====
    print("【ステップ6】単価テーブル_修正シート作成")
    sheet2 = create_template_sheet(template_wb, template_ws, "単価テーブル_修正", "修正単価")
    
    current_row = 2
    for i, file_path in enumerate(excel_files, 1):
        print(f"  ファイル {i}/{len(excel_files)}")
        rows = process_excel_file(file_path, sheet2, current_row, "F")
        current_row += rows
    
    print(f"  → 合計 {current_row - 2} 行のデータを書き込みました")
    print()
    
    # 元のテンプレートシートを削除
    if "単価テーブル" in template_wb.sheetnames:
        del template_wb["単価テーブル"]
    
    # ファイルを保存
    output_path = os.path.join(output_folder, "単価テーブル_集約結果.xlsx")
    template_wb.save(output_path)
    template_wb.close()
    
    print("【完了】")
    print(f"出力ファイル: {output_path}")
    print()
    print("=" * 60)
    print("処理が正常に完了しました！")
    print("=" * 60)


if __name__ == "__main__":
    main()