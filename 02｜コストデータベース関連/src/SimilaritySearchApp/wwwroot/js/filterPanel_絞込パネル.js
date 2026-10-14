// js/filterPanel_絞込パネル.js
// 左カラム: 絞込条件。選択肢には件数を付け、上位を変えたら下位を未指定に戻す。

import { state, U, isSet, defaultCriteria, emit } from './state_画面状態.js';
import { bindSegment, setSegment } from './ui_共通部品.js';

const $ = (id) => document.getElementById(id);

/** 上位の項目を変えたときに未指定へ戻す下位項目 */
const CASCADE = {
    prefecture: ['city', 'town'],
    city: ['town'],
    trade: ['work', 'item'],
    work: ['item'],
};

const FACET_KEYS = ['prefecture', 'city', 'town', 'structure', 'form', 'floorsAbove', 'floorsBelow'];
const TRADE_KEYS = { trade: 'tradeOptions', work: 'workOptions', item: 'itemOptions' };

let areaTimer = null;

export function initFilterPanel() {
    for (const sel of document.querySelectorAll('#filter-pane select[data-key]')) {
        sel.addEventListener('change', () => {
            const key = sel.dataset.key;
            state.criteria[key] = sel.value;
            for (const lower of CASCADE[key] ?? []) state.criteria[lower] = U;
            emit('request:analyze');
        });
    }

    bindSegment($('basis-toggle'), (v) => {
        state.criteria.basisMode = v;
        // 基準面積を変えたら面積レンジは全範囲に戻す（Python 版と同じ）
        state.criteria.areaMin = null;
        state.criteria.areaMax = null;
        emit('request:analyze');
    });
    bindSegment($('cost-toggle'), (v) => {
        state.criteria.costMode = v;
        emit('request:analyze');
    });

    // 面積スライダー: ドラッグ中は表示だけ更新し、離したら（少し待って）再分析
    for (const id of ['area-min-range', 'area-max-range']) {
        $(id).addEventListener('input', () => onAreaSlide(id));
        $(id).addEventListener('change', () => commitArea(150));
    }
    for (const id of ['area-min', 'area-max']) {
        $(id).addEventListener('change', () => {
            const mn = parseFloat($('area-min').value);
            const mx = parseFloat($('area-max').value);
            state.criteria.areaMin = Number.isFinite(mn) ? mn : null;
            state.criteria.areaMax = Number.isFinite(mx) ? mx : null;
            if (state.criteria.areaMin != null && state.criteria.areaMax != null && state.criteria.areaMin > state.criteria.areaMax) {
                state.criteria.areaMax = state.criteria.areaMin;
            }
            emit('request:analyze');
        });
    }

    for (const id of ['reset-btn', 'reset-btn-top']) {
        $(id).addEventListener('click', resetAll);
    }
}

export function resetAll() {
    if (!state.loaded) return;
    state.criteria = defaultCriteria();
    emit('request:analyze');
}

/** 分析結果に合わせて選択肢・選択値・面積レンジを描き直す */
export function renderFilterPanel(result) {
    const c = result.criteria;

    for (const key of FACET_KEYS) {
        fillSelect($(`f-${key}`), (result.facets?.[key] ?? []).map((o) => ({ value: o.value, label: `${o.value}（${o.count}）`, zero: o.count === 0 })), c[key]);
    }
    for (const [key, prop] of Object.entries(TRADE_KEYS)) {
        const opts = (result[prop] ?? []).filter((v) => v !== U).map((v) => ({ value: v, label: v }));
        fillSelect($(`f-${key}`), opts, c[key]);
        $(`f-${key}`).disabled = opts.length === 0;
    }

    setSegment($('basis-toggle'), c.basisMode);
    setSegment($('cost-toggle'), c.costMode);
    renderArea(result.areaBoundMin, result.areaBoundMax, c.areaMin, c.areaMax);
}

// ------------------------------------------------------------------

function fillSelect(sel, options, selected) {
    sel.innerHTML = '';
    sel.appendChild(new Option(U, U));
    for (const o of options) {
        const opt = new Option(o.label, o.value);
        if (o.zero) opt.className = 'zero';
        sel.appendChild(opt);
    }
    sel.value = isSet(selected) && options.some((o) => o.value === selected) ? selected : U;
    sel.classList.toggle('is-set', isSet(sel.value));
}

function renderArea(min, max, lo, hi) {
    const minR = $('area-min-range');
    const maxR = $('area-max-range');
    const floor = Math.floor(min ?? 0);
    const ceil = Math.ceil(max ?? 0);
    for (const r of [minR, maxR]) {
        r.min = floor;
        r.max = ceil;
    }
    minR.value = lo ?? floor;
    maxR.value = hi ?? ceil;
    $('area-min').value = Math.round(lo ?? floor);
    $('area-max').value = Math.round(hi ?? ceil);
    updateAreaLabel();
}

function onAreaSlide(changedId) {
    const minR = $('area-min-range');
    const maxR = $('area-max-range');
    let a = parseFloat(minR.value);
    let b = parseFloat(maxR.value);
    if (a > b) {
        // 下限が上限を越えたら、動かした側に揃える
        if (changedId === 'area-min-range') b = a; else a = b;
        minR.value = a;
        maxR.value = b;
    }
    $('area-min').value = Math.round(a);
    $('area-max').value = Math.round(b);
    updateAreaLabel();
}

function commitArea(delay) {
    clearTimeout(areaTimer);
    areaTimer = setTimeout(() => {
        state.criteria.areaMin = parseFloat($('area-min-range').value);
        state.criteria.areaMax = parseFloat($('area-max-range').value);
        emit('request:analyze');
    }, delay);
}

function updateAreaLabel() {
    const minR = $('area-min-range');
    const maxR = $('area-max-range');
    const lo = parseFloat(minR.value);
    const hi = parseFloat(maxR.value);
    const span = parseFloat(minR.max) - parseFloat(minR.min) || 1;
    const left = ((lo - parseFloat(minR.min)) / span) * 100;
    const right = ((hi - parseFloat(minR.min)) / span) * 100;
    $('area-fill').style.left = `${left}%`;
    $('area-fill').style.width = `${Math.max(0, right - left)}%`;
    $('area-label').textContent = `${Math.round(lo).toLocaleString('ja-JP')} 〜 ${Math.round(hi).toLocaleString('ja-JP')} ㎡`;
}
