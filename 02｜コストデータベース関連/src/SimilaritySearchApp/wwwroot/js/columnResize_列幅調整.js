// js/columnResize_列幅調整.js
// 表の列幅を Excel のように変える共通部品。
//   ・見出しの右端（境界）をドラッグ → 幅を変更
//   ・見出しの右端をダブルクリック → 中身に合わせて自動調整
// 幅は列キーごとに保持し、表を描き直しても保たれる（localStorage に保存できれば次回も復元）。
// 一度も幅を変えていない表は通常の自動レイアウト。変えた列があれば、全列の幅を固定するレイアウトに切り替える。

const MIN_WIDTH = 36;
const MAX_AUTO_WIDTH = 600;

/**
 * @param {HTMLTableElement} table
 * @param {{ storageKey: string, keyOf: (th: HTMLTableCellElement) => string|null }} opts
 *   keyOf: 見出しセル → 列キー（null の列は幅を変えない。例: 行番号列）
 * @returns {{ apply: () => void, reset: () => void }}  apply は表を描き直したあとに呼ぶ
 */
export function enableColumnResize(table, { storageKey, keyOf }) {
    const widths = load(storageKey);
    let drag = null;
    let suppressClick = false;

    const headCells = () => [...(table.tHead?.rows[0]?.cells ?? [])];

    function apply() {
        const ths = headCells();
        for (const th of ths) {
            if (keyOf(th) != null && !th.querySelector('.col-resizer')) {
                const grip = document.createElement('span');
                grip.className = 'col-resizer';
                grip.title = 'ドラッグで幅を変更／ダブルクリックで中身に合わせる';
                th.appendChild(grip);
            }
        }

        // いったん自動レイアウトに戻す
        table.classList.remove('fixed-cols');
        table.style.width = '';
        for (const th of ths) setWidth(th, null);

        const known = ths.some((th) => widths[keyOf(th)]);
        if (!known) return;

        // 幅を決めていない列は、自動レイアウトでの今の幅で確定してから固定レイアウトにする
        const natural = ths.map((th) => Math.ceil(th.getBoundingClientRect().width));
        let total = 0;
        ths.forEach((th, i) => {
            const key = keyOf(th);
            let w = natural[i];
            if (key != null) {
                widths[key] ??= natural[i];
                w = widths[key];
            }
            setWidth(th, w);
            total += w;
        });
        table.style.width = `${total}px`;
        table.classList.add('fixed-cols');
    }

    /** 列の中身（見出し・全セル）が収まる幅 */
    function fitWidth(th) {
        const index = th.cellIndex;
        let max = th.scrollWidth;
        for (const body of table.tBodies) {
            for (const row of body.rows) {
                const cell = row.cells[index];
                if (cell && cell.colSpan === 1) max = Math.max(max, cell.scrollWidth);
            }
        }
        return Math.min(MAX_AUTO_WIDTH, Math.max(MIN_WIDTH, max + 6));
    }

    table.addEventListener('mousedown', (e) => {
        const grip = e.target.closest('.col-resizer');
        if (!grip) return;
        e.preventDefault();
        e.stopPropagation();
        const th = grip.closest('th');
        const key = keyOf(th);
        widths[key] = Math.round(th.getBoundingClientRect().width);
        drag = { key, startX: e.clientX, startW: widths[key] };
        document.body.classList.add('col-resizing');
        apply();
    }, true);

    document.addEventListener('mousemove', (e) => {
        if (!drag) return;
        widths[drag.key] = Math.max(MIN_WIDTH, Math.round(drag.startW + e.clientX - drag.startX));
        apply();
    });

    document.addEventListener('mouseup', () => {
        if (!drag) return;
        drag = null;
        document.body.classList.remove('col-resizing');
        suppressClick = true;   // 直後の click（並べ替え）を無視する
        setTimeout(() => { suppressClick = false; }, 0);
        save(storageKey, widths);
    });

    table.addEventListener('dblclick', (e) => {
        const grip = e.target.closest('.col-resizer');
        if (!grip) return;
        e.preventDefault();
        e.stopPropagation();
        const th = grip.closest('th');
        if (!widths[keyOf(th)]) {
            widths[keyOf(th)] = Math.round(th.getBoundingClientRect().width);
            apply();   // 固定レイアウトにしてから測る
        }
        widths[keyOf(th)] = fitWidth(th);
        apply();
        save(storageKey, widths);
    }, true);

    // 取っ手の操作で並べ替えなどが動かないようにする
    table.addEventListener('click', (e) => {
        if (suppressClick || e.target.closest('.col-resizer')) {
            e.stopPropagation();
            e.preventDefault();
        }
    }, true);

    return {
        apply,
        reset() {
            for (const k of Object.keys(widths)) delete widths[k];
            save(storageKey, widths);
            apply();
        },
    };
}

function setWidth(th, w) {
    const v = w == null ? '' : `${w}px`;
    th.style.width = v;
    th.style.minWidth = v;
    th.style.maxWidth = v;
}

function load(key) {
    try {
        return JSON.parse(localStorage.getItem(key) ?? '{}') ?? {};
    } catch {
        return {};
    }
}

function save(key, widths) {
    try {
        localStorage.setItem(key, JSON.stringify(widths));
    } catch {
        // 保存できなくても動作には影響しない
    }
}
