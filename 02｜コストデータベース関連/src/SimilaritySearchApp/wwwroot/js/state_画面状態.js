// js/state_画面状態.js
// 画面全体で共有する状態と、モジュール間の簡易イベントバス。

export const U = '(未指定)';

export function defaultCriteria() {
    return {
        prefecture: U, city: U, town: U,
        structure: U, form: U, floorsAbove: U, floorsBelow: U,
        basisMode: 'LegalFloorArea', costMode: 'NetConstruction',
        areaMin: null, areaMax: null,
        trade: U, work: U, item: U,
    };
}

export const state = {
    loaded: false,
    structureType: 'RC',
    criteria: defaultCriteria(),
    result: null,            // C# の AnalysisResult
    selectedId: null,        // 選択中の project_id
    detail: null,            // 選択中物件の詳細（selectProject の結果）
    chartKey: 'revise',      // assess / revise / temp / formwork / rebar
    chartView: 'box',        // box / scatter
    sort: { key: 'reviseYenPerM2', dir: 'desc' },
};

export function isSet(v) {
    return v != null && v !== '' && v !== U;
}

// ── イベントバス ──
// 主なイベント:
//   request:analyze   条件が変わった（再分析してほしい）
//   result            分析結果が届いた (AnalysisResult)
//   select            物件を選択した (projectId)
//   chart:key         グラフの指標を切り替えた (key)
//   loaded            データ読込が完了した (LoadDataResult)
const listeners = new Map();

export function on(event, fn) {
    if (!listeners.has(event)) listeners.set(event, []);
    listeners.get(event).push(fn);
}

export function emit(event, data) {
    for (const fn of listeners.get(event) ?? []) {
        try {
            fn(data);
        } catch (e) {
            console.error(`[${event}]`, e);
        }
    }
}
