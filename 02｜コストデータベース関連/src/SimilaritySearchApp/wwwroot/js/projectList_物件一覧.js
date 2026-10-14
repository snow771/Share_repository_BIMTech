// js/projectList_物件一覧.js
// 中央下部: 該当物件の一覧テーブル。見出しクリックで並べ替え、行クリックで選択。

import { state, emit } from './state_画面状態.js';
import { yen, int, esc } from './format_数値書式.js';
import { enableColumnResize } from './columnResize_列幅調整.js';

const $ = (id) => document.getElementById(id);

/** 列定義。type: text / num / money */
const COLUMNS = [
    { key: 'projectId', label: '物件ID', type: 'text', cls: 'pid' },
    { key: 'name', label: '物件名', type: 'text', cls: 'name' },
    { key: 'prefecture', label: '都道府県', type: 'text' },
    { key: 'city', label: '市区町村', type: 'text' },
    { key: 'structure', label: '構造', type: 'text' },
    { key: 'form', label: '形態', type: 'text' },
    { key: 'floorsAbove', label: '地上', type: 'num' },
    { key: 'floorsBelow', label: '地下', type: 'num' },
    { key: 'legalFloorArea', label: '法廷床(㎡)', type: 'money' },
    { key: 'constructionFloorArea', label: '施工床(㎡)', type: 'money' },
    { key: 'reviseYenPerM2', label: '修正単価(円/㎡)', type: 'money' },
];

let resizer = null;

export function initProjectList() {
    const table = $('project-table');
    // 見出しの境界ドラッグで列幅変更、ダブルクリックで中身に合わせる（Excel と同じ）
    resizer = enableColumnResize(table, { storageKey: 'projectList.columnWidths', keyOf: (th) => th.dataset.key ?? null });
    table.tHead.addEventListener('click', (e) => {
        const th = e.target.closest('th[data-key]');
        if (!th) return;
        const key = th.dataset.key;
        state.sort = state.sort.key === key
            ? { key, dir: state.sort.dir === 'asc' ? 'desc' : 'asc' }
            : { key, dir: COLUMNS.find((c) => c.key === key).type === 'text' ? 'asc' : 'desc' };
        renderProjectList();
    });
    table.tBodies[0].addEventListener('click', (e) => {
        const tr = e.target.closest('tr[data-pid]');
        if (tr) emit('select', tr.dataset.pid);
    });
    renderHead();
}

export function renderProjectList() {
    renderHead();
    const rows = [...(state.result?.projects ?? [])];
    const { key, dir } = state.sort;
    const sign = dir === 'asc' ? 1 : -1;
    rows.sort((a, b) => {
        const va = a[key];
        const vb = b[key];
        if (va == null && vb == null) return 0;
        if (va == null) return 1;            // 欠損は常に末尾
        if (vb == null) return -1;
        return (typeof va === 'number' ? va - vb : String(va).localeCompare(String(vb), 'ja')) * sign;
    });

    const body = $('project-table').tBodies[0];
    if (rows.length === 0) {
        body.innerHTML = `<tr><td colspan="${COLUMNS.length}" class="empty-state">${state.loaded ? '該当する物件がありません。条件を緩めてください。' : 'データ未読込'}</td></tr>`;
        resizer?.apply();
        return;
    }
    body.innerHTML = rows.map((r) => `<tr data-pid="${esc(r.projectId)}"${r.projectId === state.selectedId ? ' class="selected"' : ''}>${
        COLUMNS.map((c) => cell(c, r[c.key])).join('')}</tr>`).join('');
    resizer?.apply();
}

/** 選択行の強調だけを更新し、見える位置までスクロールする */
export function highlightRow(pid) {
    for (const tr of $('project-table').tBodies[0].rows) {
        const hit = tr.dataset.pid === pid;
        tr.classList.toggle('selected', hit);
        if (hit) tr.scrollIntoView({ block: 'nearest' });
    }
}

// ------------------------------------------------------------------

function renderHead() {
    const { key, dir } = state.sort;
    $('project-table').tHead.innerHTML = `<tr>${COLUMNS.map((c) =>
        `<th data-key="${c.key}" class="${c.type !== 'text' ? 'num ' : ''}${c.key === key ? `sorted-${dir}` : ''}">${c.label}</th>`).join('')}</tr>`;
}

function cell(col, v) {
    if (col.type === 'money') return `<td class="num">${yen(v)}</td>`;
    if (col.type === 'num') return `<td class="num">${int(v)}</td>`;
    const text = v == null || v === '' ? '---' : String(v);
    return `<td class="${col.cls ?? ''}" title="${esc(text)}">${esc(text)}</td>`;
}
