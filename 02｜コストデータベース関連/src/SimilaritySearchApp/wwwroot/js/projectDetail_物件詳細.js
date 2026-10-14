// js/projectDetail_物件詳細.js
// 選択物件の詳細モーダル（グラフの点・一覧の行をクリックで開く）。修正単価と「現在条件の中央値との差」を目立たせる。
// モーダルを閉じても物件の選択は残る（グラフ・一覧の強調、【標準単価】の絞込はそのまま）。

import { esc, yen, display } from './format_数値書式.js';

const $ = (id) => document.getElementById(id);

/**
 * 表示レイアウト（左右2列。[左の項目, 右の項目]、null は空欄）。項目名は DB の日本語列名（物件情報の SELECT の別名）。
 * 主要項目は常に表示し、それ以外は「すべての項目」に折りたたむ。
 */
const MAIN_LAYOUT = [
    ['現場住所_都道府県', '構造'],
    ['現場住所_市区町村', '形態'],
    ['現場住所_町名', '階数_地上'],
    [null, '階数_地下'],
    ['請負金額', '法廷床面積'],
    ['契約時予定利益', '施工床面積'],
    ['利益率', '世帯数'],
    ['敷地面積', '工期(着工)'],
    ['建築面積', '工期(竣工)'],
    ['施工坪', 'term'],
];

const MORE_LAYOUT = [
    ['1R', '1K'],
    ['1DK', '1LDK'],
    ['2K', '2DK'],
    ['2LDK', '3K'],
    ['3DK', '3LDK'],
    ['4K', '4DK'],
    ['4LDK', '5K'],
    ['5DK', '5LDK'],
    ['1SK', '2SK'],
    ['1SLDK', '2SLDK'],
    ['3SLDK', 'OR'],
    ['テナント', '店舗'],
    ['事務所', 'その他'],
    ['ELV', null],
    ['ｺﾝｸﾘｰﾄ', '型枠'],
    ['鉄筋（Kg/m2）', '鉄骨（Kg/m2）'],
    ['ALC', '外壁タイル'],
    ['山留工法', '土工事'],
    ['型枠・鉄筋', '鉄骨'],
    ['ALC車両', '生ｺﾝ車'],
    ['ﾎﾟﾝﾌﾟ車', null],
];

/** 見出し部分で別に出すので、表には出さない項目 */
const HIDDEN_KEYS = new Set(['project_id', '物件名']);

export function initProjectDetail() {
    const modal = $('detail-modal');
    for (const el of modal.querySelectorAll('[data-close]')) el.addEventListener('click', closeProjectDetail);
    modal.addEventListener('click', (e) => {
        if (e.target === modal) closeProjectDetail();   // 背景クリックで閉じる
    });
    document.addEventListener('keydown', (e) => {
        if (e.key === 'Escape' && isProjectDetailOpen()) closeProjectDetail();
    });
}

export function isProjectDetailOpen() {
    return !$('detail-modal').classList.contains('hidden');
}

export function closeProjectDetail() {
    $('detail-modal').classList.add('hidden');
}

/** 選択が解除されたとき（絞込で外れた等）: 内容を消して閉じる */
export function clearProjectDetail() {
    $('detail-body').innerHTML = '';
    closeProjectDetail();
}

/** 詳細を描く。open=true でモーダルを開く（false なら開いているときだけ内容を更新） */
export function renderProjectDetail(d, { open = true } = {}) {
    const attrs = new Map(d.attributes.map((a) => [a.key, a.value]));

    // レイアウトに無い列（DB に列が増えた場合など）も見落とさないよう、「すべての項目」の末尾に足す
    const placed = new Set([...MAIN_LAYOUT, ...MORE_LAYOUT].flat().filter(Boolean));
    const extras = d.attributes.map((a) => a.key).filter((k) => !placed.has(k) && !HIDDEN_KEYS.has(k));
    const more = fillPairs(MORE_LAYOUT, extras);
    const moreCount = more.flat().filter((k) => k && attrs.has(k)).length;

    let delta = '';
    if (Number.isFinite(d.medianRatioPercent)) {
        const r = d.medianRatioPercent;
        const cls = r > 0 ? 'up' : 'down';
        delta = `<span class="delta ${cls}" title="現在条件の修正単価中央値 ${yen(d.medianYenPerM2)} 円/㎡ との差">中央値比 ${r > 0 ? '+' : ''}${r.toFixed(1)}%</span>`;
    }

    $('detail-body').innerHTML = `
        <p class="detail-title">${esc(d.name || '(物件名なし)')}</p>
        <div class="detail-pid">${esc(d.projectId)}</div>
        <div class="detail-price">
            <span class="value">${yen(d.reviseYenPerM2)}</span><span class="unit">円/㎡（修正単価・現在条件）</span>
        </div>
        <div>${delta}</div>
        <div class="detail-table">${pairsHtml(MAIN_LAYOUT, attrs, '---')}</div>
        <details class="detail-more">
            <summary>すべての項目（${moreCount}件）</summary>
            <div class="detail-table">${pairsHtml(more, attrs, '')}</div>
        </details>`;
    $('detail-modal-title').textContent = `選択物件　${d.projectId}`;
    if (open) $('detail-modal').classList.remove('hidden');
}

/** レイアウトの後ろに、レイアウトに無い項目（杭工事など）を左右2列で追加する（空欄の位置はレイアウトどおり残す） */
function fillPairs(layout, extras) {
    const pairs = layout.map((p) => [...p]);
    for (let i = 0; i < extras.length; i += 2) pairs.push([extras[i], extras[i + 1] ?? null]);
    return pairs;
}

/** 左右2列（項目・値・項目・値）の表。値が無いときは empty を表示する */
function pairsHtml(layout, attrs, empty) {
    return layout.map(([l, r]) => cellPair(l, attrs, empty) + cellPair(r, attrs, empty)).join('');
}

function cellPair(key, attrs, empty) {
    if (!key) return '<div class="dk"></div><div class="dv"></div>';
    const v = attrs.get(key);
    const isNum = typeof v === 'number';
    const text = v == null || v === '' ? empty : formatValue(v);
    return `<div class="dk">${esc(key)}</div><div class="dv${isNum ? ' num' : ''}">${esc(text)}</div>`;
}

/** 数値は3桁区切り（小数は2桁まで）、日付は 2026/2/10 の形 */
function formatValue(v) {
    if (typeof v === 'number') return v.toLocaleString('ja-JP', { maximumFractionDigits: 2 });
    if (typeof v === 'string') {
        const m = v.match(/^(\d{4})-(\d{2})-(\d{2})(?:T00:00:00(?:\.0+)?)?$/);
        if (m) return `${+m[1]}/${+m[2]}/${+m[3]}`;
    }
    return display(v);
}
