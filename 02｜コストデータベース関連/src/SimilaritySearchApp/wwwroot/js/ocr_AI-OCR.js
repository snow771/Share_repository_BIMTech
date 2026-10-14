// js/ocr_AI-OCR.js
// 見積AI-OCRウィンドウ（ocr.html）の画面制御。
//   ① 見積PDF・保存先・構造化ルールを選ぶ（実行前に、今日の利用枚数がいくつになるかを表示）
//   ② 実行: PDF を2ページずつ Azure で OCR → 全ページ統合 → 構造化（進捗・ログ・利用枚数を逐次更新）
//   ③ 結果の一覧から PDF を選ぶと、構造化明細／要確認／OCR行監査 をプレビュー。Excel・フォルダを開ける
// 利用枚数メーター: 上限（既定 500 枚）に対し、注意値（300）超で黄、警告値（400）超で赤、上限で赤の点滅。

import { on } from './state_画面状態.js';
import { call, isMock } from './bridge_通信.js';
import { toast, withLoading, setStatus, bindSegment, setSegment } from './ui_共通部品.js';
import { esc } from './format_数値書式.js';
import { initEstimateEntry, updateEstimateButton } from './ocrEstimate_見積DB取込.js';

const $ = (id) => document.getElementById(id);

const LEVEL_TEXT = { safe: '余裕あり', caution: '注意', danger: '残りわずか', over: '上限到達' };
const STATUS_BADGE = {
    ok: '<span class="st-badge ok">完了</span>',
    warn: '<span class="st-badge warn" title="PDF と Azure のページ数が一致しません">ページ警告</span>',
    error: '<span class="st-badge error">失敗</span>',
    canceled: '<span class="st-badge canceled">中止</span>',
};
const DETAIL_LIMIT = 1000;   // プレビューに出す行数（全件は Excel で確認）

let usage = null;        // C# の OcrUsageView
let azure = null;        // Azure 設定の状態
let defaultCommon = null;
let pdfs = [];           // { path, name, pages, error }
let jobs = [];           // 処理結果（新しいものが下）
let running = false;
let detail = null;       // { job, items, audit }
let detailTab = 'items';
let period = 'day';

init();

async function init() {
    bindInputs();
    initEstimateEntry({ selectedJob: () => detail?.job ?? null });   // 見積DB連携の入口（取込・確認ウィンドウ）
    updateEstimateButton(null);
    on('bridge:ocrProgress', onProgress);
    on('bridge:ocrUsage', renderUsage);
    if (isMock) toast('ブラウザ確認モード: サンプルデータで動作しています（Azure には接続しません）。');
    try {
        const s = await call('getOcrState');
        const st = s.settings;
        defaultCommon = s.defaultCommonRules;
        $('ocr-output').value = st.outputFolder ?? '';
        $('ocr-common').value = st.commonRulesPath || defaultCommon || '';
        $('ocr-supplier').value = st.supplierRulesPath ?? '';
        $('ocr-do-structure').checked = st.doStructure ?? true;
        $('ocr-keep-json').checked = st.keepRawJson ?? true;
        $('ocr-keep-excel').checked = st.keepRawExcel ?? true;
        $('ocr-keep-chunks').checked = !!st.keepChunks;
        applyAzure(s.azure);
        renderUsage(s.usage);
        jobs = s.results ?? [];
    } catch (e) {
        toast(`初期化に失敗しました: ${e.message}`, 'error');
    }
    syncInputs();
    renderPdfs();
    renderResults();
}

// ── 入力 ─────────────────────────────────────────────

function bindInputs() {
    $('ocr-add').addEventListener('click', addPdfs);
    $('ocr-clear').addEventListener('click', () => {
        pdfs = [];
        renderPdfs();
    });
    $('ocr-pdf-list').addEventListener('click', (e) => {
        const btn = e.target.closest('button[data-remove]');
        if (!btn) return;
        pdfs.splice(+btn.dataset.remove, 1);
        renderPdfs();
    });
    $('ocr-output-pick').addEventListener('click', async () => {
        const res = await call('pickPath', { kind: 'folder', title: '保存先フォルダを選択', initial: $('ocr-output').value || pdfs[0]?.path });
        if (res.path) $('ocr-output').value = res.path;
        syncInputs();
    });
    $('ocr-common-pick').addEventListener('click', () => pickRules('ocr-common', '共通ルール JSON を選択'));
    $('ocr-common-default').addEventListener('click', () => {
        $('ocr-common').value = defaultCommon ?? '';
        syncInputs();
    });
    $('ocr-supplier-pick').addEventListener('click', () => pickRules('ocr-supplier', '協力業者差分ルール JSON を選択'));
    $('ocr-supplier-clear').addEventListener('click', () => { $('ocr-supplier').value = ''; });
    $('ocr-do-structure').addEventListener('change', syncInputs);

    $('ocr-run').addEventListener('click', run);
    $('ocr-cancel').addEventListener('click', cancel);
    $('ocr-log-clear').addEventListener('click', () => $('ocr-log').replaceChildren());
    $('ocr-restructure').addEventListener('click', restructure);

    $('ocr-results').addEventListener('click', (e) => {
        const btn = e.target.closest('button[data-open]');
        if (btn) {
            e.stopPropagation();
            openPath(btn.dataset.open);
            return;
        }
        const tr = e.target.closest('tr[data-index]');
        if (tr) showDetail(+tr.dataset.index);
    });
    bindSegment($('ocr-detail-tabs'), (v) => {
        detailTab = v;
        renderDetail();
    });
    $('ocr-detail-close').addEventListener('click', closeDetail);

    // Azure 設定
    $('ocr-settings-btn').addEventListener('click', openSettings);
    const modal = $('ocr-settings-modal');
    for (const el of modal.querySelectorAll('[data-close]')) el.addEventListener('click', closeSettings);
    modal.addEventListener('click', (e) => { if (e.target === modal) closeSettings(); });
    document.addEventListener('keydown', (e) => {
        if (e.key === 'Escape' && !modal.classList.contains('hidden')) closeSettings();
    });
    bindSegment($('az-period'), (v) => { period = v; });
    $('az-test').addEventListener('click', testAzure);
    $('ocr-settings-form').addEventListener('submit', saveSettings);
}

async function addPdfs() {
    try {
        const res = await withLoading('PDF のページ数を確認しています...', () => call('pickPdfs', {
            title: '見積PDFを選択（複数選択可）', filter: 'PDF (*.pdf)|*.pdf', initial: pdfs.at(-1)?.path,
        }));
        const known = new Set(pdfs.map((p) => p.path.toLowerCase()));
        for (const f of res.files) {
            if (!known.has(f.path.toLowerCase())) pdfs.push(f);
        }
        if (pdfs.length && !$('ocr-output').value) {
            const dir = pdfs[0].path.replace(/[\\/][^\\/]*$/, '');
            $('ocr-output').value = `${dir}\\OCR_結果`;
        }
        renderPdfs();
    } catch (e) {
        toast(e.message, 'error');
    }
}

async function pickRules(id, title) {
    const res = await call('pickPath', { kind: 'open', title, filter: 'JSON (*.json)|*.json', initial: $(id).value || defaultCommon });
    if (res.path) $(id).value = res.path;
    syncInputs();
}

const totalPages = () => pdfs.reduce((s, p) => s + (p.pages ?? 0), 0);
const readyPdfs = () => pdfs.filter((p) => !p.error);

function renderPdfs() {
    $('ocr-pdf-list').innerHTML = pdfs.map((p, i) => `
        <li class="${p.error ? 'error' : ''}" title="${esc(p.error ?? p.path)}">
            <span class="name">${esc(p.name)}</span>
            <span class="pages">${p.error ? '読めません' : `${p.pages} 頁`}</span>
            <button type="button" data-remove="${i}" title="一覧から外す">×</button>
        </li>`).join('');
    $('ocr-pdf-count').textContent = `${pdfs.length} 件 / ${totalPages().toLocaleString('ja-JP')} ページ`;
    renderForecast();
    syncInputs();
}

/** 実行した場合の利用枚数の見込み */
function renderForecast() {
    const el = $('ocr-forecast');
    if (!usage || !readyPdfs().length) {
        el.textContent = '';
        el.className = 'ocr-forecast';
        return;
    }
    const after = usage.used + totalPages();
    const level = levelOf(after);
    el.className = `ocr-forecast ${level}`;
    el.textContent = after > usage.limit
        ? `⚠ 実行すると ${usage.periodLabel} ${after.toLocaleString('ja-JP')} 枚になり、上限 ${usage.limit} 枚を ${after - usage.limit} 枚超えます。`
        : `実行後の見込み: ${usage.periodLabel} ${after.toLocaleString('ja-JP')} / ${usage.limit} 枚（残り ${usage.limit - after} 枚）`;
}

function levelOf(used) {
    if (used >= usage.limit) return 'over';
    if (used > usage.dangerAt) return 'danger';
    if (used > usage.warnAt) return 'caution';
    return 'safe';
}

function syncInputs() {
    const doStructure = $('ocr-do-structure').checked;
    $('ocr-rules-section').classList.toggle('disabled', !doStructure);
    const ready = readyPdfs().length > 0 && $('ocr-output').value && (!doStructure || $('ocr-common').value) && azure?.source !== '未設定';
    $('ocr-run').disabled = running || !ready;
    $('ocr-run').textContent = readyPdfs().length ? `▶ OCR・構造化を実行（${readyPdfs().length} 件・${totalPages()} ページ）` : '▶ OCR・構造化を実行';
    $('ocr-run').title = azure?.source === '未設定' ? '先に「⚙ Azure設定」でエンドポイントとキーを設定してください'
        : ready ? '' : 'PDF・保存先（構造化する場合は共通ルールも）を選んでください';
    setStep(running ? 2 : jobs.length ? 3 : 1);
}

// ── 利用枚数 ─────────────────────────────────────────

function renderUsage(u) {
    if (!u) return;
    usage = u;
    const meter = $('ocr-meter');
    meter.className = `ocr-meter ${u.level}`;
    $('ocr-meter-label').textContent = `${u.periodLabel}の AI-OCR 処理枚数`;
    $('ocr-meter-state').textContent = LEVEL_TEXT[u.level] ?? '';
    $('ocr-meter-used').textContent = u.used.toLocaleString('ja-JP');
    $('ocr-meter-limit').textContent = u.limit.toLocaleString('ja-JP');
    $('ocr-meter-bar').style.width = `${Math.min(100, (u.used / u.limit) * 100)}%`;
    $('ocr-mark-warn').style.left = `${Math.min(100, (u.warnAt / u.limit) * 100)}%`;
    $('ocr-mark-danger').style.left = `${Math.min(100, (u.dangerAt / u.limit) * 100)}%`;
    $('ocr-meter-sub').textContent = `この起動から ${u.sessionPages.toLocaleString('ja-JP')} 枚・PDF ${u.sessionFiles} 件 ／ 残り ${u.remaining.toLocaleString('ja-JP')} 枚`;
    meter.title = `${u.periodLabel}: ${u.used} 枚（PDF ${u.files} 件）\n黄色: ${u.warnAt} 枚超 / 赤: ${u.dangerAt} 枚超 / 上限: ${u.limit} 枚\n\n直近の記録:\n`
        + (u.recent.length ? u.recent.map((d) => `${d.date}  ${d.pages} 枚（PDF ${d.files} 件）`).join('\n') : 'まだありません');

    const alert = $('ocr-alert');
    if (u.level === 'safe') {
        alert.classList.add('hidden');
    } else {
        alert.className = `ocr-alert ${u.level}`;
        alert.textContent = u.level === 'over'
            ? `⛔ ${u.periodLabel}の処理枚数が上限 ${u.limit} 枚に達しました（${u.used} 枚）。無料枠を超える可能性があるため、続ける場合は十分注意してください。`
            : u.level === 'danger'
                ? `⚠ ${u.periodLabel}の処理枚数が ${u.dangerAt} 枚を超えました（${u.used} / ${u.limit} 枚・残り ${u.remaining} 枚）。`
                : `⚠ ${u.periodLabel}の処理枚数が ${u.warnAt} 枚を超えました（${u.used} / ${u.limit} 枚・残り ${u.remaining} 枚）。`;
    }
    renderForecast();
}

// ── 実行 ─────────────────────────────────────────────

async function run() {
    const targets = readyPdfs();
    const pages = targets.reduce((s, p) => s + p.pages, 0);
    const after = usage ? usage.used + pages : pages;
    const lines = [
        `${targets.length} 件の PDF（合計 ${pages} ページ）を Azure AI-OCR で読み取ります。`,
        usage ? `${usage.periodLabel}の処理枚数: ${usage.used} → ${after} 枚（上限 ${usage.limit} 枚）` : '',
        $('ocr-do-structure').checked ? '読み取った後、構造化 Excel・JSON を作成します。' : '構造化はしません（OCR 結果だけ保存）。',
    ];
    if (usage && after > usage.limit) {
        lines.push('', `⚠ 上限 ${usage.limit} 枚を ${after - usage.limit} 枚超えます。無料枠を超える可能性があります。`);
    }
    lines.push('', '実行しますか？');
    if (!confirm(lines.filter((l) => l !== null).join('\n'))) return;

    $('ocr-log').replaceChildren();
    closeDetail();
    running = true;
    syncInputs();
    progress(0, 0, '準備中...');
    $('ocr-progress').classList.remove('hidden');
    $('ocr-cancel').classList.remove('hidden');
    $('ocr-cancel').disabled = false;
    log('step', `開始: ${new Date().toLocaleString('ja-JP')}`);
    setStatus('OCR 実行中...');
    try {
        const res = await call('run', {
            pdfs: targets.map((p) => p.path),
            outputFolder: $('ocr-output').value,
            commonRulesPath: $('ocr-common').value,
            supplierRulesPath: $('ocr-supplier').value || null,
            doStructure: $('ocr-do-structure').checked,
            keepRawJson: $('ocr-keep-json').checked,
            keepRawExcel: $('ocr-keep-excel').checked,
            keepChunks: $('ocr-keep-chunks').checked,
        });
        jobs = jobs.concat(res.jobs);
        renderUsage(res.usage);
        renderResults();
        const ok = res.jobs.filter((j) => j.status === 'ok' || j.status === 'warn').length;
        const ng = res.jobs.length - ok;
        toast(`処理が終了しました: 成功 ${ok} 件${ng ? ` / 失敗・中止 ${ng} 件` : ''}`, ng ? 'error' : 'success', null, 8000);
        setStatus(`完了: 成功 ${ok} 件 / 失敗・中止 ${ng} 件`);
        const first = res.jobs.find((j) => j.summary);
        if (first) showDetail(first.index);
    } catch (e) {
        log('error', e.message);
        toast(e.message, 'error', null, 10000);
        setStatus('実行できませんでした。');
    } finally {
        running = false;
        $('ocr-progress').classList.add('hidden');
        $('ocr-cancel').classList.add('hidden');
        syncInputs();
    }
}

async function cancel() {
    $('ocr-cancel').disabled = true;
    log('warn', '中止を受け付けました。処理中の PDF が終わる前に止めます...');
    await call('cancel');
}

function onProgress(p) {
    log(p.level, p.message);
    if (p.total > 0) progress(p.done, p.total, `${p.done} / ${p.total} ブロック`);
}

function progress(done, total, text) {
    $('ocr-progress-bar').style.width = total ? `${Math.round((done / total) * 100)}%` : '0';
    $('ocr-progress-text').textContent = text;
}

const TAGS = { step: '[STEP]', file: '[OCR]', ok: '[OK]', warn: '[WARN]', error: '[ERROR]', done: '[DONE]' };

function log(level, message) {
    const line = document.createElement('div');
    line.className = `lv-${level}`;
    line.innerHTML = `<span class="tag">${TAGS[level] ?? '[INFO]'}</span> ${esc(message)}`;
    $('ocr-log').appendChild(line);
    $('ocr-log').scrollTop = $('ocr-log').scrollHeight;
}

/** OCR 済みの Layout JSON から構造化だけやり直す（Azure を使わない＝利用枚数は増えない） */
async function restructure() {
    if (!$('ocr-common').value) {
        toast('先に共通ルール JSON を選んでください。', 'error');
        return;
    }
    const pick = await call('pickPath', {
        kind: 'open', title: 'OCR 済みの JSON（〜_layout_raw_full.json）を選択',
        filter: 'OCR JSON (*_layout_raw*.json)|*_layout_raw*.json|JSON (*.json)|*.json', initial: $('ocr-output').value,
    });
    if (!pick.path) return;
    try {
        const res = await withLoading('構造化しています...', () => call('restructure', {
            rawJsonPath: pick.path, commonRulesPath: $('ocr-common').value, supplierRulesPath: $('ocr-supplier').value || null,
        }));
        jobs.push(res.job);
        renderResults();
        log('done', `構造化だけやり直しました: ${res.job.structuredExcelPath}`);
        toast('構造化をやり直しました（Azure は使っていないので、利用枚数は増えていません）。', 'success', { label: 'Excelを開く', onClick: () => openPath(res.job.structuredExcelPath) }, 8000);
        showDetail(res.job.index);
    } catch (e) {
        toast(`構造化に失敗しました: ${e.message}`, 'error', null, 10000);
        log('error', e.message);
    }
}

// ── 結果 ─────────────────────────────────────────────

const yen = (v) => (v == null ? '—' : Math.round(v).toLocaleString('ja-JP'));

function renderResults() {
    const table = $('ocr-results');
    table.tHead.innerHTML = `<tr>
        <th>状態</th><th>PDF</th><th class="num">頁（PDF/OCR）</th><th>協力業者</th><th>工事名</th>
        <th class="num">明細（金額/参考）</th><th class="num">検算OK</th><th class="num">重複候補</th><th class="num">要確認</th>
        <th class="num">明細合計</th><th class="num">見積金額</th><th class="num">差額</th><th>開く</th></tr>`;
    if (!jobs.length) {
        table.tBodies[0].innerHTML = '<tr><td colspan="13" class="empty-state">左で PDF を選んで「▶ OCR・構造化を実行」を押すと、ここに結果が並びます。<br>OCR 済みの JSON がある場合は、右上のボタンで構造化だけやり直せます（利用枚数は増えません）。</td></tr>';
        return;
    }
    table.tBodies[0].innerHTML = [...jobs].reverse().map((j) => {
        const s = j.summary;
        const m = j.metadata;
        const diff = s?.differenceToHeaderAmount;
        const open = (path, label, title) => (path ? `<button type="button" data-open="${esc(path)}" title="${esc(title)}">${label}</button>` : '');
        return `<tr data-index="${j.index}" class="${j.status === 'canceled' ? 'canceled' : ''}${detail?.job.index === j.index ? ' selected' : ''}" title="${esc(j.error ?? j.warnings.join(' / '))}">
            <td>${STATUS_BADGE[j.status] ?? ''}</td>
            <td class="name" title="${esc(j.pdfPath)}">${esc(j.pdfName)}</td>
            <td class="num">${j.pdfPages ?? '—'} / ${j.ocrPages ?? '—'}</td>
            <td class="name">${esc(m?.supplierName || '—')}</td>
            <td class="name">${esc(m?.projectName || (j.error ? j.error : '—'))}</td>
            <td class="num">${s ? `${s.detailCount}（${s.moneyDetailCount}/${s.referenceDetailCount}）` : '—'}</td>
            <td class="num">${s ? s.amountCheckOkCount : '—'}</td>
            <td class="num">${s ? s.duplicateCandidateCount : '—'}</td>
            <td class="num">${s ? s.reviewRequiredCount : '—'}</td>
            <td class="num">${s ? yen(s.detailAmountTotal) : '—'}</td>
            <td class="num">${s ? yen(s.headerEstimateAmount) : '—'}</td>
            <td class="num ${diff ? 'ocr-diff-ng' : ''}">${s ? yen(diff) : '—'}</td>
            <td class="actions">${open(j.structuredExcelPath, '📊 構造化Excel', j.structuredExcelPath)}${open(j.checkExcelPath, '🔎 OCR確認', j.checkExcelPath)}${open(j.jobFolder, '📂 フォルダ', j.jobFolder)}</td>
        </tr>`;
    }).join('');
}

async function openPath(path) {
    try {
        await call('openPath', { path });
    } catch (e) {
        toast(e.message, 'error');
    }
}

async function showDetail(index) {
    try {
        detail = await call('jobDetail', { index });
    } catch (e) {
        toast(e.message, 'error');
        return;
    }
    $('ocr-detail').classList.remove('hidden');
    const j = detail.job;
    const s = j.summary;
    const m = j.metadata;
    $('ocr-detail-title').textContent = j.pdfName;
    $('ocr-detail-meta').innerHTML = s
        ? `<span>協力業者: <b>${esc(m.supplierName || '—')}</b></span><span>工事名: <b>${esc(m.projectName || '—')}</b></span><span>見積ID: <b>${esc(m.estimateId || '—')}</b></span>
           <span>明細合計: <b>${yen(s.detailAmountTotal)}</b> 円</span><span>見積金額: <b>${yen(s.headerEstimateAmount)}</b> 円</span>
           <span>差額: <b class="${s.differenceToHeaderAmount ? 'ocr-diff-ng' : ''}">${yen(s.differenceToHeaderAmount)}</b></span>
           <span>OCR行監査: ${s.auditRowCount} 行（除外 ${s.auditExcludedCount}）</span>`
        : `<span>${esc(j.error ?? '構造化していません（OCR 結果だけ保存）。')}</span>`;
    renderResults();
    renderDetail();
    updateEstimateButton(detail.job);
}

function closeDetail() {
    detail = null;
    $('ocr-detail').classList.add('hidden');
    renderResults();
    updateEstimateButton(null);
}

function renderDetail() {
    if (!detail) return;
    const table = $('ocr-detail-table');
    if (detailTab === 'audit') {
        table.tHead.innerHTML = '<tr><th class="num">頁</th><th class="num">表</th><th class="num">行</th><th>判定</th><th>理由</th><th>OCR行原文</th></tr>';
        const rows = detail.audit.slice(0, DETAIL_LIMIT);
        table.tBodies[0].innerHTML = rows.length ? rows.map((a) => `
            <tr class="${a.rowStatus === '除外' ? 'review' : a.rowStatus === '見出し' || a.rowStatus === '空行' ? 'reference' : ''}">
                <td class="num">${a.pdfPage ?? ''}</td><td class="num">${a.ocrTableIndex}</td><td class="num">${a.ocrRowIndex}</td>
                <td>${esc(a.rowStatus)}</td><td>${esc(a.reason)}</td><td class="wrap">${esc(a.rowText)}</td>
            </tr>`).join('') + more(detail.audit.length)
            : '<tr><td colspan="6" class="empty-state">OCR行監査はありません。</td></tr>';
        return;
    }
    table.tHead.innerHTML = `<tr><th class="num">頁</th><th>種別</th><th>工種</th><th>区分</th><th>名称</th><th>材料・仕様</th><th>寸法・規格</th>
        <th class="num">数量</th><th>単位</th><th class="num">単価</th><th class="num">金額</th><th>検算</th><th>重複候補</th><th>要確認理由</th></tr>`;
    const items = detailTab === 'review' ? detail.items.filter((r) => r.reviewRequired) : detail.items;
    const rows = items.slice(0, DETAIL_LIMIT);
    const n = (v, d = 0) => (v == null ? '' : v.toLocaleString('ja-JP', { maximumFractionDigits: d }));
    table.tBodies[0].innerHTML = rows.length ? rows.map((r) => `
        <tr class="${r.recordType === '参考明細' ? 'reference' : r.reviewRequired ? 'review' : ''}">
            <td class="num">${r.pdfPage ?? ''}</td><td>${esc(r.recordType)}</td><td>${esc(r.trade)}</td><td>${esc(r.section)}</td>
            <td class="wrap">${esc(r.itemName)}</td><td class="wrap">${esc(r.material)}</td><td class="wrap">${esc(r.dimension)}</td>
            <td class="num">${n(r.quantity, 2)}</td><td>${esc(r.unit)}</td><td class="num">${n(r.unitPrice)}</td><td class="num">${n(r.amount)}</td>
            <td>${r.amountCheck === '要確認' ? `<span class="ocr-diff-ng" title="差額 ${n(r.difference)}">要確認</span>` : esc(r.amountCheck)}</td>
            <td>${esc(r.duplicateCandidate)}</td><td class="wrap">${esc(r.reviewReason)}</td>
        </tr>`).join('') + more(items.length)
        : `<tr><td colspan="14" class="empty-state">${detailTab === 'review' ? '要確認の明細はありません。' : '明細はありません。'}</td></tr>`;
}

function more(total) {
    return total > DETAIL_LIMIT
        ? `<tr><td colspan="14" class="empty-state">先頭 ${DETAIL_LIMIT} 行を表示しています（全 ${total} 行は構造化 Excel で確認してください）。</td></tr>`
        : '';
}

// ── Azure 設定 ───────────────────────────────────────

function applyAzure(a) {
    azure = a;
    const badge = $('ocr-azure-badge');
    badge.classList.toggle('connected', a.source !== '未設定');
    badge.textContent = a.source === '未設定' ? 'Azure 未設定' : `Azure: ${a.source}`;
    badge.title = a.source === '未設定' ? '「⚙ Azure設定」でエンドポイントとキーを設定してください' : `接続先: ${a.effectiveEndpoint}`;
    period = a.period;
    syncInputs();
}

function openSettings() {
    $('az-endpoint').value = azure?.endpoint ?? '';
    $('az-key').value = '';
    $('az-key').placeholder = azure?.hasKey ? '保存済み（変更する場合のみ入力）' : '未設定';
    $('az-source').textContent = azure?.source === '環境変数'
        ? `いまは環境変数 DI_ENDPOINT / DI_KEY を使っています（${azure.effectiveEndpoint}）。ここで入力するとこちらが優先されます。`
        : 'キーは Windows のユーザーごとに暗号化して保存します。空欄のままにすると環境変数 DI_ENDPOINT / DI_KEY を使います。';
    $('az-chunk').value = azure?.chunkPages ?? 2;
    $('az-limit').value = azure?.limit ?? 500;
    $('az-warn').value = azure?.warnAt ?? 300;
    $('az-danger').value = azure?.dangerAt ?? 400;
    period = azure?.period ?? 'day';
    setSegment($('az-period'), period);
    $('az-result').textContent = '';
    $('ocr-settings-modal').classList.remove('hidden');
}

function closeSettings() {
    $('ocr-settings-modal').classList.add('hidden');
    $('az-key').value = '';
}

function collectSettings() {
    const key = $('az-key').value;
    return {
        endpoint: $('az-endpoint').value.trim(),
        key: key === '' ? null : key,   // 空欄は「変更しない」
        chunkPages: parseInt($('az-chunk').value, 10) || 2,
        limit: parseInt($('az-limit').value, 10) || 500,
        warnAt: parseInt($('az-warn').value, 10) || 0,
        dangerAt: parseInt($('az-danger').value, 10) || 0,
        period,
    };
}

async function testAzure() {
    const el = $('az-result');
    try {
        const res = await withLoading('Azure に接続しています...', () => call('testAzure', collectSettings()));
        el.className = 'test-result ok';
        el.textContent = `✔ ${res.message}`;
    } catch (e) {
        el.className = 'test-result ng';
        el.textContent = `✖ ${e.message}`;
    }
}

async function saveSettings(e) {
    e.preventDefault();
    const v = collectSettings();
    if (v.warnAt > v.dangerAt || v.dangerAt > v.limit) {
        toast('「黄色 ≦ 赤 ≦ 上限」になるように入力してください。', 'error');
        return;
    }
    try {
        const res = await call('saveAzureSettings', v);
        applyAzure(res.azure);
        renderUsage(res.usage);
        closeSettings();
        toast('Azure の設定を保存しました。', 'success');
    } catch (err) {
        toast(`保存に失敗しました: ${err.message}`, 'error');
    }
}

function setStep(n) {
    for (const el of document.querySelectorAll('.st-step')) {
        const s = +el.dataset.step;
        el.classList.toggle('active', s === n);
        el.classList.toggle('done', s < n);
    }
}
