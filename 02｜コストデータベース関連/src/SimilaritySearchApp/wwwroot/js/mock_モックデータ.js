// js/mock_モックデータ.js
// 画面確認用のモック。WebView2 の外（普通のブラウザ）で index.html を開いたときだけ使われる。
// C# の WebBridge と同じ形の JSON を返す。計算は簡略版で、本番の数値とは一致しない。

import { percentile } from './chartRenderer_グラフ描画.js';
import { emit } from './state_画面状態.js';
import { handleEstimate } from './mock_見積DBモック.js';

const U = '(未指定)';
const PREFS = { '東京都': ['港区', '新宿区', '世田谷区', '江東区'], '神奈川県': ['横浜市', '川崎市'], '千葉県': ['千葉市', '船橋市'], '埼玉県': ['さいたま市', '川口市'] };
const TRADES = { '内装工事': ['内装工事'], '仮設工事': ['足場工事', '仮設建物費'], '鉄筋工事': ['鉄筋工事'], '型枠工事': ['型枠工事'] };

let seed = 7;
const rand = () => ((seed = (seed * 16807) % 2147483647) / 2147483647);

const projects = Array.from({ length: 64 }, (_, i) => {
    const pref = Object.keys(PREFS)[Math.floor(rand() * 4)];
    const city = PREFS[pref][Math.floor(rand() * PREFS[pref].length)];
    const area = Math.round(800 + rand() * 6000);
    return {
        projectId: `J${String(2101 + i).padStart(5, '0')}`,
        name: `サンプル${['レジデンス', 'マンション', 'ハイツ', 'コート'][i % 4]} ${city}${i + 1}`,
        prefecture: pref, city, town: `${city}${(i % 3) + 1}丁目`,
        structure: rand() > 0.15 ? 'RC造' : 'SRC造',
        form: rand() > 0.2 ? '共同住宅' : '店舗併用住宅',
        floorsAbove: 3 + Math.floor(rand() * 12), floorsBelow: rand() > 0.8 ? 1 : 0,
        legalFloorArea: area, constructionFloorArea: Math.round(area * (1.05 + rand() * 0.1)),
        base: 180000 + rand() * 90000, deflator: 1 + rand() * 0.12,
    };
});

// 標準単価（建築材料マスタ）のサンプル: 工種コード → 材料
const CODE_OF = { '内装工事': ['06-11-10000', '06-11-20000'], '仮設工事': ['01-16-10000'], '鉄筋工事': ['02-05-30000'], '型枠工事': ['02-04-10000'] };
const MATERIALS = [
    ['06-11-10000', 'ﾎﾞｰﾄﾞ', [['PB 12.5mm', '910×1820', '㎡', 0, 0], ['PB 9.5mm', '910×1820', '㎡', 820, 800], ['強化PB 21mm', '910×1820', '㎡', 1650, 1600]]],
    ['06-11-20000', '置床･ﾌﾛｰﾘﾝｸﾞ', [['置床 H=150', null, '㎡', 5200, 5000], ['複合ﾌﾛｰﾘﾝｸﾞ', 't=12', '㎡', 6100, null]]],
    ['01-16-10000', '直接仮設', [['枠組足場', 'W=900', '架㎡', 1250, 1180], ['朝顔2段目', null, 'm', 3800, 3600]]],
    ['02-05-30000', '鉄筋工事加工組立費', [['加工組立', 'D10〜D25', 't', 68000, 65000], ['圧接', 'D22以上', '箇所', 900, 850]]],
    ['02-04-10000', '型枠工事', [['普通型枠', '合板', '㎡', 5400, 5200]]],
].flatMap(([code, sheet, rows]) => rows.map(([name, spec, unit, a, r], i) => ({
    id: null, workTypeCode: code, materialCode: `${code}-${i + 1}`, name, specification: spec, notes: i === 0 ? '※数量は図面より拾い出し' : null,
    unit, assessPrice: a, revisePrice: r, sourceSheet: sheet, sourceRow: i + 2, reviewStatus: i === 2 ? '要確認' : '確認済',
})));

let selected = null;
let last = null;

// 変更履歴のモック（新しい順）
let historySeq = 3;
const HISTORY = [
    { batchId: 3, executedAt: '2026-10-06T16:40:00', userName: 'CORP\\suzuki', machineName: 'PC-0456', dbLogin: 'CORP\\suzuki', source: 'Stencile登録',
        summary: 'Stencile登録: 物件 2 件（新規 2 / 更新 0）・査定単価 746 行・修正単価 746 行', tables: 'ProjectInformation_物件情報, Appraisal_Unit_Price_査定単価', rowTotal: 1494,
        revertsBatchIds: null, revertedByBatchId: null,
        rows: [{ op: 'INSERT', table: 'ProjectInformation_物件情報', after: { Project_id: 'J02201', ProjectName: '渋谷神宮前レジデンス', LegalFloorArea: '544.7' } }] },
    { batchId: 2, executedAt: '2026-10-05T10:12:00', userName: 'CORP\\yamada', machineName: 'PC-0123', dbLogin: 'CORP\\yamada', source: 'DB編集',
        summary: '建築材料マスタ: 更新2', tables: 'MaterialMaster_建築材料マスタ', rowTotal: 2, revertsBatchIds: null, revertedByBatchId: null,
        rows: [
            { op: 'UPDATE', table: 'MaterialMaster_建築材料マスタ', before: { MaterialMaster_ID: '2', 'Revised_Unit_Price_修正単価': '780' }, after: { MaterialMaster_ID: '2', 'Revised_Unit_Price_修正単価': '800' } },
            { op: 'UPDATE', table: 'MaterialMaster_建築材料マスタ', before: { MaterialMaster_ID: '5', 'Unit_単位': null }, after: { MaterialMaster_ID: '5', 'Unit_単位': '㎡' } },
        ] },
    { batchId: 1, executedAt: '2026-10-01T09:00:00', userName: 'CORP\\yamada', machineName: 'PC-0123', dbLogin: 'CORP\\yamada', source: 'Excel取込',
        summary: '建築材料マスタ: 追加10', tables: 'MaterialMaster_建築材料マスタ', rowTotal: 10, revertsBatchIds: null, revertedByBatchId: null,
        rows: [{ op: 'INSERT', table: 'MaterialMaster_建築材料マスタ', after: { MaterialMaster_ID: '11', 'Material_Name_名称': 'PB 15mm' } }] },
];

export async function handle(command, p) {
    await new Promise((r) => setTimeout(r, 120));
    switch (command) {
        case 'getSettings':
        case 'saveSettings':
            return {
                db: { server: 'SERVER\\SQLEXPRESS', database: 'Unit_Price_Display_単価表示', useWindowsAuth: true, user: '', hasPassword: false, trustServerCertificate: true, commandTimeoutSec: 120,
                    tables: { projects: 'ProjectInformation_物件情報', assess: 'Appraisal_Unit_Price_査定単価', revise: 'Revised_Unit_Price_修正単価', category: 'Category_カテゴリマスタ', indexAllocation: 'Index_Allocation_指数割当マスタ', deflator: 'Deflator_補正係数', term: 'Term_期間マスタ', index: 'Index_指数マスタ' } },
                llm: { baseUrl: 'http://127.0.0.1:11434', model: 'hf.co/Qwen/Qwen3-4B-GGUF:Q4_K_M', timeoutSec: 600, temperature: 0.2, numPredict: 512, numCtx: 8192 },
            };
        case 'testDbConnection': return { message: '（モック）接続OK' };
        case 'listLlmModels': return { models: ['hf.co/Qwen/Qwen3-4B-GGUF:Q4_K_M', 'gemma3:4b', 'llama3.1:8b'] };
        case 'testLlm': return { message: '（モック）接続OK' };
        case 'loadData':
            selected = null;
            return { projectCount: projects.length, assessCount: 5120, reviseCount: 5340, materialCount: MATERIALS.length, warning: null, result: analyze({}) };
        case 'analyze': return analyze(p);
        case 'selectProject': {
            const pr = projects.find((x) => x.projectId === p.projectId);
            selected = pr;
            const row = last.projects.find((x) => x.projectId === pr.projectId);
            const med = last.revise.after.median;
            return {
                projectId: pr.projectId, name: pr.name,
                attributes: Object.entries({ project_id: pr.projectId, 物件名: pr.name, 現場住所_都道府県: pr.prefecture, 現場住所_市区町村: pr.city, 現場住所_町名: pr.town, 構造: pr.structure, 形態: pr.form, 階数_地上: pr.floorsAbove, 階数_地下: pr.floorsBelow, 法廷床面積: pr.legalFloorArea, 施工床面積: pr.constructionFloorArea, 世帯数: Math.round(pr.legalFloorArea / 60), '工期(竣工)': '2023-03-31T00:00:00', term: '2023上期', 請負金額: Math.round(pr.legalFloorArea * 420000) })
                    .map(([key, value]) => ({ key, value })),
                reviseYenPerM2: row?.reviseYenPerM2 ?? null, medianYenPerM2: med,
                medianRatioPercent: row?.reviseYenPerM2 && med ? (row.reviseYenPerM2 / med - 1) * 100 : null, explanation: '',
            };
        }
        case 'insight':
            return { intent: p.kind, messages: [{ role: 'assistant', text: `【${{ summary: '判断サマリー', review: '確認観点の提案', project: '選択物件を説明' }[p.kind] ?? '判断サマリー'}】\n（モック）条件: ${last.conditionParts.join(' / ')}\n根拠: 絞込後の比較母集団は ${last.projectCount} 件です。${selected ? `\n選択物件: ${selected.projectId}` : ''}` }], result: null };
        case 'chatReset': return { cleared: true };
        case 'chat':
            // DB を調べて答える（本番は AI が テーブル・キーワードを決めて読み取り専用で検索する）
            if (/標準単価|単価は|単位は|協力業者|業者は/.test(p.text)) {
                const kw = (p.text.match(/^(.+?)の/) ?? [])[1] ?? '';
                const hits = MATERIALS.filter((m) => !kw || `${m.name}${m.sourceSheet}`.includes(kw.replace('ボード', 'ﾎﾞｰﾄﾞ')) || m.name.includes(kw));
                const lines = hits.slice(0, 5).map((m) => `・${m.name}${m.specification ? `（${m.specification}）` : ''}: 査定 ${m.assessPrice?.toLocaleString() ?? '—'}円 / 修正 ${m.revisePrice?.toLocaleString() ?? '—'}円（${m.unit}）`);
                return {
                    intent: 'lookup',
                    messages: [
                        { role: 'assistant', text: hits.length ? `（モック）建築材料マスタを調べました。「${kw}」に該当するのは ${hits.length} 件です。\n${lines.join('\n')}` : `（モック）建築材料マスタに「${kw}」は見つかりませんでした。半角カナ（ﾀｲﾙ など）や上位の工種名で試してみてください。` },
                        { role: 'system', text: `📋 参照: 建築材料マスタ（dbo.MaterialMaster_建築材料マスタ）・検索語「${kw}」・該当 ${hits.length} 件` },
                    ],
                    result: null,
                    table: { schema: 'dbo', name: 'MaterialMaster_建築材料マスタ', label: '建築材料マスタ', search: kw || null },
                };
            }
            // 会話（本番は AI が判断する。モックは条件らしい語が無ければ会話で返す）
            if (!/[都道府県市区]|造|階|工事|内装|外装|仮設|絞|条件|㎡/.test(p.text)) {
                const reply = /こんにち|おはよ|こんばん|はじめ/.test(p.text)
                    ? 'こんにちは！今日はどんな物件の単価を確認しましょうか？地域や構造、工種を教えてもらえれば条件を設定します。'
                    : /ありがと/.test(p.text)
                        ? 'どういたしまして。ほかにも気になる条件があれば、いつでもどうぞ。'
                        : `（モック）「${p.text}」について会話で回答します。本番ではローカルLLMが、いまの画面の状況（件数・単価の中央値など）をもとに答えます。`;
                return { intent: 'chat', messages: [{ role: 'assistant', text: reply }], result: null };
            }
            if (!last) return { intent: 'chat', messages: [{ role: 'assistant', text: 'まだデータが読み込まれていないため、その操作はできません。ヘッダーの「⚡ データ読込」を押してから、もう一度お願いします。' }], result: null };
        // falls through
        case 'aiFilter': {
            const c = { ...last.criteria };
            for (const pref of Object.keys(PREFS)) if (p.text.includes(pref.replace(/[都県]$/, ''))) c.prefecture = pref;
            if (p.text.includes('内装')) { c.trade = '内装工事'; c.work = '内装工事'; }
            const m = p.text.match(/(\d+)\s*階/);
            if (m) c.floorsAbove = m[1];
            const r = analyze(c);
            return { intent: 'filter', messages: [{ role: 'assistant', text: `【AIで条件設定】\n指示: ${p.text}\n（モック）地域: ${r.criteria.prefecture} / 工種: ${r.criteria.trade} / 地上: ${r.criteria.floorsAbove}\n絞込結果件数: ${r.projectCount}件` }], result: r };
        }
        case 'clearSelection':
            selected = null;
            return { cleared: true };
        case 'standardPrices':
            return standardPrices();
        case 'openStencilWindow':
            window.open('stencil.html', 'stencil', 'width=1400,height=900');
            return { opened: true };
        case 'pickPath':
            if (p.title.includes('保存先フォルダ')) return { path: 'C:\\見積OCR\\OCR_結果' };
            if (p.title.includes('ルール')) return { path: p.title.includes('差分') ? 'C:\\App\\Ocr\\Assets\\RulesSupplier_昇工業_協力業者差分.json' : 'C:\\App\\Ocr\\Assets\\RulesCommon_共通ルール.json' };
            if (p.title.includes('OCR 済み')) return { path: 'C:\\見積OCR\\OCR_結果\\防水見積_20261009_101500\\01_OCR抽出\\防水見積_20261009_101500_layout_raw_full.json' };
            return { path: p.kind === 'folder' ? 'C:\\Stencile\\2026上期' : (p.title.includes('YAML') ? 'C:\\Stencile\\mapping.yaml' : 'C:\\Stencile\\22期RC造_査定単価.xlsx') };
        // ── 見積AI-OCR ──
        case 'openOcrWindow':
            window.open('ocr.html', 'ocr', 'width=1440,height=920');
            return { opened: true };
        case 'ocrUsage':
        case 'usage': return ocrUsage();
        case 'getOcrState':
            return { settings: { outputFolder: '', commonRulesPath: '', supplierRulesPath: '', doStructure: true, keepRawJson: true, keepRawExcel: true, keepChunks: false },
                azure: ocrAzureView(), defaultCommonRules: 'C:\\App\\Ocr\\Assets\\RulesCommon_共通ルール.json', rulesFolder: 'C:\\App\\Ocr\\Assets',
                usage: ocrUsage(), results: [...OCR_JOBS] };
        case 'pickPdfs':
            return { files: [
                { path: 'C:\\見積OCR\\防水見積_昇工業.pdf', name: '防水見積_昇工業.pdf', pages: 6, error: null },
                { path: 'C:\\見積OCR\\内装見積_中川インテリア.pdf', name: '内装見積_中川インテリア.pdf', pages: 9, error: null },
                { path: 'C:\\見積OCR\\鋼製建具_スキャン.pdf', name: '鋼製建具_スキャン.pdf', pages: null, error: 'PDF を開けませんでした（パスワード付き・破損の可能性があります）' },
            ] };
        case 'saveAzureSettings':
            Object.assign(OCR_AZURE, p, { hasKey: OCR_AZURE.hasKey || !!p.key });
            return { azure: ocrAzureView(), usage: ocrUsage() };
        case 'testAzure': return { message: '（モック）Azure AI Document Intelligence に接続できました（ページは消費していません）。' };
        case 'run': return ocrRun(p);
        case 'cancel': ocrCancel = true; return { canceled: true };
        case 'restructure': {
            const job = ocrJob(OCR_JOBS.length, '防水見積_20261009_101500_layout_raw_full.json', 6, 'C:\\見積OCR\\OCR_結果\\防水見積_20261009_101500');
            job.pdfPath = p.rawJsonPath;
            OCR_JOBS.push(job);
            return { job };
        }
        case 'jobDetail': {
            const job = OCR_JOBS.find((j) => j.index === p.index);
            return { job, items: job.summary ? OCR_ITEMS : [], audit: job.summary ? OCR_AUDIT : [] };
        }
        case 'openPath': return { opened: true };
        case 'excelRead':
            return {
                path: p.path, sheets: ['材料一覧', 'メモ'], sheet: p.sheet ?? '材料一覧',
                headers: ['Work_Type_Code', '建材コード', '名称', '規格・寸法', '単位', '査定単価', '修正単価'],
                rows: Array.from({ length: 12 }, (_, i) => ['06-11-10000', `06-11-10000-${20 + i}`, `取込サンプル${i + 1}`, null, '㎡', String(1000 + i * 10), String(980 + i * 10)]),
            };
        case 'getStencilState':
            return { settings: { sourceFolder: '', includeSubfolders: true, doProjectInfo: true, doUnitPrice: true, mappingPath: '', templatePath: '' },
                defaultMapping: 'C:\\App\\Stencil\\Assets\\mapping_default_既定マッピング.yaml', database: 'SERVER\\SQLEXPRESS / Unit_Price_Display_単価表示（モック）',
                tables: { projects: 'ProjectInformation_物件情報', assess: 'Appraisal_Unit_Price_査定単価', revise: 'Revised_Unit_Price_修正単価' }, lastScan: null };
        case 'scan': return mockScan(p);
        case 'exportExcel': return { files: [`${p.outputFolder}\\物件諸情報一覧.xlsx`, `${p.outputFolder}\\単価テーブル_集約結果.xlsx`] };
        case 'openFolder': return { opened: true };
        case 'register': {
            for (const f of lastScanMock.files) if (p.indexes.includes(f.index)) f.existsInDb = true;
            return { result: { projectsInserted: p.indexes.length, projectsUpdated: 0, assessRows: p.indexes.length * 373, reviseRows: p.indexes.length * 373, assessDeleted: 0, reviseDeleted: 0, warnings: [] }, scan: lastScanMock };
        }
        case 'dbTables':
            return { server: 'SERVER\\SQLEXPRESS', database: 'Unit_Price_Display_単価表示（モック）', tables: dbTables() };
        case 'dbTable': return dbTable(p);
        case 'dbQuery': return dbQuery(p);
        case 'dbSave': {
            const res = dbSave(p);
            const rows = [
                ...p.inserts.map((i) => ({ op: 'INSERT', table: p.name, after: i.values })),
                ...p.updates.map((u) => ({ op: 'UPDATE', table: p.name, before: u.key, after: { ...u.key, ...u.values } })),
                ...p.deletes.map((d) => ({ op: 'DELETE', table: p.name, before: d.key })),
            ];
            HISTORY.unshift({ batchId: ++historySeq, executedAt: new Date().toISOString(), userName: 'CORP\\yamada', machineName: 'PC-0123', dbLogin: 'CORP\\yamada',
                source: p.source ?? 'DB編集', summary: `${p.name}: 追加${p.inserts.length}・更新${p.updates.length}・削除${p.deletes.length}`, tables: p.name,
                rowTotal: rows.length, revertsBatchIds: null, revertedByBatchId: null, rows });
            return res;
        }
        case 'historyList': return { batches: HISTORY.filter((b) => !p.table || (b.tables ?? '').includes(p.table)).map(({ rows, ...b }) => b), truncated: false };
        case 'historyDetail': {
            const b = HISTORY.find((x) => x.batchId === p.batchId);
            const { rows, ...batch } = b;
            return { batch, totalRows: rows.length, rows: rows.map((r, i) => {
                const cols = Object.keys(r.after ?? r.before ?? {});
                const changes = cols.map((c) => ({ column: c, before: r.before?.[c] ?? null, after: r.after?.[c] ?? null }))
                    .filter((c) => (r.op === 'UPDATE' ? c.before !== c.after : (r.op === 'INSERT' ? c.after : c.before) != null));
                return { seqNo: i + 1, table: r.table, operation: r.op, keyText: cols.slice(0, 2).map((c) => `${c}=${(r.after ?? r.before)[c]}`).join(' / '), changes };
            }) };
        }
        case 'historyRevert': {
            const targets = p.mode === 'since' ? HISTORY.filter((b) => b.batchId >= p.batchId && !b.revertedByBatchId) : HISTORY.filter((b) => b.batchId === p.batchId);
            if (!p.force && targets.some((b) => b.source === 'Stencile登録')) {
                return { done: false, newBatchId: null, revertedBatchIds: [], rows: 0, warnings: [],
                    conflicts: [{ batchId: targets[0].batchId, table: 'Appraisal_Unit_Price_査定単価', operation: 'INSERT', keyText: 'ID=1201', reason: '追加した後に変更されています（Appraisal_Unit_Price）。' }] };
            }
            const id = ++historySeq;
            for (const b of targets) b.revertedByBatchId = id;
            const n = targets.reduce((s, b) => s + b.rowTotal, 0);
            HISTORY.unshift({ batchId: id, executedAt: new Date().toISOString(), userName: 'CORP\\yamada', machineName: 'PC-0123', dbLogin: 'CORP\\yamada', source: '取り消し',
                summary: `操作 #${targets.map((t) => t.batchId).join(', #')} を取り消し`, tables: targets.map((t) => t.tables).join(', '), rowTotal: n,
                revertsBatchIds: targets.map((t) => t.batchId).join(','), revertedByBatchId: null, rows: [] });
            return { done: true, newBatchId: id, revertedBatchIds: targets.map((t) => t.batchId), rows: n, conflicts: [], warnings: [] };
        }
        case 'historyPurge': return { deleted: 0 };
        case 'dbDeletePreview': {
            if (p.name !== 'ProjectInformation_物件情報') return [];
            const ids = p.deletes.map((d) => d.key.Project_id);
            const rows = DB['Appraisal_Unit_Price_査定単価'].rows.filter((r) => ids.includes(r[0])).length;
            return rows ? [{ table: 'Appraisal_Unit_Price_査定単価', label: '査定単価', rows }] : [];
        }
        default: {
            const r = await handleEstimate(command, p);   // 見積DB連携（取込・確認・取決め）
            if (r !== undefined) return r;
            throw new Error(`（モック）未対応のコマンド: ${command}`);
        }
    }
}

function analyze(input) {
    const c = { prefecture: U, city: U, town: U, structure: U, form: U, floorsAbove: U, floorsBelow: U, basisMode: 'LegalFloorArea', costMode: 'NetConstruction', areaMin: null, areaMax: null, trade: U, work: U, item: U, ...input };
    const areaOf = (p) => (c.basisMode === 'LegalFloorArea' ? p.legalFloorArea : p.constructionFloorArea);
    const areas = projects.map(areaOf);
    const bmin = Math.min(...areas);
    const bmax = Math.max(...areas);
    c.areaMin = c.areaMin ?? bmin;
    c.areaMax = c.areaMax ?? bmax;

    const match = (p, skip) => ['prefecture', 'city', 'town', 'structure', 'form', 'floorsAbove', 'floorsBelow']
        .every((k) => k === skip || (skip === 'prefecture' && (k === 'city' || k === 'town')) || (skip === 'city' && k === 'town') || c[k] === U || String(p[k]) === String(c[k]))
        && areaOf(p) >= c.areaMin && areaOf(p) <= c.areaMax;
    const filtered = projects.filter((p) => match(p, null));

    const f = c.costMode === 'ConstructionCost' ? 1 / 0.705 : 1;
    const tradeFactor = c.trade === U ? 1 : 0.18;
    const series = (key, title, unit, gen, xByArea) => {
        const pts = filtered.map((p, i) => ({ p, i, v: gen(p) })).filter((x) => Number.isFinite(x.v));
        const before = pts.map(({ p, i, v }) => ({ projectId: p.projectId, x: xByArea ? areaOf(p) : i, y: v * f }));
        const after = pts.map(({ p, i, v }) => ({ projectId: p.projectId, x: xByArea ? areaOf(p) : i, y: v * p.deflator * f }));
        return { key, title, unit, xLabel: xByArea ? '床面積(㎡)' : '物件インデックス', before, after, summary: { before: summ(before.map((x) => x.y)), after: summ(after.map((x) => x.y)) } };
    };
    const charts = [
        series('assess', '査定単価', '円/㎡', (p) => p.base * tradeFactor * 0.97, true),
        series('revise', '修正単価', '円/㎡', (p) => p.base * tradeFactor, true),
        series('temp', '仮設工事単価', '円/㎡', (p) => p.base * 0.11, false),
        series('formwork', '型枠労務歩掛', '円/㎡', (p) => (p.floorsAbove > 4 ? 5200 + p.base / 90 : NaN), false),
        series('rebar', '鉄筋労務歩掛', '円/t', (p) => 68000 + p.base / 6, false),
    ];
    const by = Object.fromEntries(charts.map((s) => [s.key, s.summary]));
    const reviseMap = new Map(charts[1].after.map((x) => [x.projectId, x.y]));

    const facet = (key, skip = key) => {
        const counts = new Map();
        for (const p of projects) if (match(p, skip)) counts.set(String(p[key]), (counts.get(String(p[key])) ?? 0) + 1);
        return [...counts].sort((a, b) => a[0].localeCompare(b[0], 'ja', { numeric: true })).map(([value, count]) => ({ value, count }));
    };

    last = {
        projectCount: filtered.length,
        selectionLabel: `工種:${c.trade === U ? '未指定' : c.trade}`,
        conditionParts: [c.prefecture, c.city, c.trade].filter((v) => v !== U),
        assess: by.assess, revise: by.revise, tempUnit: by.temp,
        tempRatio: { before: summ(filtered.map((p) => 9 + (p.base % 7))), after: summ(filtered.map((p) => 9 + (p.base % 7))) },
        formwork: by.formwork, rebar: by.rebar,
        assessDeflatorMean: 1.061, reviseDeflatorMean: 1.058,
        tempN: by.temp.after.n, formworkN: by.formwork.after.n, rebarN: by.rebar.after.n,
        charts,
        projects: filtered.map((p) => ({ projectId: p.projectId, name: p.name, prefecture: p.prefecture, city: p.city, town: p.town, structure: p.structure, form: p.form, floorsAbove: p.floorsAbove, floorsBelow: p.floorsBelow, legalFloorArea: p.legalFloorArea, constructionFloorArea: p.constructionFloorArea, reviseYenPerM2: reviseMap.get(p.projectId) ?? null })),
        facets: Object.fromEntries(['prefecture', 'city', 'town', 'structure', 'form', 'floorsAbove', 'floorsBelow'].map((k) => [k, facet(k)])),
        areaBoundMin: bmin, areaBoundMax: bmax,
        tradeOptions: [U, ...Object.keys(TRADES)],
        workOptions: c.trade === U ? [U] : [U, ...(TRADES[c.trade] ?? [])],
        itemOptions: [U],
        criteria: c,
        statusLine: `（モック）表示更新: Projects絞込=${filtered.length}件 / 修正n=${by.revise.after.n}`,
    };
    return last;
}

function standardPrices() {
    const c = last.criteria;
    let codes = null;
    if (c.trade !== U) codes = new Set(CODE_OF[c.trade] ?? []);
    if (selected) {
        // モックでは物件ごとに持っている工種を番号で決める
        const n = parseInt(selected.projectId.slice(1), 10);
        const own = new Set(Object.values(CODE_OF).flat().filter((_, i) => (n + i) % 3 !== 0));
        codes = codes ? new Set([...codes].filter((x) => own.has(x))) : own;
    }
    const hits = MATERIALS.filter((m) => !codes || codes.has(m.workTypeCode));
    const byCode = new Map();
    for (const m of hits) {
        if (!byCode.has(m.workTypeCode)) byCode.set(m.workTypeCode, []);
        byCode.get(m.workTypeCode).push(m);
    }
    const tradeOf = (code) => Object.keys(CODE_OF).find((t) => CODE_OF[t].includes(code));
    const groups = [...byCode].sort((a, b) => a[0].localeCompare(b[0])).map(([code, list]) => {
        const amount = selected ? selected.legalFloorArea * (1200 + (code.charCodeAt(4) % 7) * 900) : null;
        return {
            workTypeCode: code, trade: tradeOf(code), work: list[0].sourceSheet, item: null, sourceSheet: list[0].sourceSheet,
            projectAssessAmount: amount ? amount * 1.04 : null, projectReviseAmount: amount,
            projectAssessPerM2: amount ? amount * 1.04 / selected.legalFloorArea : null, projectRevisePerM2: amount ? amount / selected.legalFloorArea : null,
            medianRevisePerM2: 1500 + (code.charCodeAt(4) % 7) * 850, sampleCount: last.projectCount, materials: list,
        };
    });
    const parts = [];
    if (c.trade !== U) parts.push(`工種:${c.trade}`);
    if (selected) parts.push(`物件 ${selected.projectId} の工種コード`);
    return {
        available: true, message: groups.length ? null : '該当する標準材料がありません。',
        scopeLabel: parts.join(' ∩ ') || '全工種（工種で絞るか物件を選ぶと、該当する標準材料だけになります）',
        projectId: selected?.projectId ?? null, projectName: selected?.name ?? null,
        materialCount: hits.length, groupCount: groups.length, groups,
    };
}

// ── DB編集のモック（メモリ上の簡易テーブル） ──
const col = (name, typeName, o = {}) => ({
    name, typeName, maxLength: typeName.startsWith('n') ? (o.max ?? 100) : null, precision: o.p ?? 0, scale: o.s ?? 0,
    isNullable: o.nullable ?? true, isIdentity: !!o.identity, isComputed: false, isPrimaryKey: !!o.pk,
    isEditable: !o.identity, keyName: name.toLowerCase(),
});
const DB = {
    'Category_カテゴリマスタ': {
        role: 'カテゴリ',
        columns: [col('ID', 'int', { pk: true, identity: true, nullable: false }), col('Work_Type_Code', 'nvarchar', { max: 20, nullable: false }), col('Category_Code', 'int'), col('Category_Name', 'nvarchar')],
        rows: [...new Set(MATERIALS.map((m) => m.workTypeCode))].map((c, i) => [String(i + 1), c, String(10 + i), MATERIALS.find((m) => m.workTypeCode === c).sourceSheet]),
    },
    'MaterialMaster_建築材料マスタ': {
        role: '標準単価',
        columns: [col('MaterialMaster_ID', 'int', { pk: true, identity: true, nullable: false }), col('Work_Type_Code', 'nvarchar', { max: 20 }), col('Material_Code_建材コード', 'nvarchar', { max: 30 }),
            col('Material_Name_名称', 'nvarchar'), col('Specification_Dimensions_規格寸法', 'nvarchar'), col('Unit_単位', 'nvarchar', { max: 10 }),
            col('Appraisal_Unit_Price_査定単価', 'decimal', { p: 19, s: 4 }), col('Revised_Unit_Price_修正単価', 'decimal', { p: 19, s: 4 }), col('ReviewStatus_確認状態', 'nvarchar', { max: 10, nullable: false })],
        rows: MATERIALS.map((m, i) => [String(i + 1), m.workTypeCode, m.materialCode, m.name, m.specification, m.unit,
            m.assessPrice == null ? null : String(m.assessPrice), m.revisePrice == null ? null : String(m.revisePrice), i === 3 ? '' : m.reviewStatus]),
    },
    'ProjectInformation_物件情報': {
        role: '物件情報',
        columns: [col('Project_id', 'nvarchar', { max: 20, nullable: false }), col('ProjectName', 'nvarchar'), col('SiteAddress_State_Province', 'nvarchar'), col('LegalFloorArea', 'float'), col('ConstructionPeriod_Completion', 'date')],
        rows: projects.map((p) => [p.projectId, p.name, p.prefecture, String(p.legalFloorArea), '2023-03-31']),
    },
    'Appraisal_Unit_Price_査定単価': {
        role: '査定単価',
        columns: [col('Project_id', 'nvarchar', { max: 20 }), col('Work_Type_Code', 'nvarchar', { max: 20 }), col('Work_Type_Item', 'nvarchar'), col('Appraisal_Unit_Price', 'decimal', { p: 18, s: 0 })],
        rows: projects.slice(0, 30).map((p, i) => [p.projectId, Object.values(CODE_OF).flat()[i % 5], '内装工事', String(100000 + i * 1234)]),
    },
    'Term_期間マスタ': {
        role: '期間',
        columns: [col('term', 'nvarchar', { max: 20 }), col('start_date', 'date'), col('end_date', 'date')],
        rows: [['2023上期', '2023-04-01', '2023-09-30'], ['2023下期', '2023-10-01', '2024-03-31']],
    },
};
const MOCK_LINKS = {
    'Category_カテゴリマスタ': [['MaterialMaster_建築材料マスタ', 'child', false, 'Work_Type_Code ⇄ Work_Type_Code'], ['Appraisal_Unit_Price_査定単価', 'child', true, 'Work_Type_Code → Work_Type_Code']],
    'MaterialMaster_建築材料マスタ': [['Category_カテゴリマスタ', 'parent', false, 'Work_Type_Code ⇄ Work_Type_Code']],
    'Appraisal_Unit_Price_査定単価': [['ProjectInformation_物件情報', 'parent', false, 'Project_id ⇄ Project_id'], ['Category_カテゴリマスタ', 'parent', true, 'Work_Type_Code → Work_Type_Code']],
    'ProjectInformation_物件情報': [['Appraisal_Unit_Price_査定単価', 'child', false, 'Project_id ⇄ Project_id']],
    'Term_期間マスタ': [],
};

function tableInfo(name) {
    const i = name.search(/_[^\x00-\x7F]/);
    return { schema: 'dbo', name, fullName: `dbo.${name}`, baseName: i < 0 ? name : name.slice(0, i), label: i < 0 ? name : name.slice(i + 1), rowCount: DB[name].rows.length, appRole: DB[name].role };
}
function dbTables() {
    return Object.keys(DB).map(tableInfo);
}
function dbTable(p) {
    const t = DB[p.name];
    const neighbors = (MOCK_LINKS[p.name] ?? []).map(([name, direction, isForeignKey, link]) => ({ table: tableInfo(name), direction, isForeignKey, links: [link] }));
    return { table: tableInfo(p.name), columns: t.columns, hasPrimaryKey: t.columns.some((c) => c.isPrimaryKey), neighborhood: { center: tableInfo(p.name), neighbors } };
}
function dbQuery(p) {
    const t = DB[p.name];
    let rows = t.rows;
    if (p.search) rows = rows.filter((r) => r.some((v) => String(v ?? '').normalize('NFKC').includes(p.search.normalize('NFKC'))));   // 全角/半角カナを同一視（本番と同じ）
    if (p.sortColumn) {
        const i = t.columns.findIndex((c) => c.name === p.sortColumn);
        rows = [...rows].sort((a, b) => String(a[i] ?? '').localeCompare(String(b[i] ?? ''), 'ja', { numeric: true }) * (p.sortDesc ? -1 : 1));
    }
    const start = (p.page - 1) * p.pageSize;
    return { columns: t.columns, rows: rows.slice(start, start + p.pageSize).map((r) => [...r]), total: rows.length, page: p.page, pageSize: p.pageSize, hasPrimaryKey: t.columns.some((c) => c.isPrimaryKey) };
}
function dbSave(p) {
    const t = DB[p.name];
    const keyCols = t.columns.some((c) => c.isPrimaryKey) ? t.columns.filter((c) => c.isPrimaryKey) : t.columns;
    const idx = (key) => t.rows.findIndex((r) => keyCols.every((c) => (r[t.columns.indexOf(c)] ?? null) === (key[c.name] ?? null)));
    for (const d of p.deletes) {
        const i = idx(d.key);
        if (i < 0) throw new Error(`${d.rowNo}行目の削除で失敗したため、すべての変更を取り消しました。
対象の行が見つかりません。`);
        t.rows.splice(i, 1);
    }
    for (const u of p.updates) {
        const i = idx(u.key);
        for (const [k, v] of Object.entries(u.values)) t.rows[i][t.columns.findIndex((c) => c.name === k)] = v === '' ? null : v;
    }
    for (const ins of p.inserts) {
        const row = t.columns.map((c) => (c.isIdentity ? String(Math.max(0, ...t.rows.map((r) => +r[0])) + 1) : (ins.values[c.name] ?? null)));
        const bad = t.columns.find((c, i) => !c.isNullable && row[i] == null);
        if (bad) throw new Error(`${ins.rowNo}行目の追加で失敗したため、すべての変更を取り消しました。
必須の列（NULL 不可）が空です: ${bad.name}`);
        t.rows.push(row);
    }
    return { inserted: p.inserts.length, updated: p.updates.length, deleted: p.deletes.length };
}

let lastScanMock = null;
async function mockScan(p) {
    const names = ['渋谷神宮前PJ', '世田谷三軒茶屋PJ', '横浜港北PJ', '船橋本町PJ', '川口栄町PJ', '江東豊洲PJ', '品川大井PJ'];
    const total = names.length + 1;
    const say = (level, message, done) => emit('bridge:stencilProgress', { level, message, done, total });
    say('step', 'マッピング YAML を読み込んでいます...', 0);
    say('ok', 'マッピング: mapping_default_既定マッピング.yaml（4 セクション）', 0);
    if (p.doUnitPrice) say('ok', 'テンプレート: 22期RC造_査定単価.xlsx（工種コード 373 件）', 0);
    const files = [];
    for (let i = 0; i < total; i++) {
        await new Promise((r) => setTimeout(r, 120));
        const broken = i === total - 1;
        const pid = broken ? null : `J0${2201 + i}`;
        const f = {
            index: i, fileName: broken ? '旧様式\\メモ.xlsx' : `${names[i]}\\実行予算_${pid}.xlsm`, filePath: `C:\\Stencile\\${broken ? 'メモ.xlsx' : names[i]}`,
            projectId: pid, projectName: broken ? null : names[i].replace('PJ', 'レジデンス'),
            projectInfo: broken ? null : { project_id: pid, 物件名: names[i].replace('PJ', 'レジデンス'), 現場住所_都道府県: ['東京都', '東京都', '神奈川県', '千葉県', '埼玉県', '東京都', '東京都'][i], 現場住所_市区町村: ['渋谷区', '世田谷区', '横浜市港北区', '船橋市', '川口市', '江東区', '品川区'][i], 構造: '壁式鉄筋ｺﾝｸﾘｰﾄ造', 階数_地上: 4 + i, 施工床面積: 584.3 + i * 120, 請負金額: 430000000 + i * 1e7, '工期(竣工)': '2027-03-26T00:00:00', '1K': 10 + i, '1LDK': i },
            unitPriceMatched: broken ? 0 : 360 + i, unitPriceTotal: p.doUnitPrice && !broken ? 373 : 0,
            warnings: i === 2 ? ['① 物件諸情報: 構造が空です'] : [], error: broken ? '必要なシートがありません: 実行予算表書、原価推移表' : null,
            existsInDb: i === 1 || i === 4, existingAssessRows: i === 1 || i === 4 ? 373 : 0, existingReviseRows: i === 1 || i === 4 ? 373 : 0, isValid: !broken,
        };
        files.push(f);
        say(broken ? 'error' : (f.warnings.length ? 'warn' : 'file'), `${f.fileName} → ${pid ?? '(IDなし)'}${broken ? '：' + f.error : ''}`, i + 1);
    }
    say('done', `読込完了: ${total - 1} / ${total} 件（エラー 1 件）`, total);
    lastScanMock = { sourceFolder: p.sourceFolder, mappingPath: 'mapping_default_既定マッピング.yaml', templatePath: p.templatePath, templateCodeCount: 373,
        doProjectInfo: p.doProjectInfo, doUnitPrice: p.doUnitPrice, files, dbChecked: true, dbMessage: 'SERVER\\SQLEXPRESS / Unit_Price_Display_単価表示（モック）' };
    return lastScanMock;
}

// ── 見積AI-OCR のモック ──────────────────────────────────

const OCR_AZURE = { endpoint: '', hasKey: false, chunkPages: 2, limit: 500, warnAt: 300, dangerAt: 400, period: 'day' };
const OCR_USAGE = { used: 288, files: 41, sessionPages: 0, sessionFiles: 0 };
const OCR_JOBS = [];
let ocrCancel = false;

function ocrAzureView() {
    const set = OCR_AZURE.endpoint && OCR_AZURE.hasKey;
    return { ...OCR_AZURE, effectiveEndpoint: set ? OCR_AZURE.endpoint : 'https://estimate-ocr.cognitiveservices.azure.com/', source: set ? '設定' : '環境変数' };
}

function ocrUsage() {
    const a = OCR_AZURE;
    const u = OCR_USAGE.used;
    const level = u >= a.limit ? 'over' : u > a.dangerAt ? 'danger' : u > a.warnAt ? 'caution' : 'safe';
    return {
        period: a.period, periodLabel: a.period === 'month' ? '今月' : '本日', used: u, files: OCR_USAGE.files, limit: a.limit, warnAt: a.warnAt, dangerAt: a.dangerAt,
        level, remaining: Math.max(0, a.limit - u), sessionPages: OCR_USAGE.sessionPages, sessionFiles: OCR_USAGE.sessionFiles,
        recent: [{ date: '2026-10-09', pages: u, files: OCR_USAGE.files }, { date: '2026-10-08', pages: 126, files: 18 }, { date: '2026-10-07', pages: 64, files: 9 }],
    };
}

const OCR_ITEMS = [
    ['防水工事', '', 'ウレタン塗膜防水', 'X-2工法', 't=2.0', 128.5, '㎡', 6800, 873800],
    ['防水工事', '', '立上り ウレタン塗膜防水', 'X-2工法', 'H=300', 64.2, 'm', 2400, 154080],
    ['防水工事', '', '改質アスファルトシート防水', 'トーチ工法', '', 212, '㎡', 5600, 1187200],
    ['防水工事', '', 'ドレン改修', '鋳鉄製', 'φ100', 6, 'ヶ所', 18000, 108000],
    ['防水工事', '(バルコニー)', '塩ビシート防水', '', 't=2.0', 96, '㎡', 5200, 499300],
    ['防水工事', '(バルコニー)', '端末シール', '', '', 140, 'm', 650, 91000],
    ['防水工事', '(バルコニー)', '既存防水撤去', '', '', null, '', null, null],
    ['シーリング工事', '', '外壁目地シーリング', '変成シリコン', '10×10', 820, 'm', 900, 738000],
    ['シーリング工事', '', 'サッシ廻りシーリング', '変成シリコン', '', 410, 'm', 1100, 451000],
    ['シーリング工事', '', 'サッシ廻りシーリング', '変成シリコン', '', 410, 'm', 1100, 451000],
    ['シーリング工事', '', '養生・清掃', '', '', 1, '式', 85000, 85000],
    ['諸経費', '', '運搬費', '', '', 1, '式', 60000, 60000],
].map(([trade, section, name, material, dimension, q, unit, price, amount], i) => {
    const money = q != null && price != null && amount != null;
    const calc = money ? Math.round(q * price) : null;
    const diff = money ? Math.round(amount - calc) : null;
    const reasons = [];
    if (!money) reasons.push('単価・金額が未記載の参考明細（集計対象外）');
    if (money && Math.abs(diff) > 1) reasons.push('数量×単価と金額が不一致');
    const dup = i === 8 || i === 9 ? '同一明細候補' : '';
    if (dup) reasons.push('ページ継続・重複を確認');
    return {
        estimateId: 'M-2026-0815', supplierName: '株式会社 昇工業', projectName: '文京区本郷4丁目計画 新築工事', sourceFile: '防水見積_layout_raw_full.json',
        recordType: money ? '金額明細' : '参考明細', pdfPage: i < 4 ? 2 : i < 9 ? 3 : 4, ocrTableIndex: i < 4 ? 1 : i < 9 ? 2 : 3, ocrRowIndex: (i % 4) + 2,
        trade, section: section.replace(/[()]/g, ''), itemName: name, material, dimension, quantity: q, unit, unitPrice: price, amount, remarksOcr: '',
        calculatedAmount: calc, difference: diff, amountCheck: money ? (Math.abs(diff) <= 1 ? 'OK' : '要確認') : '対象外',
        duplicateCandidate: dup, reviewRequired: reasons.length ? '要確認' : '', reviewReason: reasons.join(' / '),
    };
});

const OCR_AUDIT = [
    { pdfPage: 2, ocrTableIndex: 1, ocrRowIndex: 2, rowStatus: '見出し', reason: '工種・大見出し', rowText: '防水工事 |  |  |  |  |  | ' },
    ...OCR_ITEMS.slice(0, 4).map((r, i) => ({ pdfPage: 2, ocrTableIndex: 1, ocrRowIndex: i + 3, rowStatus: '採用', reason: r.recordType, rowText: `${r.itemName} | ${r.material} | ${r.dimension} | ${r.quantity ?? ''} | ${r.unit} | ${r.unitPrice ?? ''} | ${r.amount ?? ''}` })),
    { pdfPage: 3, ocrTableIndex: 2, ocrRowIndex: 2, rowStatus: '見出し', reason: '区分見出し', rowText: '(バルコニー) |  |  |  |  |  | ' },
    { pdfPage: 3, ocrTableIndex: 2, ocrRowIndex: 6, rowStatus: '除外', reason: '名称なし、または合計行', rowText: '小計 |  |  |  |  |  | 2,914,380' },
    { pdfPage: 3, ocrTableIndex: 2, ocrRowIndex: 7, rowStatus: '空行', reason: '', rowText: ':unselected: |  |  |  |  |  | ' },
    { pdfPage: 4, ocrTableIndex: 3, ocrRowIndex: 9, rowStatus: '除外', reason: '合計行', rowText: '合計 |  |  |  |  |  | 4,698,380' },
];

function ocrJob(index, name, pages, folder) {
    const money = OCR_ITEMS.filter((r) => r.recordType === '金額明細');
    const total = money.reduce((s, r) => s + r.amount, 0);
    const stem = name.replace(/\.pdf$/i, '');
    return {
        index, pdfPath: `C:\\見積OCR\\${name}`, pdfName: name, status: 'ok', error: null, warnings: [], jobFolder: folder, pdfPages: pages, ocrPages: pages, pagesCounted: pages,
        rawJsonPath: `${folder}\\01_OCR抽出\\${stem}_layout_raw_full.json`, checkExcelPath: `${folder}\\01_OCR抽出\\${stem}_layout_check_full.xlsx`,
        structuredJsonPath: `${folder}\\02_構造化\\${stem}_full_structured.json`, structuredExcelPath: `${folder}\\02_構造化\\${stem}_full_structured.xlsx`,
        metadata: { createdAt: '2026-10-09T10:15:00', sourceLayoutJson: `${stem}_layout_raw_full.json`, commonRulesFile: 'RulesCommon_共通ルール.json', supplierRulesFile: '',
            supplierName: OCR_ITEMS[0].supplierName, projectName: OCR_ITEMS[0].projectName, estimateId: OCR_ITEMS[0].estimateId },
        summary: { detailCount: OCR_ITEMS.length, moneyDetailCount: money.length, referenceDetailCount: OCR_ITEMS.length - money.length,
            amountCheckOkCount: money.filter((r) => r.amountCheck === 'OK').length, duplicateCandidateCount: OCR_ITEMS.filter((r) => r.duplicateCandidate).length,
            headerEstimateAmount: 4698380, detailAmountTotal: total, differenceToHeaderAmount: 4698380 - total, auditRowCount: OCR_AUDIT.length,
            auditExcludedCount: OCR_AUDIT.filter((a) => a.rowStatus === '除外').length, reviewRequiredCount: OCR_ITEMS.filter((r) => r.reviewRequired).length },
    };
}

async function ocrRun(p) {
    ocrCancel = false;
    const wait = (ms) => new Promise((r) => setTimeout(r, ms));
    const say = (level, message, done, total) => emit('bridge:ocrProgress', { level, message, done, total });
    const pages = p.pdfs.map((path) => (path.includes('内装') ? 9 : 6));
    const total = pages.reduce((s, n) => s + Math.ceil(n / 2), 0);
    let done = 0;
    const jobs = [];
    for (let n = 0; n < p.pdfs.length; n++) {
        const name = p.pdfs[n].split('\\').pop();
        const folder = `${p.outputFolder}\\${name.replace(/\.pdf$/i, '')}_20261009_1015${String(n).padStart(2, '0')}`;
        if (ocrCancel) {
            jobs.push({ ...ocrJob(OCR_JOBS.length + n, name, pages[n], folder), status: 'canceled', error: '中止したため処理していません。', summary: null, metadata: null, structuredExcelPath: null, checkExcelPath: null, ocrPages: null });
            continue;
        }
        say('step', `[${n + 1}/${p.pdfs.length}] ${name}`, done, total);
        for (let s = 1; s <= pages[n]; s += 2) {
            const e = Math.min(s + 1, pages[n]);
            say('file', `  OCR処理中: ${Math.ceil(s / 2)}ブロック目（元PDF ${s}-${e}ページ / 全${pages[n]}ページ）`, done, total);
            await wait(350);
            OCR_USAGE.used += e - s + 1;
            OCR_USAGE.sessionPages += e - s + 1;
            emit('bridge:ocrUsage', ocrUsage());
            done++;
        }
        OCR_USAGE.files++;
        OCR_USAGE.sessionFiles++;
        say('step', `  ページ照合: PDF ${pages[n]}ページ / Azure取得 ${pages[n]}ページ`, done, total);
        say('ok', '  ページ照合: OK（PDF全ページを統合済み）', done, total);
        const job = ocrJob(OCR_JOBS.length + n, name, pages[n], folder);
        if (p.doStructure) say('ok', `  構造化: 明細 ${job.summary.detailCount} 件（金額 ${job.summary.moneyDetailCount} / 参考 ${job.summary.referenceDetailCount}）・検算OK ${job.summary.amountCheckOkCount}・重複候補 ${job.summary.duplicateCandidateCount}`, done, total);
        else Object.assign(job, { summary: null, metadata: null, structuredExcelPath: null, structuredJsonPath: null });
        say('ok', `  完了: ${folder}`, done, total);
        jobs.push(job);
    }
    say('done', `完了: 成功 ${jobs.filter((j) => j.status === 'ok').length} 件 / 失敗 0 件 / ページ警告 0 件`, total, total);
    OCR_JOBS.push(...jobs);
    return { jobs, usage: ocrUsage() };
}

function summ(values) {
    const s = values.filter(Number.isFinite).sort((a, b) => a - b);
    if (s.length === 0) return { n: 0, q1: null, median: null, q3: null };
    return { n: s.length, q1: percentile(s, 25), median: percentile(s, 50), q3: percentile(s, 75) };
}
