// js/router_画面遷移.js
// 画面（ホーム／検索・分析／DB編集）の切り替えと、マウスの「戻る」ボタンの扱い。
//
// 戻るボタン（マウスのサイドボタン・Alt+←）は「作業していた1つ前の画面」に戻る。
//   ・作業した画面の順番（検索・分析 / DB編集、DB編集では開いたテーブルも）をアプリ側で記録する
//   ・ホームは記録しない（ホームへはヘッダーの「🏠 ホーム」で移動）。ホームで戻ると、直前の作業画面に戻る
//   ・ブラウザの履歴には常に1つ「番兵」の項目を積んでおき、戻るの操作を popstate で受け取って上の処理に置き換える
// Stencile変換は別ウィンドウなので、ここでは扱わない。

import { setSegment } from './ui_共通部品.js';

const $ = (id) => document.getElementById(id);

export const VIEWS = ['home', 'search', 'db'];

const TITLES = { home: 'ホーム', search: '検索・分析', db: 'DB編集' };
const MAX_TRAIL = 50;

let current = null;
let hooks = { enter: {}, canLeave: {}, openTable: null };

/** 作業した画面の順番。{ view: 'search' | 'db', table?: { schema, name } } */
const trail = [];

/**
 * @param {object} opts
 * @param {Record<string, () => void>} [opts.enter]      画面に入ったときの処理
 * @param {Record<string, () => boolean>} [opts.canLeave] 離れてよいか（false で移動を取りやめ）
 * @param {(table: {schema:string, name:string}) => void} [opts.openTable] 戻るで DB編集のテーブルを開き直す
 */
export function initRouter(opts) {
    hooks = { enter: opts.enter ?? {}, canLeave: opts.canLeave ?? {}, openTable: opts.openTable ?? null };

    $('mode-toggle').addEventListener('click', (e) => {
        const btn = e.target.closest('.segmented-btn[data-value]');
        if (btn) go(btn.dataset.value);
    });
    document.addEventListener('click', (e) => {
        const el = e.target.closest('[data-go]');
        if (el) go(el.dataset.go);
    });

    // 番兵: 履歴を1つ積んでおき、戻る操作（popstate）をアプリの「1つ前の作業画面へ」に置き換える
    const start = viewFromHash() ?? 'home';
    history.replaceState({ base: true }, '', `#${start}`);
    history.pushState({ guard: true }, '', `#${start}`);
    window.addEventListener('popstate', () => {
        history.pushState({ guard: true }, '', `#${current ?? start}`);
        back();
    });

    go(start);
}

/** 画面を切り替える。移動しなかった（確認でキャンセル等）ときは false */
export function go(view, { record = true } = {}) {
    if (!VIEWS.includes(view)) view = 'home';
    if (view === current) return true;
    if (current && hooks.canLeave[current] && !hooks.canLeave[current]()) {
        setSegment($('mode-toggle'), current);
        return false;
    }

    document.body.dataset.view = view;
    $('home-body').classList.toggle('hidden', view !== 'home');
    document.querySelector('.app-body').classList.toggle('hidden', view !== 'search');
    $('db-body').classList.toggle('hidden', view !== 'db');
    setSegment($('mode-toggle'), view);
    document.title = `${TITLES[view]} ─ 類似物件・実績検索`;
    history.replaceState(history.state, '', `#${view}`);   // 再読込したときに同じ画面で開くよう URL だけ更新

    current = view;
    if (record && view !== 'home') recordStep({ view });
    hooks.enter[view]?.();
    return true;
}

/** 作業の1歩を記録する（DB編集でテーブルを開いたときなど）。 */
export function recordStep(step) {
    const top = trail[trail.length - 1];
    if (top && sameStep(top, step)) return;
    if (top && top.view === step.view) {
        if (!step.table) return;                         // テーブルを開いている DB編集に入り直しただけ
        if (!top.table) {                                // 「DB編集に入った」をテーブルを開いた記録で置き換える
            trail[trail.length - 1] = step;
            return;
        }
    }
    trail.push(step);
    if (trail.length > MAX_TRAIL) trail.shift();
}

/** 戻る: 作業していた1つ前の画面へ（ホームにいるときは直前の作業画面へ） */
export function back() {
    if (current === 'home') {
        const top = trail[trail.length - 1];
        if (top) apply(top);
        return;
    }
    if (trail.length < 2) return;   // これより前の作業画面が無い
    const popped = trail.pop();
    if (!apply(trail[trail.length - 1])) trail.push(popped);   // 移動を取りやめたら記録を戻す
}

export function currentView() {
    return current;
}

// ------------------------------------------------------------------

function apply(step) {
    if (step.view !== current && !go(step.view, { record: false })) return false;
    if (step.table && hooks.openTable) hooks.openTable(step.table);
    return true;
}

function sameStep(a, b) {
    return a.view === b.view && (a.table?.schema ?? '') === (b.table?.schema ?? '') && (a.table?.name ?? '') === (b.table?.name ?? '');
}

function viewFromHash() {
    const v = location.hash.replace('#', '');
    return VIEWS.includes(v) ? v : null;
}
