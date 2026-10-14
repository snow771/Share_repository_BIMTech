// js/mock_見積DBモック.js
// 画面確認用: 見積DB連携（取込・確認・取決め）のモック。WebView2 の外（普通のブラウザ）で開いたときだけ使われる。
// AI-OCR 画面と確認ウィンドウ（別ウィンドウ）で同じデータを使えるよう、localStorage に保存する。
// 計算は本番（C# の AgreementCalculator）と同じ規則の簡略版で、確定条件も主なものだけ。

const KEY = 'mockEstimateDb.v1';
const OPERATOR = 'CORP\\yamada';

const SUPPLIERS = [
    { supplierId: 11, code: '002828', name: '中川インテリア株式会社', kana: 'ナカガワインテリア', address: '東京都江東区新砂1丁目2-3', phone: '03-1234-5678', department: '内装' },
    { supplierId: 12, code: '000277', name: '柏田工業株式会社', kana: 'カシワダコウギョウ', address: '千葉県船橋市本町3丁目4-5', phone: '047-111-2222', department: '左官' },
    { supplierId: 13, code: '002423', name: '株式会社八千代工業', kana: 'ヤチヨコウギョウ', address: '千葉県八千代市大和田新田1-1', phone: '047-333-4444', department: null },
    { supplierId: 14, code: '015676', name: '株式会社八千代工業', kana: 'ヤチヨコウギョウ', address: '埼玉県川口市本町2-2', phone: '048-555-6666', department: 'LGS' },
    { supplierId: 15, code: '003901', name: '株式会社昇工業', kana: 'ノボルコウギョウ', address: '東京都墨田区押上1-1-1', phone: '03-9999-0000', department: '防水' },
];
const PROJECTS = [
    { projectId: 'J02201', name: 'Mill押上', address: '東京都墨田区押上1丁目', structure: 'RC造' },
    { projectId: 'J02145', name: '文京区本郷4丁目計画', address: '東京都文京区本郷4丁目', structure: 'RC造' },
    { projectId: 'J02133', name: '(仮称)墨田区緑1丁目計画新築工事', address: '東京都墨田区緑1丁目', structure: 'RC造' },
    { projectId: 'J02120', name: '(仮称)K様亀戸2丁目計画新築工事', address: '東京都江東区亀戸2丁目', structure: 'RC造' },
];

const LINES = [
    ['シーリング工事', '外部', 'スリット目地 仕上', 'ポリウレタン', '30*25', 14, 'm', 1050, 14700],
    ['シーリング工事', '外部', 'サッシ廻り', '変成シリコン', '10*10', 410, 'm', 1100, 451000],
    ['シーリング工事', '外部', 'サッシ廻り', '変成シリコン', '10*10', 410, 'm', 1100, 451000, '同一明細候補'],
    ['防水工事', '屋上', 'ウレタン塗膜防水', 'X-2工法', 't=2.0', 128.5, '㎡', 6800, 873800],
    ['防水工事', '屋上', 'ドレン改修', '鋳鉄製', 'φ100', 6, 'ヶ所', 18000, 108500, '', '要確認'],
    ['防水工事', 'バルコニー', '既存防水撤去', '', '', null, '', null, null],
    ['諸経費', '', '運搬費', '', '', 1, '式', 60000, 60000],
    ['', '', '諸経費', '', '', 1, '式', 150000, 150000],
];

function load() {
    try {
        return JSON.parse(localStorage.getItem(KEY)) ?? { seq: 0, estimates: {} };
    } catch {
        return { seq: 0, estimates: {} };
    }
}

function save(db) {
    try {
        localStorage.setItem(KEY, JSON.stringify(db));
    } catch {
        // 保存できなくても画面確認は続ける
    }
}

const rv = () => btoa(String(Math.random())).slice(0, 12);
const headerIdOf = () => +(new URLSearchParams(location.search).get('id') ?? 0);

function makeEstimate(id, req) {
    const name = (req.structuredJsonPath ?? '').split(/[\\/]/).pop() ?? '';
    const supplierName = name.includes('内装') ? '中川インテリア株式会社' : name.includes('LGS') ? '株式会社八千代工業' : '株式会社昇 工';
    const projectName = name.includes('内装') ? '文京区本郷4 丁目計画' : name.includes('LGS') ? '(仮称)K様亀戸2丁目計画新築工事' : 'Mill押上';
    const lines = LINES.map(([trade, section, item, mat, dim, q, unit, up, am, dup = '', check = 'OK'], i) => {
        const money = am != null;
        return {
            lineId: id * 100 + i + 1, order: i + 1, recordType: money ? '金額明細' : '参考明細', pdfPage: Math.floor(i / 3) + 1, ocrTableIndex: 1, ocrRowIndex: i + 2,
            trade, section, itemName: item, materialSpec: mat || null, dimensionSpec: dim || null, quantity: q, unit: unit || null, unitPrice: up, amount: am,
            remarksOcr: null, calculatedAmount: money ? q * up : null, difference: money ? am - q * up : null,
            amountCheck: money ? (check === '要確認' ? '要確認' : 'OK') : '対象外', duplicateCandidate: dup || null,
            reviewRequired: (!money || dup || check === '要確認') ? '要確認' : null,
            reviewReason: !money ? '単価・金額が未記載の参考明細（集計対象外）' : dup ? 'ページ継続・重複を確認' : check === '要確認' ? '数量×単価と金額が不一致' : null,
            humanReviewStatus: '未確認', humanReviewNotes: null, reviewedBy: null, reviewedAt: null,
            isCalculationTarget: money, exclusionReason: money ? null : '参考明細（計算対象外）',
            correctedQuantity: null, correctedUnit: null, correctedUnitPrice: null, correctedAmount: null, correctionReason: null, rowVersion: rv(),
        };
    });
    const total = lines.filter((l) => l.recordType === '金額明細').reduce((s, l) => s + l.amount, 0);
    const docs = [
        { documentId: id * 10 + 1, type: 'StructuredJson', fileName: name || 'sample_structured.json', size: 48211, storedAt: new Date().toISOString(), storedBy: OPERATOR, hashHex: 'a1b2c3d4e5f6a7b8' },
        { documentId: id * 10 + 2, type: 'StructuredExcel', fileName: name.replace('.json', '.xlsx') || 'sample_structured.xlsx', size: 30411, storedAt: new Date().toISOString(), storedBy: OPERATOR, hashHex: 'b2c3d4e5f6a7b8c9' },
    ];
    if (req.pdfPath) docs.push(pdfDoc(id, req.pdfPath));
    return {
        identity: req.structuredJsonPath,
        header: {
            headerId: id, batchId: id, projectId: null, projectName: null, projectAddress: null, supplierId: null, supplierCode: null, supplierName: null, supplierAddress: null,
            projectNameOcr: projectName, supplierNameOcr: supplierName, estimateIdOcr: null, estimateDate: null,
            headerEstimateAmount: name.includes('防水') || !name ? 1000000 : null, detailAmountTotal: total, differenceToHeaderAmount: null,
            reviewStatus: '未確認', isTestData: !!req.isTestData, importedAt: new Date().toISOString(), importedBy: OPERATOR,
            commonRulesFile: 'RulesCommon_共通ルール.json', supplierRulesFile: null, rowVersion: rv(),
        },
        lines,
        audit: lines.map((l) => ({ pdfPage: l.pdfPage, ocrTableIndex: 1, ocrRowIndex: l.ocrRowIndex, rowStatus: '採用', reason: l.recordType, rowText: `${l.itemName} | ${l.materialSpec ?? ''} | ${l.quantity ?? ''}` })),
        documents: docs,
        agreements: [],
    };
}

const pdfDoc = (id, path) => ({ documentId: id * 10 + 5 + Math.floor(Math.random() * 4), type: 'OriginalPdf', fileName: path.split(/[\\/]/).pop(), size: 812345, storedAt: new Date().toISOString(), storedBy: OPERATOR, hashHex: 'c3d4e5f6a7b8c9d0' });

const round = (v, d) => {
    const f = 10 ** d;
    return Math.sign(v) * Math.round(Math.abs(v) * f) / f;
};

function compute(e, input) {
    const h = e.header;
    const blocking = [];
    const warnings = [];
    if (!h.projectId) blocking.push('物件が紐づけられていません（上の「紐づけ」で物件を選んで保存してください）。');
    if (!h.supplierId) blocking.push('業者が紐づけられていません（上の「紐づけ」で業者を選んで保存してください）。');
    const hasPdf = e.documents.some((d) => d.type === 'OriginalPdf');
    if (!hasPdf) blocking.push('元PDFが登録されていません。右の「＋ PDFを追加」で元PDFを登録してください（下書き保存はできます）。');
    else if (!input.evidencePage) blocking.push('取決め総額の根拠になる PDF のページ番号を入力してください。');
    const base = input.baseAmount == null ? null : Number(input.baseAmount);
    const agreed = input.agreedAmount == null ? null : Number(input.agreedAmount);
    const detailTotal = e.lines.filter((l) => l.recordType === '金額明細').reduce((s, l) => s + Number(l.correctedAmount ?? l.amount ?? 0), 0)
        + (input.overheadSource === 'input' ? Number(input.overheadAmount ?? 0) : 0);
    const differs = h.headerEstimateAmount != null && h.headerEstimateAmount !== detailTotal;
    if (!input.baseSource) blocking.push('基準総額の根拠（表紙金額／明細合計／確認入力）を選んでください。');
    if (!(base > 0)) blocking.push('基準総額は 0 より大きい金額にしてください（0 では掛率を計算できません）。');
    if ((input.baseSource === '確認入力' || differs) && !input.baseReason) {
        blocking.push(differs ? `表紙金額（${h.headerEstimateAmount.toLocaleString()}円）と金額明細の合計（${detailTotal.toLocaleString()}円）が異なります。基準総額を選んだ理由を入力してください。` : '「確認入力」を選んだ場合は、基準総額の根拠を入力してください。');
    }
    if (!input.taxBasis) blocking.push('税区分（税抜／税込）を選んでください。');
    if (agreed == null) blocking.push('取決め総額を入力してください。');
    else if (agreed === 0 && !input.zeroConfirmed) blocking.push('取決め総額が 0 円です。0 円で確定する場合は「0 円で確定する」にチェックしてください。');
    const rate = base > 0 && agreed != null && agreed >= 0 ? round(agreed / base, 12) : null;
    const ohIds = new Set(input.overheadSource === 'lines' ? input.overheadLineIds ?? [] : []);
    const isOh = (l) => ohIds.has(l.lineId);
    const amt = (l) => Number(l.correctedAmount ?? l.amount ?? 0);
    const overhead = input.overheadSource === 'lines' ? e.lines.filter(isOh).reduce((x, l) => x + amt(l), 0)
        : input.overheadSource === 'input' ? Number(input.overheadAmount ?? 0) : 0;
    if (input.overheadSource === 'lines' && !ohIds.size) blocking.push('諸経費として扱う明細の行を選んでください（無ければ「諸経費なし」か「手入力」にしてください）。');
    const itemsLines = e.lines.filter((l) => !isOh(l) && (l.isCalculationTarget ?? l.recordType === '金額明細')).reduce((x, l) => x + amt(l), 0);
    const itemsTotal = input.itemsTotalOverride != null && input.overheadSource !== 'none' ? Number(input.itemsTotalOverride) : itemsLines;
    if (itemsTotal !== itemsLines && input.overheadSource !== 'none') warnings.push(`内訳合計を ${itemsTotal.toLocaleString()} 円にしています（計算対象の明細の合計は ${itemsLines.toLocaleString()} 円）。`);
    const ohRate = (input.overheadSource === 'lines' || input.overheadSource === 'input') && overhead && itemsTotal > 0 ? round((itemsTotal + overhead) / itemsTotal, 12) : null;
    const factor = ohRate ?? 1;
    if (ohRate) warnings.push(`諸経費 ${overhead.toLocaleString()} 円を各明細の単価へ配分しました（諸経費掛率 ${(ohRate * 100).toFixed(4)}%）。換算単価は 単価 × 諸経費掛率 × 掛率 です。`);
    if (rate > 1) {
        warnings.push(`掛率が ${(rate * 100).toFixed(2)}% で、見積より増額です。`);
        if (!input.increaseReason) blocking.push('掛率が 100% を超えています（増額）。増額の理由を入力してください。');
    }
    let total = 0;
    const need = [];
    const lines = e.lines.map((l) => {
        const target = !isOh(l) && (l.isCalculationTarget ?? l.recordType === '金額明細');
        const up = l.correctedUnitPrice ?? l.unitPrice;
        const am = l.correctedAmount ?? l.amount;
        let cup = null;
        let cam = null;
        if (target) {
            if ((l.amountCheck === '要確認' || l.duplicateCandidate || l.reviewRequired) && l.humanReviewStatus !== '確認済') need.push(`${l.order}行目「${l.itemName}」`);
            if (rate != null) {
                cup = up == null ? null : round(up * factor * rate / 10, 0) * 10;
                cam = am == null ? null : round(am * factor * rate, 0);
                total += cam ?? 0;
            }
        }
        const oup = target && ohRate && up != null ? round(up * ohRate / 10, 0) * 10 : null;
        const oam = target && ohRate && am != null ? round(am * ohRate, 0) : null;
        return { lineId: l.lineId, order: l.order, recordType: l.recordType, itemName: l.itemName, materialSpec: l.materialSpec, dimensionSpec: l.dimensionSpec,
            quantity: l.correctedQuantity ?? l.quantity, unit: l.correctedUnit ?? l.unit, unitPrice: up, amount: am, isTarget: target,
            exclusionReason: target ? null : isOh(l) ? '諸経費（各明細の単価へ配分）' : l.exclusionReason ?? '計算対象外', convertedUnitPrice: cup, convertedAmount: cam,
            isOverhead: isOh(l), overheadIncludedUnitPrice: oup, overheadIncludedAmount: oam,
            corrected: l.correctedAmount != null || l.correctedUnitPrice != null || l.correctedQuantity != null };
    });
    if (need.length) blocking.push(`要確認・検算不一致・重複候補の明細が ${need.length} 行、未確認のままです（「確認済」にするか計算対象外にしてください）: ${need.join('、')}`);
    const residual = rate == null ? null : agreed - total;
    if (residual) {
        warnings.push(`取決め総額と換算金額の合計に ${residual.toLocaleString()} 円の差（残差）があります。明細へは配賦せず、残差として保存します。`);
        if (!input.residualReason) blocking.push(`残差が ${residual.toLocaleString()} 円あります（端数・税・値引き・明細の欠落など）。理由を入力してください。`);
    }
    const targets = lines.filter((l) => l.isTarget).length;
    return { rate, convertedTotal: rate == null ? null : total, residual, baseAmount: base, agreedAmount: agreed, targetCount: targets, excludedCount: lines.length - targets,
        lines, blocking, warnings, canConfirm: blocking.length === 0,
        itemsTotal, overheadAmount: input.overheadSource === 'lines' || input.overheadSource === 'input' ? overhead : null, overheadRate: ohRate };
}

function notes(i) {
    return [i.notes, i.residualReason ? `【残差の理由】${i.residualReason}` : null, i.increaseReason ? `【増額の理由】${i.increaseReason}` : null].filter(Boolean).join('\n') || null;
}

function agreementFrom(e, i, status, revision, p, extra = {}) {
    return {
        agreementId: Date.now() % 100000 + revision, revisionNo: revision, status, previousAgreementId: extra.previous ?? null,
        baseEstimateAmount: i.baseAmount == null ? null : Number(i.baseAmount), baseAmountSource: i.baseSource, baseAmountReason: i.baseReason,
        agreedAmount: i.agreedAmount == null ? null : Number(i.agreedAmount), taxBasis: i.taxBasis, appliedRate: p?.rate ?? null, agreementDate: i.agreementDate,
        evidenceDocumentId: i.evidenceDocumentId, evidencePage: i.evidencePage, convertedDetailTotal: p?.convertedTotal ?? null, residualAmount: p?.residual ?? null,
        confirmedBy: status === 'Draft' ? null : OPERATOR, confirmedAt: status === 'Draft' ? null : new Date().toISOString(), notes: notes(i),
        changeReason: extra.changeReason ?? null, rowVersion: rv(), confirmRequestKey: extra.key ?? null,
        overheadSource: i.overheadSource ?? 'none', overheadAmount: p?.overheadAmount ?? (i.overheadAmount == null ? null : Number(i.overheadAmount)),
        overheadRate: p?.overheadRate ?? null, overheadLineIds: i.overheadLineIds ?? [], overheadItemsTotal: p?.itemsTotal ?? null,
        lines: status === 'Draft' ? [] : p.lines.map((l, n) => ({
            agreementLineId: n + 1, lineId: l.lineId, lineOrder: l.order, recordType: l.recordType, itemName: l.itemName, materialSpec: l.materialSpec, dimensionSpec: l.dimensionSpec,
            quantity: l.quantity, unit: l.unit, originalUnitPrice: l.unitPrice, originalAmount: l.amount, appliedRate: l.isTarget ? p.rate : null,
            convertedUnitPrice: l.convertedUnitPrice, convertedAmount: l.convertedAmount, isCalculationTarget: l.isTarget, exclusionReason: l.exclusionReason,
            calculationType: l.isTarget ? 'TotalRateAllocation' : l.isOverhead ? 'Overhead' : 'Excluded',
            overheadIncludedUnitPrice: l.overheadIncludedUnitPrice, overheadIncludedAmount: l.overheadIncludedAmount })),
    };
}

function detailOf(db, id) {
    const e = db.estimates[id];
    if (!e) throw new Error(`見積 #${id} が見つかりません（モック）。AI-OCR 画面から取り込んでください。`);
    e.agreements.sort((a, b) => b.revisionNo - a.revisionNo);
    return { ...e, locked: e.agreements[0]?.status === 'Confirmed', operator: OPERATOR };
}

function rank(items, fields, q, keyword) {
    const n = (s) => String(s ?? '').normalize('NFKC').replace(/\s/g, '');
    const qq = n(q);
    if (!qq) return [];
    return items.map((it) => {
        let best = 0;
        for (const f of fields(it)) {
            const v = n(f);
            if (!v) continue;
            const s = v === qq ? 3 : (qq.length >= 3 && v.includes(qq)) || (v.length >= 3 && qq.includes(v)) ? 2 : keyword && v.includes(qq) ? 1 : 0;
            best = Math.max(best, s);
        }
        return { it, best };
    }).filter((x) => x.best > 0).sort((a, b) => b.best - a.best)
        .map((x) => ({ ...x.it, match: ['', '検索語を含む', '部分一致', '正規化一致'][x.best] }));
}

/** 見積DB連携のコマンドなら結果を返す。対象外なら undefined。 */
export async function handleEstimate(command, p) {
    const db = load();
    const id = headerIdOf();
    switch (command) {
        // ── AI-OCR 画面の入口 ──
        case 'pickEstimateFile':
            if (p.title.includes('PDF')) return { path: 'C:\\見積OCR\\防水見積_元.pdf' };
            return { path: 'C:\\見積OCR\\testフォルダ\\防水_rotated_20261009_144459\\02_構造化\\防水_rotated_20261009_144459_full_structured.json' };
        case 'estimateImportPreview': {
            const existing = Object.values(db.estimates).find((e) => e.identity === p.structuredJsonPath);
            const sample = makeEstimate(0, p);
            const money = sample.lines.filter((l) => l.recordType === '金額明細').length;
            const base = p.structuredJsonPath.replace(/\.json$/i, '');
            return {
                request: { ...p, structuredExcelPath: `${base}.xlsx`, rawJsonPath: p.structuredJsonPath.replace('02_構造化', '01_OCR抽出').replace('_full_structured.json', '_layout_raw_full.json') },
                files: [
                    { type: 'StructuredJson', label: '構造化JSON（明細の正本）', path: p.structuredJsonPath, size: 48211, required: true },
                    { type: 'StructuredExcel', label: '構造化Excel', path: `${base}.xlsx`, size: 30411, required: false },
                    { type: 'RawJson', label: '統合OCR JSON（再構造化用）', path: 'C:\\…\\01_OCR抽出\\layout_raw_full.json', size: 912345, required: false },
                    { type: 'OriginalPdf', label: '元PDF', path: p.pdfPath ?? null, size: p.pdfPath ? 812345 : null, required: false },
                ],
                metadata: { supplierName: sample.header.supplierNameOcr, projectName: sample.header.projectNameOcr },
                summary: { moneyDetailCount: money, referenceDetailCount: sample.lines.length - money, headerEstimateAmount: sample.header.headerEstimateAmount, detailAmountTotal: sample.header.detailAmountTotal },
                itemCount: sample.lines.length, auditCount: sample.audit.length, existingHeaderId: existing?.header.headerId ?? null,
                warnings: [
                    ...(existing ? [`この構造化結果は登録済みです（見積 #${existing.header.headerId}）。取り込まずに、登録済みの見積を開きます。`] : []),
                    ...(p.pdfPath ? [] : ['元PDFが指定されていません。下書き・確認はできますが、取決めの確定には元PDFが必要です（あとから追加できます）。']),
                ],
            };
        }
        case 'estimateImport': {
            let e = Object.values(db.estimates).find((x) => x.identity === p.structuredJsonPath);
            const reused = !!e;
            const added = [];
            if (!e) {
                const newId = ++db.seq;
                e = makeEstimate(newId, p);
                db.estimates[newId] = e;
                added.push('構造化JSON', '構造化Excel', '統合OCR JSON');
                if (p.pdfPath) added.push('元PDF');
            } else if (p.pdfPath && !e.documents.some((d) => d.type === 'OriginalPdf')) {
                e.documents.push(pdfDoc(e.header.headerId, p.pdfPath));
                added.push('元PDF');
            }
            save(db);
            window.open(`estimate-review.html?id=${e.header.headerId}`, `estimate-${e.header.headerId}`, 'width=1500,height=940');
            return { headerId: e.header.headerId, reused, addedDocuments: added, warnings: p.pdfPath ? [] : ['元PDFが未登録です。下書き・確認はできますが、取決めの確定には元PDFの登録が必要です。'] };
        }
        case 'openEstimateWindow':
            window.open(`estimate-review.html?id=${p.headerId}`, `estimate-${p.headerId}`, 'width=1500,height=940');
            return { opened: true };
        case 'openEstimateList':
            window.open('estimate-review.html', 'estimate-list', 'width=1400,height=900');
            return { opened: true };

        // ── 確認ウィンドウ ──
        case 'getContext': return { mode: id ? 'detail' : 'list', headerId: id || null, operator: OPERATOR };
        case 'list':
            return Object.values(db.estimates).map((e) => {
                const a = [...e.agreements].sort((x, y) => y.revisionNo - x.revisionNo)[0];
                const h = e.header;
                const state = a ? `${a.status === 'Confirmed' ? '確定' : '下書き'} 第${a.revisionNo}版` : h.projectId && h.supplierId ? '未確定' : '未紐づけ';
                return { headerId: h.headerId, importedAt: h.importedAt, importedBy: h.importedBy, supplierNameOcr: h.supplierNameOcr, projectNameOcr: h.projectNameOcr,
                    projectId: h.projectId, projectName: h.projectName, supplierId: h.supplierId, supplierCode: h.supplierCode, supplierName: h.supplierName,
                    lineCount: e.lines.length, detailAmountTotal: h.detailAmountTotal, state, revisionNo: a?.revisionNo ?? null, isTestData: h.isTestData,
                    hasPdf: e.documents.some((d) => d.type === 'OriginalPdf') };
            }).filter((r) => (p.includeTest || !r.isTestData)
                && (p.state === 'all' || (p.state === 'unlinked' ? (!r.projectId || !r.supplierId) : p.state === 'confirmed' ? r.state.startsWith('確定') : !r.state.startsWith('確定')))
                && (!p.keyword || JSON.stringify(r).includes(p.keyword)))
                .sort((a, b) => b.headerId - a.headerId);
        case 'openEstimate':
            window.open(`estimate-review.html?id=${p.headerId}`, `estimate-${p.headerId}`, 'width=1500,height=940');
            return { opened: true };
        case 'detail': return detailOf(db, id);
        case 'projectCandidates':
            return rank(PROJECTS, (x) => [x.name, x.projectId, x.address], p.keyword || db.estimates[id]?.header.projectNameOcr, !!p.keyword);
        case 'supplierCandidates':
            return rank(SUPPLIERS, (x) => [x.name, x.code, x.kana], p.keyword || db.estimates[id]?.header.supplierNameOcr, !!p.keyword);
        case 'saveLink': {
            const e = db.estimates[id];
            if (e.header.rowVersion !== p.rowVersion) throw new Error('他の人（または別の画面）が先に変更したため、保存しませんでした。「⟳ 再読込」で最新の内容を読み込んでから、もう一度操作してください。');
            const pr = PROJECTS.find((x) => x.projectId === p.projectId);
            const su = SUPPLIERS.find((x) => x.supplierId === p.supplierId);
            Object.assign(e.header, { projectId: pr?.projectId ?? null, projectName: pr?.name ?? null, projectAddress: pr?.address ?? null,
                supplierId: su?.supplierId ?? null, supplierCode: su?.code ?? null, supplierName: su?.name ?? null, supplierAddress: su?.address ?? null, rowVersion: rv() });
            save(db);
            return { rowVersion: e.header.rowVersion };
        }
        case 'saveLineReview': {
            const e = db.estimates[id];
            const l = e.lines.find((x) => x.lineId === p.lineId);
            if ((p.humanReviewStatus === '計算対象外' || !p.isCalculationTarget) && !p.exclusionReason) throw new Error('計算対象外にする場合は、理由を入力してください。');
            const excluded = p.humanReviewStatus === '計算対象外' || !p.isCalculationTarget;
            const num = (v) => (v == null ? null : Number(v));
            Object.assign(l, { humanReviewStatus: p.humanReviewStatus, humanReviewNotes: p.humanReviewNotes, reviewedBy: OPERATOR, reviewedAt: new Date().toISOString(),
                isCalculationTarget: !excluded, exclusionReason: excluded ? p.exclusionReason : null, correctedQuantity: num(p.correctedQuantity), correctedUnit: p.correctedUnit,
                correctedUnitPrice: num(p.correctedUnitPrice), correctedAmount: num(p.correctedAmount), correctionReason: p.correctionReason, rowVersion: rv() });
            save(db);
            return l;
        }
        case 'preview': return compute(db.estimates[id], p);
        case 'saveDraft': {
            const e = db.estimates[id];
            e.agreements.sort((a, b) => b.revisionNo - a.revisionNo);
            const latest = e.agreements[0];
            const pv = compute(e, p);
            const draft = agreementFrom(e, p, 'Draft', latest?.revisionNo ?? 1, pv, { previous: latest?.previousAgreementId, changeReason: latest?.changeReason });
            if (latest?.status === 'Draft') e.agreements[0] = { ...draft, agreementId: latest.agreementId };
            else e.agreements.unshift(draft);
            save(db);
            return e.agreements[0];
        }
        case 'confirm': {
            const e = db.estimates[id];
            const done = e.agreements.find((a) => a.confirmRequestKey === p.requestKey);
            if (done) return done;
            const pv = compute(e, p);
            if (!pv.canConfirm) throw new Error(`確定できません。\n・${pv.blocking.join('\n・')}`);
            e.agreements.sort((a, b) => b.revisionNo - a.revisionNo);
            const latest = e.agreements[0];
            const prev = e.agreements.find((a) => a.status === 'Confirmed');
            if (prev) prev.status = 'Superseded';
            const rev = latest?.status === 'Draft' ? latest.revisionNo : (latest?.revisionNo ?? 0) + 1;
            const a = agreementFrom(e, p, 'Confirmed', rev, pv, { previous: prev?.agreementId, key: p.requestKey, changeReason: latest?.status === 'Draft' ? latest.changeReason : null });
            if (latest?.status === 'Draft') e.agreements[0] = a;
            else e.agreements.unshift(a);
            save(db);
            return a;
        }
        case 'newRevision': {
            const e = db.estimates[id];
            e.agreements.sort((a, b) => b.revisionNo - a.revisionNo);
            const latest = e.agreements[0];
            const input = { baseSource: latest.baseAmountSource, baseAmount: latest.baseEstimateAmount, baseReason: latest.baseAmountReason, taxBasis: latest.taxBasis,
                agreedAmount: latest.agreedAmount, agreementDate: latest.agreementDate, evidenceDocumentId: latest.evidenceDocumentId, evidencePage: latest.evidencePage, notes: latest.notes,
                overheadSource: latest.overheadSource, overheadAmount: latest.overheadSource === 'input' ? latest.overheadAmount : null, overheadLineIds: latest.overheadLineIds };
            const draft = agreementFrom(e, input, 'Draft', latest.revisionNo + 1, null, { previous: latest.agreementId, changeReason: p.reason });
            e.agreements.unshift(draft);
            save(db);
            return draft;
        }
        case 'deleteEstimate': {
            const e = db.estimates[id];
            if (e.header.rowVersion !== p.rowVersion) throw new Error('他の人（または別の画面）が先に変更したため、保存しませんでした。');
            const fixed = e.agreements.filter((a) => a.status !== 'Draft').length;
            if (fixed && p.confirmText !== String(id)) throw new Error(`確定済みの取決めがある見積です。削除するには、確認のため見積ID「${id}」を入力してください。`);
            if (fixed && !p.reason) throw new Error('確定済みの取決めがある見積を削除する理由を入力してください。');
            const rows = e.lines.length + e.audit.length + e.documents.length + e.agreements.length + 2;
            delete db.estimates[id];
            save(db);
            return { headerId: id, rows, changeBatchId: 99 };
        }
        case 'closeWindow':
            setTimeout(() => window.close(), 50);
            return { closed: true };
        case 'showPdf': return { url: 'about:blank', fileName: 'モック.pdf' };
        case 'openDocument': return { opened: true, fileName: 'モック.xlsx' };
        case 'addPdf': {
            const e = db.estimates[id];
            e.documents.push(pdfDoc(id, 'C:\\見積OCR\\防水見積_元.pdf'));
            save(db);
            return { added: true, canceled: false, pages: 6, fileName: '防水見積_元.pdf' };
        }
        default:
            return undefined;
    }
}
