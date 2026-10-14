// js/chartPanel_グラフパネル.js
// 中央: グラフの指標タブ・表示切替と再描画のタイミング管理。描画そのものは chartRenderer が行う。

import { state, emit, on } from './state_画面状態.js';
import { renderChart, legendHtml } from './chartRenderer_グラフ描画.js';
import { bindSegment } from './ui_共通部品.js';
import { highlightCard } from './statsCards_統計カード.js';

const $ = (id) => document.getElementById(id);

export function initChartPanel() {
    $('metric-tabs').addEventListener('click', (e) => {
        const tab = e.target.closest('.metric-tab');
        if (tab) emit('chart:key', tab.dataset.key);
    });
    on('chart:key', (key) => {
        if (!key) return;
        state.chartKey = key;
        for (const t of document.querySelectorAll('.metric-tab')) t.classList.toggle('active', t.dataset.key === key);
        highlightCard(key);
        renderChartPanel();
    });

    bindSegment($('view-toggle'), (v) => {
        state.chartView = v;
        renderChartPanel();
    });

    // ウィンドウ幅に合わせて描き直す
    let timer = null;
    new ResizeObserver(() => {
        clearTimeout(timer);
        timer = setTimeout(renderChartPanel, 120);
    }).observe($('chart-area'));

    renderChartPanel();
}

export function renderChartPanel() {
    const svg = $('chart-svg');
    const series = state.result?.charts?.find((c) => c.key === state.chartKey);
    if (!series) {
        svg.replaceChildren();
        $('chart-legend').innerHTML = '';
        return;
    }
    renderChart(svg, series, state.chartView, state.selectedId, {
        onPick: (pid) => emit('select', pid),
        tooltip: $('chart-tooltip'),
    });
    $('chart-legend').innerHTML = legendHtml(series, state.chartView);
}
