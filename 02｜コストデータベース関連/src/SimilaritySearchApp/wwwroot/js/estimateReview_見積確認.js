// js/estimateReview_見積確認.js
// 見積確認・取決めウィンドウ（estimate-review.html）。
//   一覧モード（?id なし）: DB に保存した見積を検索して開く
//   詳細モード（?id=見積ID）: 物件・業者の紐づけ／明細の確認・補正／原資料（PDF・Excel）／取決めの入力・プレビュー・確定・訂正
// 金額の計算・確定条件の判定はすべて C#（AgreementCalculator）で行い、ここでは表示と入力だけを扱う。
// 数値は文字列のまま C# へ送る（JavaScript の小数誤差を持ち込まない）。

import { call, isMock } from './bridge_通信.js';
import { toast, withLoading, setStatus, bindSegment, setSegment } from './ui_共通部品.js';
import { esc } from './format_数値書式.js';

const $ = (id) => document.getElementById(id);

const STATUS_LABEL = { Draft: '下書き', Confirmed: '確定', Superseded: '旧版（差し替え済）' };
const TAX_LABEL = { TaxExcluded: '税抜', TaxIncluded: '税込' };
const REVIEW = { pending: '未確認', done: '確認済', excluded: '計算対象外' };
const BASE = { header: '表紙金額', detail: '明細合計', input: '確認入力' };

let ctx = null;            // { mode, headerId, operator }
let detail = null;         // C# の EstimateDetail
let tab = 'lines';
let selectedLine = null;   // 選択中の明細ID
let link = { projectId: null, supplierId: null };
let preview = null;        // 直近の計算プレビュー
let confirmKey = null;     // 確定要求キー（再送・二重クリックで同じ確定にする）
let pdfDocId = null;
const dirty = { link: false, line: false, agreement: false };
let listState = 'all';

init();

async function init() {
    try {
        ctx = await call('getContext');
    } catch (e) {
        toast(`初期化に失敗しました: ${e.message}`, 'error');
        return;
    }
    if (isMock && !location.search.includes('id=')) ctx = { ...ctx, mode: 'list' };
    $('er-operator').textContent = `利用者: ${ctx.operator}`;
    $('er-reload').addEventListener('click', () => (ctx.mode === 'list' ? loadList() : reload()));
    if (ctx.mode === 'list') {
        initList();
        await loadList();
    } else {
        initDetail();
        await reload();
    }
}

function setDirty(key, value) {
    dirty[key] = value;
    window.__estimateDirty = Object.values(dirty).some(Boolean);   // ウィンドウを閉じるときの確認に使う（C# が読む）
}

// =====================================================================
// 一覧モード
// =====================================================================

function initList() {
    $('er-list').classList.remove('hidden');
    $('er-title').textContent = '🗂 保存済み見積（見積DB）';
    $('er-subtitle').textContent = 'SAVED ESTIMATES';
    document.title = '保存済み見積';
    bindSegment($('er-state'), (v) => {
        listState = v;
        loadList();
    });
    $('er-list-form').addEventListener('submit', (e) => {
        e.preventDefault();
        loadList();
    });
    $('er-include-test').addEventListener('change', loadList);
    window.addEventListener('focus', loadList);   // 確認ウィンドウで確定・削除したあとに戻ったとき
    $('er-list-table').addEventListener('click', async (e) => {
        const tr = e.target.closest('tr[data-id]');
        if (!tr) return;
        try {
            await call('openEstimate', { headerId: +tr.dataset.id });
        } catch (err) {
            toast(err.message, 'error');
        }
    });
}

async function loadList() {
    try {
        const rows = await withLoading('見積を検索しています...', () => call('list', {
            keyword: $('er-kw').value.trim() || null, state: listState, includeTest: $('er-include-test').checked, limit: 300,
        }));
        $('er-list-table').tBodies[0].innerHTML = rows.length ? rows.map((r) => `
            <tr data-id="${r.headerId}" title="クリックで確認・取決めウィンドウを開く">
                <td class="num">#${r.headerId}</td>
                <td>${stateBadge(r.state)}${r.isTestData ? ' <span class="st-badge unknown">試験</span>' : ''}</td>
                <td>${esc(dateText(r.importedAt))}</td>
                <td class="name">${esc(r.supplierNameOcr ?? '—')}</td>
                <td class="name">${esc(r.projectNameOcr ?? '—')}</td>
                <td class="name">${r.projectId ? `${esc(r.projectId)} ${esc(r.projectName ?? '')}` : '<span class="er-muted">未</span>'}</td>
                <td class="name">${r.supplierId ? `${esc(r.supplierCode ?? '')} ${esc(r.supplierName ?? '')}` : '<span class="er-muted">未</span>'}</td>
                <td class="num">${r.lineCount}</td>
                <td class="num">${yen(r.detailAmountTotal)}</td>
                <td>${r.hasPdf ? '📄' : '<span class="er-muted">未登録</span>'}</td>
                <td>${esc(shortUser(r.importedBy))}</td>
            </tr>`).join('')
            : '<tr><td colspan="11" class="empty-state">該当する見積はありません。AI-OCR 画面の「📥 既存OCR結果を取り込む」や「📋 内容確認・取決め確定」で登録できます。</td></tr>';
        setStatus(`${rows.length} 件`);
    } catch (e) {
        toast(`一覧を読み込めませんでした: ${e.message}`, 'error', null, 10000);
    }
}

function stateBadge(state) {
    const cls = state.startsWith('確定') ? 'ok' : state.startsWith('下書き') ? 'warn' : state === '未紐づけ' ? 'error' : 'unknown';
    return `<span class="st-badge ${cls}">${esc(state)}</span>`;
}

// =====================================================================
// 詳細モード
// =====================================================================

function initDetail() {
    $('er-detail').classList.remove('hidden');
    bindSegment($('er-tabs'), (v) => {
        tab = v;
        render();
    });
    for (const form of document.querySelectorAll('.er-search')) {
        form.addEventListener('submit', (e) => {
            e.preventDefault();
            searchCandidates(form.dataset.kind, form.querySelector('input').value.trim());
        });
    }
    for (const id of ['er-project-candidates', 'er-supplier-candidates']) {
        $(id).addEventListener('click', (e) => {
            const li = e.target.closest('li[data-value]');
            if (!li || detail?.locked) return;
            const chosen = { label: li.dataset.label, sub: li.dataset.sub };
            if (id === 'er-project-candidates') {
                link.projectId = li.dataset.value;
                link.projectView = chosen;
            } else {
                link.supplierId = +li.dataset.value;
                link.supplierView = chosen;
            }
            // 選んだ候補を入力欄と「選択中」の欄にすぐ入れる（保存は「紐づけを保存」）
            $(id).previousElementSibling.querySelector('input').value = chosen.label;
            for (const x of $(id).querySelectorAll('li')) x.classList.toggle('chosen', x === li);
            setDirty('link', link.projectId !== detail.header.projectId || link.supplierId !== detail.header.supplierId);
            $('er-save-link').disabled = !dirty.link;
            renderLinkCurrent(li);
        });
    }
    $('er-save-link').addEventListener('click', saveLink);
    $('er-delete').classList.remove('hidden');
    $('er-delete').addEventListener('click', deleteEstimate);
    $('er-lines').addEventListener('click', (e) => {
        const tr = e.target.closest('tr[data-line]');
        if (tr) selectLine(+tr.dataset.line);
    });
    $('er-pdf-select').addEventListener('change', () => showPdf(+$('er-pdf-select').value, null));
    $('er-add-pdf').addEventListener('click', addPdf);
    $('er-open-excel').addEventListener('click', openExcel);
}

async function reload() {
    if (Object.values(dirty).some(Boolean) && !confirm('保存していない変更があります。破棄して再読込しますか？')) return;
    try {
        detail = await withLoading('見積を読み込んでいます...', () => call('detail'));
    } catch (e) {
        toast(`読み込めませんでした: ${e.message}`, 'error', null, 12000);
        return;
    }
    for (const k of Object.keys(dirty)) setDirty(k, false);
    link = { projectId: detail.header.projectId, supplierId: detail.header.supplierId };
    preview = null;
    confirmKey = null;
    document.title = `見積 #${detail.header.headerId} 確認・取決め`;
    renderHeader();
    renderLink();
    renderSummary();
    renderDocs();
    render();
    setStatus(`見積 #${detail.header.headerId} を読み込みました（${detail.lines.length} 明細）`);
}

const current = () => detail.agreements[0] ?? null;
const confirmed = () => detail.agreements.find((a) => a.status === 'Confirmed') ?? null;

function renderHeader() {
    const h = detail.header;
    const a = current();
    $('er-title').textContent = `📋 見積 #${h.headerId}`;
    $('er-subtitle').textContent = `${h.supplierNameOcr ?? '業者名なし'} ／ ${h.projectNameOcr ?? '工事名なし'}（OCR）`;
    $('er-badges').innerHTML = [
        a ? `<span class="st-badge ${a.status === 'Confirmed' ? 'ok' : 'warn'}">${STATUS_LABEL[a.status]} 第${a.revisionNo}版</span>` : '<span class="st-badge unknown">取決め未入力</span>',
        a?.status === 'Confirmed' ? `<span class="er-badge-text">確定: ${esc(shortUser(a.confirmedBy))} ${esc(dateText(a.confirmedAt, true))}</span>` : '',
        h.isTestData ? '<span class="st-badge error" title="試験データ。本番の検索では除外します">試験データ</span>' : '',
        `<span class="er-badge-text">取込: ${esc(dateText(h.importedAt, true))} ${esc(shortUser(h.importedBy))}</span>`,
    ].join(' ');
}

// ── 紐づけ ──

function renderLink() {
    renderLinkCurrent(null);
    $('er-save-link').disabled = true;
    for (const f of document.querySelectorAll('.er-search input, .er-search button')) f.disabled = detail.locked;
    $('er-project-candidates').innerHTML = '';
    $('er-supplier-candidates').innerHTML = '';
    if (!detail.locked) {
        if (!detail.header.projectId) searchCandidates('project', '');
        if (!detail.header.supplierId) searchCandidates('supplier', '');
    }
}

function renderLinkCurrent(chosenLi) {
    const h = detail.header;
    const pChanged = link.projectId !== h.projectId;
    const sChanged = link.supplierId !== h.supplierId;
    const pending = (v) => `<span class="er-pending">選択中（未保存）: <b>${esc(v?.label ?? '')}</b><small>${esc(v?.sub ?? '')}　→「紐づけを保存」で確定</small></span>`;
    $('er-project-current').innerHTML = `<span class="er-label">物件</span>${h.projectId
        ? `<b>${esc(h.projectId)}</b> ${esc(h.projectName ?? '')}<small>${esc(h.projectAddress ?? '')}</small>`
        : '<span class="er-muted">未紐づけ</span>'}${pChanged ? pending(link.projectView) : ''}<small>OCR: ${esc(h.projectNameOcr ?? '—')}</small>`;
    $('er-supplier-current').innerHTML = `<span class="er-label">業者</span>${h.supplierId
        ? `<b>${esc(h.supplierCode ?? '')}</b> ${esc(h.supplierName ?? '')}<small>${esc(h.supplierAddress ?? '')}</small>`
        : '<span class="er-muted">未紐づけ</span>'}${sChanged ? pending(link.supplierView) : ''}<small>OCR: ${esc(h.supplierNameOcr ?? '—')}</small>`;
    if (detail.locked) {
        $('er-project-current').innerHTML += '<small class="er-locked">🔒 確定済みのため変更できません（「新しい版で訂正」で変更）</small>';
    }
}

async function searchCandidates(kind, keyword) {
    const list = $(kind === 'project' ? 'er-project-candidates' : 'er-supplier-candidates');
    try {
        const rows = await call(kind === 'project' ? 'projectCandidates' : 'supplierCandidates', { keyword: keyword || null });
        const chosen = kind === 'project' ? link.projectId : link.supplierId;
        list.innerHTML = rows.length ? rows.map((r) => kind === 'project'
            ? `<li data-value="${esc(r.projectId)}" data-label="${esc(`${r.projectId} ${r.name ?? ''}`)}" data-sub="${esc(r.address ?? '')}" class="${r.projectId === chosen ? 'chosen' : ''}">
                   <span class="er-match">${esc(r.match)}</span><b>${esc(r.projectId)}</b> ${esc(r.name ?? '')}
                   <small>${esc(r.address ?? '')} ${esc(r.structure ?? '')}</small></li>`
            : `<li data-value="${r.supplierId}" data-label="${esc(`${r.code ?? ''} ${r.name ?? ''}`)}" data-sub="${esc(r.address ?? '')}" class="${r.supplierId === chosen ? 'chosen' : ''}">
                   <span class="er-match">${esc(r.match)}</span><b>${esc(r.code ?? '（CDなし）')}</b> ${esc(r.name ?? '')}
                   <small>${esc(r.address ?? '住所なし')}${r.department ? ` ／ ${esc(r.department)}` : ''}${r.phone ? ` ／ ${esc(r.phone)}` : ''}</small></li>`).join('')
            : `<li class="er-none">${keyword ? '該当がありません。別の言葉で検索してください。' : '正規化して一致する候補がありません。上の欄で検索してください。'}</li>`;
    } catch (e) {
        list.innerHTML = `<li class="er-none">候補を取得できませんでした: ${esc(e.message)}</li>`;
    }
}

async function saveLink() {
    try {
        const res = await withLoading('紐づけを保存しています...', () => call('saveLink', {
            projectId: link.projectId, supplierId: link.supplierId, rowVersion: detail.header.rowVersion,
        }));
        detail.header.rowVersion = res.rowVersion;
        setDirty('link', false);
        toast('紐づけを保存しました。', 'success');
        await reload();
    } catch (e) {
        toast(e.message, 'error', null, 12000);
    }
}

// ── 削除 ──

/**
 * この見積を DB から削除する（明細・行監査・取決め・資料・取込記録をまとめて）。
 * 確定済みの取決めがある場合は、確認ダイアログ → 見積ID の入力 → 理由の入力 の 3 段階で確かめる。
 */
async function deleteEstimate() {
    const h = detail.header;
    const fixed = detail.agreements.filter((a) => a.status !== 'Draft');
    const msg = [
        `見積 #${h.headerId}（${h.supplierNameOcr ?? '業者名なし'} ／ ${h.projectNameOcr ?? '工事名なし'}）を DB から削除します。`,
        '',
        `・明細 ${detail.lines.length} 行、OCR行監査 ${detail.audit.length} 行`,
        `・資料 ${detail.documents.length} 件（${detail.documents.map((d) => docLabel(d.type)).join('・') || 'なし'}）`,
        `・取決め ${detail.agreements.length} 版${fixed.length ? `（うち確定済み・旧版 ${fixed.length} 版）` : ''}`,
        '',
        '削除の記録は「📜 変更履歴」に残りますが、資料（PDF・Excel）の本体は元に戻せません。',
        '元のファイル（PC 上の PDF・JSON）は削除しません。',
        '',
        '削除しますか？',
    ];
    if (!confirm(msg.join('\n'))) return;

    let confirmText = null;
    let reason;
    if (fixed.length) {
        confirmText = prompt(`確定済みの取決め（${fixed.length} 版）がある見積です。\n本当に削除する場合は、確認のため見積ID「${h.headerId}」を入力してください。`);
        if (confirmText == null) return;
        if (confirmText.trim() !== String(h.headerId)) {
            toast('見積ID が一致しないため、削除しませんでした。', 'error');
            return;
        }
        reason = prompt('削除の理由を入力してください（必須。例: 業者変更のため取決めを取り消し）');
        if (reason == null) return;
        if (!reason.trim()) {
            toast('理由が無いため、削除しませんでした。', 'error');
            return;
        }
    } else {
        reason = prompt('削除の理由（任意）', '');
        if (reason == null) return;
    }

    try {
        const res = await withLoading('削除しています...', () => call('deleteEstimate', {
            rowVersion: h.rowVersion, reason: reason || null, confirmText,
        }));
        for (const k of Object.keys(dirty)) setDirty(k, false);
        alert(`見積 #${h.headerId} を削除しました（${res.rows} 行）。\n変更履歴の操作 #${res.changeBatchId} に記録しました。ウィンドウを閉じます。`);
        await call('closeWindow');
    } catch (e) {
        toast(e.message, 'error', null, 15000);
    }
}

// ── 集計 ──

function detailTotal() {
    return detail.lines.filter((l) => l.recordType === '金額明細')
        .reduce((s, l) => s + Number(l.correctedAmount ?? l.amount ?? 0), 0);
}

const needsReview = (l) => l.amountCheck === '要確認' || !!l.duplicateCandidate || !!l.reviewRequired;
const isTarget = (l) => l.isCalculationTarget ?? l.recordType === '金額明細';

function renderSummary() {
    const h = detail.header;
    const pending = detail.lines.filter((l) => isTarget(l) && needsReview(l) && l.humanReviewStatus !== REVIEW.done).length;
    const total = detailTotal();
    const diff = h.headerEstimateAmount == null ? null : Number(h.headerEstimateAmount) - total;
    const kpi = (cls, label, value, title = '') => `<div class="st-kpi ${cls}" title="${esc(title)}"><div class="label">${label}</div><div class="value">${value}</div></div>`;
    $('er-summary').innerHTML = [
        kpi('info', '表紙見積総額', yen(h.headerEstimateAmount), '見積の表紙から OCR で読み取った金額'),
        kpi('', '金額明細合計', yen(total), '金額明細の合計（補正値があれば補正値）'),
        kpi(diff ? 'existing' : 'new', '差額（表紙－明細）', diff == null ? '—' : yen(diff)),
        kpi(pending ? 'error' : 'new', '要確認（未確認）', `${pending} 行`, '要確認・検算不一致・重複候補の計算対象行のうち、まだ確認していない行'),
        kpi('info', '計算対象 / 対象外', `${detail.lines.filter(isTarget).length} / ${detail.lines.filter((l) => !isTarget(l)).length}`),
    ].join('');
}

// ── タブ ──

function render() {
    for (const el of document.querySelectorAll('.er-tab')) {
        const t = el.dataset.tab;
        el.classList.toggle('hidden', !(t === tab || (t === 'table' && ['lines', 'review', 'audit'].includes(tab))));
    }
    const hints = {
        lines: '行を選ぶと、右の PDF が該当ページに移動し、下で確認結果・補正を保存できます。',
        review: 'OCR が要確認・検算不一致・重複候補とした行です。PDF と照らして「確認済」にするか、計算対象外にしてください。',
        audit: 'OCR が表の各行を 採用／見出し／除外／空行 に判定した記録です（原文のまま）。',
        agreement: '基準総額と取決め総額を入力し、計算プレビュー → 確定 の順に進めます。計算は C# で行います。',
        history: '取決めの版の履歴です。確定済みの版は変更されません。',
    };
    $('er-tab-hint').textContent = hints[tab];
    if (tab === 'agreement') renderAgreement();
    else if (tab === 'history') renderHistory();
    else renderTable();
}

function renderTable() {
    const table = $('er-lines');
    if (tab === 'audit') {
        table.tHead.innerHTML = '<tr><th class="num">頁</th><th class="num">表</th><th class="num">行</th><th>判定</th><th>理由</th><th>OCR行原文</th></tr>';
        table.tBodies[0].innerHTML = detail.audit.map((a) => `
            <tr class="${a.rowStatus === '除外' ? 'er-review' : a.rowStatus === '採用' ? '' : 'er-ref'}">
                <td class="num">${a.pdfPage ?? ''}</td><td class="num">${a.ocrTableIndex ?? ''}</td><td class="num">${a.ocrRowIndex ?? ''}</td>
                <td>${esc(a.rowStatus ?? '')}</td><td>${esc(a.reason ?? '')}</td><td class="wrap">${esc(a.rowText ?? '')}</td>
            </tr>`).join('') || '<tr><td colspan="6" class="empty-state">行監査はありません。</td></tr>';
        $('er-line-editor').classList.add('hidden');
        return;
    }
    const rows = tab === 'review' ? detail.lines.filter((l) => needsReview(l)) : detail.lines;
    table.tHead.innerHTML = `<tr><th class="num">#</th><th class="num">頁</th><th>種別</th><th>工種／区分</th><th>名称</th><th>仕様</th><th>寸法</th>
        <th class="num">数量</th><th>単位</th><th class="num">単価</th><th class="num">金額</th><th>OCRの判定</th><th>人の確認</th><th>計算</th></tr>`;
    table.tBodies[0].innerHTML = rows.map((l) => {
        const c = (orig, corr, f) => (corr != null && corr !== '' ? `<span class="er-corrected" title="OCR: ${esc(f(orig))}">${esc(f(corr))}</span>` : esc(f(orig)));
        const ocr = [l.amountCheck === '要確認' ? '検算不一致' : '', l.duplicateCandidate ?? '', l.reviewRequired ? '要確認' : ''].filter(Boolean).join('・');
        return `<tr data-line="${l.lineId}" class="${l.lineId === selectedLine ? 'selected' : ''} ${!isTarget(l) ? 'er-ref' : needsReview(l) && l.humanReviewStatus !== REVIEW.done ? 'er-review' : ''}"
                    title="${esc(l.reviewReason ?? '')}">
            <td class="num">${l.order}</td><td class="num">${l.pdfPage ?? ''}</td><td>${esc(l.recordType ?? '')}</td>
            <td>${esc(l.trade ?? '')}${l.section ? `／${esc(l.section)}` : ''}</td>
            <td class="wrap">${esc(l.itemName ?? '')}</td><td class="wrap">${esc(l.materialSpec ?? '')}</td><td class="wrap">${esc(l.dimensionSpec ?? '')}</td>
            <td class="num">${c(l.quantity, l.correctedQuantity, num)}</td><td>${c(l.unit, l.correctedUnit, (v) => v ?? '')}</td>
            <td class="num">${c(l.unitPrice, l.correctedUnitPrice, yen)}</td><td class="num">${c(l.amount, l.correctedAmount, yen)}</td>
            <td>${ocr ? `<span class="er-ocr-flag">${esc(ocr)}</span>` : ''}</td>
            <td>${reviewBadge(l)}</td>
            <td>${isTarget(l) ? '対象' : `<span class="er-muted" title="${esc(l.exclusionReason ?? '')}">対象外</span>`}</td>
        </tr>`;
    }).join('') || `<tr><td colspan="14" class="empty-state">${tab === 'review' ? '要確認の行はありません。' : '明細はありません。'}</td></tr>`;
    renderLineEditor();
}

function reviewBadge(l) {
    const s = l.humanReviewStatus ?? REVIEW.pending;
    const cls = s === REVIEW.done ? 'ok' : s === REVIEW.excluded ? 'unknown' : 'warn';
    return `<span class="st-badge ${cls}" title="${esc([l.reviewedBy, dateText(l.reviewedAt, true), l.humanReviewNotes].filter(Boolean).join(' ／ '))}">${esc(s)}</span>`;
}

function selectLine(id) {
    if (dirty.line && id !== selectedLine && !confirm('明細の確認結果が保存されていません。破棄して別の行を選びますか？')) return;
    setDirty('line', false);
    selectedLine = id;
    const line = detail.lines.find((l) => l.lineId === id);
    if (line?.pdfPage && pdfDocId) showPdf(pdfDocId, line.pdfPage);
    renderTable();
}

function renderLineEditor() {
    const form = $('er-line-editor');
    const l = detail.lines.find((x) => x.lineId === selectedLine);
    if (!l || tab === 'audit') {
        form.classList.add('hidden');
        return;
    }
    form.classList.remove('hidden');
    const ro = detail.locked ? 'disabled' : '';
    const v = (x) => (x == null ? '' : esc(String(x)));
    form.innerHTML = `
        <div class="er-editor-head">
            <b>${l.order} 行目「${esc(l.itemName ?? '')}」</b>
            <span class="st-hint">OCR: ${esc(l.reviewReason || '指摘なし')}</span>
            ${detail.locked ? '<span class="er-locked">🔒 確定済み（「新しい版で訂正」で変更）</span>' : ''}
        </div>
        <div class="er-editor-grid">
            <label>人の確認
                <select name="status" ${ro}>${Object.values(REVIEW).map((s) => `<option ${s === (l.humanReviewStatus ?? REVIEW.pending) ? 'selected' : ''}>${s}</option>`).join('')}</select>
            </label>
            <label class="er-check"><input type="checkbox" name="target" ${isTarget(l) ? 'checked' : ''} ${ro}> 取決めの計算対象にする</label>
            <label class="wide">対象外の理由（計算対象外のとき必須）<input name="exclusion" value="${v(l.exclusionReason)}" ${ro}></label>
            <label>補正 数量<input name="cq" inputmode="decimal" value="${v(l.correctedQuantity)}" placeholder="${v(l.quantity)}" ${ro}></label>
            <label>補正 単位<input name="cu" value="${v(l.correctedUnit)}" placeholder="${v(l.unit)}" ${ro}></label>
            <label>補正 単価<input name="cp" inputmode="decimal" value="${v(l.correctedUnitPrice)}" placeholder="${v(l.unitPrice)}" ${ro}></label>
            <label>補正 金額<input name="ca" inputmode="decimal" value="${v(l.correctedAmount)}" placeholder="${v(l.amount)}" ${ro}></label>
            <label class="wide">補正の理由（補正値を入れたとき必須）<input name="cr" value="${v(l.correctionReason)}" ${ro}></label>
            <label class="wide">確認メモ<input name="notes" value="${v(l.humanReviewNotes)}" ${ro}></label>
        </div>
        <div class="er-editor-actions">
            <span class="st-hint">補正値は OCR の原値とは別に保存します（原値は変わりません）。空欄なら OCR の値を使います。</span>
            <button type="submit" class="active-btn" ${ro}>この行の確認結果を保存</button>
        </div>`;
    form.oninput = () => setDirty('line', true);
    form.onsubmit = (e) => {
        e.preventDefault();
        saveLine(l);
    };
}

async function saveLine(l) {
    const f = new FormData($('er-line-editor'));
    const status = f.get('status');
    try {
        const updated = await withLoading('確認結果を保存しています...', () => call('saveLineReview', {
            lineId: l.lineId, humanReviewStatus: status, humanReviewNotes: f.get('notes') || null,
            isCalculationTarget: status !== REVIEW.excluded && f.get('target') === 'on',
            exclusionReason: f.get('exclusion') || null,
            correctedQuantity: numText(f.get('cq')), correctedUnit: f.get('cu') || null,
            correctedUnitPrice: numText(f.get('cp')), correctedAmount: numText(f.get('ca')),
            correctionReason: f.get('cr') || null, rowVersion: l.rowVersion,
        }));
        Object.assign(l, updated);
        setDirty('line', false);
        preview = null;
        toast(`${l.order} 行目の確認結果を保存しました。`, 'success');
        renderSummary();
        renderTable();
    } catch (e) {
        toast(e.message, 'error', null, 12000);
    }
}

// ── 原資料 ──

function renderDocs() {
    const pdfs = detail.documents.filter((d) => d.type === 'OriginalPdf');
    $('er-pdf-select').innerHTML = pdfs.map((d) => `<option value="${d.documentId}">${esc(d.fileName)}</option>`).join('');
    $('er-pdf-select').classList.toggle('hidden', pdfs.length === 0);
    $('er-open-excel').disabled = !detail.documents.some((d) => d.type === 'StructuredExcel');
    $('er-doc-note').textContent = detail.documents.map((d) => `${docLabel(d.type)}: ${d.fileName}（${kb(d.size)}・SHA-256 ${d.hashHex.slice(0, 12)}…）`).join(' ／ ');
    if (pdfs.length) showPdf(pdfs.at(-1).documentId, null);
    else {
        pdfDocId = null;
        $('er-pdf').classList.add('hidden');
        $('er-pdf-empty').classList.remove('hidden');
    }
}

async function showPdf(documentId, page) {
    try {
        const res = await call('showPdf', { documentId, page });
        pdfDocId = documentId;
        $('er-pdf-select').value = String(documentId);
        $('er-pdf').src = res.url;
        $('er-pdf').classList.remove('hidden');
        $('er-pdf-empty').classList.add('hidden');
    } catch (e) {
        toast(`PDF を表示できませんでした: ${e.message}`, 'error');
    }
}

async function addPdf() {
    try {
        const res = await call('addPdf');
        if (res.canceled) return;
        toast(res.added ? `元PDF「${res.fileName}」（${res.pages} ページ）を登録しました。` : 'この PDF は登録済みです。', res.added ? 'success' : 'info');
        await reload();
    } catch (e) {
        toast(e.message, 'error', null, 10000);
    }
}

async function openExcel() {
    const doc = detail.documents.find((d) => d.type === 'StructuredExcel');
    if (!doc) return;
    try {
        await call('openDocument', { documentId: doc.documentId });
        toast('構造化Excel を開きました（DB から取り出した写しです。編集しても DB には反映されません）。');
    } catch (e) {
        toast(e.message, 'error');
    }
}

// ── 取決め ──

function agreementDraft() {
    const a = current();
    return a && a.status !== 'Confirmed' ? a : null;
}

function renderAgreement() {
    const box = $('er-agreement');
    const a = current();
    if (a?.status === 'Confirmed') {
        box.innerHTML = `${agreementSummary(a)}
            <div class="er-agreement-actions">
                <span class="st-hint">確定済みの版は変更できません。訂正する場合は新しい版（下書き）を作ります（確定するまでこの版が有効です）。</span>
                <button type="button" class="small-btn delete-btn" id="er-new-revision">✏ 新しい版で訂正</button>
            </div>
            ${agreementLines(a.lines)}`;
        $('er-new-revision').onclick = newRevision;
        return;
    }
    const d = agreementDraft();
    const split = splitNotes(d?.notes);
    const h = detail.header;
    const total = detailTotal();
    const pdfs = detail.documents.filter((x) => x.type === 'OriginalPdf');
    const src = d?.baseAmountSource ?? (h.headerEstimateAmount != null ? BASE.header : BASE.detail);
    const ro = detail.locked ? 'disabled' : '';
    box.innerHTML = `
        ${d ? `<p class="er-note">第${d.revisionNo}版の下書き${d.changeReason ? `（訂正の理由: ${esc(d.changeReason)}）` : ''}。確定するまで、${confirmed() ? `第${confirmed().revisionNo}版（確定済み）が有効です` : '取決めは未確定です'}。</p>` : ''}
        <form class="er-agreement-form" id="er-agreement-form" autocomplete="off">
            ${overheadFieldset(d)}
            <fieldset><legend>① 基準総額（見積の総額）</legend>
                <label class="er-radio"><input type="radio" name="baseSource" value="${BASE.header}" ${src === BASE.header ? 'checked' : ''} ${h.headerEstimateAmount == null ? 'disabled' : ''}> 表紙金額 <b>${yen(h.headerEstimateAmount)}</b> 円${h.headerEstimateAmount == null ? '（読み取れていません）' : ''}</label>
                <label class="er-radio"><input type="radio" name="baseSource" value="${BASE.detail}" ${src === BASE.detail ? 'checked' : ''}> 金額明細の合計（手入力の諸経費を含む） <b id="er-detail-total">${yen(total)}</b> 円</label>
                <label class="er-radio"><input type="radio" name="baseSource" value="${BASE.input}" ${src === BASE.input ? 'checked' : ''}> PDF で確認して入力</label>
                <div class="er-form-grid">
                    <label>基準総額（円）<input name="baseAmount" inputmode="decimal" value="${val(d?.baseEstimateAmount)}"></label>
                    <label>税区分
                        <select name="taxBasis"><option value="">選んでください</option>
                            ${Object.entries(TAX_LABEL).map(([k, v]) => `<option value="${k}" ${d?.taxBasis === k ? 'selected' : ''}>${v}</option>`).join('')}</select>
                    </label>
                    <label class="wide">基準総額の根拠・理由（確認入力のとき、表紙と明細合計が異なるときは必須）<input name="baseReason" value="${val(d?.baseAmountReason)}"></label>
                </div>
            </fieldset>
            <fieldset><legend>② 取決め総額（PDF で確認した金額）</legend>
                <div class="er-form-grid">
                    <label>取決め総額（円・基準総額と同じ税区分）<input name="agreedAmount" inputmode="decimal" value="${val(d?.agreedAmount)}"></label>
                    <label>取決め日（不明なら空欄）<input type="date" name="agreementDate" value="${d?.agreementDate ? d.agreementDate.slice(0, 10) : ''}"></label>
                    <label>根拠の PDF
                        <select name="evidenceDoc">${pdfs.length ? pdfs.map((p) => `<option value="${p.documentId}" ${d?.evidenceDocumentId === p.documentId ? 'selected' : ''}>${esc(p.fileName)}</option>`).join('') : '<option value="">（元PDF未登録）</option>'}</select>
                    </label>
                    <label>根拠ページ<input name="evidencePage" type="number" min="1" value="${val(d?.evidencePage)}"></label>
                    <label class="er-check wide"><input type="checkbox" name="zeroConfirmed"> 取決め総額 0 円で確定する（0 円のときだけ必要）</label>
                    <label class="wide">残差の理由（取決め総額と換算金額の合計に差があるとき必須）<input name="residualReason" value="${val(split.residual)}"></label>
                    <label class="wide">増額の理由（掛率が 100% を超えるとき必須）<input name="increaseReason" value="${val(split.increase)}"></label>
                    <label class="wide">メモ<input name="notes" value="${val(split.notes)}"></label>
                </div>
            </fieldset>
            <div class="er-agreement-actions">
                <span class="st-hint">「取決め換算単価」は 単価 × 掛率 で求めた値です（PDF に書かれた個別の単価ではありません）。</span>
                <button type="button" class="small-btn edit-btn" id="er-save-draft" ${ro}>💾 下書き保存</button>
                <button type="button" class="small-btn edit-btn" id="er-preview">🧮 計算プレビュー</button>
                <button type="button" class="active-btn" id="er-confirm" disabled>✔ 確定</button>
            </div>
        </form>
        <div id="er-preview-box"></div>`;

    const form = $('er-agreement-form');
    const fillBase = () => {
        const s = form.baseSource.value;
        if (s === BASE.header) form.baseAmount.value = val(h.headerEstimateAmount);
        if (s === BASE.detail) form.baseAmount.value = String(total + (form.ohSource.value === 'input' ? Number(numText(form.ohAmount.value) ?? 0) : 0));
        form.baseAmount.readOnly = s !== BASE.input;
        $('er-detail-total').textContent = yen(total + (form.ohSource.value === 'input' ? Number(numText(form.ohAmount.value) ?? 0) : 0));
    };
    if (!d?.baseEstimateAmount) fillBase();
    form.baseAmount.readOnly = form.baseSource.value !== BASE.input;
    updateOverheadHint();
    form.addEventListener('change', (e) => {
        if (e.target.name === 'baseSource') fillBase();
        if (['ohSource', 'ohLine'].includes(e.target.name)) {
            updateOverheadHint();
            if (form.baseSource.value === BASE.detail) fillBase();
        }
    });
    form.ohAmount.addEventListener('input', () => {
        updateOverheadHint();
        if (form.baseSource.value === BASE.detail) fillBase();
    });
    form.ohItems.addEventListener('input', () => {
        form.ohItems.dataset.edited = '1';
        updateOverheadHint();
    });
    $('er-oh-items-reset').addEventListener('click', () => {
        form.ohItems.dataset.edited = '';
        updateOverheadHint();
        form.dispatchEvent(new Event('input', { bubbles: true }));
    });
    form.addEventListener('input', () => {
        setDirty('agreement', true);
        preview = null;
        confirmKey = null;   // 入力が変わったら別の確定要求
        $('er-confirm').disabled = true;
    });
    $('er-save-draft').onclick = saveDraft;
    $('er-preview').onclick = runPreview;
    $('er-confirm').onclick = runConfirm;
    if (preview) renderPreview();
}

function agreementInput() {
    const f = $('er-agreement-form');
    return {
        baseSource: f.baseSource.value || null,
        baseAmount: numText(f.baseAmount.value),
        baseReason: f.baseReason.value || null,
        taxBasis: f.taxBasis.value || null,
        agreedAmount: numText(f.agreedAmount.value),
        agreementDate: f.agreementDate.value || null,
        evidenceDocumentId: f.evidenceDoc.value ? +f.evidenceDoc.value : null,
        evidencePage: f.evidencePage.value ? +f.evidencePage.value : null,
        notes: f.notes.value || null,
        zeroConfirmed: f.zeroConfirmed.checked,
        residualReason: f.residualReason.value || null,
        increaseReason: f.increaseReason.value || null,
        overheadSource: f.ohSource.value,
        overheadAmount: f.ohSource.value === 'input' ? numText(f.ohAmount.value) : null,
        overheadLineIds: f.ohSource.value === 'lines' ? [...f.querySelectorAll('input[name="ohLine"]:checked')].map((x) => +x.value) : [],
        itemsTotalOverride: f.ohSource.value !== 'none' && f.ohItems.dataset.edited === '1' ? numText(f.ohItems.value) : null,
        headerRowVersion: detail.header.rowVersion,
    };
}

// ── 諸経費（内訳合計＋諸経費＝総合計 の見積）──

const OVERHEAD_RE = /諸経費|経費|一般管理費|現場管理費|法定福利費|共通費/;

/** 諸経費の入力欄。明細の中から「諸経費」らしい行を探して候補に出し、無ければ手入力できる。 */
function overheadFieldset(d) {
    const amountLines = detail.lines.filter((l) => (l.correctedAmount ?? l.amount) != null);
    const found = amountLines.filter((l) => OVERHEAD_RE.test((l.itemName ?? '').normalize('NFKC')));
    const others = amountLines.filter((l) => !found.includes(l));
    const saved = new Set(d?.overheadLineIds ?? []);
    const source = d?.overheadSource ?? (found.some((l) => /諸経費/.test(l.itemName ?? '')) ? 'lines' : 'none');
    const checked = (l) => (saved.size ? saved.has(l.lineId) : /諸経費/.test(l.itemName ?? ''));
    const row = (l) => `<label class="er-oh-line"><input type="checkbox" name="ohLine" value="${l.lineId}" ${checked(l) ? 'checked' : ''}>
        ${l.order}行目 ${esc(l.itemName ?? '')} <b>${yen(l.correctedAmount ?? l.amount)}</b> 円</label>`;
    return `<fieldset><legend>⓪ 諸経費（「内訳合計＋諸経費＝総合計」の見積のとき）</legend>
        <label class="er-radio"><input type="radio" name="ohSource" value="none" ${source === 'none' ? 'checked' : ''}> 諸経費なし（掛率だけで換算）</label>
        <label class="er-radio"><input type="radio" name="ohSource" value="lines" ${source === 'lines' ? 'checked' : ''}> 明細の行から（チェックした行を諸経費として各明細の単価へ配分）</label>
        <div class="er-oh-lines">
            ${found.length ? found.map(row).join('') : '<span class="er-muted">名称に「諸経費」などを含む行は見つかりませんでした。</span>'}
            ${others.length ? `<details><summary>ほかの行から選ぶ（${others.length} 行）</summary>${others.map(row).join('')}</details>` : ''}
        </div>
        <label class="er-radio"><input type="radio" name="ohSource" value="input" ${source === 'input' ? 'checked' : ''}> 手入力（明細に無いとき）
            <input name="ohAmount" class="er-oh-amount" inputmode="decimal" value="${source === 'input' ? val(d?.overheadAmount) : ''}" placeholder="諸経費の金額（円）"></label>
        <div class="er-oh-items">
            <label>内訳合計（諸経費を除く・円）<input name="ohItems" inputmode="decimal" value="${val(d?.overheadItemsTotal)}" data-edited="${d?.overheadItemsTotal != null ? '1' : ''}"></label>
            <button type="button" class="small-btn edit-btn" id="er-oh-items-reset" title="計算対象の明細の合計（OCR の結果）に戻す">OCR結果に戻す</button>
            <span class="st-hint" id="er-oh-items-ocr"></span>
        </div>
        <p class="st-hint" id="er-oh-hint"></p>
    </fieldset>`;
}

/** 諸経費の目安（表示だけ。確定の計算は C# が行う） */
function updateOverheadHint() {
    const f = $('er-agreement-form');
    const src = f.ohSource.value;
    const ids = new Set([...f.querySelectorAll('input[name="ohLine"]:checked')].map((x) => +x.value));
    const amt = (l) => Number(l.correctedAmount ?? l.amount ?? 0);
    const itemsOcr = detail.lines.filter((l) => isTarget(l) && !(src === 'lines' && ids.has(l.lineId))).reduce((s, l) => s + amt(l), 0);
    // 内訳合計: 手で変えていなければ OCR の結果（明細の合計）を入れる。変えた値は保持する
    if (f.ohItems.dataset.edited !== '1') f.ohItems.value = String(itemsOcr);
    $('er-oh-items-ocr').textContent = `OCR の結果（計算対象の明細の合計）: ${yen(itemsOcr)} 円${f.ohItems.dataset.edited === '1' && Number(numText(f.ohItems.value)) !== itemsOcr ? '（変更しています）' : ''}`;
    f.ohItems.disabled = src === 'none';
    const items = Number(numText(f.ohItems.value) ?? itemsOcr);
    const overhead = src === 'lines' ? detail.lines.filter((l) => ids.has(l.lineId)).reduce((s, l) => s + amt(l), 0)
        : src === 'input' ? Number(numText(f.ohAmount.value) ?? 0) : 0;
    $('er-oh-hint').textContent = src === 'none' || !overhead
        ? '諸経費を配分しない場合、換算単価 ＝ 単価 × 掛率 です。'
        : `内訳合計 ${yen(items)} 円 ＋ 諸経費 ${yen(overhead)} 円 ＝ ${yen(items + overhead)} 円 ／ 諸経費掛率 約 ${pct(items ? (items + overhead) / items : null)}。換算単価 ＝ 単価 × 諸経費掛率 × 掛率（確定値は計算プレビューで表示）`;
}

async function saveDraft() {
    try {
        await withLoading('下書きを保存しています...', () => call('saveDraft', agreementInput()));
        setDirty('agreement', false);
        toast('取決めの下書きを保存しました。', 'success');
        const keep = tab;
        await reload();
        tab = keep;
    } catch (e) {
        toast(e.message, 'error', null, 12000);
    }
}

async function runPreview() {
    try {
        preview = await withLoading('計算しています...', () => call('preview', agreementInput()));
        renderPreview();
    } catch (e) {
        toast(e.message, 'error', null, 12000);
    }
}

function renderPreview() {
    const p = preview;
    $('er-confirm').disabled = !p.canConfirm || detail.locked;
    $('er-preview-box').innerHTML = `
        ${p.overheadRate != null ? `<div class="st-summary er-preview-kpis">
            <div class="st-kpi info"><div class="label">内訳合計（諸経費を除く）</div><div class="value">${yen(p.itemsTotal)}</div></div>
            <div class="st-kpi info"><div class="label">諸経費</div><div class="value">${yen(p.overheadAmount)}</div></div>
            <div class="st-kpi"><div class="label">諸経費掛率（(内訳＋諸経費)÷内訳）</div><div class="value">${pct(p.overheadRate)}</div></div>
            <div class="st-kpi"><div class="label">合成（諸経費掛率 × 掛率）</div><div class="value">${pct(p.rate == null ? null : p.overheadRate * p.rate)}</div></div>
        </div>` : ''}
        <div class="st-summary er-preview-kpis">
            <div class="st-kpi ${p.rate > 1 ? 'existing' : ''}"><div class="label">掛率（取決め総額 ÷ 基準総額）</div><div class="value">${pct(p.rate)}</div></div>
            <div class="st-kpi"><div class="label">換算金額の合計</div><div class="value">${yen(p.convertedTotal)}</div></div>
            <div class="st-kpi ${p.residual ? 'existing' : 'new'}"><div class="label">残差（取決め総額－換算合計）</div><div class="value">${yen(p.residual)}</div></div>
            <div class="st-kpi info"><div class="label">計算対象 / 対象外</div><div class="value">${p.targetCount} / ${p.excludedCount}</div></div>
        </div>
        ${p.blocking.length ? `<div class="er-blocking"><b>確定できない理由（${p.blocking.length} 件）</b><ul>${p.blocking.map((b) => `<li>${esc(b)}</li>`).join('')}</ul></div>`
            : '<div class="er-ok">✔ 確定できます。内容を確認して「✔ 確定」を押してください（確定後は変更できず、訂正は新しい版で行います）。</div>'}
        ${p.warnings.length ? `<div class="er-warnings"><ul>${p.warnings.map((w) => `<li>${esc(w)}</li>`).join('')}</ul></div>` : ''}
        ${agreementLines(p.lines.map((l) => ({
            lineOrder: l.order, itemName: l.itemName, materialSpec: l.materialSpec, quantity: l.quantity, unit: l.unit,
            originalUnitPrice: l.unitPrice, originalAmount: l.amount, convertedUnitPrice: l.convertedUnitPrice, convertedAmount: l.convertedAmount,
            isCalculationTarget: l.isTarget, exclusionReason: l.exclusionReason, corrected: l.corrected,
            overheadIncludedUnitPrice: l.overheadIncludedUnitPrice, overheadIncludedAmount: l.overheadIncludedAmount, isOverhead: l.isOverhead,
        })))}`;
}

async function runConfirm() {
    if (!preview?.canConfirm) return;
    const input = agreementInput();
    const lines = [
        `取決めを確定します（${current() && current().status === 'Draft' ? `第${current().revisionNo}版` : '新しい版'}）。`,
        '',
        `基準総額: ${yen(preview.baseAmount)} 円 ／ 取決め総額: ${yen(preview.agreedAmount)} 円（${TAX_LABEL[input.taxBasis]}）`,
        `掛率: ${pct(preview.rate)} ／ 換算合計: ${yen(preview.convertedTotal)} 円 ／ 残差: ${yen(preview.residual)} 円`,
        '',
        '確定後は変更できません（訂正は「新しい版で訂正」）。確定者として記録されます: ' + ctx.operator,
        detail.header.isTestData ? '※ 試験データの見積です。' : '',
        'よろしいですか？',
    ];
    if (!confirm(lines.join('\n'))) return;
    confirmKey ??= crypto.randomUUID();   // 失敗して押し直しても同じ確定要求として扱う
    $('er-confirm').disabled = true;
    try {
        const saved = await withLoading('確定しています...', () => call('confirm', { ...input, requestKey: confirmKey }));
        confirmKey = null;
        setDirty('agreement', false);
        toast(`第${saved.revisionNo}版を確定しました（掛率 ${pct(saved.appliedRate)}）。`, 'success', null, 8000);
        await reload();
        tab = 'agreement';
        setSegment($('er-tabs'), 'agreement');
        render();
    } catch (e) {
        $('er-confirm').disabled = false;
        toast(e.message, 'error', null, 15000);
    }
}

async function newRevision() {
    const reason = prompt('訂正の理由を入力してください（必須）。\n確定済みの版はそのまま残り、新しい版の下書きを作ります。');
    if (reason == null) return;
    if (!reason.trim()) {
        toast('訂正の理由を入力してください。', 'error');
        return;
    }
    try {
        const draft = await withLoading('新しい版を作っています...', () => call('newRevision', { reason }));
        toast(`第${draft.revisionNo}版の下書きを作りました。内容を直して確定してください。`, 'success', null, 8000);
        await reload();
        tab = 'agreement';
        setSegment($('er-tabs'), 'agreement');
        render();
    } catch (e) {
        toast(e.message, 'error', null, 12000);
    }
}

function agreementSummary(a) {
    const split = splitNotes(a.notes);
    return `<dl class="er-agreement-summary">
        <dt>版・状態</dt><dd>第${a.revisionNo}版 ${STATUS_LABEL[a.status]}${a.previousAgreementId ? `（前の版 ID ${a.previousAgreementId}）` : ''}</dd>
        <dt>基準総額</dt><dd>${yen(a.baseEstimateAmount)} 円（${esc(a.baseAmountSource ?? '—')}）${a.baseAmountReason ? ` 理由: ${esc(a.baseAmountReason)}` : ''}</dd>
        <dt>取決め総額</dt><dd>${yen(a.agreedAmount)} 円（${esc(TAX_LABEL[a.taxBasis] ?? '—')}）${a.agreementDate ? ` 取決め日: ${esc(a.agreementDate.slice(0, 10))}` : ''}</dd>
        ${a.overheadSource && a.overheadSource !== 'none' ? `<dt>諸経費</dt><dd>${yen(a.overheadAmount)} 円（${a.overheadSource === 'lines' ? `明細 ${a.overheadLineIds.length} 行から` : '手入力'}）／ 内訳合計 ${yen(a.overheadItemsTotal)} 円 ／ 諸経費掛率 ${pct(a.overheadRate)}</dd>` : ''}
        <dt>掛率</dt><dd><b>${pct(a.appliedRate)}</b>${a.overheadRate ? `（換算単価 ＝ 単価 × 諸経費掛率 × 掛率）` : ''}</dd>
        <dt>換算合計・残差</dt><dd>${yen(a.convertedDetailTotal)} 円 ／ 残差 ${yen(a.residualAmount)} 円${split.residual ? `（${esc(split.residual)}）` : ''}</dd>
        <dt>根拠</dt><dd>PDF 資料ID ${a.evidenceDocumentId ?? '—'} の ${a.evidencePage ?? '—'} ページ</dd>
        <dt>確定</dt><dd>${esc(a.confirmedBy ?? '—')} ${esc(dateText(a.confirmedAt, true))}</dd>
        ${split.increase ? `<dt>増額の理由</dt><dd>${esc(split.increase)}</dd>` : ''}
        ${split.notes ? `<dt>メモ</dt><dd>${esc(split.notes)}</dd>` : ''}
        ${a.changeReason ? `<dt>訂正の理由</dt><dd>${esc(a.changeReason)}</dd>` : ''}
    </dl>`;
}

function agreementLines(lines) {
    if (!lines?.length) return '';
    const oh = lines.some((l) => l.overheadIncludedUnitPrice != null || l.overheadIncludedAmount != null);
    return `<div class="st-table-wrap er-agreement-lines"><table class="project-table st-table er-table">
        <thead><tr><th class="num">#</th><th>名称</th><th>仕様</th><th class="num">数量</th><th>単位</th>
            <th class="num">当初単価</th>${oh ? '<th class="num">諸経費込み単価</th>' : ''}<th class="num">取決め換算単価</th>
            <th class="num">当初金額</th>${oh ? '<th class="num">諸経費込み金額</th>' : ''}<th class="num">換算金額</th><th>計算</th></tr></thead>
        <tbody>${lines.map((l) => `<tr class="${l.isCalculationTarget ? '' : 'er-ref'}">
            <td class="num">${l.lineOrder}</td><td class="wrap">${esc(l.itemName ?? '')}${l.corrected ? ' <span class="er-corrected" title="補正値で計算">補正</span>' : ''}</td>
            <td class="wrap">${esc(l.materialSpec ?? '')}</td><td class="num">${num(l.quantity)}</td><td>${esc(l.unit ?? '')}</td>
            <td class="num">${yen(l.originalUnitPrice)}</td>${oh ? `<td class="num">${yen(l.overheadIncludedUnitPrice)}</td>` : ''}<td class="num er-converted">${yen(l.convertedUnitPrice)}</td>
            <td class="num">${yen(l.originalAmount)}</td>${oh ? `<td class="num">${yen(l.overheadIncludedAmount)}</td>` : ''}<td class="num er-converted">${yen(l.convertedAmount)}</td>
            <td>${l.isCalculationTarget ? '対象' : `<span class="er-muted" title="${esc(l.exclusionReason ?? '')}">${l.isOverhead || l.calculationType === 'Overhead' ? '諸経費（配分）' : '対象外'}</span>`}</td>
        </tr>`).join('')}</tbody></table></div>`;
}

function renderHistory() {
    const box = $('er-history');
    if (!detail.agreements.length) {
        box.innerHTML = '<div class="empty-state">取決めはまだありません。「取決め」タブで入力してください。</div>';
        return;
    }
    box.innerHTML = detail.agreements.map((a) => `
        <details class="er-history-item" ${a === current() ? 'open' : ''}>
            <summary><span class="st-badge ${a.status === 'Confirmed' ? 'ok' : a.status === 'Draft' ? 'warn' : 'unknown'}">${STATUS_LABEL[a.status]}</span>
                第${a.revisionNo}版　掛率 ${pct(a.appliedRate)}　取決め総額 ${yen(a.agreedAmount)} 円　${esc(shortUser(a.confirmedBy))} ${esc(dateText(a.confirmedAt, true))}</summary>
            ${agreementSummary(a)}
            ${agreementLines(a.lines)}
        </details>`).join('');
}

// ── 共通 ──

function splitNotes(stored) {
    const out = { notes: null, residual: null, increase: null };
    if (!stored) return out;
    const rest = [];
    for (const line of stored.split('\n')) {
        if (line.startsWith('【残差の理由】')) out.residual = line.slice(7);
        else if (line.startsWith('【増額の理由】')) out.increase = line.slice(7);
        else rest.push(line);
    }
    out.notes = rest.join('\n') || null;
    return out;
}

/** 入力の数値を文字列のまま送る（カンマ・空白・円を除く）。空なら null */
function numText(v) {
    const t = String(v ?? '').replace(/[,\s円¥￥]/g, '').normalize('NFKC');
    return t === '' ? null : t;
}

const val = (v) => (v == null ? '' : esc(String(v)));
const yen = (v) => (v == null || v === '' ? '—' : Number(v).toLocaleString('ja-JP', { maximumFractionDigits: 4 }));
const num = (v) => (v == null || v === '' ? '' : Number(v).toLocaleString('ja-JP', { maximumFractionDigits: 6 }));
const pct = (r) => (r == null ? '—' : `${(Number(r) * 100).toLocaleString('ja-JP', { maximumFractionDigits: 6 })}%`);
const kb = (n) => (n >= 1048576 ? `${(n / 1048576).toFixed(1)}MB` : `${Math.max(1, Math.round(n / 1024))}KB`);
const shortUser = (u) => (u ?? '').split('\\').pop();
const docLabel = (t) => ({ OriginalPdf: '元PDF', StructuredExcel: '構造化Excel', StructuredJson: '構造化JSON', RawJson: '統合OCR JSON' }[t] ?? t);

function dateText(v, time = false) {
    if (!v) return '';
    const d = new Date(v);
    if (Number.isNaN(d.getTime())) return String(v);
    const p = (n) => String(n).padStart(2, '0');
    return `${d.getFullYear()}/${p(d.getMonth() + 1)}/${p(d.getDate())}${time ? ` ${p(d.getHours())}:${p(d.getMinutes())}` : ''}`;
}
