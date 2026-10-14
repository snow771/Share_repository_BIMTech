// js/bridge_通信.js
// C#（WebBridge）との通信。{ id, command, payload } を送り、{ id, ok, result | error } を待つ。
// WebView2 の外（普通のブラウザ）で開いたときは、画面確認用のモック（mock_モックデータ.js）に切り替える。
// C# からのお知らせ（{ event, data }）は、イベントバスに "bridge:<event>" として流す。

import { emit } from './state_画面状態.js';

const webview = window.chrome?.webview;
const pending = new Map();
let seq = 0;

export const isMock = !webview;

if (webview) {
    webview.addEventListener('message', (e) => {
        const msg = e.data;
        // C# からのお知らせ（進捗など）: { event, data } → イベントバスへ "bridge:<event>"
        if (msg && msg.event) {
            emit(`bridge:${msg.event}`, msg.data);
            return;
        }
        const p = msg && pending.get(msg.id);
        if (!p) return;
        pending.delete(msg.id);
        if (msg.ok) p.resolve(msg.result);
        else p.reject(new Error(msg.error || '不明なエラー'));
    });
}

/** C# のコマンドを呼ぶ。失敗時は Error を投げる。 */
export async function call(command, payload = {}) {
    if (!webview) {
        const mock = await import('./mock_モックデータ.js');
        return mock.handle(command, payload);
    }
    const id = `${Date.now()}-${++seq}`;
    return new Promise((resolve, reject) => {
        pending.set(id, { resolve, reject });
        webview.postMessage({ id, command, payload });
    });
}
