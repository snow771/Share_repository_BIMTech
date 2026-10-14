// js/dbDiagram_関連図.js
// 関連テーブル図。選択テーブルを中央に、親（参照先）を左、子（参照元）を右に並べたコンパクトな配置で描き、
// 最初は全体が枠に収まるよう縮小して表示する。
//   ・パネル（カード）をドラッグ → 自由に配置し直す（線も追従）。動かさずに離せばクリック＝そのテーブルを開く
//   ・背景をドラッグ → 全体を移動 ／ マウスホイール → カーソル位置を中心に拡大縮小
//   ・実線 = DB の外部キー制約、点線 = 同名のキー列からの推定
// 配置は中央テーブルごとに覚えておき、同じテーブルを開き直すと前回の配置で表示する（アプリ起動中のみ）。

import { esc } from './format_数値書式.js';

const NS = 'http://www.w3.org/2000/svg';
const CARD_W = 180;
const CARD_H = 46;
const GAP_X = 110;     // 列の間隔（カードの外側どうし）
const GAP_Y = 14;
const MIN_SCALE = 0.3;
const MAX_SCALE = 2.0;
const DRAG_THRESHOLD = 4;
const DIR_LABEL = { parent: '親', child: '子', peer: '関連' };

const savedLayouts = new Map();   // 中央テーブル → { 'schema.name': {x, y} }

let ctx = null;   // 現在の図 { box, world, svg, cards: Map, hood, onOpen, view: {x, y, s} }

/**
 * @param {HTMLElement} box
 * @param {object} hood   C# の DbNeighborhood { center, neighbors[] }
 * @param {(schema:string, name:string) => void} onOpen
 */
export function renderDiagram(box, hood, onOpen) {
    box.replaceChildren();
    const world = document.createElement('div');
    world.className = 'db-world';
    const svg = document.createElementNS(NS, 'svg');
    svg.setAttribute('class', 'db-links');
    world.appendChild(svg);
    box.appendChild(world);

    ctx = { box, world, svg, hood, onOpen, cards: new Map(), view: { x: 0, y: 0, s: 1 } };

    const key = hood.center.fullName ?? `${hood.center.schema}.${hood.center.name}`;
    const layout = savedLayouts.get(key) ?? defaultLayout(hood);
    savedLayouts.set(key, layout);

    addCard(hood.center, null, layout[key] ?? { x: 0, y: 0 }, true);
    for (const n of hood.neighbors) {
        const k = n.table.fullName ?? `${n.table.schema}.${n.table.name}`;
        addCard(n.table, n, layout[k] ?? { x: 0, y: 0 }, false);
    }
    drawLinks();

    if (!hood.neighbors.length) {
        const note = document.createElement('div');
        note.className = 'db-diagram-note';
        note.textContent = '関連するテーブルは見つかりませんでした（外部キー制約・同名のキー列なし）。';
        box.appendChild(note);
    }

    bindViewEvents(box);
    fitDiagram();
}

/** 全体が枠に収まるように表示（拡大はしない） */
export function fitDiagram() {
    if (!ctx) return;
    const { box } = ctx;
    const b = bounds();
    const pad = 24;
    const w = box.clientWidth || 800;
    const h = box.clientHeight || 240;
    const s = clamp(Math.min((w - pad * 2) / b.w, (h - pad * 2) / b.h, 1), MIN_SCALE, MAX_SCALE);
    ctx.view = { s, x: (w - b.w * s) / 2 - b.x * s, y: (h - b.h * s) / 2 - b.y * s };
    applyView();
}

/** パネルの配置を初期状態に戻す */
export function resetDiagramLayout() {
    if (!ctx) return;
    const key = ctx.hood.center.fullName ?? `${ctx.hood.center.schema}.${ctx.hood.center.name}`;
    savedLayouts.delete(key);
    renderDiagram(ctx.box, ctx.hood, ctx.onOpen);
}

// ------------------------------------------------------------------
// 配置

/** 初期配置: 中央に選択テーブル、左に親、右に子。関連（peer）は少ない側へ。 */
function defaultLayout(hood) {
    const left = hood.neighbors.filter((n) => n.direction === 'parent');
    const right = hood.neighbors.filter((n) => n.direction === 'child');
    for (const n of hood.neighbors.filter((x) => x.direction === 'peer')) (left.length <= right.length ? left : right).push(n);

    const rows = Math.max(left.length, right.length, 1);
    const height = rows * CARD_H + (rows - 1) * GAP_Y;
    const layout = {};
    const key = (t) => t.fullName ?? `${t.schema}.${t.name}`;
    layout[key(hood.center)] = { x: CARD_W + GAP_X, y: (height - CARD_H) / 2 };

    const column = (list, x) => {
        const total = list.length * CARD_H + Math.max(0, list.length - 1) * GAP_Y;
        let y = (height - total) / 2;
        for (const n of list) {
            layout[key(n.table)] = { x, y };
            y += CARD_H + GAP_Y;
        }
    };
    column(left, 0);
    column(right, (CARD_W + GAP_X) * 2);
    return layout;
}

function addCard(t, neighbor, pos, isCenter) {
    const el = document.createElement('div');
    el.className = `db-card${isCenter ? ' center' : ''}`;
    el.style.width = `${CARD_W}px`;
    el.title = neighbor
        ? `${t.name}\n${neighbor.isForeignKey ? '外部キー制約' : '同名キー列からの推定'}: ${neighbor.links.join(' / ')}\nクリックで開く／ドラッグで移動`
        : `${t.name}（選択中）\nドラッグで移動`;
    el.innerHTML = `
        ${neighbor ? `<span class="c-dir ${neighbor.direction}">${DIR_LABEL[neighbor.direction] ?? ''}</span>` : ''}
        <span class="c-label">${esc(t.label)}</span>
        <span class="c-sub"><span>${esc(t.baseName)}</span><span>${Number(t.rowCount).toLocaleString('ja-JP')} 行</span></span>`;
    ctx.world.appendChild(el);

    const card = { el, t, neighbor, pos: { ...pos } };
    const key = t.fullName ?? `${t.schema}.${t.name}`;
    ctx.cards.set(key, card);
    move(card, pos.x, pos.y);
    bindCardDrag(card, key);
}

function move(card, x, y) {
    card.pos = { x, y };
    card.el.style.left = `${x}px`;
    card.el.style.top = `${y}px`;
}

// ------------------------------------------------------------------
// 線

function drawLinks() {
    const { svg, hood, cards } = ctx;
    svg.replaceChildren();
    const centerKey = hood.center.fullName ?? `${hood.center.schema}.${hood.center.name}`;
    const c = cards.get(centerKey);
    for (const n of hood.neighbors) {
        const card = cards.get(n.table.fullName ?? `${n.table.schema}.${n.table.name}`);
        if (!card) continue;

        // 左右の位置関係で、どちらの辺どうしを結ぶか決める
        const nLeft = card.pos.x + CARD_W / 2 < c.pos.x + CARD_W / 2;
        const x1 = nLeft ? card.pos.x + CARD_W : card.pos.x;
        const y1 = card.pos.y + CARD_H / 2;
        const x2 = nLeft ? c.pos.x : c.pos.x + CARD_W;
        const y2 = c.pos.y + CARD_H / 2;
        const bend = Math.max(40, Math.abs(x2 - x1) / 2);
        const cx1 = nLeft ? x1 + bend : x1 - bend;
        const cx2 = nLeft ? x2 - bend : x2 + bend;

        const path = document.createElementNS(NS, 'path');
        path.setAttribute('d', `M${x1},${y1} C${cx1},${y1} ${cx2},${y2} ${x2},${y2}`);
        path.setAttribute('class', `link ${n.isForeignKey ? 'fk' : 'inferred'}`);
        svg.appendChild(path);
        card.path = path;

        const label = document.createElementNS(NS, 'text');
        label.setAttribute('class', 'link-label');
        label.setAttribute('x', nLeft ? x1 + 6 : x1 - 6);
        label.setAttribute('y', y1 - 5);
        label.setAttribute('text-anchor', nLeft ? 'start' : 'end');
        const first = shortLink(n.links[0] ?? '');
        label.textContent = n.links.length > 1 ? `${first} 他${n.links.length - 1}` : first;
        svg.appendChild(label);
    }
}

// ------------------------------------------------------------------
// 操作（パネルのドラッグ・背景のパン・ホイールズーム）

function bindCardDrag(card, key) {
    card.el.addEventListener('mousedown', (e) => {
        if (e.button !== 0) return;
        e.preventDefault();
        e.stopPropagation();   // 背景のパンを始めない
        const start = { mx: e.clientX, my: e.clientY, x: card.pos.x, y: card.pos.y };
        let moved = false;
        card.el.classList.add('dragging');

        const onMove = (ev) => {
            const dx = (ev.clientX - start.mx) / ctx.view.s;
            const dy = (ev.clientY - start.my) / ctx.view.s;
            if (!moved && Math.hypot(ev.clientX - start.mx, ev.clientY - start.my) < DRAG_THRESHOLD) return;
            moved = true;
            move(card, start.x + dx, start.y + dy);
            drawLinks();
        };
        const onUp = () => {
            document.removeEventListener('mousemove', onMove);
            document.removeEventListener('mouseup', onUp);
            card.el.classList.remove('dragging');
            const centerKey = ctx.hood.center.fullName ?? `${ctx.hood.center.schema}.${ctx.hood.center.name}`;
            if (moved) {
                savedLayouts.get(centerKey)[key] = { ...card.pos };
            } else if (card.neighbor) {
                ctx.onOpen(card.t.schema, card.t.name);   // 動かさずに離した = クリック
            }
        };
        document.addEventListener('mousemove', onMove);
        document.addEventListener('mouseup', onUp);
    });
    card.el.addEventListener('mouseenter', () => card.path?.classList.add('hover'));
    card.el.addEventListener('mouseleave', () => card.path?.classList.remove('hover'));
}

function bindViewEvents(box) {
    if (box.dataset.bound) return;   // 枠へのイベントは1回だけ登録
    box.dataset.bound = '1';

    box.addEventListener('wheel', (e) => {
        if (!ctx || ctx.box !== box) return;
        e.preventDefault();
        const rect = box.getBoundingClientRect();
        const mx = e.clientX - rect.left;
        const my = e.clientY - rect.top;
        const { x, y, s } = ctx.view;
        const next = clamp(s * Math.exp(-e.deltaY * 0.0015), MIN_SCALE, MAX_SCALE);
        // カーソル位置を中心に拡大縮小
        ctx.view = { s: next, x: mx - (mx - x) * (next / s), y: my - (my - y) * (next / s) };
        applyView();
    }, { passive: false });

    box.addEventListener('mousedown', (e) => {
        if (!ctx || e.button !== 0 || e.target.closest('.db-card')) return;
        e.preventDefault();
        const start = { mx: e.clientX, my: e.clientY, x: ctx.view.x, y: ctx.view.y };
        box.classList.add('panning');
        const onMove = (ev) => {
            ctx.view.x = start.x + ev.clientX - start.mx;
            ctx.view.y = start.y + ev.clientY - start.my;
            applyView();
        };
        const onUp = () => {
            box.classList.remove('panning');
            document.removeEventListener('mousemove', onMove);
            document.removeEventListener('mouseup', onUp);
        };
        document.addEventListener('mousemove', onMove);
        document.addEventListener('mouseup', onUp);
    });

    box.addEventListener('dblclick', (e) => {
        if (!e.target.closest('.db-card')) fitDiagram();   // 背景のダブルクリックで全体表示
    });
}

function applyView() {
    const { x, y, s } = ctx.view;
    ctx.world.style.transform = `translate(${x}px, ${y}px) scale(${s})`;
    ctx.box.dataset.zoom = `${Math.round(s * 100)}%`;
}

// ------------------------------------------------------------------

function bounds() {
    const xs = [...ctx.cards.values()].map((c) => c.pos);
    const minX = Math.min(...xs.map((p) => p.x));
    const minY = Math.min(...xs.map((p) => p.y)) - 12;   // 上に出る「親/子」札のぶん
    const maxX = Math.max(...xs.map((p) => p.x + CARD_W));
    const maxY = Math.max(...xs.map((p) => p.y + CARD_H));
    return { x: minX, y: minY, w: Math.max(1, maxX - minX), h: Math.max(1, maxY - minY) };
}

function clamp(v, lo, hi) {
    return Math.max(lo, Math.min(hi, v));
}

/** "Work_Type_Code → Work_Type_Code_工種コード" → "Work_Type_Code" */
function shortLink(s) {
    const left = s.split(/\s*[→⇄]\s*/)[0] ?? s;
    return left.length > 24 ? `${left.slice(0, 23)}…` : left;
}
