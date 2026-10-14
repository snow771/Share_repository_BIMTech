// js/history_変更履歴.js
// DB編集の「📜 変更履歴」。いつ・だれが・どのPCで・どのテーブルのどの行を・どう変えたかを操作（保存1回分）ごとに表示し、
// 「この操作を取り消す」「この時点まで戻す（以降すべて取り消し）」で元に戻せる。
// 取り消しは C# 側で1トランザクション。その後に別の変更が入っている行があれば「衝突」として何もせずに知らせる。

import { call } from './bridge_通信.js';
import { toast, withLoading } from './ui_共通部品.js';
import { esc } from './format_数値書式.js';

const $ = (id) => document.getElementById(id);

const SOURCE_CLASS = { DB編集: 'edit', Excel取込: 'import', Stencile登録: 'stencil', 取り消し: 'revert' };
const OP_LABEL = { INSERT: '追加', UPDATE: '更新', DELETE: '削除' };
const MAX_DIFF_COLUMNS = 12;

let batches = [];
let selected = null;   // 選択中の操作
let onChanged = () => {};

/**
 * @param {{ tables: () => Array<{name:string,label:string}>, onChanged: () => void }} opts
 *   tables: テーブルの絞込に出す一覧 / onChanged: 取り消しで DB が変わったとき（表の読み直しなど）
 */
export function initHistory(opts) {
    onChanged = opts.onChanged;
    const modal = $('history-modal');
    for (const el of modal.querySelectorAll('[data-close]')) el.addEventListener('click', close);
    modal.addEventListener('click', (e) => { if (e.target === modal) close(); });
    document.addEventListener('keydown', (e) => {
        if (e.key === 'Escape' && !modal.classList.contains('hidden')) close();
    });

    $('db-history').addEventListener('click', () => open(opts.tables()));
    $('history-filters').addEventListener('submit', (e) => {
        e.preventDefault();
        load();
    });
    $('history-purge').addEventListener('click', purge);
    $('history-list').tBodies[0].addEventListener('click', (e) => {
        const tr = e.target.closest('tr[data-id]');
        if (tr) showDetail(+tr.dataset.id);
    });
    $('history-detail').addEventListener('click', (e) => {
        const btn = e.target.closest('button[data-revert]');
        if (btn) revert(btn.dataset.revert, btn.dataset.force === '1');
    });
}

/** 履歴を開く。tableName を渡すとそのテーブルで絞り込んだ状態で開く */
export async function open(tables, tableName = '') {
    const sel = $('hf-table');
    sel.innerHTML = '<option value="">（すべて）</option>' + tables.map((t) => `<option value="${esc(t.name)}">${esc(t.label)}（${esc(t.name)}）</option>`).join('');
    sel.value = tableName;
    $('history-modal').classList.remove('hidden');
    await load();
}

function close() {
    $('history-modal').classList.add('hidden');
}

async function load() {
    try {
        const res = await withLoading('変更履歴を読み込んでいます...', () => call('historyList', {
            from: $('hf-from').value || null,
            to: $('hf-to').value || null,
            user: $('hf-user').value.trim() || null,
            table: $('hf-table').value || null,
            keyword: $('hf-keyword').value.trim() || null,
        }));
        batches = res.batches;
        renderList(res.truncated);
        if (selected && batches.some((b) => b.batchId === selected.batch.batchId)) await showDetail(selected.batch.batchId);
        else $('history-detail').innerHTML = `<div class="empty-state">${batches.length ? '左の一覧から操作を選ぶと、変更の中身（前 → 後）が表示されます。' : '条件に合う履歴はありません。'}</div>`;
    } catch (e) {
        toast(`変更履歴を読み込めませんでした: ${e.message}`, 'error', null, 10000);
    }
}

function renderList(truncated) {
    const body = $('history-list').tBodies[0];
    if (!batches.length) {
        body.innerHTML = '<tr><td colspan="6" class="empty-state">履歴はありません。</td></tr>';
        return;
    }
    body.innerHTML = batches.map((b) => `
        <tr data-id="${b.batchId}" class="${b.revertedByBatchId ? 'reverted' : ''}${selected?.batch.batchId === b.batchId ? ' selected' : ''}">
            <td class="num">${b.batchId}</td>
            <td>${esc(formatDate(b.executedAt))}</td>
            <td title="${esc(`${b.userName} / ${b.machineName ?? ''} / DB: ${b.dbLogin ?? ''}`)}">${esc(shortUser(b.userName))}</td>
            <td class="state">${sourceBadge(b)}</td>
            <td class="summary" title="${esc(b.summary ?? '')}">${esc(b.summary ?? '')}</td>
            <td class="num">${b.rowTotal.toLocaleString('ja-JP')}</td>
        </tr>`).join('') + (truncated ? '<tr><td colspan="6" class="empty-state">表示件数の上限に達しました。期間などで絞り込んでください。</td></tr>' : '');
}

function sourceBadge(b) {
    const main = `<span class="hs-badge ${SOURCE_CLASS[b.source] ?? ''}">${esc(b.source)}</span>`;
    return b.revertedByBatchId ? `${main} <span class="hs-badge done" title="操作 #${b.revertedByBatchId} で取り消し済み">取消済 #${b.revertedByBatchId}</span>` : main;
}

async function showDetail(batchId, conflicts = null) {
    let d;
    try {
        d = await call('historyDetail', { batchId });
    } catch (e) {
        toast(e.message, 'error');
        return;
    }
    selected = d;
    for (const tr of $('history-list').tBodies[0].rows) tr.classList.toggle('selected', +tr.dataset.id === batchId);

    const b = d.batch;
    const newer = batches.filter((x) => x.batchId > b.batchId && !x.revertedByBatchId).length;
    const canRevert = !b.revertedByBatchId;
    $('history-detail').innerHTML = `
        <div class="hd-head"><h3>操作 #${b.batchId} ${sourceBadge(b)}</h3></div>
        <dl class="hd-meta">
            <dt>いつ</dt><dd>${esc(formatDate(b.executedAt, true))}</dd>
            <dt>だれが</dt><dd>${esc(b.userName)}（PC: ${esc(b.machineName ?? '—')} ／ DBログイン: ${esc(b.dbLogin ?? '—')}）</dd>
            <dt>どこを</dt><dd>${esc(b.tables ?? '—')}</dd>
            <dt>内容</dt><dd>${esc(b.summary ?? '')}（${b.rowTotal.toLocaleString('ja-JP')} 行）</dd>
            ${b.revertsBatchIds ? `<dt>取り消した操作</dt><dd>#${esc(b.revertsBatchIds.replaceAll(',', ', #'))}</dd>` : ''}
        </dl>
        <div class="hd-actions">
            <button type="button" class="small-btn delete-btn" data-revert="one" ${canRevert ? '' : 'disabled'} title="この操作で行った変更だけを元に戻す">↶ この操作を取り消す</button>
            <button type="button" class="small-btn delete-btn" data-revert="since" title="この操作と、それより後のまだ取り消していない操作をすべて元に戻す">⏪ この時点まで戻す（${newer + (canRevert ? 1 : 0)} 件を取り消し）</button>
        </div>
        ${b.revertedByBatchId ? `<p class="hd-note">この操作は、操作 #${b.revertedByBatchId} で取り消し済みです。</p>` : ''}
        ${conflicts ? conflictHtml(conflicts) : ''}
        ${d.totalRows > d.rows.length ? `<p class="hd-note">明細 ${d.totalRows.toLocaleString('ja-JP')} 行のうち、先頭 ${d.rows.length.toLocaleString('ja-JP')} 行を表示しています（取り消しは全行が対象です）。</p>` : ''}
        ${d.rows.map(rowHtml).join('')}`;
}

function rowHtml(r) {
    const diffs = r.changes.slice(0, MAX_DIFF_COLUMNS).map((c) => {
        const v = (x, cls) => (x == null ? '<span class="null">NULL</span>' : `<span class="${cls}">${esc(x)}</span>`);
        const body = r.operation === 'UPDATE' ? `${v(c.before, 'before')} → ${v(c.after, 'after')}`
            : r.operation === 'INSERT' ? v(c.after, 'after')
            : v(c.before, 'before');
        return `<dt>${esc(c.column)}</dt><dd>${body}</dd>`;
    }).join('');
    const more = r.changes.length > MAX_DIFF_COLUMNS ? `<dt></dt><dd>… ほか ${r.changes.length - MAX_DIFF_COLUMNS} 列</dd>` : '';
    return `<div class="hd-row ${r.operation}">
        <div class="hd-row-head"><span class="op">${OP_LABEL[r.operation] ?? r.operation}</span><span class="tbl">${esc(r.table)}</span><span class="key">${esc(r.keyText)}</span></div>
        <dl class="hd-diff">${diffs}${more}</dl>
    </div>`;
}

function conflictHtml(conflicts) {
    return `<div class="hd-note error">
        <strong>取り消せない行が ${conflicts.length} 件あるため、何も変更していません。</strong>
        その後に別の変更が入っている可能性があります。内容を確認し、それでも戻す場合は「強制的に戻す」を押してください（その後の変更は上書きされます）。
        <ul>${conflicts.slice(0, 20).map((c) => `<li>#${c.batchId} ${esc(c.table)} ${esc(OP_LABEL[c.operation] ?? c.operation)}（${esc(c.keyText)}）: ${esc(c.reason)}</li>`).join('')}</ul>
        ${conflicts.length > 20 ? `<p>… ほか ${conflicts.length - 20} 件</p>` : ''}
        <div class="hd-actions">
            <button type="button" class="small-btn delete-btn" data-revert="${conflicts.mode}" data-force="1">⚠ 強制的に戻す</button>
        </div>
    </div>`;
}

async function revert(mode, force) {
    if (!selected) return;
    const b = selected.batch;
    const targets = mode === 'since'
        ? batches.filter((x) => x.batchId >= b.batchId && !x.revertedByBatchId)
        : [b];
    const lines = [
        mode === 'since'
            ? `操作 #${b.batchId} 以降の ${targets.length} 件の操作を、新しい順にすべて取り消します（#${targets.map((t) => t.batchId).join(', #')}）。`
            : `操作 #${b.batchId}（${b.source}・${formatDate(b.executedAt)}・${b.rowTotal} 行）を取り消します。`,
        '',
        '追加した行は削除、更新した行は元の値に、削除した行は元に戻します。',
        force ? '⚠ その後に別の変更が入っている行も、上書きして戻します。' : 'その後に別の変更が入っている行があれば、何も変更せずにお知らせします。',
        '取り消したことも履歴に残ります。よろしいですか？',
    ];
    if (!confirm(lines.join('\n'))) return;

    try {
        const res = await withLoading('取り消しています...', () => call('historyRevert', { batchId: b.batchId, mode, force }));
        if (!res.done) {
            res.conflicts.mode = mode;
            await showDetail(b.batchId, res.conflicts);
            toast(`取り消せない行が ${res.conflicts.length} 件あったため、何も変更していません。`, 'error', null, 10000);
            return;
        }
        toast(`取り消しました（操作 #${res.newBatchId}・${res.rows} 行）。`, 'success', null, 8000);
        for (const w of res.warnings) toast(w, 'info', null, 10000);
        onChanged();
        await load();
    } catch (e) {
        toast(`取り消しに失敗しました（何も変更していません）: ${e.message}`, 'error', null, 12000);
    }
}

async function purge() {
    const days = prompt('何日より前の履歴を削除しますか？（例: 365）\n削除した履歴は戻せず、その操作は取り消せなくなります。', '365');
    if (days == null) return;
    const n = parseInt(days, 10);
    if (!Number.isFinite(n) || n < 1) {
        toast('1 以上の日数を入れてください。', 'error');
        return;
    }
    const before = new Date(Date.now() - n * 86400000);
    if (!confirm(`${before.toLocaleDateString('ja-JP')} より前の変更履歴を削除します。よろしいですか？`)) return;
    try {
        const res = await withLoading('古い履歴を削除しています...', () => call('historyPurge', { before: before.toISOString() }));
        toast(`${res.deleted} 件の操作の履歴を削除しました。`, 'success');
        await load();
    } catch (e) {
        toast(e.message, 'error');
    }
}

// ------------------------------------------------------------------

function formatDate(v, seconds = false) {
    const d = new Date(v);
    if (Number.isNaN(d.getTime())) return String(v ?? '');
    const p = (n) => String(n).padStart(2, '0');
    return `${d.getFullYear()}/${p(d.getMonth() + 1)}/${p(d.getDate())} ${p(d.getHours())}:${p(d.getMinutes())}${seconds ? ':' + p(d.getSeconds()) : ''}`;
}

/** "DOMAIN\\user" → "user" */
function shortUser(u) {
    const i = (u ?? '').lastIndexOf('\\');
    return i >= 0 ? u.slice(i + 1) : u;
}
