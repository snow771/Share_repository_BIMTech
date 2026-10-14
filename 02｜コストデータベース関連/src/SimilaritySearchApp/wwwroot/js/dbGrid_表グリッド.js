// js/dbGrid_表グリッド.js
// Excel 風の表グリッド。セル選択・範囲選択・直接入力・F2編集・コピー/貼り付け（Excel と TSV で相互）・
// 行の追加/削除・元に戻す を、保存するまで画面の中だけで保持する（DB への反映は呼び出し側が changes() を保存）。
//
// 行の状態: clean（読込時のまま）/ new（追加）/ deleted（削除予定）。変更したセルは modified に記録する。
// 値は文字列。null は DB の NULL。空文字で保存すると NULL（NULL 不可の文字列列は空文字）になる。

import { esc } from './format_数値書式.js';
import { enableColumnResize } from './columnResize_列幅調整.js';

const NUMERIC_TYPES = new Set(['tinyint', 'smallint', 'int', 'bigint', 'decimal', 'numeric', 'money', 'smallmoney', 'float', 'real']);

export class DbGrid {
    /**
     * @param {HTMLElement} wrap  スクロールする入れ物
     * @param {{ onChange: () => void, onSort: (col:string) => void, onSave: () => void }} opts
     */
    constructor(wrap, opts) {
        this.wrap = wrap;
        this.opts = opts;
        this.columns = [];
        this.rows = [];
        this.rowOffset = 0;
        this.sort = { column: null, desc: false };
        this.active = { r: 0, c: 0 };
        this.anchor = { r: 0, c: 0 };
        this.editing = null;          // null | 'enter'（入力で開始）| 'edit'（F2・ダブルクリック）
        this.undoStack = [];
        this.dragging = false;

        this.table = document.createElement('table');
        this.table.className = 'db-grid';
        this.editor = document.createElement('input');
        this.editor.className = 'cell-editor';
        this.editor.setAttribute('aria-label', 'セルの入力');
        this.editor.autocomplete = 'off';
        this.hint = document.createElement('div');
        this.hint.className = 'cell-edit-hint';
        wrap.replaceChildren(this.table, this.editor, this.hint);
        this.tableKey = '';
        // 列幅はテーブル名＋列名ごとに保存（見出しの境界ドラッグ／ダブルクリックで自動調整）
        this.resizer = enableColumnResize(this.table, {
            storageKey: 'dbGrid.columnWidths',
            keyOf: (th) => (th.dataset.col ? `${this.tableKey}|${th.dataset.col}` : null),
        });
        this.bindEvents();
    }

    // ── 公開 API ─────────────────────────────────────────

    /** 表示データを読み込む（未保存の変更は捨てる） */
    load(columns, rows, { rowOffset = 0, sort = this.sort, tableKey = '' } = {}) {
        this.tableKey = tableKey;
        this.columns = columns;
        this.rows = rows.map((v) => ({ orig: v, values: [...v], state: 'clean', modified: new Set() }));
        this.rowOffset = rowOffset;
        this.sort = sort;
        this.undoStack = [];
        this.editing = null;
        this.active = { r: 0, c: this.firstEditableCol() };
        this.anchor = { ...this.active };
        this.render();
        this.opts.onChange();
    }

    clear(message = '') {
        this.columns = [];
        this.rows = [];
        this.undoStack = [];
        this.table.innerHTML = message ? `<tbody><tr><td class="empty-state">${esc(message)}</td></tr></tbody>` : '';
        this.editor.classList.remove('editing');
        this.opts.onChange();
    }

    get pendingCount() {
        const c = this.changes();
        return c.inserts.length + c.updates.length + c.deletes.length;
    }

    get hasData() {
        return this.columns.length > 0;
    }

    /** 保存する変更一式 { inserts, updates, deletes }（C# の DbChangeSet の形） */
    changes() {
        const inserts = [];
        const updates = [];
        const deletes = [];
        this.rows.forEach((row, r) => {
            const rowNo = this.rowNo(r);
            if (row.state === 'new') {
                const values = {};
                this.columns.forEach((col, c) => {
                    if (col.isEditable && (row.values[c] ?? '') !== '') values[col.name] = row.values[c];
                });
                inserts.push({ values, rowNo });
            } else if (row.state === 'deleted' && row.orig) {
                deletes.push({ key: this.keyOf(row), rowNo });
            } else if (row.state === 'clean' && row.modified.size > 0) {
                const values = {};
                for (const c of row.modified) values[this.columns[c].name] = row.values[c] ?? '';
                updates.push({ key: this.keyOf(row), values, rowNo });
            }
        });
        return { inserts, updates, deletes };
    }

    addRow() {
        this.commitEdit();
        const values = this.columns.map(() => null);
        this.rows.push({ orig: null, values, state: 'new', modified: new Set() });
        const r = this.rows.length - 1;
        this.undoStack.push({ type: 'add', r });
        this.render();
        this.select(r, this.firstEditableCol());
        this.opts.onChange();
    }

    /**
     * 複数行をまとめて「追加」として末尾に入れる（Excel取込用）。values は列順の文字列配列。
     * 元に戻す（Ctrl+Z）でまとめて取り消せる。
     */
    appendRows(rowsValues) {
        this.commitEdit();
        const from = this.rows.length;
        for (const v of rowsValues) {
            const values = this.columns.map((_, c) => (v[c] == null || v[c] === '' ? null : String(v[c])));
            this.rows.push({ orig: null, values, state: 'new', modified: new Set() });
        }
        this.undoStack.push({ type: 'addMany', from, count: rowsValues.length });
        this.render();
        if (rowsValues.length) this.select(from, this.firstEditableCol());
        this.opts.onChange();
    }

    /** 選択範囲の行を削除予定にする。すべて削除予定ならもとに戻す。 */
    toggleDeleteRows() {
        this.commitEdit();
        const { r1, r2 } = this.range();
        const targets = [];
        for (let r = r1; r <= r2; r++) targets.push(r);
        const restore = targets.every((r) => this.rows[r].state === 'deleted');
        const before = targets.map((r) => ({ r, state: this.rows[r].state }));
        for (const r of targets) {
            const row = this.rows[r];
            row.state = restore ? (row.orig ? 'clean' : 'new') : 'deleted';
        }
        this.undoStack.push({ type: 'rows', before });
        this.render();
        this.opts.onChange();
    }

    undo() {
        this.commitEdit();
        const op = this.undoStack.pop();
        if (!op) return;
        if (op.type === 'cells') {
            for (const ch of op.changes) this.applyValue(ch.r, ch.c, ch.old);
        } else if (op.type === 'add') {
            this.rows[op.r].state = 'deleted';   // 追加行は「削除予定」にして保存対象から外す
        } else if (op.type === 'addMany') {
            for (let r = op.from; r < op.from + op.count; r++) this.rows[r].state = 'deleted';
        } else if (op.type === 'rows') {
            for (const b of op.before) this.rows[b.r].state = b.state;
        }
        this.render();
        this.opts.onChange();
    }

    focus() {
        if (this.hasData) this.placeEditor(true);
    }

    // ── 描画 ───────────────────────────────────────────

    render() {
        const head = `<thead><tr><th class="rowhead"></th>${this.columns.map((col) => {
            const sorted = this.sort.column === col.name ? (this.sort.desc ? ' sorted-desc' : ' sorted-asc') : '';
            const marks = `${col.isPrimaryKey ? '🔑' : ''}${!col.isEditable ? '🔒' : ''}${!col.isNullable && col.isEditable ? '<span title="必須">*</span>' : ''}`;
            return `<th class="${sorted}" data-col="${esc(col.name)}" title="${esc(this.columnTitle(col))}">
                <span class="col-name">${marks}${esc(col.name)}</span><span class="col-type">${esc(typeLabel(col))}</span></th>`;
        }).join('')}</tr></thead>`;

        const body = this.rows.map((row, r) =>
            `<tr class="${row.state === 'clean' ? '' : row.state}"><th class="rowhead" data-r="${r}">${row.state === 'new' ? '＋' : this.rowNo(r)}</th>${
                this.columns.map((col, c) => this.cellHtml(row, r, c)).join('')}</tr>`).join('');

        this.table.innerHTML = head + `<tbody>${body}</tbody>`;
        this.resizer.apply();
        this.paintSelection();
        this.placeEditor();
    }

    cellHtml(row, r, c) {
        const col = this.columns[c];
        const v = row.values[c];
        const cls = ['cell'];
        if (NUMERIC_TYPES.has(col.typeName.toLowerCase())) cls.push('num');
        if (this.isReadOnly(r, c)) cls.push('readonly');
        if (row.modified.has(c)) cls.push('modified');
        // 空欄の注意表示: 必須（*）は赤、任意は別の色（変更したセルは「変更」の色を優先）
        const blank = this.blankKind(row, c);
        if (blank === 'required') cls.push('missing');
        else if (blank === 'optional' && !row.modified.has(c)) cls.push('empty');
        let text = v ?? '';
        if (v == null && !(row.state === 'new' && col.isIdentity)) cls.push('null');
        if (row.state === 'new' && col.isIdentity) text = '(自動)';
        return `<td class="${cls.join(' ')}" data-r="${r}" data-c="${c}" title="${esc(text)}">${esc(text)}</td>`;
    }

    updateCell(r, c) {
        const td = this.cell(r, c);
        if (!td) return;
        const tmp = document.createElement('tr');
        tmp.innerHTML = this.cellHtml(this.rows[r], r, c);
        td.replaceWith(tmp.firstChild);
    }

    paintSelection() {
        for (const td of this.table.querySelectorAll('td.in-range, td.active')) td.classList.remove('in-range', 'active');
        if (!this.rows.length) return;
        const { r1, r2, c1, c2 } = this.range();
        if (r1 !== r2 || c1 !== c2) {
            for (let r = r1; r <= r2; r++) for (let c = c1; c <= c2; c++) this.cell(r, c)?.classList.add('in-range');
        }
        this.cell(this.active.r, this.active.c)?.classList.add('active');
    }

    /**
     * 入力欄を選択セルの上に重ねる（編集していないときは透明）。
     * 焦点は、表の中を操作しているとき（または force）だけ移す。検索欄などで入力中に焦点を奪わないため。
     */
    placeEditor(force = false) {
        const td = this.cell(this.active.r, this.active.c);
        if (!td) {
            this.editor.style.display = 'none';
            return;
        }
        this.editor.style.display = '';
        const wrapBox = this.wrap.getBoundingClientRect();
        const box = td.getBoundingClientRect();
        this.editor.style.left = `${box.left - wrapBox.left + this.wrap.scrollLeft}px`;
        this.editor.style.top = `${box.top - wrapBox.top + this.wrap.scrollTop}px`;
        this.editor.style.height = `${box.height}px`;
        this.editor.style.width = `${Math.max(box.width, this.editing ? 220 : 0)}px`;
        if (this.editing) {
            this.hint.style.left = this.editor.style.left;
            this.hint.style.top = `${box.top - wrapBox.top + this.wrap.scrollTop - 18}px`;
        }
        const active = document.activeElement;
        const inGrid = !active || active === document.body || this.wrap.contains(active);
        if (active !== this.editor && (force || inGrid)) this.editor.focus({ preventScroll: true });
    }

    /** 編集できないセルで編集しようとしたときに理由を少しだけ表示する */
    flashReadOnly(r, c) {
        const col = this.columns[c];
        const why = this.rows[r]?.state === 'deleted' ? '削除予定の行です'
            : col?.isIdentity ? '自動採番の列です'
            : col?.isComputed ? '計算列です'
            : 'この型の列は編集できません';
        const td = this.cell(r, c);
        if (!td) return;
        const wrapBox = this.wrap.getBoundingClientRect();
        const box = td.getBoundingClientRect();
        this.hint.textContent = `🔒 編集できません（${why}）`;
        this.hint.style.left = `${box.left - wrapBox.left + this.wrap.scrollLeft}px`;
        this.hint.style.top = `${box.top - wrapBox.top + this.wrap.scrollTop - 18}px`;
        this.hint.classList.add('show', 'readonly');
        clearTimeout(this.hintTimer);
        this.hintTimer = setTimeout(() => this.hint.classList.remove('show', 'readonly'), 1600);
    }

    /** 編集中の表示（札・行番号・列見出しの強調）を切り替える */
    markEditing(on) {
        for (const el of this.table.querySelectorAll('.editing-row, .editing-col')) el.classList.remove('editing-row', 'editing-col');
        this.hint.classList.toggle('show', on);
        if (!on) return;
        const { r, c } = this.active;
        const col = this.columns[c];
        this.hint.textContent = `✏ 編集中: ${col?.name ?? ''}　Enter 確定 / Esc 取消`;
        this.cell(r, c)?.parentElement.classList.add('editing-row');
        this.table.tHead?.rows[0]?.cells[c + 1]?.classList.add('editing-col');
    }

    // ── 選択 ───────────────────────────────────────────

    select(r, c, extend = false) {
        if (!this.rows.length) return;
        r = clamp(r, 0, this.rows.length - 1);
        c = clamp(c, 0, this.columns.length - 1);
        this.active = { r, c };
        if (!extend) this.anchor = { r, c };
        this.paintSelection();
        this.cell(r, c)?.scrollIntoView({ block: 'nearest', inline: 'nearest' });
        this.placeEditor(true);   // セルを選んだら表に焦点を移す
    }

    range() {
        return {
            r1: Math.min(this.anchor.r, this.active.r), r2: Math.max(this.anchor.r, this.active.r),
            c1: Math.min(this.anchor.c, this.active.c), c2: Math.max(this.anchor.c, this.active.c),
        };
    }

    // ── 編集 ───────────────────────────────────────────

    startEdit(mode, initial) {
        const { r, c } = this.active;
        if (this.isReadOnly(r, c)) {
            this.editor.value = '';
            if (mode === 'edit' || initial) this.flashReadOnly(r, c);
            return false;
        }
        this.editing = mode;
        this.editor.value = initial ?? (this.rows[r].values[c] ?? '');
        this.editor.classList.add('editing');
        this.markEditing(true);
        this.placeEditor();
        if (mode === 'edit') this.editor.setSelectionRange(this.editor.value.length, this.editor.value.length);
        return true;
    }

    commitEdit() {
        if (!this.editing) return;
        const { r, c } = this.active;
        const value = this.editor.value;
        this.editing = null;
        this.editor.classList.remove('editing');
        this.markEditing(false);
        this.editor.value = '';
        this.setValues([{ r, c, value }]);
        this.placeEditor();
    }

    cancelEdit() {
        this.editing = null;
        this.editor.classList.remove('editing');
        this.markEditing(false);
        this.editor.value = '';
        this.placeEditor();
    }

    /** 複数セルをまとめて書き換える（元に戻すは1回でまとめて戻る） */
    setValues(list) {
        const changes = [];
        for (const { r, c, value } of list) {
            if (this.isReadOnly(r, c)) continue;
            const old = this.rows[r].values[c];
            if ((old ?? '') === (value ?? '')) continue;   // NULL と空欄は同じ扱い（保存すると NULL）
            changes.push({ r, c, old, value });
            this.applyValue(r, c, value);
        }
        if (changes.length) {
            this.undoStack.push({ type: 'cells', changes });
            this.opts.onChange();
        }
    }

    applyValue(r, c, value) {
        const row = this.rows[r];
        row.values[c] = value;
        if (row.state !== 'new' && row.orig) {
            if ((row.orig[c] ?? '') === (value ?? '')) row.modified.delete(c);
            else row.modified.add(c);
        }
        this.updateCell(r, c);
        this.paintSelection();
    }

    clearRange() {
        const { r1, r2, c1, c2 } = this.range();
        const list = [];
        for (let r = r1; r <= r2; r++) for (let c = c1; c <= c2; c++) list.push({ r, c, value: '' });
        this.setValues(list);
    }

    // ── コピー・貼り付け（Excel の TSV 形式） ───────────

    copyText() {
        const { r1, r2, c1, c2 } = this.range();
        const lines = [];
        for (let r = r1; r <= r2; r++) {
            const cells = [];
            for (let c = c1; c <= c2; c++) cells.push(tsvCell(this.rows[r].values[c] ?? ''));
            lines.push(cells.join('\t'));
        }
        return lines.join('\r\n');
    }

    pasteText(text) {
        const data = parseTsv(text);
        if (!data.length) return;
        const { r1, c1 } = this.range();
        // 単一値を範囲に貼ると範囲全体に入れる（Excel と同じ）
        const { r2, c2 } = this.range();
        const fill = data.length === 1 && data[0].length === 1 && (r2 > r1 || c2 > c1);
        const height = fill ? r2 - r1 + 1 : data.length;
        const width = fill ? c2 - c1 + 1 : Math.max(...data.map((d) => d.length));

        // 足りない行は追加する
        let added = false;
        while (this.rows.length < r1 + height) {
            this.rows.push({ orig: null, values: this.columns.map(() => null), state: 'new', modified: new Set() });
            added = true;
        }
        if (added) this.render();

        const list = [];
        for (let i = 0; i < height; i++) {
            for (let j = 0; j < width; j++) {
                const c = c1 + j;
                if (c >= this.columns.length) continue;
                const value = fill ? data[0][0] : data[i][j];
                if (value === undefined) continue;
                list.push({ r: r1 + i, c, value });
            }
        }
        this.setValues(list);
        this.anchor = { r: r1, c: c1 };
        this.active = { r: Math.min(r1 + height - 1, this.rows.length - 1), c: Math.min(c1 + width - 1, this.columns.length - 1) };
        this.paintSelection();
        this.placeEditor();
        this.opts.onChange();
    }

    // ── イベント ───────────────────────────────────────

    bindEvents() {
        const t = this.table;

        t.addEventListener('mousedown', (e) => {
            const td = e.target.closest('td[data-r]');
            const rowHead = e.target.closest('tbody th.rowhead');
            if (td) {
                e.preventDefault();
                this.commitEdit();
                this.select(+td.dataset.r, +td.dataset.c, e.shiftKey);
                this.dragging = true;
            } else if (rowHead) {
                e.preventDefault();
                this.commitEdit();
                // 行番号のクリックで行全体を選択（Shift で複数行）
                const r = +rowHead.dataset.r;
                if (!e.shiftKey) this.anchor = { r, c: 0 };
                this.active = { r, c: this.columns.length - 1 };
                this.paintSelection();
                this.placeEditor(true);
            }
        });
        t.addEventListener('mouseover', (e) => {
            if (!this.dragging) return;
            const td = e.target.closest('td[data-r]');
            if (td) this.select(+td.dataset.r, +td.dataset.c, true);
        });
        document.addEventListener('mouseup', () => { this.dragging = false; });
        t.addEventListener('dblclick', (e) => {
            const td = e.target.closest('td[data-r]');
            if (td) this.startEdit('edit');
        });
        t.addEventListener('click', (e) => {
            const th = e.target.closest('thead th[data-col]');
            if (th) {
                this.commitEdit();
                this.opts.onSort(th.dataset.col);
            }
        });
        this.wrap.addEventListener('scroll', () => { if (this.editing) this.placeEditor(); });

        const ed = this.editor;
        ed.addEventListener('compositionstart', () => {
            if (!this.editing) this.startEdit('enter', '');
        });
        ed.addEventListener('input', () => {
            if (!this.editing) {
                const typed = ed.value;
                if (!this.startEdit('enter', typed)) ed.value = '';
            }
        });
        ed.addEventListener('blur', () => {
            // 別のボタンを押したときなどは入力を確定する
            if (this.editing) this.commitEdit();
        });
        ed.addEventListener('copy', (e) => {
            if (this.editing) return;
            e.preventDefault();
            e.clipboardData.setData('text/plain', this.copyText());
        });
        ed.addEventListener('cut', (e) => {
            if (this.editing) return;
            e.preventDefault();
            e.clipboardData.setData('text/plain', this.copyText());
            this.clearRange();
        });
        ed.addEventListener('paste', (e) => {
            if (this.editing) return;
            e.preventDefault();
            this.pasteText(e.clipboardData.getData('text/plain'));
        });
        ed.addEventListener('keydown', (e) => this.onKey(e));
    }

    onKey(e) {
        if (e.isComposing || !this.rows.length) return;
        const ctrl = e.ctrlKey || e.metaKey;
        const { r, c } = this.active;

        if (ctrl && e.key.toLowerCase() === 's') {
            e.preventDefault();
            this.commitEdit();
            this.opts.onSave();
            return;
        }

        if (this.editing) {
            const moveKeys = { Enter: [e.shiftKey ? -1 : 1, 0], Tab: [0, e.shiftKey ? -1 : 1] };
            if (this.editing === 'enter') Object.assign(moveKeys, { ArrowDown: [1, 0], ArrowUp: [-1, 0] });
            if (e.key === 'Escape') {
                e.preventDefault();
                this.cancelEdit();
            } else if (moveKeys[e.key]) {
                e.preventDefault();
                this.commitEdit();
                this.select(r + moveKeys[e.key][0], c + moveKeys[e.key][1]);
            }
            return;
        }

        const arrows = { ArrowUp: [-1, 0], ArrowDown: [1, 0], ArrowLeft: [0, -1], ArrowRight: [0, 1] };
        if (arrows[e.key]) {
            e.preventDefault();
            const [dr, dc] = arrows[e.key];
            if (ctrl) {
                // Ctrl+矢印: 端まで移動
                const nr = dr ? (dr > 0 ? this.rows.length - 1 : 0) : r;
                const nc = dc ? (dc > 0 ? this.columns.length - 1 : 0) : c;
                this.select(nr, nc, e.shiftKey);
            } else {
                this.select(r + dr, c + dc, e.shiftKey);
            }
            return;
        }

        switch (e.key) {
            case 'Enter':
                e.preventDefault();
                this.select(r + (e.shiftKey ? -1 : 1), c);
                return;
            case 'Tab':
                e.preventDefault();
                this.select(r, c + (e.shiftKey ? -1 : 1));
                return;
            case 'F2':
                e.preventDefault();
                this.startEdit('edit');
                return;
            case 'Delete':
            case 'Backspace':
                e.preventDefault();
                this.clearRange();
                return;
            case 'Home':
                e.preventDefault();
                this.select(ctrl ? 0 : r, 0, e.shiftKey);
                return;
            case 'End':
                e.preventDefault();
                this.select(ctrl ? this.rows.length - 1 : r, this.columns.length - 1, e.shiftKey);
                return;
            case 'PageDown':
            case 'PageUp':
                e.preventDefault();
                this.select(r + (e.key === 'PageDown' ? 20 : -20), c, e.shiftKey);
                return;
        }

        if (ctrl && e.key.toLowerCase() === 'z') {
            e.preventDefault();
            this.undo();
        } else if (ctrl && e.key.toLowerCase() === 'a') {
            e.preventDefault();
            this.anchor = { r: 0, c: 0 };
            this.active = { r: this.rows.length - 1, c: this.columns.length - 1 };
            this.paintSelection();
            this.placeEditor();
        }
        // それ以外の文字キーは入力欄に入り、input イベントで編集が始まる
    }

    // ── 補助 ───────────────────────────────────────────

    /**
     * 空欄の種類: 'required'（必須＝NULL 不可の列が空）/ 'optional'（任意の列が空）/ null（空でない・対象外）。
     * 読込済みの行も対象（DB に空文字で入っている必須列も見つけられるように）。自動採番・計算列・削除予定の行は除く。
     */
    blankKind(row, c) {
        const col = this.columns[c];
        if (!col || !col.isEditable || row.state === 'deleted') return null;
        if ((row.values[c] ?? '') !== '') return null;
        return col.isNullable ? 'optional' : 'required';
    }

    /** 保存しようとしている行（追加・変更）にある、必須の空欄の数（保存前の確認用） */
    get missingRequiredCount() {
        let n = 0;
        this.rows.forEach((row) => {
            if (row.state !== 'new' && row.modified.size === 0) return;
            this.columns.forEach((_, c) => { if (this.blankKind(row, c) === 'required') n++; });
        });
        return n;
    }

    cell(r, c) {
        return this.table.querySelector(`td[data-r="${r}"][data-c="${c}"]`);
    }

    rowNo(r) {
        return this.rowOffset + r + 1;
    }

    isReadOnly(r, c) {
        const col = this.columns[c];
        const row = this.rows[r];
        return !col || !row || !col.isEditable || row.state === 'deleted';
    }

    firstEditableCol() {
        const i = this.columns.findIndex((c) => c.isEditable);
        return i < 0 ? 0 : i;
    }

    /** 行を特定するキー（読込時の全列の値。C# 側で主キー列だけを使う） */
    keyOf(row) {
        const key = {};
        this.columns.forEach((col, c) => { key[col.name] = row.orig[c]; });
        return key;
    }

    columnTitle(col) {
        const notes = [];
        if (col.isPrimaryKey) notes.push('主キー');
        if (col.isIdentity) notes.push('自動採番（編集不可）');
        if (col.isComputed) notes.push('計算列（編集不可）');
        if (!col.isEditable && !col.isIdentity && !col.isComputed) notes.push('この型は編集できません');
        if (!col.isNullable) notes.push('必須');
        return `${col.name}（${typeLabel(col)}）${notes.length ? '\n' + notes.join(' / ') : ''}\nクリックで並べ替え`;
    }
}

// ------------------------------------------------------------------

function typeLabel(col) {
    const t = col.typeName.toLowerCase();
    if (col.maxLength != null) return `${t}(${col.maxLength === -1 ? 'max' : col.maxLength})`;
    if (t === 'decimal' || t === 'numeric') return `${t}(${col.precision},${col.scale})`;
    return t;
}

function clamp(v, lo, hi) {
    return Math.max(lo, Math.min(hi, v));
}

/** TSV の1セル。タブ・改行・" を含むときは " で囲む（Excel と同じ） */
function tsvCell(s) {
    return /[\t\r\n"]/.test(s) ? `"${s.replace(/"/g, '""')}"` : s;
}

/** Excel からコピーした TSV を2次元配列に（" で囲まれたセル内の改行・タブに対応） */
export function parseTsv(text) {
    const rows = [];
    let row = [];
    let cell = '';
    let quoted = false;
    let i = 0;
    const s = text.replace(/\r\n?/g, '\n');
    while (i < s.length) {
        const ch = s[i];
        if (quoted) {
            if (ch === '"' && s[i + 1] === '"') {
                cell += '"';
                i += 2;
                continue;
            }
            if (ch === '"') {
                quoted = false;
                i++;
                continue;
            }
            cell += ch;
            i++;
            continue;
        }
        if (ch === '"' && cell === '') {
            quoted = true;
        } else if (ch === '\t') {
            row.push(cell);
            cell = '';
        } else if (ch === '\n') {
            row.push(cell);
            rows.push(row);
            row = [];
            cell = '';
        } else {
            cell += ch;
        }
        i++;
    }
    if (cell !== '' || row.length) {
        row.push(cell);
        rows.push(row);
    }
    return rows;
}
