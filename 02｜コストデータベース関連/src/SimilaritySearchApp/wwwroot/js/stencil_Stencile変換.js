// js/stencil_Stencile変換.js
// Stencile変換ウィンドウ（stencil.html）の画面制御。
//   ① 元データフォルダ・マッピング・テンプレートを選ぶ → 「読み込む」（進捗とログを逐次表示）
//   ② 一覧で内容を確認（新規／既存あり／警告／エラー）。既存ありは初期状態で未選択＝チェックしたものだけ上書き
//   ③ 選んだ物件を DB に登録（1トランザクション）／ 全件を Excel に保存

import { on } from './state_画面状態.js';
import { call, isMock } from './bridge_通信.js';
import { toast, withLoading, setStatus, bindSegment } from './ui_共通部品.js';
import { esc, display } from './format_数値書式.js';

const $ = (id) => document.getElementById(id);

/** 一覧の列 */
const COLUMNS = [
    { key: 'check', label: '' },
    { key: 'status', label: '状態' },
    { key: 'projectId', label: 'project_id' },
    { key: 'projectName', label: '物件名' },
    { key: 'place', label: '所在地' },
    { key: 'area', label: '施工床面積', num: true },
    { key: 'price', label: '② 単価（一致/全体）', num: true },
    { key: 'db', label: 'DB既存（査定/修正 行）', num: true },
    { key: 'fileName', label: 'ファイル' },
    { key: 'msg', label: 'メッセージ' },
];

/** 詳細パネルで最初に見せる項目（①の出力列） */
const DETAIL_KEYS = [
    'project_id', '物件名', '現場住所_都道府県', '現場住所_市区町村', '現場住所_町名', '構造', '形態',
    '階数_地上', '階数_地下', '請負金額', '契約時予定利益', '利益率', '敷地面積', '建築面積', '法廷床面積', '施工床面積',
    '施工坪単価', '工期(着工)', '工期(竣工)', '世帯数', 'ELV',
];

let scan = null;               // C# の StencilScanResult
const checked = new Set();     // 登録対象のファイル Index
let filter = 'all';
let detailIndex = null;

init();

async function init() {
    bindInputs();
    on('bridge:stencilProgress', onProgress);
    if (isMock) toast('ブラウザ確認モード: サンプルデータで動作しています。');
    try {
        const s = await call('getStencilState');
        const st = s.settings;
        $('st-folder').value = st.sourceFolder ?? '';
        $('st-subfolders').checked = st.includeSubfolders ?? true;
        $('st-do-info').checked = st.doProjectInfo ?? true;
        $('st-do-price').checked = st.doUnitPrice ?? true;
        $('st-mapping').value = st.mappingPath ?? '';
        $('st-mapping').placeholder = s.defaultMapping ? '自動（元データフォルダ内 → 同梱の既定マッピング）' : '自動（元データフォルダ内）';
        $('st-template').value = st.templatePath ?? '';
        $('st-db').textContent = `登録先: ${s.database}`;
        $('st-db').title = `物件情報: ${s.tables.projects}\n査定単価: ${s.tables.assess}\n修正単価: ${s.tables.revise}\n（メイン画面の 設定 → DB接続 で変更）`;
        $('st-db').classList.add('connected');
        if (s.lastScan) applyScan(s.lastScan);
    } catch (e) {
        toast(`初期化に失敗しました: ${e.message}`, 'error');
    }
    syncInputs();
    renderTable();
}

// ── 入力 ─────────────────────────────────────────────

function bindInputs() {
    $('st-folder-pick').addEventListener('click', async () => {
        const res = await call('pickPath', { kind: 'folder', title: 'Stencile（元データ Excel）が入っているフォルダを選択', initial: $('st-folder').value });
        if (res.path) $('st-folder').value = res.path;
        syncInputs();
    });
    $('st-mapping-pick').addEventListener('click', async () => {
        const res = await call('pickPath', {
            kind: 'open', title: 'マッピング YAML を選択', filter: 'YAML (*.yaml;*.yml)|*.yaml;*.yml|すべてのファイル (*.*)|*.*',
            initial: $('st-mapping').value || $('st-folder').value,
        });
        if (res.path) $('st-mapping').value = res.path;
    });
    $('st-mapping-auto').addEventListener('click', () => { $('st-mapping').value = ''; });
    $('st-template-pick').addEventListener('click', async () => {
        const res = await call('pickPath', {
            kind: 'open', title: '単価テンプレート（〇〇期RC造_〇〇単価.xlsx）を選択', filter: 'Excel (*.xlsx;*.xlsm)|*.xlsx;*.xlsm',
            initial: $('st-template').value || $('st-folder').value,
        });
        if (res.path) $('st-template').value = res.path;
        syncInputs();
    });
    for (const id of ['st-do-info', 'st-do-price']) $(id).addEventListener('change', syncInputs);
    $('st-scan').addEventListener('click', runScan);
    $('st-log-clear').addEventListener('click', () => $('st-log').replaceChildren());

    bindSegment($('st-filter'), (v) => {
        filter = v;
        renderTable();
    });
    $('st-search').addEventListener('input', renderTable);
    $('st-export').addEventListener('click', exportExcel);
    $('st-register').addEventListener('click', register);
    for (const id of ['st-reg-info', 'st-reg-price']) $(id).addEventListener('change', updateActions);
    $('st-detail-close').addEventListener('click', closeDetail);

    const table = $('st-table');
    table.addEventListener('change', (e) => {
        const box = e.target.closest('input[type="checkbox"]');
        if (!box) return;
        if (box.dataset.all != null) {
            for (const f of visibleFiles().filter(isSelectable)) {
                if (box.checked) checked.add(f.index); else checked.delete(f.index);
            }
        } else {
            const i = +box.dataset.index;
            if (box.checked) checked.add(i); else checked.delete(i);
        }
        renderTable();
    });
    table.addEventListener('click', (e) => {
        if (e.target.closest('input')) return;
        const tr = e.target.closest('tr[data-index]');
        if (tr) openDetail(+tr.dataset.index);
    });
}

/** 入力の状態に合わせて、テンプレート欄の有効／無効と「読み込む」ボタンを切り替える */
function syncInputs() {
    const doPrice = $('st-do-price').checked;
    $('st-template-section').classList.toggle('disabled', !doPrice);
    const ready = $('st-folder').value && ($('st-do-info').checked || doPrice) && (!doPrice || $('st-template').value);
    $('st-scan').disabled = !ready;
    $('st-scan').title = ready ? '' : '元データフォルダ（②を行う場合はテンプレートも）を選んでください';
    setStep(scan ? 2 : 1);
}

// ── 読込 ─────────────────────────────────────────────

async function runScan() {
    $('st-log').replaceChildren();
    closeDetail();
    progress(0, 0, '準備中...');
    $('st-progress').classList.remove('hidden');
    $('st-scan').disabled = true;
    setStatus('読み込み中...');
    try {
        const res = await call('scan', {
            sourceFolder: $('st-folder').value,
            includeSubfolders: $('st-subfolders').checked,
            doProjectInfo: $('st-do-info').checked,
            doUnitPrice: $('st-do-price').checked,
            mappingPath: $('st-mapping').value || null,
            templatePath: $('st-template').value || null,
        });
        applyScan(res);
        toast(`読み込みが完了しました（${res.files.length} 件）。一覧で内容を確認してください。`, 'success');
    } catch (e) {
        log('error', e.message);
        toast(e.message, 'error');
        setStatus('読み込みに失敗しました。');
    } finally {
        $('st-progress').classList.add('hidden');
        syncInputs();
    }
}

function onProgress(p) {
    log(p.level, p.message);
    if (p.total > 0) progress(p.done, p.total, `${p.done} / ${p.total}`);
}

function progress(done, total, text) {
    $('st-progress-bar').style.width = total ? `${Math.round((done / total) * 100)}%` : '0';
    $('st-progress-text').textContent = text;
}

const TAGS = { step: '[STEP]', file: '[FILE]', ok: '[OK]', warn: '[WARN]', error: '[ERROR]', done: '[DONE]' };

function log(level, message) {
    const line = document.createElement('div');
    line.className = `lv-${level}`;
    line.innerHTML = `<span class="tag">${TAGS[level] ?? '[INFO]'}</span> ${esc(message)}`;
    $('st-log').appendChild(line);
    $('st-log').scrollTop = $('st-log').scrollHeight;
}

/** 読込結果を反映。初期選択 = 読込できた新規の物件（既存あり・重複・エラーは未選択） */
function applyScan(res) {
    scan = res;
    checked.clear();
    for (const f of res.files) {
        if (isSelectable(f) && !f.existsInDb && !hasDuplicate(f)) checked.add(f.index);
    }
    $('st-reg-info').checked = res.doProjectInfo;
    $('st-reg-info').disabled = !res.doProjectInfo;
    $('st-reg-price').checked = res.doUnitPrice;
    $('st-reg-price').disabled = !res.doUnitPrice;
    renderSummary();
    renderTable();
    setStep(2);
    setStatus(res.dbChecked ? `読込完了 / 登録先: ${res.dbMessage}` : (res.dbMessage ?? '読込完了'));
}

// ── 一覧 ─────────────────────────────────────────────

const isSelectable = (f) => f.isValid;
const hasDuplicate = (f) => f.warnings.some((w) => w.includes('同じ project_id'));

function statusOf(f) {
    if (!f.isValid) return 'error';
    if (!scan.dbChecked) return 'unknown';
    return f.existsInDb ? 'existing' : 'new';
}

function visibleFiles() {
    if (!scan) return [];
    const q = $('st-search').value.trim().toLowerCase();
    return scan.files.filter((f) => {
        const st = statusOf(f);
        if (filter === 'new' && st !== 'new') return false;
        if (filter === 'existing' && st !== 'existing') return false;
        if (filter === 'warn' && !(f.warnings.length && f.isValid)) return false;
        if (filter === 'error' && st !== 'error') return false;
        if (!q) return true;
        return [f.projectId, f.projectName, f.fileName].some((v) => (v ?? '').toLowerCase().includes(q));
    });
}

function renderSummary() {
    if (!scan) return;
    const files = scan.files;
    const valid = files.filter((f) => f.isValid);
    const kpi = (cls, label, value) => `<div class="st-kpi ${cls}"><div class="label">${label}</div><div class="value">${value}</div></div>`;
    $('st-summary').innerHTML = [
        kpi('', '読み込んだファイル', files.length),
        kpi('new', 'DBに無い（新規）', scan.dbChecked ? valid.filter((f) => !f.existsInDb).length : '—'),
        kpi('existing', 'DBに既存あり', scan.dbChecked ? valid.filter((f) => f.existsInDb).length : '—'),
        kpi('error', 'エラー', files.length - valid.length),
        kpi('info', '② 工種コード数', scan.doUnitPrice ? scan.templateCodeCount : '—'),
    ].join('');
}

function renderTable() {
    const table = $('st-table');
    const files = visibleFiles();
    const selectable = files.filter(isSelectable);
    const allOn = selectable.length > 0 && selectable.every((f) => checked.has(f.index));
    table.tHead.innerHTML = `<tr>${COLUMNS.map((c) => c.key === 'check'
        ? `<th><input type="checkbox" data-all ${allOn ? 'checked' : ''} title="表示中をすべて選択／解除"></th>`
        : `<th class="${c.num ? 'num' : ''}">${c.label}</th>`).join('')}</tr>`;

    if (!scan) {
        table.tBodies[0].innerHTML = `<tr><td colspan="${COLUMNS.length}" class="empty-state">まだ読み込んでいません。</td></tr>`;
        updateActions();
        return;
    }
    if (!files.length) {
        table.tBodies[0].innerHTML = `<tr><td colspan="${COLUMNS.length}" class="empty-state">該当するファイルがありません。</td></tr>`;
        updateActions();
        return;
    }

    table.tBodies[0].innerHTML = files.map((f) => {
        const st = statusOf(f);
        const on = checked.has(f.index);
        const badge = st === 'error' ? '<span class="st-badge error">エラー</span>'
            : st === 'existing' ? (on ? '<span class="st-badge overwrite" title="DB の既存データを上書きします">上書き</span>' : '<span class="st-badge existing">既存あり</span>')
            : st === 'new' ? '<span class="st-badge new">新規</span>'
            : '<span class="st-badge unknown">未照合</span>';
        const info = f.projectInfo ?? {};
        const place = [info['現場住所_都道府県'], info['現場住所_市区町村']].filter(Boolean).join(' ');
        const price = f.unitPriceTotal ? `${f.unitPriceMatched} / ${f.unitPriceTotal}` : '—';
        const db = st === 'existing' ? `${f.existingAssessRows} / ${f.existingReviseRows}` : '—';
        const msg = f.error ?? f.warnings.join(' / ');
        return `<tr data-index="${f.index}" class="${st === 'error' ? 'error' : ''}${!on && st !== 'error' ? ' unchecked' : ''}${detailIndex === f.index ? ' selected' : ''}">
            <td>${isSelectable(f) ? `<input type="checkbox" data-index="${f.index}" ${on ? 'checked' : ''}>` : ''}</td>
            <td>${badge}${f.warnings.length && f.isValid ? ' <span class="st-warn-mark" title="警告あり">⚠</span>' : ''}</td>
            <td class="pid">${esc(f.projectId ?? '—')}</td>
            <td class="name" title="${esc(f.projectName ?? '')}">${esc(f.projectName ?? '—')}</td>
            <td>${esc(place || '—')}</td>
            <td class="num">${esc(display(info['施工床面積']))}</td>
            <td class="num">${price}</td>
            <td class="num">${db}</td>
            <td class="name" title="${esc(f.filePath)}">${esc(f.fileName)}</td>
            <td class="msg" title="${esc(msg)}">${esc(msg)}</td>
        </tr>`;
    }).join('');
    updateActions();
}

function updateActions() {
    const files = scan ? scan.files.filter((f) => checked.has(f.index)) : [];
    const overwrite = files.filter((f) => f.existsInDb).length;
    const doInfo = $('st-reg-info').checked;
    const doPrice = $('st-reg-price').checked;
    $('st-actions-info').innerHTML = scan
        ? `登録対象: <strong>${files.length}</strong> 件（新規 ${files.length - overwrite} / 上書き ${overwrite}）`
        : '登録対象: 0 件';
    $('st-register').disabled = !scan || !scan.dbChecked || files.length === 0 || (!doInfo && !doPrice);
    $('st-register').title = scan && !scan.dbChecked ? 'DB に接続できないため登録できません（Excel 保存は可能）' : '';
    $('st-register').textContent = files.length ? `🗄 DBに登録（${files.length}件）` : '🗄 DBに登録';
    $('st-export').disabled = !scan || !scan.files.some((f) => f.isValid);
}

// ── 詳細 ─────────────────────────────────────────────

function openDetail(index) {
    const f = scan.files.find((x) => x.index === index);
    if (!f) return;
    detailIndex = index;
    const info = f.projectInfo ?? {};
    const rest = Object.keys(info).filter((k) => !DETAIL_KEYS.includes(k) && info[k] != null && info[k] !== 0 && info[k] !== '');
    const row = (k) => {
        const v = info[k];
        return `<dt>${esc(k)}</dt><dd class="${typeof v === 'number' ? 'num' : ''}">${esc(v == null || v === '' ? '—' : display(v))}</dd>`;
    };
    $('st-detail-title').textContent = f.projectName ?? f.fileName;
    $('st-detail-body').innerHTML = `
        <div class="st-detail-file">${esc(f.filePath)}</div>
        ${f.error ? `<div class="st-detail-warn error">${esc(f.error)}</div>` : ''}
        ${f.warnings.map((w) => `<div class="st-detail-warn">${esc(w)}</div>`).join('')}
        ${f.projectInfo ? `<dl class="st-fields">${DETAIL_KEYS.filter((k) => k in info).map(row).join('')}${rest.map(row).join('')}</dl>` : '<p class="st-hint">① 物件諸情報はありません。</p>'}
        ${f.unitPriceTotal ? `<p class="st-hint">② 単価: テンプレートの工種コード ${f.unitPriceTotal} 件のうち ${f.unitPriceMatched} 件が積算予算に見つかりました（見つからない工種コードは 0 で登録）。</p>` : ''}`;
    $('st-detail').classList.remove('hidden');
    document.querySelector('.st-body').classList.add('with-detail');
    renderTable();
}

function closeDetail() {
    detailIndex = null;
    $('st-detail').classList.add('hidden');
    document.querySelector('.st-body').classList.remove('with-detail');
    if (scan) renderTable();
}

// ── 実行 ─────────────────────────────────────────────

async function exportExcel() {
    const res = await call('pickPath', { kind: 'folder', title: 'Excel の保存先フォルダを選択', initial: $('st-folder').value });
    if (!res.path) return;
    try {
        const out = await withLoading('Excel に保存しています...', () => call('exportExcel', { outputFolder: res.path }));
        log('done', `Excel を保存しました: ${out.files.join(' / ')}`);
        toast(`Excel を保存しました（${out.files.length} ファイル）`, 'success', { label: 'フォルダを開く', onClick: () => call('openFolder', { path: res.path }) });
        setStep(3);
    } catch (e) {
        toast(`Excel の保存に失敗しました: ${e.message}`, 'error');
        log('error', e.message);
    }
}

async function register() {
    const files = scan.files.filter((f) => checked.has(f.index));
    const overwrite = files.filter((f) => f.existsInDb);
    const doInfo = $('st-reg-info').checked;
    const doPrice = $('st-reg-price').checked;
    const lines = [
        `${files.length} 件の物件を DB に登録します。`,
        '',
        `　新規: ${files.length - overwrite.length} 件`,
        `　上書き: ${overwrite.length} 件${overwrite.length ? '　← 既存のデータを置き換えます' : ''}`,
        '',
        `　① 物件情報: ${doInfo ? '登録する（既存は更新）' : 'しない'}`,
        `　② 査定・修正単価: ${doPrice ? `登録する（その物件の既存行を削除して入れ替え。1件あたり ${scan.templateCodeCount} 行）` : 'しない'}`,
        '',
        '1件でも失敗した場合は、すべて取り消します。よろしいですか？',
    ];
    if (overwrite.length) lines.splice(5, 0, `　上書きする物件: ${overwrite.slice(0, 8).map((f) => f.projectId).join('、')}${overwrite.length > 8 ? ' ほか' : ''}`);
    if (!confirm(lines.join('\n'))) return;

    try {
        const res = await withLoading('DB に登録しています...', () => call('register', {
            indexes: files.map((f) => f.index), doProjectInfo: doInfo, doUnitPrice: doPrice,
        }));
        const r = res.result;
        log('done', `DB 登録完了: 物件 新規 ${r.projectsInserted} / 更新 ${r.projectsUpdated}、査定単価 ${r.assessRows} 行（削除 ${r.assessDeleted}）、修正単価 ${r.reviseRows} 行（削除 ${r.reviseDeleted}）`);
        for (const w of r.warnings) log('warn', w);
        toast('DB に登録しました。メイン画面で「データ読込」をすると分析に反映されます。', 'success', null, 8000);
        applyScan(res.scan);
        setStep(3);
    } catch (e) {
        log('error', e.message);
        toast(e.message, 'error', null, 12000);
    }
}

function setStep(n) {
    for (const el of document.querySelectorAll('.st-step')) {
        const s = +el.dataset.step;
        el.classList.toggle('active', s === n);
        el.classList.toggle('done', s < n);
    }
}
