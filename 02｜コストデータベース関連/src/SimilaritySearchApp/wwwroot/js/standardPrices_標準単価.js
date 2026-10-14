// js/standardPrices_標準単価.js
// 中央: 【単価分析】【標準単価】タブの切替と、標準単価（建築材料マスタ）の工種コード別一覧。
// 表示範囲は C# 側で決まる（工種の絞込 ∩ 選択物件の工種コード）。ここではキーワード検索と表示だけを行う。

import { state, on, emit } from './state_画面状態.js';
import { call } from './bridge_通信.js';
import { yen, esc } from './format_数値書式.js';
import { toast } from './ui_共通部品.js';

const $ = (id) => document.getElementById(id);

/** グループ数がこれ以下なら最初から開いておく */
const AUTO_OPEN_GROUPS = 8;

let activeTab = 'analysis';
let data = null;          // C# の StandardPriceResult
let dirty = true;         // 条件・選択が変わり、取り直しが必要か
let refreshTimer = null;
let keywordTimer = null;
let onClearSelection = () => {};

export function initStandardPrices(opts) {
    onClearSelection = opts.onClearSelection;

    $('main-tabs').addEventListener('click', (e) => {
        const tab = e.target.closest('[data-main-tab]');
        if (tab) showTab(tab.dataset.mainTab);
    });
    on('main:tab', showTab);

    $('standard-keyword').addEventListener('input', () => {
        clearTimeout(keywordTimer);
        keywordTimer = setTimeout(render, 150);
    });
    $('standard-hide-zero').addEventListener('change', render);
    $('standard-expand').addEventListener('click', () => {
        const groups = [...document.querySelectorAll('#standard-groups details')];
        const open = groups.some((d) => !d.open);
        for (const d of groups) d.open = open;
        $('standard-expand').textContent = open ? 'すべて閉じる' : 'すべて開く';
    });
    $('standard-project').addEventListener('click', (e) => {
        if (e.target.closest('button')) onClearSelection();
    });

    // 条件・選択が変わったら取り直す（タブが見えているときだけ即時）
    for (const ev of ['result', 'loaded', 'project:selected', 'selection:cleared']) on(ev, markDirty);
}

function showTab(key) {
    activeTab = key;
    for (const t of document.querySelectorAll('[data-main-tab]')) t.classList.toggle('active', t.dataset.mainTab === key);
    $('tab-analysis').classList.toggle('hidden', key !== 'analysis');
    $('tab-standard').classList.toggle('hidden', key !== 'standard');
    if (key === 'standard' && dirty) scheduleRefresh();
    if (key === 'analysis') emit('chart:key', state.chartKey);   // 非表示中にサイズが変わったグラフを描き直す
}

function markDirty() {
    dirty = true;
    if (activeTab === 'standard') scheduleRefresh();
    else scheduleRefresh(400);  // 裏でも件数バッジ用に取得しておく
}

function scheduleRefresh(delay = 80) {
    clearTimeout(refreshTimer);
    refreshTimer = setTimeout(refresh, delay);
}

async function refresh() {
    if (!state.loaded) return;
    dirty = false;
    try {
        data = await call('standardPrices');
        render();
    } catch (e) {
        dirty = true;
        toast(`標準単価を取得できませんでした: ${e.message}`, 'error');
    }
}

// ------------------------------------------------------------------

function render() {
    const badge = $('standard-badge');
    if (!data) return;

    badge.textContent = data.available ? data.materialCount.toLocaleString('ja-JP') : '!';
    badge.classList.remove('hidden');

    $('standard-scope').textContent = data.scopeLabel || '---';
    const proj = $('standard-project');
    if (data.projectId) {
        proj.innerHTML = `選択物件: ${esc(data.projectId)} ${esc(data.projectName ?? '')}<button type="button" title="物件の選択を解除して、工種条件だけで表示">選択を解除</button>`;
        proj.classList.remove('hidden');
    } else {
        proj.classList.add('hidden');
    }

    if (!data.available || data.groups.length === 0) {
        $('standard-summary').textContent = '';
        $('standard-groups').innerHTML = `<div class="card empty-state">${esc(data.message ?? '該当する標準材料がありません。')}</div>`;
        return;
    }

    const keyword = $('standard-keyword').value.trim().toLowerCase();
    const hideZero = $('standard-hide-zero').checked;
    const groups = data.groups
        .map((g) => ({ ...g, materials: g.materials.filter((m) => matches(m, keyword) && (!hideZero || hasPrice(m))) }))
        .filter((g) => g.materials.length > 0);
    const shown = groups.reduce((n, g) => n + g.materials.length, 0);

    $('standard-summary').innerHTML =
        `標準材料 <strong>${shown.toLocaleString('ja-JP')}</strong> 件 / 工種コード <strong>${groups.length}</strong> 件` +
        (shown !== data.materialCount ? `（全 ${data.materialCount.toLocaleString('ja-JP')} 件中）` : '') +
        '　<span class="card-hint">標準単価は「円/単位」、実績は「円」と「円/㎡」で単位が異なります。差額単価を含む材料もあるため、比較は目安として見てください。</span>';

    if (groups.length === 0) {
        $('standard-groups').innerHTML = '<div class="card empty-state">検索条件に合う標準材料がありません。</div>';
        return;
    }

    const autoOpen = groups.length <= AUTO_OPEN_GROUPS || !!keyword || !!data.projectId;
    $('standard-groups').innerHTML = groups.map((g) => groupHtml(g, keyword, autoOpen)).join('');
    $('standard-expand').textContent = autoOpen ? 'すべて閉じる' : 'すべて開く';
}

function groupHtml(g, keyword, open) {
    const name = [g.trade, g.work, g.item].filter(Boolean).join(' / ') || g.sourceSheet || '(名称なし)';
    const actuals = [];
    if (data.projectId) {
        actuals.push(actual('assess', '選択物件 査定（実績）', g.projectAssessAmount, g.projectAssessPerM2));
        actuals.push(actual('revise', '選択物件 修正（実績）', g.projectReviseAmount, g.projectRevisePerM2));
    }
    actuals.push(`<div class="std-actual median" title="絞込後の物件群での、この工種コードの修正単価（円/㎡）中央値">
        <span class="label">修正 中央値（n=${g.sampleCount}）</span>
        <span class="value">${yen(g.medianRevisePerM2)}</span><span class="sub"> 円/㎡</span></div>`);

    return `<details class="card std-group"${open ? ' open' : ''}>
        <summary>
            <div class="std-title">
                <span class="std-code">${esc(g.workTypeCode)}</span>
                <span class="std-name">${esc(name)}</span>
                <span class="std-count">${g.materials.length} 件</span>
            </div>
            <div class="std-actuals">${actuals.join('')}</div>
        </summary>
        <table class="std-table">
            <thead><tr>
                <th>建材コード</th><th>名称</th><th>規格・寸法</th><th>単位</th>
                <th class="num">標準 査定単価</th><th class="num">標準 修正単価</th><th>備考</th><th></th>
            </tr></thead>
            <tbody>${g.materials.map((m) => materialRow(m, keyword)).join('')}</tbody>
        </table>
    </details>`;
}

function actual(cls, label, amount, perM2) {
    return `<div class="std-actual ${cls}">
        <span class="label">${label}</span>
        <span class="value">${yen(amount)}</span><span class="sub"> 円</span><br>
        <span class="sub">${yen(perM2)} 円/㎡</span></div>`;
}

function materialRow(m, keyword) {
    const review = m.reviewStatus && m.reviewStatus !== '確認済' ? `<span class="badge-review">${esc(m.reviewStatus)}</span>` : '';
    return `<tr>
        <td class="mcode">${hl(m.materialCode, keyword)}</td>
        <td>${hl(m.name, keyword)}</td>
        <td class="spec">${hl(m.specification, keyword)}</td>
        <td>${esc(m.unit ?? '')}</td>
        <td class="num price-assess">${price(m.assessPrice)}</td>
        <td class="num price-revise">${price(m.revisePrice)}</td>
        <td class="notes">${hl(m.notes, keyword)}</td>
        <td>${review}</td>
    </tr>`;
}

/** 0 と未設定（null）を区別して表示する */
function price(v) {
    if (v == null) return '<span class="unset" title="未設定">---</span>';
    return yen(v);
}

function hasPrice(m) {
    return (m.assessPrice ?? 0) !== 0 || (m.revisePrice ?? 0) !== 0;
}

function matches(m, keyword) {
    if (!keyword) return true;
    return [m.materialCode, m.name, m.specification, m.notes].some((v) => (v ?? '').toLowerCase().includes(keyword));
}

/** キーワードを <mark> で強調（エスケープ後に適用） */
function hl(text, keyword) {
    const s = esc(text ?? '');
    if (!keyword || !text) return s;
    const k = esc(keyword).replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
    return s.replace(new RegExp(k, 'gi'), (x) => `<mark>${x}</mark>`);
}
