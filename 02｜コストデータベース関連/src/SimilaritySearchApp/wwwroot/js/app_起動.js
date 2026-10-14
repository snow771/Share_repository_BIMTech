// js/app_起動.js
// 起動処理と全体の配線。条件変更 → 分析 → 各パネル再描画、物件選択 → 詳細表示 の流れをここで管理する。

import { state, on, emit } from './state_画面状態.js';
import { call, isMock } from './bridge_通信.js';
import { setStatus, toast } from './ui_共通部品.js';
import { initHeader } from './header_ヘッダー.js';
import { initFilterPanel, renderFilterPanel } from './filterPanel_絞込パネル.js';
import { initStatsCards, renderStatsCards } from './statsCards_統計カード.js';
import { initChartPanel, renderChartPanel } from './chartPanel_グラフパネル.js';
import { initProjectList, renderProjectList, highlightRow } from './projectList_物件一覧.js';
import { initProjectDetail, renderProjectDetail, clearProjectDetail } from './projectDetail_物件詳細.js';
import { initChatPanel, runAiFilter, addBubble } from './chatPanel_AIチャット.js';
import { initSettingsModal, openSettings } from './settingsModal_設定画面.js';
import { initStandardPrices } from './standardPrices_標準単価.js';
import { initDbEditor, enterDbEditor, canLeaveDbEditor, openTable } from './dbEditor_DB編集.js';
import { initRouter, go } from './router_画面遷移.js';

let analyzeSeq = 0;
let analyzeTimer = null;

function init() {
    initSettingsModal();
    initHeader({ onOpenSettings: openSettings, onAiSearch: runAiFilter });
    initFilterPanel();
    initStatsCards();
    initChartPanel();
    initProjectList();
    initProjectDetail();
    initChatPanel({ onOpenSettings: openSettings });
    initStandardPrices({ onClearSelection: clearSelection });
    initDbEditor({ onOpenSettings: openSettings });

    // ホーム／検索・分析／DB編集 の切り替え（マウスの戻るボタンにも対応）
    initRouter({
        enter: {
            db: enterDbEditor,
            search: () => emit('chart:key', state.chartKey),   // 非表示中にサイズが変わったグラフを描き直す
        },
        canLeave: { db: canLeaveDbEditor },
        openTable: (t) => openTable(t.schema, t.name, { record: false }),   // 戻るで1つ前のテーブルへ
    });
    document.getElementById('home-stencil').addEventListener('click', async () => {
        try {
            await call('openStencilWindow');
        } catch (e) {
            toast(`Stencile変換ウィンドウを開けませんでした: ${e.message}`, 'error');
        }
    });
    // AIアシスタントが DB を調べて答えたあと「DB編集で開く」
    on('request:openTable', (t) => {
        if (go('db') === false) return;   // 保存していない変更があって移動をやめた
        openTable(t.schema, t.name, { search: t.search ?? '' });
    });
    document.getElementById('home-ocr').addEventListener('click', openOcrWindow);
    document.getElementById('ocr-btn').addEventListener('click', openOcrWindow);
    on('bridge:ocrUsageChanged', refreshOcrUsage);
    refreshOcrUsage();

    // 条件変更 → 分析（連続操作はまとめて1回にする）
    on('request:analyze', () => {
        if (!state.loaded) return;
        clearTimeout(analyzeTimer);
        analyzeTimer = setTimeout(analyze, 60);
    });

    on('result', applyResult);
    on('select', selectProject);
    on('loaded', (res) => {
        const st = document.getElementById('home-status-search');
        st.textContent = `読込済み: 物件 ${res.projectCount.toLocaleString('ja-JP')} 件（${state.structureType}造）`;
        st.classList.add('ok');
        clearProjectDetail();
        addBubble('system', 'データを読み込みました。条件を設定するか、上の検索バーに自然文で入力してください。');
    });

    renderProjectList();
    if (isMock) {
        toast('ブラウザ確認モード: WebView2 の外で開いているため、サンプルデータで動作しています。');
    }
}

async function openOcrWindow() {
    try {
        await call('openOcrWindow');
    } catch (e) {
        toast(`見積AI-OCRウィンドウを開けませんでした: ${e.message}`, 'error');
    }
}

/** ホームの「見積AI-OCR」に今日（今月）の処理枚数を出す。注意値・警告値を超えたら色を変える */
async function refreshOcrUsage() {
    const el = document.getElementById('home-status-ocr');
    try {
        const u = await call('ocrUsage');
        el.textContent = `${u.periodLabel}の処理枚数: ${u.used.toLocaleString('ja-JP')} / ${u.limit.toLocaleString('ja-JP')} 枚`;
        el.className = `home-status ocr-${u.level}`;
        el.title = `黄色: ${u.warnAt} 枚超 / 赤: ${u.dangerAt} 枚超（別ウィンドウで開きます）`;
    } catch {
        el.textContent = '別ウィンドウで開きます';
    }
}

async function analyze() {
    const seq = ++analyzeSeq;
    try {
        const result = await call('analyze', state.criteria);
        if (seq === analyzeSeq) emit('result', result);   // 古い応答は捨てる（result → applyResult・標準単価の更新）
    } catch (e) {
        toast(`計算エラー: ${e.message}`, 'error');
    }
}

/** 分析結果を全パネルに反映する */
function applyResult(result) {
    state.result = result;
    state.criteria = { ...result.criteria };

    // 選択中の物件が絞込で外れたら選択を解除
    if (state.selectedId && !result.projects.some((p) => p.projectId === state.selectedId)) {
        state.selectedId = null;
        state.detail = null;
        clearProjectDetail();
    }

    renderFilterPanel(result);
    renderStatsCards(result);
    renderChartPanel();
    renderProjectList();
    setStatus(result.statusLine);

    // 選択が残っている場合は、新しい条件の中央値で詳細を更新
    if (state.selectedId) selectProject(state.selectedId, { quiet: true });
}

async function selectProject(pid, { quiet = false } = {}) {
    if (!pid) return;
    try {
        const detail = await call('selectProject', { projectId: pid });
        state.selectedId = pid;
        state.detail = detail;
        renderProjectDetail(detail, { open: !quiet });   // 条件変更に伴う更新ではモーダルを開かない
        if (!quiet) {
            renderChartPanel();
            highlightRow(pid);
            emit('project:selected', pid);
        }
    } catch (e) {
        toast(e.message, 'error');
    }
}

/** 物件の選択を解除する（標準単価は工種条件だけの表示に戻る） */
async function clearSelection() {
    try {
        await call('clearSelection');
    } catch (e) {
        toast(e.message, 'error');
        return;
    }
    state.selectedId = null;
    state.detail = null;
    clearProjectDetail();
    renderChartPanel();
    highlightRow(null);
    emit('selection:cleared');
}

init();
