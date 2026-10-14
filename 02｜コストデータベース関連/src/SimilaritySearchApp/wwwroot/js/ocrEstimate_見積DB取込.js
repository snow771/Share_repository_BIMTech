// js/ocrEstimate_見積DB取込.js
// AI-OCR 画面に置いた「見積DB連携」の入口。既存の OCR 処理（ocr_AI-OCR.js）とは分けている。
//   📋 内容確認・取決め確定 … 選択中の結果を DB に取り込み（登録済みならそのまま）、確認・取決めウィンドウを開く
//   📥 既存OCR結果を取り込む … 構造化JSON（＋元PDF）を選んで取り込む（Azure は使わない）
//   🗂 保存済み見積を開く     … DB の見積一覧ウィンドウ
// 取込の前に、何を・どれだけ登録するかを確認ダイアログで見せる。

import { call } from './bridge_通信.js';
import { toast, withLoading } from './ui_共通部品.js';
import { esc } from './format_数値書式.js';

const $ = (id) => document.getElementById(id);

let request = null;   // 取込の指定（C# の ImportRequest）
let preview = null;   // 直近の取込前確認

/** @param {{ selectedJob: () => any }} opts 選択中の処理結果（ocr_AI-OCR.js の detail.job） */
export function initEstimateEntry(opts) {
    $('ocr-estimate-review').addEventListener('click', () => reviewSelected(opts.selectedJob()));
    $('ocr-import-existing').addEventListener('click', importExisting);
    $('ocr-open-saved').addEventListener('click', async () => {
        try {
            await call('openEstimateList');
        } catch (e) {
            toast(`保存済み見積を開けませんでした: ${e.message}`, 'error', null, 10000);
        }
    });

    const modal = $('ocr-import-modal');
    for (const el of modal.querySelectorAll('[data-close]')) el.addEventListener('click', close);
    modal.addEventListener('click', (e) => { if (e.target === modal) close(); });
    $('ocr-import-pdf').addEventListener('click', choosePdf);
    $('ocr-import-test').addEventListener('change', () => { if (request) request.isTestData = $('ocr-import-test').checked; });
    $('ocr-import-run').addEventListener('click', run);
}

/** 選択中の結果について、確認・取決めボタンを押せるか（押せない理由を title に出す） */
export function updateEstimateButton(job) {
    const btn = $('ocr-estimate-review');
    const reason = !job ? '結果を選んでください'
        : job.status === 'error' || job.status === 'canceled' ? '失敗・中止した結果は取り込めません'
            : !job.structuredJsonPath ? '構造化していない結果は取り込めません（「構造化 Excel・JSON を作成する」で実行するか、構造化だけやり直してください）'
                : '';
    btn.disabled = !!reason;
    btn.title = reason || 'この結果を DB に取り込み（登録済みならそのまま）、確認・取決めウィンドウを開きます';
}

function reviewSelected(job) {
    if (!job?.structuredJsonPath) {
        toast('構造化した結果を選んでください。', 'error');
        return;
    }
    openImport({
        structuredJsonPath: job.structuredJsonPath,
        structuredExcelPath: job.structuredExcelPath ?? null,
        rawJsonPath: job.rawJsonPath ?? null,
        pdfPath: /\.pdf$/i.test(job.pdfPath ?? '') ? job.pdfPath : null,   // OCR した元の PDF
        isTestData: false,
    });
}

async function importExisting() {
    const pick = await call('pickEstimateFile', {
        kind: 'open', title: '構造化JSON（〜_structured.json）を選択',
        filter: '構造化JSON (*_structured*.json)|*_structured*.json|JSON (*.json)|*.json',
    });
    if (!pick.path) return;
    openImport({ structuredJsonPath: pick.path, structuredExcelPath: null, rawJsonPath: null, pdfPath: null, isTestData: false });
}

async function openImport(req) {
    request = req;
    $('ocr-import-test').checked = !!req.isTestData;
    await refresh();
}

async function refresh() {
    try {
        preview = await withLoading('取込の内容を確認しています...', () => call('estimateImportPreview', request));
    } catch (e) {
        toast(`取込の確認ができませんでした: ${e.message}`, 'error', null, 12000);
        return;
    }
    request = { ...preview.request, isTestData: $('ocr-import-test').checked };
    render();
    $('ocr-import-modal').classList.remove('hidden');
}

function render() {
    const p = preview;
    const m = p.metadata;
    const s = p.summary;
    const yen = (v) => (v == null ? '—' : Number(v).toLocaleString('ja-JP'));
    $('ocr-import-body').innerHTML = `
        ${p.existingHeaderId ? `<div class="er-ok">この構造化結果は DB に登録済みです（見積 #${p.existingHeaderId}）。取り込まずに、登録済みの見積を開きます。</div>` : ''}
        <dl class="ocr-import-summary">
            <dt>協力業者（OCR）</dt><dd>${esc(m.supplierName || '—')}</dd>
            <dt>工事名（OCR）</dt><dd>${esc(m.projectName || '—')}</dd>
            <dt>明細</dt><dd>${p.itemCount} 行（金額明細 ${s.moneyDetailCount} ／ 参考明細 ${s.referenceDetailCount}）・OCR行監査 ${p.auditCount} 行</dd>
            <dt>金額</dt><dd>表紙 ${yen(s.headerEstimateAmount)} 円 ／ 明細合計 ${yen(s.detailAmountTotal)} 円</dd>
        </dl>
        <table class="project-table st-table ocr-import-files">
            <thead><tr><th>資料</th><th>ファイル</th><th class="num">大きさ</th></tr></thead>
            <tbody>${p.files.map((f) => `<tr class="${f.path ? '' : 'missing'}">
                <td>${esc(f.label)}${f.required ? ' *' : ''}</td>
                <td class="wrap" title="${esc(f.path ?? '')}">${f.path ? esc(f.path.split(/[\\/]/).pop()) : '<span class="er-muted">なし</span>'}</td>
                <td class="num">${f.size == null ? '' : kb(f.size)}</td></tr>`).join('')}</tbody>
        </table>
        ${p.warnings.length ? `<div class="er-warnings"><ul>${p.warnings.map((w) => `<li>${esc(w)}</li>`).join('')}</ul></div>` : ''}
        <p class="st-hint">取り込むと、明細・行監査と資料の本体（構造化JSON・Excel・統合OCR JSON・元PDF）を DB に保存します。元のファイルは消しません。同じ内容をもう一度取り込んでも、重複して登録はしません。</p>`;
    $('ocr-import-run').textContent = p.existingHeaderId ? '登録済みの見積を開く' : '取り込んで開く';
    $('ocr-import-pdf').textContent = request.pdfPath ? '📄 元PDFを選び直す' : '📄 元PDFを選ぶ';
    $('ocr-import-test').disabled = !!p.existingHeaderId;
}

async function choosePdf() {
    const pick = await call('pickEstimateFile', { kind: 'open', title: 'この見積の元PDFを選択', filter: 'PDF (*.pdf)|*.pdf', initial: request?.structuredJsonPath });
    if (!pick.path) return;
    request.pdfPath = pick.path;
    await refresh();
}

async function run() {
    try {
        if (preview.existingHeaderId && !request.pdfPath) {
            await call('openEstimateWindow', { headerId: preview.existingHeaderId });
            close();
            return;
        }
        const res = await withLoading('DB に取り込んでいます...', () => call('estimateImport', request));
        close();
        toast(res.reused
            ? `登録済みの見積 #${res.headerId} を開きました。${res.addedDocuments.length ? `（${res.addedDocuments.join('・')} を追加）` : ''}`
            : `見積 #${res.headerId} として取り込みました（${res.addedDocuments.join('・')}）。`, 'success', null, 8000);
        for (const w of res.warnings) toast(w, 'info', null, 10000);
    } catch (e) {
        toast(`取り込めませんでした: ${e.message}`, 'error', null, 15000);
    }
}

function close() {
    $('ocr-import-modal').classList.add('hidden');
}

const kb = (n) => (n >= 1048576 ? `${(n / 1048576).toFixed(1)}MB` : `${Math.max(1, Math.round(n / 1024))}KB`);
