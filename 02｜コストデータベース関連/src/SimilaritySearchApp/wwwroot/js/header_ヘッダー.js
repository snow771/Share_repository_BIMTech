// js/header_ヘッダー.js
// 上部ヘッダー: 構造種別の切替・AI検索バー・データ読込・設定・DB接続状態。

import { state, emit, on } from './state_画面状態.js';
import { call } from './bridge_通信.js';
import { withLoading, toast, setStatus, bindSegment, isConnectionError } from './ui_共通部品.js';

const $ = (id) => document.getElementById(id);

export function initHeader({ onOpenSettings, onAiSearch }) {
    bindSegment($('structure-toggle'), (v) => {
        state.structureType = v;
        if (state.loaded) {
            setStatus(`構造種別を ${v}造 に変更しました。「データ読込」を再実行してください。`);
            toast(`構造種別を ${v}造 に変更しました。「⚡ データ読込」で読み直してください。`);
        }
    });

    $('load-btn').addEventListener('click', loadData);
    on('request:reload', loadData);   // DB編集で分析用のテーブルを保存したあとなど
    $('settings-btn').addEventListener('click', () => onOpenSettings('db'));
    $('stencil-btn').addEventListener('click', async () => {
        try {
            await call('openStencilWindow');
        } catch (e) {
            toast(`Stencile変換ウィンドウを開けませんでした: ${e.message}`, 'error');
        }
    });
    // Stencile変換ウィンドウで DB に登録されたら、分析データの再読込を促す
    on('bridge:dbChanged', () => {
        toast('Stencile変換で DB が更新されました。分析に反映するには再読込してください。', 'info',
            { label: '今すぐ再読込', onClick: () => emit('request:reload') }, 12000);
    });

    $('ai-search').addEventListener('submit', (e) => {
        e.preventDefault();
        const text = $('ai-search-input').value.trim();
        if (!text) return;
        if (!state.loaded) {
            toast('先に「⚡ データ読込」を実行してください。', 'error');
            return;
        }
        onAiSearch(text);
    });

    async function loadData() {
        const st = state.structureType;
        setStatus('SQL Server に接続中...');
        try {
            const res = await withLoading(`DBから読込中（${st}造）...`, () => call('loadData', { structureType: st }));
            state.loaded = true;
            state.selectedId = null;
            setDbBadge(true, st);
            toast(`DB読込完了（${st}造）: 物件 ${res.projectCount.toLocaleString('ja-JP')} 件 / 査定 ${res.assessCount.toLocaleString('ja-JP')} 行 / 修正 ${res.reviseCount.toLocaleString('ja-JP')} 行 / 標準材料 ${res.materialCount.toLocaleString('ja-JP')} 件`, 'success');
            if (res.warning) toast(res.warning, 'error');
            emit('loaded', res);
            emit('result', res.result);
        } catch (e) {
            setDbBadge(false);
            setStatus('DB接続エラー。サーバー名・ネットワーク・認証設定を確認してください。');
            toast(`DB接続に失敗しました。\n${e.message}`, 'error',
                isConnectionError(e.message) ? { label: '設定を開く', onClick: () => onOpenSettings('db') } : null);
        }
    }
}

export function setDbBadge(connected, structure) {
    const b = $('db-badge');
    b.classList.toggle('connected', connected);
    b.textContent = connected ? `接続中（${structure}造）` : '未接続';
}
