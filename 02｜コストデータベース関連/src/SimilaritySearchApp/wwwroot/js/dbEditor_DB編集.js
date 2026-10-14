// js/dbEditor_DB編集.js
// DB編集モード: テーブル選択 → 関連図とグリッドに表示 → 編集・追加・削除 → 保存（1トランザクション）。
// 未保存の変更があるまま別のテーブル・ページへ移るときは確認する。

import { emit } from './state_画面状態.js';
import { call } from './bridge_通信.js';
import { toast, withLoading, setStatus, bindSegment, isConnectionError } from './ui_共通部品.js';
import { DbGrid } from './dbGrid_表グリッド.js';
import { renderDiagram, fitDiagram, resetDiagramLayout } from './dbDiagram_関連図.js';
import { esc } from './format_数値書式.js';
import { initDbImport, startImport } from './dbImport_Excel取込.js';
import { recordStep } from './router_画面遷移.js';
import { initHistory } from './history_変更履歴.js';

const $ = (id) => document.getElementById(id);
const PAGE_SIZE = 200;

let tables = [];
let current = null;           // { table, columns, hasPrimaryKey, neighborhood }
let page = 1;
let total = 0;
let sort = { column: null, desc: false };
let grid = null;
let openSettings = () => {};
let loadedOnce = false;
let importedPending = false;   // Excel取込で入れた行が未保存か（履歴の種類を「Excel取込」にする）

export function initDbEditor(opts) {
    openSettings = opts.onOpenSettings;


    grid = new DbGrid($('db-grid-wrap'), {
        onChange: updateButtons,
        onSort: (col) => guard(() => {
            sort = sort.column === col ? { column: col, desc: !sort.desc } : { column: col, desc: false };
            loadPage(1);
        }),
        onSave: save,
    });
    grid.clear('左の一覧からテーブルを選んでください。');

    $('db-refresh').addEventListener('click', () => loadTables(true));
    $('db-table-search').addEventListener('input', renderTableList);
    $('db-table-list').addEventListener('click', (e) => {
        const item = e.target.closest('[data-table]');
        if (item) openTable(item.dataset.schema, item.dataset.table);
    });

    // 表内検索は Enter（または × で消したとき）だけ実行する。入力の途中で検索が走ると文字が打ちにくいため
    // （type="search" の search イベントは Enter と × の両方で1回だけ発生する）
    $('db-search').addEventListener('search', () => guard(() => loadPage(1)));

    $('db-add-row').addEventListener('click', () => grid.addRow());
    initDbImport();
    $('db-import').addEventListener('click', () => current && startImport(grid, current.table.label, () => { importedPending = true; }));
    initHistory({
        tables: () => tables,
        onChanged: async () => {
            // 取り消しで DB が変わったので、表と行数を読み直す（未保存の変更は無い前提: 履歴はいつでも開けるが取り消し後は読み直す）
            await refreshCounts();
            if (current) await loadPage(page);
            if (tables.some((t) => t.appRole)) {
                toast('分析で使っているテーブルが変わった可能性があります。検索・分析に反映するには再読込してください。', 'info',
                    { label: '今すぐ再読込', onClick: () => emit('request:reload') }, 10000);
            }
        },
    });
    $('db-delete-rows').addEventListener('click', () => grid.toggleDeleteRows());
    $('db-undo').addEventListener('click', () => grid.undo());
    $('db-discard').addEventListener('click', () => {
        if (!grid.pendingCount || confirm('保存していない変更をすべて破棄しますか？')) loadPage(page);
    });
    $('db-save').addEventListener('click', save);
    $('db-prev').addEventListener('click', () => guard(() => loadPage(page - 1)));
    $('db-next').addEventListener('click', () => guard(() => loadPage(page + 1)));
    $('db-diagram-toggle').addEventListener('click', () => {
        const card = $('db-diagram-card');
        card.classList.toggle('collapsed');
        $('db-diagram-toggle').textContent = card.classList.contains('collapsed') ? '▲ 表示' : '▼ 隠す';
        if (!card.classList.contains('collapsed')) fitDiagram();
    });
    $('db-diagram-fit').addEventListener('click', fitDiagram);
    $('db-diagram-reset').addEventListener('click', resetDiagramLayout);

    let resizeTimer = null;
    new ResizeObserver(() => {
        clearTimeout(resizeTimer);
        resizeTimer = setTimeout(() => { if (current) fitDiagram(); }, 150);   // 枠の大きさが変わったら全体表示し直す
    }).observe($('db-diagram'));

    window.addEventListener('beforeunload', (e) => {
        if (grid.pendingCount) e.preventDefault();
    });
}

// ── 画面の出入り（router_画面遷移.js から呼ばれる） ─────────

/** DB編集の画面に入ったとき: 初回はテーブル一覧を読む */
export function enterDbEditor() {
    if (!loadedOnce) loadTables(false);
    grid.focus();
}

/** DB編集の画面から離れてよいか。未保存の変更があれば確認し、離れるなら変更を破棄する */
export function canLeaveDbEditor() {
    if (!grid.pendingCount) return true;
    if (!confirm('DB編集に保存していない変更があります。破棄して移動しますか？')) return false;
    loadPage(page);   // 変更を捨てて読み直す
    return true;
}

// ── テーブル一覧 ───────────────────────────────────────

async function loadTables(refresh) {
    try {
        const res = await withLoading('DBの構造を読み込んでいます...', () => call('dbTables', { refresh }));
        tables = res.tables;
        loadedOnce = true;
        $('db-conn').textContent = `${res.server} / ${res.database}（${tables.length} テーブル）`;
        renderTableList();
    } catch (e) {
        $('db-conn').textContent = '未接続';
        $('db-table-list').innerHTML = `<div class="empty-state">${esc(e.message)}</div>`;
        toast(`DBに接続できませんでした。\n${e.message}`, 'error',
            isConnectionError(e.message) ? { label: '設定を開く', onClick: () => openSettings('db') } : null);
    }
}

function renderTableList() {
    const q = $('db-table-search').value.trim().toLowerCase();
    const hit = tables.filter((t) => !q || t.name.toLowerCase().includes(q) || t.label.toLowerCase().includes(q));
    const groups = [
        { title: 'このアプリで使用中', list: hit.filter((t) => t.appRole) },
        { title: 'その他のテーブル', list: hit.filter((t) => !t.appRole) },
    ];
    $('db-table-list').innerHTML = groups.filter((g) => g.list.length).map((g) => `
        <div class="db-group-title">${g.title}（${g.list.length}）</div>
        ${g.list.map((t) => `
            <button type="button" class="db-table-item${current && current.table.name === t.name && current.table.schema === t.schema ? ' active' : ''}"
                    data-schema="${esc(t.schema)}" data-table="${esc(t.name)}" title="${esc(t.fullName ?? t.name)}">
                <span class="t-label">${esc(t.label)}</span>
                <span class="t-sub"><span>${esc(t.baseName)}</span><span>${Number(t.rowCount).toLocaleString('ja-JP')} 行</span></span>
                ${t.appRole ? `<span class="role-badge">${esc(t.appRole)}</span>` : ''}
            </button>`).join('')}`).join('') || '<div class="empty-state">該当するテーブルがありません。</div>';
}

// ── テーブルを開く ─────────────────────────────────────

/**
 * テーブルを開く。record=false は「戻る」で開き直すとき（作業の順番に記録しない）。
 * 開いたテーブルは作業の順番に記録され、マウスの戻るボタンで1つ前のテーブルに戻れる。
 */
/** テーブルを開く。search を渡すと検索欄に入れた状態で開く（AIアシスタントの「DB編集で開く」から） */
export function openTable(schema, name, { record = true, search = '' } = {}) {
    if (!record && current && current.table.schema === schema && current.table.name === name) return;
    guard(async () => {
        try {
            current = await withLoading('テーブルを開いています...', () => call('dbTable', { schema, name }));
        } catch (e) {
            toast(e.message, 'error');
            return;
        }
        if (record) recordStep({ view: 'db', table: { schema, name } });
        sort = { column: null, desc: false };
        $('db-search').value = search ?? '';
        $('db-title').textContent = current.table.label;
        $('db-subtitle').textContent = `${current.table.schema}.${current.table.name}` +
            (current.hasPrimaryKey ? '' : '　⚠ 主キーなし（全列の値で行を特定します）');
        renderTableList();
        drawDiagram();
        await loadPage(1);
    });
}

function drawDiagram() {
    renderDiagram($('db-diagram'), current.neighborhood, (schema, name) => openTable(schema, name));
}

async function loadPage(p) {
    if (!current) return;
    const maxPage = Math.max(1, Math.ceil(total / PAGE_SIZE));
    page = Math.max(1, total ? Math.min(p, maxPage) : p);
    try {
        const res = await withLoading('データを読み込んでいます...', () => call('dbQuery', {
            schema: current.table.schema, name: current.table.name,
            page, pageSize: PAGE_SIZE, search: $('db-search').value.trim() || null,
            sortColumn: sort.column, sortDesc: sort.desc,
        }));
        total = res.total;
        page = res.page;
        importedPending = false;   // 読み直すと未保存の取込行は消える
        grid.load(res.columns, res.rows, { rowOffset: (page - 1) * PAGE_SIZE, sort, tableKey: `${current.table.schema}.${current.table.name}` });
        if (!res.rows.length) toast(total ? 'このページに行がありません。' : '行がありません。「＋ 行を追加」で追加できます。');
        updatePager();
        // 検索欄で Enter したときは、続けて打ち直せるよう入力欄に焦点を残す
        if (document.activeElement !== $('db-search')) grid.focus();
    } catch (e) {
        toast(`読み込めませんでした: ${e.message}`, 'error');
    }
}

function updatePager() {
    const from = total ? (page - 1) * PAGE_SIZE + 1 : 0;
    const to = Math.min(page * PAGE_SIZE, total);
    $('db-page-info').textContent = `${from.toLocaleString('ja-JP')}–${to.toLocaleString('ja-JP')} / ${total.toLocaleString('ja-JP')} 行`;
    $('db-prev').disabled = page <= 1;
    $('db-next').disabled = to >= total;
}

function updateButtons() {
    const n = grid.pendingCount;
    $('db-save').disabled = n === 0;
    $('db-save').textContent = n ? `💾 保存（${n}件）` : '💾 保存';
    for (const id of ['db-import', 'db-add-row', 'db-delete-rows', 'db-undo', 'db-discard']) $(id).disabled = !grid.hasData;
}

// ── 保存 ─────────────────────────────────────────────

async function save() {
    if (!current) return;
    const ch = grid.changes();
    const n = ch.inserts.length + ch.updates.length + ch.deletes.length;
    if (!n) {
        toast('保存する変更はありません。');
        return;
    }
    const lines = [
        `テーブル「${current.table.label}」に次の変更を保存します。`,
        '',
        `　追加: ${ch.inserts.length} 行`,
        `　更新: ${ch.updates.length} 行`,
        `　削除: ${ch.deletes.length} 行${ch.deletes.length ? '　← 元に戻せません' : ''}`,
    ];

    // 物件情報の削除: 同じ project_id を持つ関連テーブルの行も一緒に削除される
    if (ch.deletes.length) {
        try {
            const related = await call('dbDeletePreview', { schema: current.table.schema, name: current.table.name, deletes: ch.deletes });
            if (related.length) {
                lines.push('', '⚠ 物件の削除に合わせて、同じ project_id の行を次のテーブルからも削除します:');
                for (const r of related) lines.push(`　・${r.label}（${r.table}）: ${r.rows.toLocaleString('ja-JP')} 行`);
            }
        } catch (e) {
            toast(`関連テーブルの確認に失敗しました: ${e.message}`, 'error');
            return;
        }
    }

    const missing = grid.missingRequiredCount;
    if (missing) lines.push('', `⚠ 必須の列（*）が空欄のセルが ${missing} 箇所あります（赤いセル）。このまま保存すると失敗する可能性があります。`);
    lines.push('', '1件でも失敗した場合は、すべての変更を取り消します。よろしいですか？');
    if (!confirm(lines.join('\n'))) return;

    try {
        const res = await withLoading('保存しています...', () => call('dbSave', {
            schema: current.table.schema, name: current.table.name, ...ch,
            source: importedPending ? 'Excel取込' : 'DB編集',   // 変更履歴に残す種類
        }));
        importedPending = false;
        const relatedText = (res.related ?? []).map((r) => `${r.label} ${r.rows}行`).join('、');
        toast(`保存しました（追加 ${res.inserted} / 更新 ${res.updated} / 削除 ${res.deleted}）${relatedText ? `
関連テーブルからも削除: ${relatedText}` : ''}`, 'success', null, 8000);
        setStatus(`DB編集: ${current.table.name} を保存しました。`);
        await loadPage(page);
        await refreshCounts();
        if (current.table.appRole) {
            toast(`「${current.table.appRole}」はこのアプリの分析で使っているテーブルです。検索・分析画面に反映するには再読込してください。`, 'info',
                { label: '今すぐ再読込', onClick: () => emit('request:reload') }, 10000);
        }
    } catch (e) {
        toast(e.message, 'error', null, 12000);
    }
}

async function refreshCounts() {
    try {
        const res = await call('dbTables', { refresh: false });
        tables = res.tables;
        renderTableList();
    } catch {
        // 一覧の行数更新に失敗しても保存自体は済んでいる
    }
}

/** 未保存の変更があれば確認してから実行する */
function guard(fn, onCancel = () => {}) {
    if (grid.pendingCount && !confirm('保存していない変更があります。破棄して移動しますか？')) {
        onCancel();
        grid.focus();
        return;
    }
    fn();
}
