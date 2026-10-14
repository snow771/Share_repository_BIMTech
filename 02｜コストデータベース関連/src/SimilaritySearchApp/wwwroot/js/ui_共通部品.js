// js/ui_共通部品.js
// トースト・ローディング・ステータスバー・セグメント切替など、画面共通の小部品。

const $ = (id) => document.getElementById(id);

let loadingCount = 0;

/** 画面全体のローディング表示。入れ子で呼んでも最後の終了で消える。 */
export async function withLoading(text, fn) {
    loadingCount++;
    $('loading-text').textContent = text;
    $('loading').classList.remove('hidden');
    try {
        return await fn();
    } finally {
        loadingCount--;
        if (loadingCount <= 0) {
            loadingCount = 0;
            $('loading').classList.add('hidden');
        }
    }
}

/**
 * トースト通知。type: info / success / error。
 * action: { label, onClick } を渡すとリンクを付ける（例: 設定を開く）。
 */
export function toast(message, type = 'info', action = null, ms = 5000) {
    const el = document.createElement('div');
    el.className = `toast ${type}`;
    el.textContent = message;
    if (action) {
        const a = document.createElement('a');
        a.textContent = action.label;
        a.addEventListener('click', () => {
            el.remove();
            action.onClick();
        });
        el.appendChild(a);
    }
    $('toast-area').appendChild(el);
    setTimeout(() => el.remove(), type === 'error' ? ms * 2 : ms);
}

export function setStatus(text) {
    $('status-bar').textContent = text;
}

/** .segmented の選択状態を value に合わせる */
export function setSegment(container, value, attr = 'value') {
    for (const b of container.querySelectorAll('.segmented-btn')) {
        b.classList.toggle('active', b.dataset[attr] === value);
    }
}

/** .segmented のクリックで onChange(value) を呼ぶ */
export function bindSegment(container, onChange, attr = 'value') {
    container.addEventListener('click', (e) => {
        const btn = e.target.closest('.segmented-btn');
        if (!btn || btn.classList.contains('active')) return;
        setSegment(container, btn.dataset[attr], attr);
        onChange(btn.dataset[attr]);
    });
}

/** DB・LLM 関連のエラーかどうか（設定画面へのリンクを出す判断） */
export function isConnectionError(message) {
    return /接続|Ollama|SQL|server|サーバー|タイムアウト|login|ログイン/i.test(message || '');
}
