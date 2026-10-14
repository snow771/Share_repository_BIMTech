// js/chartRenderer_グラフ描画.js
// 自前の SVG グラフ描画（箱ひげ図・散布図）。外部ライブラリを使わないのでオフラインでも動く。
// 色はすべて CSS 変数（--series-<key>, --stat-median など）で指定する。

import { yen, compact, esc } from './format_数値書式.js';

const NS = 'http://www.w3.org/2000/svg';
const HEIGHT = 340;
const M = { left: 72, right: 120, top: 18, bottom: 46 };

/**
 * グラフを描く。
 * @param {SVGSVGElement} svg
 * @param {object} series   C# の ChartSeries（key, title, unit, xLabel, before[], after[]）
 * @param {'box'|'scatter'} view
 * @param {string|null} selectedId  強調する project_id
 * @param {{ onPick: (pid:string)=>void, tooltip: HTMLElement }} opts
 */
export function renderChart(svg, series, view, selectedId, opts) {
    const width = Math.max(480, svg.clientWidth || svg.parentElement.clientWidth || 900);
    svg.setAttribute('viewBox', `0 0 ${width} ${HEIGHT}`);
    svg.setAttribute('preserveAspectRatio', 'xMidYMid meet');
    svg.replaceChildren();

    const ctx = {
        svg, width, selectedId, opts,
        strong: `var(--series-${series.key})`,
        weak: `var(--series-${series.key}-weak)`,
        unit: series.unit,
        plotW: width - M.left - M.right,
        plotH: HEIGHT - M.top - M.bottom,
    };

    const all = [...series.before, ...series.after].map((p) => p.y).filter(Number.isFinite);
    if (all.length === 0) {
        el(svg, 'text', { x: width / 2, y: HEIGHT / 2, 'text-anchor': 'middle', class: 'no-data' }, 'データなし（条件を緩めてください）');
        return;
    }

    el(svg, 'rect', { x: M.left, y: M.top, width: ctx.plotW, height: ctx.plotH, class: 'plot-bg' });
    ctx.y = linearScale(paddedDomain(all), [M.top + ctx.plotH, M.top]);
    drawYAxis(ctx);

    if (view === 'scatter') drawScatter(ctx, series);
    else drawBoxes(ctx, series);

    bindTooltip(svg, opts.tooltip);
}

/** 凡例（HTML） */
export function legendHtml(series, view) {
    const items = view === 'scatter'
        ? [
            `<span><span class="swatch" style="background:var(--series-${series.key}-weak)"></span>● 補正前</span>`,
            `<span><span class="swatch" style="background:var(--series-${series.key})"></span>▲ 補正後</span>`,
            '<span><span class="line"></span>中央値（補正後）</span>',
            '<span><span class="line q"></span>Q1 / Q3（補正後）</span>',
            `<span>X軸: ${esc(series.xLabel)}</span>`,
        ]
        : [
            `<span><span class="swatch" style="background:var(--series-${series.key}-weak)"></span>補正前</span>`,
            `<span><span class="swatch" style="background:var(--series-${series.key})"></span>補正後</span>`,
            '<span><span class="line"></span>中央値</span>',
            '<span>箱 = Q1〜Q3 / ひげ = 1.5×IQR / 点 = 各物件</span>',
        ];
    return items.join('');
}

// ------------------------------------------------------------------
// 箱ひげ図（補正前・補正後を並べ、各物件の点を重ねる）

function drawBoxes(ctx, series) {
    const groups = [
        { label: '補正前', points: series.before, color: ctx.weak, center: M.left + ctx.plotW * 0.3 },
        { label: '補正後', points: series.after, color: ctx.strong, center: M.left + ctx.plotW * 0.7 },
    ];
    const boxW = Math.min(110, ctx.plotW * 0.18);

    for (const g of groups) {
        el(ctx.svg, 'text', { x: g.center, y: HEIGHT - 18, 'text-anchor': 'middle', class: 'axis-title' }, `${g.label}（n=${g.points.length}）`);
        const ys = g.points.map((p) => p.y).filter(Number.isFinite).sort((a, b) => a - b);
        if (ys.length === 0) continue;

        const q1 = percentile(ys, 25);
        const med = percentile(ys, 50);
        const q3 = percentile(ys, 75);
        const iqr = q3 - q1;
        const loW = ys.find((v) => v >= q1 - 1.5 * iqr) ?? ys[0];
        const hiW = [...ys].reverse().find((v) => v <= q3 + 1.5 * iqr) ?? ys[ys.length - 1];
        const x0 = g.center - boxW / 2;
        const x1 = g.center + boxW / 2;

        // ひげ
        el(ctx.svg, 'line', { x1: g.center, x2: g.center, y1: ctx.y(loW), y2: ctx.y(q1), class: 'whisker' });
        el(ctx.svg, 'line', { x1: g.center, x2: g.center, y1: ctx.y(q3), y2: ctx.y(hiW), class: 'whisker' });
        el(ctx.svg, 'line', { x1: g.center - boxW / 4, x2: g.center + boxW / 4, y1: ctx.y(loW), y2: ctx.y(loW), class: 'whisker' });
        el(ctx.svg, 'line', { x1: g.center - boxW / 4, x2: g.center + boxW / 4, y1: ctx.y(hiW), y2: ctx.y(hiW), class: 'whisker' });
        // 箱と中央値
        el(ctx.svg, 'rect', {
            x: x0, y: ctx.y(q3), width: boxW, height: Math.max(1, ctx.y(q1) - ctx.y(q3)),
            class: 'box', style: `fill:${g.color};stroke:${ctx.strong}`,
        });
        el(ctx.svg, 'line', { x1: x0, x2: x1, y1: ctx.y(med), y2: ctx.y(med), class: 'median-line' });

        // Q1 / 中央値 / Q3 の値ラベル（重なりを避けるため最低間隔を確保）
        const labels = spread([
            { v: q3, text: `Q3 ${yen(q3)}`, cls: 'q' },
            { v: med, text: `Med ${yen(med)}`, cls: 'med' },
            { v: q1, text: `Q1 ${yen(q1)}`, cls: 'q' },
        ].map((l) => ({ ...l, py: ctx.y(l.v) })), 13);
        for (const l of labels) {
            el(ctx.svg, 'text', { x: x1 + 8, y: l.py + 4, class: `stat-text ${l.cls}` }, l.text);
        }

        // 各物件の点（左右に少し散らす）
        const jitterW = boxW * 0.8;
        for (const p of g.points) {
            if (!Number.isFinite(p.y)) continue;
            const jx = g.center + (hash01(p.projectId) - 0.5) * jitterW;
            point(ctx, 'circle', jx, ctx.y(p.y), g.color, p, `${g.label}`);
        }
    }
}

// ------------------------------------------------------------------
// 散布図（X=面積 or 物件インデックス、Y=単価）

function drawScatter(ctx, series) {
    const xs = [...series.before, ...series.after].map((p) => p.x).filter(Number.isFinite);
    const x = linearScale(paddedDomain(xs, 0.04), [M.left, M.left + ctx.plotW]);
    drawXAxis(ctx, x, series.xLabel);

    // 補正後の中央値・四分位の線
    const ys = series.after.map((p) => p.y).filter(Number.isFinite).sort((a, b) => a - b);
    if (ys.length > 0) {
        const lines = [
            { v: percentile(ys, 75), cls: 'q-line', text: `Q3 ${yen(percentile(ys, 75))}`, tcls: 'q' },
            { v: percentile(ys, 50), cls: 'median-line', text: `Med▲ ${yen(percentile(ys, 50))}`, tcls: 'med' },
            { v: percentile(ys, 25), cls: 'q-line', text: `Q1 ${yen(percentile(ys, 25))}`, tcls: 'q' },
        ];
        for (const l of lines) {
            el(ctx.svg, 'line', { x1: M.left, x2: M.left + ctx.plotW, y1: ctx.y(l.v), y2: ctx.y(l.v), class: l.cls });
        }
        for (const l of spread(lines.map((l) => ({ ...l, py: ctx.y(l.v) })), 13)) {
            el(ctx.svg, 'text', { x: M.left + ctx.plotW + 8, y: l.py + 4, class: `stat-text ${l.tcls}` }, l.text);
        }
    }

    for (const p of series.before) {
        if (Number.isFinite(p.x) && Number.isFinite(p.y)) point(ctx, 'circle', x(p.x), ctx.y(p.y), ctx.weak, p, '補正前', series.xLabel);
    }
    for (const p of series.after) {
        if (Number.isFinite(p.x) && Number.isFinite(p.y)) point(ctx, 'triangle', x(p.x), ctx.y(p.y), ctx.strong, p, '補正後', series.xLabel);
    }
}

// ------------------------------------------------------------------
// 部品

function point(ctx, shape, px, py, color, p, label, xLabel) {
    const selected = ctx.selectedId != null && p.projectId === ctx.selectedId;
    const r = selected ? 7 : 4.5;
    const attrs = {
        class: `point${selected ? ' selected' : ''}`,
        style: selected ? '' : `fill:${color}`,
        'data-pid': p.projectId,
        'data-tip': `${p.projectId}  ${label}\n${yen(p.y)} ${ctx.unit}${xLabel ? `\n${xLabel}: ${yen(p.x)}` : ''}`,
        'fill-opacity': selected ? 1 : 0.8,
    };
    const node = shape === 'triangle'
        ? el(ctx.svg, 'path', { ...attrs, d: `M${px},${py - r * 1.2} L${px + r},${py + r * 0.8} L${px - r},${py + r * 0.8} Z` })
        : el(ctx.svg, 'circle', { ...attrs, cx: px, cy: py, r });
    node.addEventListener('click', () => ctx.opts.onPick(p.projectId));
    if (selected) ctx.svg.appendChild(node); // 最前面へ
}

function drawYAxis(ctx) {
    const [lo, hi] = ctx.y.domain;
    for (const t of niceTicks(lo, hi, 6)) {
        const py = ctx.y(t);
        el(ctx.svg, 'line', { x1: M.left, x2: M.left + ctx.plotW, y1: py, y2: py, class: 'grid-line' });
        el(ctx.svg, 'text', { x: M.left - 8, y: py + 4, 'text-anchor': 'end', class: 'axis-text' }, compact(t));
    }
    el(ctx.svg, 'text', {
        x: 16, y: M.top + ctx.plotH / 2, 'text-anchor': 'middle', class: 'axis-title',
        transform: `rotate(-90 16 ${M.top + ctx.plotH / 2})`,
    }, ctx.unit);
}

function drawXAxis(ctx, x, title) {
    const [lo, hi] = x.domain;
    for (const t of niceTicks(lo, hi, 8)) {
        const px = x(t);
        el(ctx.svg, 'line', { x1: px, x2: px, y1: M.top, y2: M.top + ctx.plotH, class: 'grid-line' });
        el(ctx.svg, 'text', { x: px, y: M.top + ctx.plotH + 16, 'text-anchor': 'middle', class: 'axis-text' }, compact(t));
    }
    el(ctx.svg, 'text', { x: M.left + ctx.plotW / 2, y: HEIGHT - 6, 'text-anchor': 'middle', class: 'axis-title' }, title);
}

function bindTooltip(svg, tip) {
    if (!tip) return;
    svg.onmousemove = (e) => {
        const t = e.target.closest?.('[data-tip]');
        if (!t) {
            tip.classList.add('hidden');
            return;
        }
        const box = svg.parentElement.getBoundingClientRect();
        tip.textContent = t.dataset.tip;
        tip.style.whiteSpace = 'pre';
        tip.style.left = `${e.clientX - box.left + 12}px`;
        tip.style.top = `${e.clientY - box.top + 12}px`;
        tip.classList.remove('hidden');
    };
    svg.onmouseleave = () => tip.classList.add('hidden');
}

function el(parent, tag, attrs = {}, text = null) {
    const node = document.createElementNS(NS, tag);
    for (const [k, v] of Object.entries(attrs)) {
        if (v !== '' && v != null) node.setAttribute(k, v);
    }
    if (text != null) node.textContent = text;
    parent.appendChild(node);
    return node;
}

// ------------------------------------------------------------------
// 計算ヘルパ

/** numpy.percentile（linear）と同じ。sorted は昇順。 */
export function percentile(sorted, p) {
    if (sorted.length === 0) return NaN;
    const rank = (sorted.length - 1) * p / 100;
    const lo = Math.floor(rank);
    const hi = Math.ceil(rank);
    return sorted[lo] + (sorted[hi] - sorted[lo]) * (rank - lo);
}

function paddedDomain(values, ratio = 0.08) {
    let lo = Math.min(...values);
    let hi = Math.max(...values);
    if (lo === hi) {
        lo -= Math.abs(lo) * 0.1 || 1;
        hi += Math.abs(hi) * 0.1 || 1;
    }
    const pad = (hi - lo) * ratio;
    return [lo >= 0 ? Math.max(0, lo - pad) : lo - pad, hi + pad];
}

function linearScale([d0, d1], [r0, r1]) {
    const f = (v) => r0 + ((v - d0) / (d1 - d0 || 1)) * (r1 - r0);
    f.domain = [d0, d1];
    return f;
}

function niceTicks(lo, hi, count) {
    const span = hi - lo;
    if (!(span > 0)) return [lo];
    const step0 = span / count;
    const mag = 10 ** Math.floor(Math.log10(step0));
    const step = [1, 2, 2.5, 5, 10].map((m) => m * mag).find((s) => s >= step0) ?? mag * 10;
    const ticks = [];
    for (let t = Math.ceil(lo / step) * step; t <= hi + 1e-9; t += step) ticks.push(Math.round(t / step) * step);
    return ticks;
}

/** ラベルの y 座標が近すぎるときに上下へずらす（上から順に並んでいる前提） */
function spread(labels, gap) {
    const sorted = [...labels].sort((a, b) => a.py - b.py);
    for (let i = 1; i < sorted.length; i++) {
        if (sorted[i].py - sorted[i - 1].py < gap) sorted[i].py = sorted[i - 1].py + gap;
    }
    return sorted;
}

/** project_id から 0〜1 の安定した値（点の散らし用） */
function hash01(s) {
    let h = 2166136261;
    for (const ch of String(s)) {
        h ^= ch.codePointAt(0);
        h = Math.imul(h, 16777619);
    }
    return ((h >>> 0) % 1000) / 1000;
}
