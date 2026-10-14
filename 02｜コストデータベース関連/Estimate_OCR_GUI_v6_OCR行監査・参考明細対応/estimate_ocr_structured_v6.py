"""
Azure Document Intelligence の Layout JSON を、共通ルール＋協力業者差分ルールで
構造化JSON・Excelへ変換する（全ページ統合JSON対応・v3）。

実行例:
  python .\estimate_ocr_structured_v2.py ".\OCR_出力\防水_rotated_layout_raw.json" `
    --common-rules ".\rules_common.json" `
    --supplier-rules ".\rules_昇工業_v2.json"

設計:
  - 共通ルール: 建設見積一般に共通する列名候補・行判定・数値処理
  - 差分ルール: 協力業者や帳票固有の項目、列名、ヘッダの取得方法
  - Azureには再送しない。OCR済みJSONだけを読み込む。
"""

from __future__ import annotations

import argparse
import json
import re
import sys
from collections import defaultdict
from copy import deepcopy
from datetime import datetime
from pathlib import Path
from typing import Any

from openpyxl import Workbook
from openpyxl.formatting.rule import FormulaRule
from openpyxl.styles import Alignment, Border, Font, PatternFill, Side
from openpyxl.utils import get_column_letter


OUTPUT_COLUMNS = [
    "estimate_id", "supplier_name", "project_name", "source_file",
    "record_type", "pdf_page", "ocr_table_index", "ocr_row_index",
    "trade", "section", "item_name", "material", "dimension", "quantity",
    "unit", "unit_price", "amount", "remarks_ocr", "calculated_amount",
    "difference", "amount_check", "duplicate_candidate", "review_required",
    "review_reason",
]

DISPLAY_NAMES = {
    "estimate_id": "見積ID",
    "supplier_name": "協力業者名",
    "project_name": "工事名",
    "source_file": "元JSON",
    "record_type": "明細種別",
    "pdf_page": "PDFページ",
    "ocr_table_index": "OCR表番号",
    "ocr_row_index": "OCR行番号",
    "trade": "工種",
    "section": "区分",
    "item_name": "名称",
    "material": "材料・仕様",
    "dimension": "寸法・規格",
    "quantity": "数量",
    "unit": "単位",
    "unit_price": "単価",
    "amount": "金額",
    "remarks_ocr": "備考_OCR",
    "calculated_amount": "計算金額",
    "difference": "差額",
    "amount_check": "金額検算",
    "duplicate_candidate": "重複候補",
    "review_required": "要確認",
    "review_reason": "要確認理由",
}


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="見積Layout JSONを構造化します。")
    parser.add_argument("layout_json", help="Azure Layoutの生JSON")
    parser.add_argument("--common-rules", required=True, help="共通ルールJSON")
    parser.add_argument("--supplier-rules", required=False, help="協力業者・帳票差分ルールJSON")
    parser.add_argument("--output-dir", default=None, help="出力先。未指定なら入力JSON配下の構造化出力")
    return parser.parse_args()


def load_json(path: Path) -> dict[str, Any]:
    with path.open("r", encoding="utf-8") as f:
        return json.load(f)


def merge_rules(base: Any, override: Any) -> Any:
    """
    辞書は再帰的に統合し、リストは「共通→差分」の順で重複を除いて連結する。
    数値・文字列・真偽値は差分側を優先する。
    """
    if isinstance(base, dict) and isinstance(override, dict):
        merged = deepcopy(base)
        for key, value in override.items():
            merged[key] = merge_rules(merged[key], value) if key in merged else deepcopy(value)
        return merged

    if isinstance(base, list) and isinstance(override, list):
        merged = []
        for value in base + override:
            if value not in merged:
                merged.append(deepcopy(value))
        return merged

    return deepcopy(override)


def clean_text(value: Any) -> str:
    return re.sub(r"\s+", " ", str(value or "")).strip()


def normalize_label(value: Any) -> str:
    text = clean_text(value)
    return (
        text.replace(" ", "").replace("　", "")
            .replace("（", "(").replace("）", ")")
            .replace("･", "・").replace("·", "・")
    )


def normalize_unit(value: str, rules: dict[str, Any]) -> str:
    unit = clean_text(value)
    aliases = rules.get("normalization", {}).get("unit_aliases", {})
    return aliases.get(unit, unit)


def to_number(value: Any) -> float | None:
    """OCR数値内に混入した空白を補正して数値化する。"""
    text = clean_text(value)
    if not text:
        return None
    text = (
        text.replace(",", "").replace("¥", "").replace("￥", "")
            .replace("円", "").replace("−", "-").replace("―", "-")
    )
    # 例: "79. 30" -> "79.30" / "2, 000" -> "2000"
    previous = None
    while text != previous:
        previous = text
        text = re.sub(r"(?<=[0-9.\-])\s+(?=[0-9.\-])", "", text)
    return float(text) if re.fullmatch(r"[-+]?\d+(?:\.\d+)?", text) else None


def get_table_matrix(table: dict[str, Any]) -> list[list[str]]:
    rows = int(table.get("rowCount", 0))
    cols = int(table.get("columnCount", 0))
    matrix = [["" for _ in range(cols)] for _ in range(rows)]
    for cell in table.get("cells", []):
        r, c = int(cell.get("rowIndex", 0)), int(cell.get("columnIndex", 0))
        if 0 <= r < rows and 0 <= c < cols:
            matrix[r][c] = clean_text(cell.get("content", ""))
    return matrix


def page_number(table: dict[str, Any]) -> int | None:
    regions = table.get("boundingRegions") or []
    return regions[0].get("pageNumber") if regions else None


def matches_any(text: str, patterns: list[str]) -> bool:
    return any(re.search(p, text, flags=re.IGNORECASE) for p in patterns)


def find_column_map(header: list[str], aliases: dict[str, list[str]]) -> dict[str, int]:
    """
    見出しの列対応を作る。

    Azure OCRでは、罫線が薄い帳票で「備考 数量」のように複数見出しが
    1セルへ結合されることがある。その場合は後続の空白セルを数量列として補正する。
    """
    result: dict[str, int] = {}
    normalized_header = [normalize_label(label) for label in header]

    # 通常判定：1セルに複数の見出しが含まれる場合も、全ての該当項目を候補化する。
    for index, normalized in enumerate(normalized_header):
        if not normalized:
            continue
        for field, candidates in aliases.items():
            if any(normalize_label(candidate) in normalized for candidate in candidates):
                # 同じフィールドを複数回見つけた場合は先に出現した列を優先
                result.setdefault(field, index)

    # OCR結合見出しの補正。
    # 例：["記号","名称","規格・寸法","備考 数量","","単位","単価","金額"]
    #      → remarks=3, quantity=4, unit=5, unit_price=6, amount=7
    for index, normalized in enumerate(normalized_header):
        contains_remarks = any(
            normalize_label(candidate) in normalized
            for candidate in aliases.get("remarks", [])
        )
        contains_quantity = any(
            normalize_label(candidate) in normalized
            for candidate in aliases.get("quantity", [])
        )
        if contains_remarks and contains_quantity:
            result["remarks"] = index
            if index + 1 < len(header):
                result["quantity"] = index + 1

    # 数量見出しが備考列と誤って同じ位置になったケースを安全側に補正する。
    if "remarks" in result and "quantity" in result and result["remarks"] == result["quantity"]:
        next_index = result["remarks"] + 1
        if next_index < len(header):
            result["quantity"] = next_index

    return result

def get_cell(row: list[str], mapping: dict[str, int], field: str) -> str:
    idx = mapping.get(field)
    return clean_text(row[idx]) if idx is not None and idx < len(row) else ""


def _clean_header_value(value: str) -> str:
    value = clean_text(value)
    value = re.sub(r'^(?:[:：]|[-―－]+)\s*', '', value)
    value = re.sub(r'\s*(?:御中|様)\s*$', '', value)
    return value.strip()


def _is_excluded_supplier(value: str, rules: dict[str, Any]) -> bool:
    return any(
        re.search(p, value, flags=re.IGNORECASE)
        for p in rules.get("header_fields", {}).get("exclude_supplier_patterns", [])
    )


def _header_lines(layout: dict[str, Any]) -> list[str]:
    lines: list[str] = []
    for page in layout.get("pages", []):
        for line in page.get("lines", []):
            value = clean_text(line.get("content", ""))
            if value:
                lines.append(value)
    return lines


def _valid_company_candidate(value: str) -> bool:
    residual = re.sub(r"(?:株式会社|有限会社|合同会社|合資会社|合名会社|[\s=＝:：・,，.．])", "", value)
    return len(residual) >= 2


def _company_from_line(line: str, next_line: str, rules: dict[str, Any]) -> str:
    """一行または二行に分かれた社名を整形する。"""
    line = _clean_header_value(line)
    next_line = _clean_header_value(next_line)
    legal = r"(?:株式会社|有限会社|合同会社|合資会社|合名会社)"

    # OCRが「株式会社」だけで改行しているケース。
    if re.fullmatch(legal, line):
        if next_line and not re.search(r"^(?:御中|様|〒|TEL|FAX)$", next_line):
            candidate = f"{line}{next_line}"
            return "" if _is_excluded_supplier(candidate, rules) or not _valid_company_candidate(candidate) else candidate
        return ""

    # 「中川インテリア株式会社」のような後置法人格を先に判定する。
    suffix = re.search(rf"([^\n〒]{{2,50}}?{legal})\s*(?:御中|様)?$", line)
    if suffix:
        candidate = _clean_header_value(suffix.group(1))
        return "" if _is_excluded_supplier(candidate, rules) or not _valid_company_candidate(candidate) else candidate

    # 「株式会社八千代工業」のような前置法人格。
    prefix = re.search(rf"({legal}\s*[^\n〒御中様]{{2,50}})", line)
    if prefix:
        candidate = _clean_header_value(prefix.group(1))
        return "" if _is_excluded_supplier(candidate, rules) or not _valid_company_candidate(candidate) else candidate

    return ""

def _find_line_field(lines: list[str], labels: list[str], field: str, rules: dict[str, Any]) -> str:
    normalized_labels = [normalize_label(label) for label in labels]

    for idx, line in enumerate(lines):
        norm = normalize_label(line)
        for label in normalized_labels:
            # ラベルだけの行 → 次行が値
            if norm == label:
                candidate = _clean_header_value(lines[idx + 1]) if idx + 1 < len(lines) else ""
            # 例: 件 名 文京区本郷4丁目計画
            elif norm.startswith(label) and len(norm) > len(label):
                # 元文字列から「空白を許容したラベル」を除去
                pattern = "".join(re.escape(ch) + r"\s*" for ch in label)
                candidate = _clean_header_value(re.sub(r"^" + pattern, "", line))
            else:
                continue

            if field == "supplier_name":
                next_line = lines[idx + 1] if idx + 1 < len(lines) else ""
                candidate = _company_from_line(candidate, next_line, rules) or candidate
                if _is_excluded_supplier(candidate, rules):
                    continue
            if candidate:
                return candidate
    return ""


def _find_table_field(layout: dict[str, Any], labels: list[str], field: str, rules: dict[str, Any]) -> str:
    normalized_labels = [normalize_label(label) for label in labels]
    for table in layout.get("tables", []):
        for row in get_table_matrix(table):
            for idx, cell in enumerate(row):
                if normalize_label(cell) not in normalized_labels:
                    continue
                for value in row[idx + 1:]:
                    candidate = _clean_header_value(value)
                    if not candidate:
                        continue
                    if field == "supplier_name":
                        candidate = _company_from_line(candidate, "", rules)
                        if not candidate or _is_excluded_supplier(candidate, rules):
                            continue
                    return candidate
    return ""


def extract_header_fields(layout: dict[str, Any], rules: dict[str, Any]) -> dict[str, str]:
    fields: dict[str, str] = {}
    header_rules = rules.get("header_fields", {})
    labels_by_field = header_rules.get("table_labels", {})
    lines = _header_lines(layout)

    # 帳票のヘッダは図形化され、Azureが表ではなく行として返すことが多い。
    # そのため「OCR行のラベル→次行」を最優先にする。
    for field, labels in labels_by_field.items():
        value = _find_line_field(lines, labels, field, rules)
        if not value:
            value = _find_table_field(layout, labels, field, rules)
        if value:
            fields[field] = value

    # 正規表現は補完用途。短すぎる候補は採用しない。
    content = layout.get("content", "")
    for field, patterns in header_rules.get("content_patterns", {}).items():
        if fields.get(field):
            continue
        for pattern in patterns:
            for match in re.finditer(pattern, content, flags=re.MULTILINE):
                candidate = _clean_header_value(match.group(1))
                if len(candidate) < 3:
                    continue
                if field == "supplier_name":
                    candidate = _company_from_line(candidate, "", rules)
                    if not candidate or _is_excluded_supplier(candidate, rules):
                        continue
                fields[field] = candidate
                break
            if fields.get(field):
                break

    # 最終補完：会社名の行候補を探索する。
    if not fields.get("supplier_name"):
        for idx, line in enumerate(lines):
            candidate = _company_from_line(
                line, lines[idx + 1] if idx + 1 < len(lines) else "", rules
            )
            if candidate:
                fields["supplier_name"] = candidate
                break

    return fields


def find_detail_tables(layout: dict[str, Any], rules: dict[str, Any]):
    aliases = rules["column_aliases"]
    required = set(rules["detail_table"]["required_columns"])
    search_rows = int(rules["detail_table"].get("header_search_rows", 5))
    found = []

    for table_index, table in enumerate(layout.get("tables", []), start=1):
        matrix = get_table_matrix(table)
        for header_index, row in enumerate(matrix[:search_rows]):
            mapping = find_column_map(row, aliases)
            # 金額列がなくても、名称・数量列がある数量表を監査対象として認識する。
            if required.issubset(mapping) or {"item_name", "quantity"}.issubset(mapping):
                found.append((table_index, table, matrix, header_index, mapping))
                break
    return found


def _is_total_row(name: str) -> bool:
    normalized = normalize_label(name)
    return normalized in {"合計", "小計", "総計", "計"} or normalized.endswith("合計")


def extract_records(layout: dict[str, Any], rules: dict[str, Any], header: dict[str, str], source_name: str):
    """
    金額明細だけでなく、OCRが取得した名称付きの参考・数量行も出力する。

    金額がない行は「参考明細」として残し、金額集計からは除外する。
    これにより、OCRで読めた行が黙って消えることを防ぐ。
    """
    records = []
    audit_rows = []
    current_trade, current_section = "", ""
    ignore_values = {normalize_label(v) for v in rules.get("ignore_values", [])}
    cls = rules["row_classification"]

    for table_index, table, matrix, header_index, mapping in find_detail_tables(layout, rules):
        for physical_row_index, raw_row in enumerate(matrix[header_index + 1:], start=header_index + 2):
            row = [clean_text(v) for v in raw_row]
            meaningful = [normalize_label(v) for v in row if normalize_label(v) not in ignore_values and normalize_label(v)]
            if not meaningful:
                audit_rows.append({
                    "pdf_page": page_number(table), "ocr_table_index": table_index,
                    "ocr_row_index": physical_row_index, "row_status": "空行",
                    "reason": "", "row_text": " | ".join(row)
                })
                continue

            name = get_cell(row, mapping, "item_name")
            material = get_cell(row, mapping, "material")
            dimension = get_cell(row, mapping, "dimension")
            quantity = to_number(get_cell(row, mapping, "quantity"))
            unit = normalize_unit(get_cell(row, mapping, "unit"), rules)
            unit_price = to_number(get_cell(row, mapping, "unit_price"))
            amount = to_number(get_cell(row, mapping, "amount"))
            remarks = get_cell(row, mapping, "remarks")

            numeric_blank = quantity is None and unit_price is None and amount is None
            other_blank = not material and not dimension and not unit and not remarks

            # 見出し・区分行
            if name and numeric_blank and other_blank:
                if matches_any(name, cls["section_patterns"]):
                    current_section = name.strip("()（） ")
                    audit_rows.append({
                        "pdf_page": page_number(table), "ocr_table_index": table_index,
                        "ocr_row_index": physical_row_index, "row_status": "見出し",
                        "reason": "区分見出し", "row_text": " | ".join(row)
                    })
                    continue
                if _is_total_row(name):
                    audit_rows.append({
                        "pdf_page": page_number(table), "ocr_table_index": table_index,
                        "ocr_row_index": physical_row_index, "row_status": "除外",
                        "reason": "合計行", "row_text": " | ".join(row)
                    })
                    continue
                if not matches_any(name, cls["ignore_name_patterns"]):
                    current_trade = name
                    audit_rows.append({
                        "pdf_page": page_number(table), "ocr_table_index": table_index,
                        "ocr_row_index": physical_row_index, "row_status": "見出し",
                        "reason": "工種・大見出し", "row_text": " | ".join(row)
                    })
                    continue

            # 名称がない行は最終明細にはしないが、監査シートには残す。
            if not name or _is_total_row(name):
                audit_rows.append({
                    "pdf_page": page_number(table), "ocr_table_index": table_index,
                    "ocr_row_index": physical_row_index, "row_status": "除外",
                    "reason": "名称なし、または合計行", "row_text": " | ".join(row)
                })
                continue

            is_money = quantity is not None and unit_price is not None and amount is not None
            # 数量のみ、仕様・備考のみ、参考数量（数量が備考欄にある等）も残す。
            record_type = "金額明細" if is_money else "参考明細"
            review_reason = []
            calc = None
            diff = None
            amount_check = "対象外"

            if is_money:
                calc = round(quantity * unit_price)
                diff = round(amount - calc)
                amount_check = "OK" if abs(diff) <= 1 else "要確認"
                if abs(diff) > 1:
                    review_reason.append("数量×単価と金額が不一致")
            else:
                review_reason.append("単価・金額が未記載の参考明細（集計対象外）")

            if remarks and matches_any(remarks, cls["remark_review_patterns"]):
                review_reason.append("備考OCRに誤認識候補")

            record = {
                "estimate_id": header.get("estimate_id", ""),
                "supplier_name": header.get("supplier_name", ""),
                "project_name": header.get("project_name", ""),
                "source_file": source_name,
                "record_type": record_type,
                "pdf_page": page_number(table),
                "ocr_table_index": table_index,
                "ocr_row_index": physical_row_index,
                "trade": current_trade,
                "section": current_section,
                "item_name": name,
                "material": material,
                "dimension": dimension,
                "quantity": quantity,
                "unit": unit,
                "unit_price": unit_price,
                "amount": amount,
                "remarks_ocr": remarks,
                "calculated_amount": calc,
                "difference": diff,
                "amount_check": amount_check,
                "duplicate_candidate": "",
                "review_required": "要確認" if review_reason else "",
                "review_reason": " / ".join(review_reason),
            }
            records.append(record)
            audit_rows.append({
                "pdf_page": page_number(table), "ocr_table_index": table_index,
                "ocr_row_index": physical_row_index, "row_status": "採用",
                "reason": record_type, "row_text": " | ".join(row)
            })

    return records, audit_rows


def mark_duplicates(records: list[dict[str, Any]]) -> None:
    exact_fields = ["item_name", "material", "dimension", "quantity", "unit", "unit_price", "amount"]
    near_fields = ["item_name", "material", "quantity", "unit", "unit_price", "amount"]
    exact, near = defaultdict(list), defaultdict(list)

    for i, record in enumerate(records):
        exact[tuple(record[k] for k in exact_fields)].append(i)
        near[tuple(record[k] for k in near_fields)].append(i)

    def flag(indexes: list[int], label: str, reason: str):
        for i in indexes:
            rec = records[i]
            rec["duplicate_candidate"] = label
            rec["review_required"] = "要確認"
            rec["review_reason"] = f"{rec['review_reason']} / {reason}".strip(" /")

    for indexes in exact.values():
        pages = {records[i]["pdf_page"] for i in indexes}
        if len(indexes) > 1 and len(pages) > 1:
            flag(indexes, "同一明細候補", "ページ継続・重複を確認")

    for indexes in near.values():
        pages = {records[i]["pdf_page"] for i in indexes}
        dimensions = {clean_text(records[i]["dimension"]) for i in indexes}
        if len(indexes) > 1 and len(pages) > 1 and ("" in dimensions or len(dimensions) > 1):
            for i in indexes:
                if not records[i]["duplicate_candidate"]:
                    flag(indexes, "近似重複候補", "ページ継続時のセル欠落・重複を確認")
                    break



def make_result(records: list[dict[str, Any]], audit_rows: list[dict[str, Any]], header: dict[str, str], source_name: str, common_name: str, supplier_name: str | None):
    header_total = to_number(header.get("header_estimate_amount", ""))
    money_records = [record for record in records if record.get("record_type") == "金額明細"]
    reference_records = [record for record in records if record.get("record_type") == "参考明細"]
    total = sum((record["amount"] or 0) for record in money_records)
    return {
        "metadata": {
            "created_at": datetime.now().isoformat(timespec="seconds"),
            "source_layout_json": source_name,
            "common_rules_file": common_name,
            "supplier_rules_file": supplier_name or "",
            "supplier_name": header.get("supplier_name", ""),
            "project_name": header.get("project_name", ""),
            "estimate_id": header.get("estimate_id", ""),
        },
        "summary": {
            "detail_count": len(records),
            "money_detail_count": len(money_records),
            "reference_detail_count": len(reference_records),
            "amount_check_ok_count": sum(r["amount_check"] == "OK" for r in money_records),
            "duplicate_candidate_count": sum(bool(r["duplicate_candidate"]) for r in records),
            "header_estimate_amount": header_total,
            "detail_amount_total": total,
            "difference_to_header_amount": round(header_total - total) if header_total is not None else None,
            "audit_row_count": len(audit_rows),
            "audit_excluded_count": sum(r["row_status"] == "除外" for r in audit_rows),
        },
        "items": records,
        "audit_rows": audit_rows,
    }


def style_header(ws, row: int):
    for cell in ws[row]:
        cell.fill = PatternFill("solid", fgColor="2F7D62")
        cell.font = Font(color="FFFFFF", bold=True)
        cell.alignment = Alignment(horizontal="center", vertical="center", wrap_text=True)


def export_excel(result: dict[str, Any], path: Path):
    wb = Workbook()
    ws = wb.active
    ws.title = "構造化明細"
    thin = Side(style="thin", color="D1D5DB")
    border = Border(top=thin, bottom=thin, left=thin, right=thin)

    ws.merge_cells(start_row=1, start_column=1, end_row=1, end_column=len(OUTPUT_COLUMNS))
    ws["A1"] = "協力業者見積：構造化明細 v6（参考明細・OCR行監査対応）"
    ws["A1"].fill = PatternFill("solid", fgColor="1F5E4B")
    ws["A1"].font = Font(color="FFFFFF", bold=True, size=14)
    ws["A1"].alignment = Alignment(vertical="center")
    ws.row_dimensions[1].height = 26

    ws.merge_cells(start_row=2, start_column=1, end_row=2, end_column=len(OUTPUT_COLUMNS))
    ws["A2"] = "Azure Layout JSONを標準列へ変換。差分ルールは協力業者・帳票の例外だけを補完。"
    ws["A2"].fill = PatternFill("solid", fgColor="E8F3EE")

    for col, field in enumerate(OUTPUT_COLUMNS, 1):
        ws.cell(row=4, column=col, value=DISPLAY_NAMES[field])
    style_header(ws, 4)

    for r, record in enumerate(result["items"], 5):
        for c, field in enumerate(OUTPUT_COLUMNS, 1):
            ws.cell(row=r, column=c, value=record.get(field))

    last = max(4, len(result["items"]) + 4)
    for row in ws.iter_rows(min_row=4, max_row=last):
        for cell in row:
            cell.border = border
            cell.alignment = Alignment(vertical="top", wrap_text=True)

    numeric = {"quantity": "#,##0.0", "unit_price": "#,##0", "amount": "#,##0", "calculated_amount": "#,##0", "difference": "#,##0"}
    for field, fmt in numeric.items():
        col = OUTPUT_COLUMNS.index(field) + 1
        for r in range(5, last + 1):
            ws.cell(row=r, column=col).number_format = fmt

    if last >= 5:
        col_check = get_column_letter(OUTPUT_COLUMNS.index("amount_check") + 1)
        col_dup = get_column_letter(OUTPUT_COLUMNS.index("duplicate_candidate") + 1)
        col_review = get_column_letter(OUTPUT_COLUMNS.index("review_required") + 1)
        ws.conditional_formatting.add(f"{col_check}5:{col_check}{last}", FormulaRule(formula=[f'${col_check}5="OK"'], fill=PatternFill("solid", fgColor="DCFCE7"), font=Font(color="166534", bold=True)))
        ws.conditional_formatting.add(f"{col_dup}5:{col_dup}{last}", FormulaRule(formula=[f'${col_dup}5<>""'], fill=PatternFill("solid", fgColor="FEF3C7"), font=Font(color="92400E", bold=True)))
        ws.conditional_formatting.add(f"{col_review}5:{col_review}{last}", FormulaRule(formula=[f'${col_review}5="要確認"'], fill=PatternFill("solid", fgColor="FEE2E2"), font=Font(color="991B1B", bold=True)))

    widths = [18,20,18,32,10,16,12,28,18,14,10,10,12,14,42,14,10,12,14,12,28]
    for i, width in enumerate(widths, 1):
        ws.column_dimensions[get_column_letter(i)].width = width
    ws.freeze_panes = "A5"
    ws.auto_filter.ref = f"A4:{get_column_letter(len(OUTPUT_COLUMNS))}{last}"

    summary = wb.create_sheet("集計・確認")
    summary.append(["項目", "値"])
    for key, value in [
        ("協力業者名", result["metadata"]["supplier_name"]),
        ("工事名", result["metadata"]["project_name"]),
        ("見積ID", result["metadata"]["estimate_id"]),
        ("見積ヘッダ金額", result["summary"]["header_estimate_amount"]),
        ("抽出明細金額合計", result["summary"]["detail_amount_total"]),
        ("差額", result["summary"]["difference_to_header_amount"]),
        ("出力明細件数（総数）", result["summary"]["detail_count"]),
        ("うち金額明細", result["summary"]["money_detail_count"]),
        ("うち参考明細", result["summary"]["reference_detail_count"]),
        ("金額検算OK件数", result["summary"]["amount_check_ok_count"]),
        ("OCR行監査件数", result["summary"]["audit_row_count"]),
        ("OCR行監査：除外件数", result["summary"]["audit_excluded_count"]),
        ("重複候補件数", result["summary"]["duplicate_candidate_count"]),
        ("共通ルール", result["metadata"]["common_rules_file"]),
        ("差分ルール", result["metadata"]["supplier_rules_file"]),
    ]:
        summary.append([key, value])
    style_header(summary, 1)
    for row in summary.iter_rows():
        for cell in row:
            cell.border = border
            cell.alignment = Alignment(wrap_text=True, vertical="top")
    summary.column_dimensions["A"].width = 32
    summary.column_dimensions["B"].width = 56

    # OCRで認識した明細表の各行について、採用・見出し・除外の理由を残す。
    audit = wb.create_sheet("OCR行監査")
    audit_headers = ["PDFページ", "OCR表番号", "OCR行番号", "判定", "理由", "OCR行原文"]
    audit.append(audit_headers)
    style_header(audit, 1)
    for entry in result.get("audit_rows", []):
        audit.append([
            entry.get("pdf_page"), entry.get("ocr_table_index"), entry.get("ocr_row_index"),
            entry.get("row_status"), entry.get("reason"), entry.get("row_text")
        ])
    for row in audit.iter_rows():
        for cell in row:
            cell.border = border
            cell.alignment = Alignment(wrap_text=True, vertical="top")
    audit.column_dimensions["A"].width = 10
    audit.column_dimensions["B"].width = 12
    audit.column_dimensions["C"].width = 12
    audit.column_dimensions["D"].width = 12
    audit.column_dimensions["E"].width = 30
    audit.column_dimensions["F"].width = 100
    audit.freeze_panes = "A2"
    audit.auto_filter.ref = f"A1:F{max(1, len(result.get('audit_rows', [])) + 1)}"

    wb.save(path)


def main() -> int:
    args = parse_args()
    layout_path = Path(args.layout_json).expanduser().resolve()
    common_path = Path(args.common_rules).expanduser().resolve()
    supplier_path = Path(args.supplier_rules).expanduser().resolve() if args.supplier_rules else None

    for label, path in [("入力JSON", layout_path), ("共通ルール", common_path)]:
        if not path.exists():
            print(f"エラー: {label}が見つかりません: {path}", file=sys.stderr)
            return 1
    if supplier_path and not supplier_path.exists():
        print(f"エラー: 差分ルールが見つかりません: {supplier_path}", file=sys.stderr)
        return 1

    layout = load_json(layout_path)
    common_rules = load_json(common_path)
    supplier_rules = load_json(supplier_path) if supplier_path else {}
    rules = merge_rules(common_rules, supplier_rules)

    if not find_detail_tables(layout, rules):
        print("エラー: 明細表を特定できませんでした。共通または差分ルールの列名候補を確認してください。", file=sys.stderr)
        return 1

    header = extract_header_fields(layout, rules)
    records, audit_rows = extract_records(layout, rules, header, layout_path.name)
    mark_duplicates(records)

    result = make_result(
        records, audit_rows, header, layout_path.name, common_path.name,
        supplier_path.name if supplier_path else None
    )

    output_dir = Path(args.output_dir).expanduser().resolve() if args.output_dir else layout_path.parent / "構造化出力_v6"
    output_dir.mkdir(parents=True, exist_ok=True)
    stem = layout_path.stem.replace("_layout_raw", "")
    json_path = output_dir / f"{stem}_structured_v6.json"
    excel_path = output_dir / f"{stem}_structured_v6.xlsx"

    with json_path.open("w", encoding="utf-8") as f:
        json.dump(result, f, ensure_ascii=False, indent=2)
    export_excel(result, excel_path)

    print("構造化が完了しました。")
    print(f"共通ルール  : {common_path}")
    print(f"差分ルール  : {supplier_path or 'なし'}")
    print(f"構造化JSON  : {json_path}")
    print(f"構造化Excel : {excel_path}")
    print(f"抽出明細件数: {result['summary']['detail_count']}")
    print(f"金額検算OK  : {result['summary']['amount_check_ok_count']}")
    print(f"重複候補    : {result['summary']['duplicate_candidate_count']}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
