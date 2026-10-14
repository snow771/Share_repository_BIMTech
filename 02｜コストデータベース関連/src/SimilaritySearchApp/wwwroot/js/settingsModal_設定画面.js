// js/settingsModal_設定画面.js
// 設定モーダル: DB 接続（サーバー・認証・テーブル名）と ローカルLLM（URL・モデル選択・パラメータ）。

import { call } from './bridge_通信.js';
import { toast, withLoading, bindSegment, setSegment } from './ui_共通部品.js';

const $ = (id) => document.getElementById(id);

const TABLE_LABELS = {
    projects: '物件情報',
    assess: '査定単価',
    revise: '修正単価',
    category: 'カテゴリマスタ',
    indexAllocation: '指数割当マスタ',
    deflator: '補正係数',
    term: '期間マスタ',
    index: '指数マスタ',
    materialMaster: '建築材料マスタ（標準単価）',
    changeBatch: '変更履歴（操作）',
    changeRow: '変更履歴（明細）',
};

let current = null;          // getSettings の結果
let useWindowsAuth = true;
let onSaved = () => {};

export function initSettingsModal({ onSettingsSaved } = {}) {
    onSaved = onSettingsSaved ?? onSaved;

    $('s-tables').innerHTML = Object.entries(TABLE_LABELS).map(([k, label]) =>
        `<div class="form-group"><label for="s-t-${k}">${label}</label><input id="s-t-${k}" data-table="${k}"></div>`).join('');

    for (const el of document.querySelectorAll('#settings-modal [data-close]')) {
        el.addEventListener('click', close);
    }
    $('settings-modal').addEventListener('click', (e) => {
        if (e.target.id === 'settings-modal') close();
    });
    document.addEventListener('keydown', (e) => {
        if (e.key === 'Escape' && !$('settings-modal').classList.contains('hidden')) close();
    });

    bindSegment($('settings-tabs'), showTab, 'tab');
    bindSegment($('s-auth'), (v) => {
        useWindowsAuth = v === 'windows';
        toggleSqlFields();
    });

    $('s-model').addEventListener('change', () => {
        if ($('s-model').value) $('s-model-text').value = $('s-model').value;
    });
    $('s-refresh-models').addEventListener('click', refreshModels);
    $('s-test-db').addEventListener('click', testDb);
    $('s-test-llm').addEventListener('click', testLlm);
    $('settings-form').addEventListener('submit', save);
}

/** 設定を開く。tab: 'db' | 'llm' */
export async function openSettings(tab = 'db') {
    try {
        current = await call('getSettings');
    } catch (e) {
        toast(`設定を読み込めませんでした: ${e.message}`, 'error');
        return;
    }
    fill(current);
    setSegment($('settings-tabs'), tab, 'tab');
    showTab(tab);
    $('s-db-result').textContent = '';
    $('s-llm-result').textContent = '';
    $('settings-modal').classList.remove('hidden');
}

// ------------------------------------------------------------------

function close() {
    $('settings-modal').classList.add('hidden');
    $('s-password').value = '';
}

function showTab(tab) {
    for (const p of document.querySelectorAll('#settings-form [data-panel]')) {
        p.classList.toggle('hidden', p.dataset.panel !== tab);
    }
}

function toggleSqlFields() {
    for (const el of document.querySelectorAll('#settings-form .sql-only')) {
        el.classList.toggle('hidden', useWindowsAuth);
    }
}

function fill(s) {
    const db = s.db;
    $('s-server').value = db.server;
    $('s-database').value = db.database;
    useWindowsAuth = db.useWindowsAuth;
    setSegment($('s-auth'), useWindowsAuth ? 'windows' : 'sql');
    toggleSqlFields();
    $('s-user').value = db.user ?? '';
    $('s-password').value = '';
    $('s-password').placeholder = db.hasPassword ? '保存済み（変更する場合のみ入力）' : '未設定';
    $('s-trust').checked = !!db.trustServerCertificate;
    $('s-timeout-db').value = db.commandTimeoutSec;
    for (const input of document.querySelectorAll('#s-tables [data-table]')) {
        input.value = db.tables?.[input.dataset.table] ?? '';
    }

    const llm = s.llm;
    $('s-baseurl').value = llm.baseUrl;
    setModelOptions([llm.model], llm.model);
    $('s-model-text').value = llm.model;
    $('s-timeout-llm').value = llm.timeoutSec;
    $('s-temperature').value = llm.temperature;
    $('s-numpredict').value = llm.numPredict;
    $('s-numctx').value = llm.numCtx ?? 8192;
}

function collect() {
    const tables = {};
    for (const input of document.querySelectorAll('#s-tables [data-table]')) {
        tables[input.dataset.table] = input.value.trim();
    }
    const password = $('s-password').value;
    return {
        db: {
            server: $('s-server').value.trim(),
            database: $('s-database').value.trim(),
            useWindowsAuth,
            user: $('s-user').value.trim(),
            password: password === '' ? null : password,   // 空欄は「変更しない」
            trustServerCertificate: $('s-trust').checked,
            commandTimeoutSec: parseInt($('s-timeout-db').value, 10) || 120,
            tables,
        },
        llm: collectLlm(),
    };
}

function collectLlm() {
    return {
        baseUrl: $('s-baseurl').value.trim(),
        model: $('s-model-text').value.trim() || $('s-model').value,
        timeoutSec: parseInt($('s-timeout-llm').value, 10) || 600,
        temperature: parseFloat($('s-temperature').value) || 0,
        numPredict: parseInt($('s-numpredict').value, 10) || 512,
        numCtx: Number.isFinite(parseInt($('s-numctx').value, 10)) ? parseInt($('s-numctx').value, 10) : 8192,
    };
}

function setModelOptions(models, selected) {
    const sel = $('s-model');
    sel.innerHTML = '';
    const list = [...new Set([...(models ?? []), selected].filter(Boolean))];
    for (const m of list) sel.appendChild(new Option(m, m));
    sel.value = selected ?? '';
}

async function refreshModels() {
    const llm = collectLlm();
    try {
        const res = await withLoading('モデル一覧を取得しています...', () => call('listLlmModels', llm));
        setModelOptions(res.models, llm.model);
        result('s-llm-result', true, `${res.models.length} 件のモデルを取得しました。`);
    } catch (e) {
        result('s-llm-result', false, e.message);
    }
}

async function testDb() {
    try {
        const res = await withLoading('DBに接続しています...', () => call('testDbConnection', collect()));
        result('s-db-result', true, res.message);
    } catch (e) {
        result('s-db-result', false, e.message);
    }
}

async function testLlm() {
    try {
        const res = await withLoading('LLMに接続しています...', () => call('testLlm', collectLlm()));
        result('s-llm-result', true, res.message);
    } catch (e) {
        result('s-llm-result', false, e.message);
    }
}

async function save(e) {
    e.preventDefault();
    try {
        current = await call('saveSettings', collect());
        toast('設定を保存しました。', 'success');
        close();
        onSaved(current);
    } catch (err) {
        toast(`保存に失敗しました: ${err.message}`, 'error');
    }
}

function result(id, ok, message) {
    const el = $(id);
    el.className = `test-result ${ok ? 'ok' : 'ng'}`;
    el.textContent = `${ok ? '✔' : '✖'} ${message}`;
}
