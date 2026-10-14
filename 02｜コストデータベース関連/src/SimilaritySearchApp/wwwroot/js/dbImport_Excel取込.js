// js/dbImport_Excel取込.js
// DB編集の「📥 Excel取込」。Excel の1行目を見出しとして DB の列に対応付け、表へ「追加行」として入れる。
// DB への反映は通常どおり「💾 保存」（1トランザクション・失敗したら全体を取り消し）。

import { call } from './bridge_通信.js';
import { toast, withLoading } from './ui_共通部品.js';
import { esc } from './format_数値書式.js';

const $ = (id) => document.getElementById(id);
const UNUSED = '';

let ctx = null;   // { grid, tableLabel, excel, mapping: Map<colIndex, headerIndex> }

export function initDbImport() {
    const modal = $('import-modal');
    for (const el of modal.querySelectorAll('[data-close]')) el.addEventListener('click', close);
    modal.addEventListener('click', (e) => { if (e.target === modal) close(); });
    $('import-sheet').addEventListener('change', async () => {
        await read(ctx.excel.path, $('import-sheet').value);
    });
    $('import-map').addEventListener('change', (e) => {
        const sel = e.target.closest('select[data-col]');
        if (!sel) return;
        const c = +sel.dataset.col;
        if (sel.value === UNUSED) ctx.mapping.delete(c); else ctx.mapping.set(c, +sel.value);
        renderMap();
    });
    $('import-run').addEventListener('click', run);
}

/** 取込を始める（ファイル選択 → 読込 → 対応付けダイアログ） */
export async function startImport(grid, tableLabel, onImported = () => {}) {
    if (!grid.hasData) {
        toast('先にテーブルを開いてください。');
        return;
    }
    const pick = await call('pickPath', {
        kind: 'open', title: `「${tableLabel}」に取り込む Excel を選択`,
        filter: 'Excel (*.xlsx;*.xlsm;*.xls)|*.xlsx;*.xlsm;*.xls',
    });
    if (!pick.path) return;
    ctx = { grid, tableLabel, excel: null, mapping: new Map(), onImported };
    if (await read(pick.path, null)) $('import-modal').classList.remove('hidden');
}

async function read(path, sheet) {
    try {
        ctx.excel = await withLoading('Excel を読み込んでいます...', () => call('excelRead', { path, sheet }));
    } catch (e) {
        toast(`Excel を読み込めませんでした: ${e.message}`, 'error');
        return false;
    }
    autoMap();
    $('import-title').textContent = `Excel取込 → ${ctx.tableLabel}`;
    $('import-path').textContent = ctx.excel.path;
    $('import-sheet').innerHTML = ctx.excel.sheets.map((s) => `<option${s === ctx.excel.sheet ? ' selected' : ''}>${esc(s)}</option>`).join('');
    renderMap();
    return true;
}

/** 見出し名と列名が一致するものを自動で対応付ける（完全一致 → 大文字小文字・空白を無視 → 英字部分／日本語部分） */
function autoMap() {
    ctx.mapping = new Map();
    const norm = (s) => String(s ?? '').normalize('NFKC').toLowerCase().replace(/[\s_\-・･()（）]/g, '');
    const headers = ctx.excel.headers.map(norm);
    ctx.grid.columns.forEach((col, c) => {
        if (!col.isEditable) return;
        const [base, label] = splitName(col.name);
        for (const cand of [col.name, base, label]) {
            if (!cand) continue;
            const i = headers.indexOf(norm(cand));
            if (i >= 0) {
                ctx.mapping.set(c, i);
                return;
            }
        }
    });
}

/** "Material_Name_名称" → ["Material_Name", "名称"] */
function splitName(name) {
    const m = name.match(/^(.*?)_([^\x00-\x7F].*)$/);
    return m ? [m[1], m[2]] : [name, ''];
}

function renderMap() {
    const { grid, excel, mapping } = ctx;
    const options = (selected) => `<option value="${UNUSED}">（取り込まない）</option>` +
        excel.headers.map((h, i) => `<option value="${i}"${selected === i ? ' selected' : ''}>${esc(h)}</option>`).join('');
    $('import-map').tBodies[0].innerHTML = grid.columns.map((col, c) => {
        if (!col.isEditable) {
            return `<tr><td>${esc(col.name)}</td><td>${esc(col.typeName)}</td><td colspan="2" class="preview">${col.isIdentity ? '自動採番（取り込まない）' : '編集できない列'}</td></tr>`;
        }
        const h = mapping.get(c);
        const preview = h == null ? '' : excel.rows.slice(0, 3).map((r) => r[h] ?? '').join(' / ');
        const required = !col.isNullable && h == null;
        return `<tr class="${h != null ? 'mapped' : ''}${required ? ' required-missing' : ''}" title="${required ? '必須の列です（既定値が無い場合は保存時にエラーになります）' : ''}">
            <td>${col.isPrimaryKey ? '🔑' : ''}${esc(col.name)}${!col.isNullable ? ' *' : ''}</td>
            <td>${esc(col.typeName)}</td>
            <td><select data-col="${c}">${options(h)}</select></td>
            <td class="preview" title="${esc(preview)}">${esc(preview)}</td>
        </tr>`;
    }).join('');
    $('import-count').textContent = `${excel.rows.length.toLocaleString('ja-JP')} 行 / 対応付け ${mapping.size} 列`;
    $('import-run').textContent = `表に追加（${excel.rows.length.toLocaleString('ja-JP')} 行）`;
    $('import-run').disabled = mapping.size === 0 || excel.rows.length === 0;
}

function run() {
    const { grid, excel, mapping } = ctx;
    const rows = excel.rows.map((r) => grid.columns.map((_, c) => (mapping.has(c) ? r[mapping.get(c)] : null)));
    grid.appendRows(rows);
    ctx.onImported();
    close();
    toast(`${rows.length.toLocaleString('ja-JP')} 行を「追加」として表に入れました。内容を確認して「💾 保存」で DB に反映してください（Ctrl+Z で取り消し）。`, 'success', null, 9000);
}

function close() {
    $('import-modal').classList.add('hidden');
}
