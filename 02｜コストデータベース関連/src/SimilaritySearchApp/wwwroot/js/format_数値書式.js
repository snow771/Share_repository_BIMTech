// js/format_数値書式.js
// 数値・文字列の表示書式。

export const TSUBO_PER_M2 = 3.305785;

const isNum = (v) => typeof v === 'number' && Number.isFinite(v);

/** 円表示（四捨五入・3桁区切り）。欠損は --- */
export function yen(v) {
    return isNum(v) ? Math.round(v).toLocaleString('ja-JP') : '---';
}

/** 補正前 → 補正後 */
export function arrow(a, b, unit = '') {
    if (!isNum(a) && !isNum(b)) return `---${unit}`;
    return `${yen(a)} → ${yen(b)}${unit}`;
}

export function pct(v, digits = 2) {
    return isNum(v) ? `${v.toFixed(digits)}%` : '---';
}

export function num(v, digits = 0) {
    if (!isNum(v)) return '---';
    return v.toLocaleString('ja-JP', { minimumFractionDigits: digits, maximumFractionDigits: digits });
}

/** 軸ラベル用の短い表記 */
export function compact(v) {
    if (!isNum(v)) return '';
    const a = Math.abs(v);
    if (a >= 1e6) return `${(v / 1e6).toFixed(a >= 1e7 ? 0 : 1)}M`;
    if (a >= 1e4) return `${Math.round(v / 1e3).toLocaleString('ja-JP')}k`;
    return Math.round(v).toLocaleString('ja-JP');
}

/** 階数などの整数表示（7.0 → 7） */
export function int(v) {
    return isNum(v) ? String(Math.round(v * 100) / 100) : '---';
}

/** HTML エスケープ（innerHTML に文字列を入れるときは必ず通す） */
export function esc(s) {
    return String(s ?? '').replace(/[&<>"']/g, (c) => ({
        '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;',
    }[c]));
}

/** DB 値の汎用表示 */
export function display(v) {
    if (v == null || v === '') return '---';
    if (typeof v === 'number') return Number.isInteger(v) ? v.toLocaleString('ja-JP') : v.toLocaleString('ja-JP', { maximumFractionDigits: 2 });
    if (typeof v === 'string' && /^\d{4}-\d{2}-\d{2}T/.test(v)) return v.slice(0, 10);
    if (typeof v === 'boolean') return v ? 'はい' : 'いいえ';
    return String(v);
}
