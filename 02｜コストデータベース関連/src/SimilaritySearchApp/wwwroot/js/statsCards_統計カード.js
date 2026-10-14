// js/statsCards_統計カード.js
// 中央上部: 件数・条件チップ・統計カード4枚。カードをクリックするとグラフの指標が切り替わる。

import { state, U, isSet, emit } from './state_画面状態.js';
import { yen, pct, esc, TSUBO_PER_M2 } from './format_数値書式.js';

const $ = (id) => document.getElementById(id);

/** n がこの件数以下なら「参考値」バッジを付ける */
const FEW_SAMPLES = 5;

/** チップの表示名と、解除したときに一緒に戻す下位項目 */
const CHIP_DEFS = [
    { key: 'trade', label: '工種', lower: ['work', 'item'] },
    { key: 'work', label: '工事項目', lower: ['item'] },
    { key: 'item', label: '項目', lower: [] },
    { key: 'prefecture', label: '', lower: ['city', 'town'] },
    { key: 'city', label: '', lower: ['town'] },
    { key: 'town', label: '', lower: [] },
    { key: 'structure', label: '', lower: [] },
    { key: 'form', label: '', lower: [] },
    { key: 'floorsAbove', label: '地上', suffix: '階', lower: [] },
    { key: 'floorsBelow', label: '地下', suffix: '階', lower: [] },
];

const CARD_TO_CHART = { assess: 'assess', revise: 'revise', temp: 'temp', labor: 'formwork' };

export function initStatsCards() {
    $('chip-list').addEventListener('click', (e) => {
        const btn = e.target.closest('button[data-key]');
        if (!btn) return;
        const def = CHIP_DEFS.find((d) => d.key === btn.dataset.key);
        if (btn.dataset.key === 'area') {
            state.criteria.areaMin = null;
            state.criteria.areaMax = null;
        } else if (def) {
            state.criteria[def.key] = U;
            for (const k of def.lower) state.criteria[k] = U;
        }
        emit('request:analyze');
    });
    $('chip-clear').addEventListener('click', () => {
        for (const d of CHIP_DEFS) state.criteria[d.key] = U;
        state.criteria.areaMin = null;
        state.criteria.areaMax = null;
        emit('request:analyze');
    });
    $('stat-grid').addEventListener('click', (e) => {
        const card = e.target.closest('.stat-card');
        if (card) emit('chart:key', CARD_TO_CHART[card.dataset.key]);
    });
}

export function renderStatsCards(result) {
    $('result-count').textContent = result.projectCount.toLocaleString('ja-JP');
    renderChips(result);

    const active = Object.entries(CARD_TO_CHART).find(([, chart]) => chart === state.chartKey)?.[0]
        ?? (state.chartKey === 'rebar' ? 'labor' : '');

    $('stat-grid').innerHTML = [
        priceCard('assess', '査定単価', result.assess, result.assessDeflatorMean, active),
        priceCard('revise', '修正単価', result.revise, result.reviseDeflatorMean, active),
        tempCard(result, active),
        laborCard(result, active),
    ].join('');
}

export function highlightCard(chartKey) {
    const cardKey = chartKey === 'rebar' ? 'labor' : Object.keys(CARD_TO_CHART).find((k) => CARD_TO_CHART[k] === chartKey);
    for (const card of document.querySelectorAll('.stat-card')) {
        card.classList.toggle('active', card.dataset.key === cardKey);
    }
}

// ------------------------------------------------------------------

function renderChips(result) {
    const c = result.criteria;
    const chips = [];
    for (const d of CHIP_DEFS) {
        if (!isSet(c[d.key])) continue;
        const text = `${d.label ? d.label + ':' : ''}${c[d.key]}${d.suffix ?? ''}`;
        chips.push(`<span class="chip">${esc(text)}<button type="button" data-key="${d.key}" title="この条件を解除">×</button></span>`);
    }
    const fullRange = Math.round(c.areaMin ?? 0) <= Math.floor(result.areaBoundMin) && Math.round(c.areaMax ?? 0) >= Math.floor(result.areaBoundMax);
    if (!fullRange) {
        chips.push(`<span class="chip">面積:${Math.round(c.areaMin).toLocaleString('ja-JP')}〜${Math.round(c.areaMax).toLocaleString('ja-JP')}㎡<button type="button" data-key="area" title="全範囲に戻す">×</button></span>`);
    }
    chips.push(`<span class="chip fixed">${c.basisMode === 'LegalFloorArea' ? '法廷床面積' : '施工床面積'}</span>`);
    chips.push(`<span class="chip fixed">${c.costMode === 'NetConstruction' ? '純工事費' : '工事原価'}</span>`);
    $('chip-list').innerHTML = chips.join('');
    $('chip-clear').classList.toggle('hidden', !CHIP_DEFS.some((d) => isSet(c[d.key])) && fullRange);
}

function head(key, title, n, active) {
    const caution = n <= FEW_SAMPLES ? '<span class="badge-caution" title="件数が少ないため参考値として扱ってください">参考値</span>' : '';
    return `<div class="card stat-card${active === key ? ' active' : ''}" data-key="${key}" title="クリックでグラフを切替">
        <div class="stat-card-head"><h3>${title}${caution}</h3><span class="stat-n">n = ${n}</span></div>`;
}

function iqrBar(summary) {
    const { q1, median, q3 } = summary ?? {};
    if (![q1, median, q3].every(Number.isFinite) || q3 <= 0) return '<div class="iqr-bar"></div>';
    const scale = q3 * 1.25;
    const p = (v) => Math.max(0, Math.min(100, (v / scale) * 100));
    return `<div class="iqr-bar" title="Q1〜Q3 の帯（赤線が中央値）">
        <div class="iqr" style="left:${p(q1)}%;width:${p(q3) - p(q1)}%"></div>
        <div class="med" style="left:${p(median)}%"></div></div>`;
}

function main(value, unit) {
    return `<div class="stat-main"><span class="value">${value}</span><span class="unit">${unit}</span></div>`;
}

function priceCard(key, title, ba, deflator, active) {
    const a = ba.after;
    const b = ba.before;
    return `${head(key, title, a.n, active)}
        <div class="stat-block">
            <div class="stat-label">中央値（補正後）</div>
            ${main(yen(a.median), '円/㎡')}
            ${iqrBar(a)}
            <div class="stat-sub">
                Q1〜Q3 ${yen(a.q1)} 〜 ${yen(a.q3)}<br>
                <span class="muted">補正前</span> ${yen(b.median)} → <span class="muted">補正後</span> ${yen(a.median)}<br>
                <span class="muted">坪単価</span> ${yen(Number.isFinite(a.median) ? a.median * TSUBO_PER_M2 : null)} 円/坪<br>
                <span class="muted">deflator平均</span> ${Number.isFinite(deflator) ? deflator.toFixed(3) : '---'}
            </div>
        </div></div>`;
}

function tempCard(r, active) {
    const u = r.tempUnit.after;
    const ratio = r.tempRatio.after;
    return `${head('temp', '仮設工事', r.tempN, active)}
        <div class="stat-block">
            <div class="stat-label">施工床単価 中央値（補正後）</div>
            ${main(yen(u.median), '円/㎡')}
            <div class="stat-sub">Q1〜Q3 ${yen(u.q1)} 〜 ${yen(u.q3)}<br>
            <span class="muted">補正前</span> ${yen(r.tempUnit.before.median)}</div>
        </div>
        <div class="stat-block">
            <div class="stat-label">仮設工事歩掛（直接工事費比）</div>
            ${main(pct(ratio.median), '')}
            <div class="stat-sub">Q1〜Q3 ${pct(ratio.q1)} 〜 ${pct(ratio.q3)}</div>
        </div></div>`;
}

function laborCard(r, active) {
    const f = r.formwork.after;
    const rb = r.rebar.after;
    return `${head('labor', '型枠・鉄筋歩掛', Math.max(r.formworkN, r.rebarN), active)}
        <div class="stat-block">
            <div class="stat-label">型枠労務 中央値（n=${r.formworkN}）</div>
            ${main(yen(f.median), '円/㎡')}
            <div class="stat-sub">Q1〜Q3 ${yen(f.q1)} 〜 ${yen(f.q3)}</div>
        </div>
        <div class="stat-block">
            <div class="stat-label">鉄筋労務 中央値（n=${r.rebarN}）</div>
            ${main(yen(rb.median), '円/t')}
            <div class="stat-sub">Q1〜Q3 ${yen(rb.q1)} 〜 ${yen(rb.q3)}</div>
        </div></div>`;
}
