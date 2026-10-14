// js/chatPanel_AIチャット.js
// 右下: AIアシスタント。クイックボタン（判断サマリー等）と自由入力。結果に条件変更があれば画面へ反映する。

import { state, emit } from './state_画面状態.js';
import { call } from './bridge_通信.js';
import { toast, isConnectionError } from './ui_共通部品.js';

const $ = (id) => document.getElementById(id);

let openSettings = () => {};
let busy = false;

export function initChatPanel({ onOpenSettings }) {
    openSettings = onOpenSettings;

    $('chat-form').addEventListener('submit', (e) => {
        e.preventDefault();
        sendChat($('chat-input').value);
    });
    $('chat-input').addEventListener('keydown', (e) => {
        if (e.key === 'Enter' && !e.shiftKey && !e.isComposing) {
            e.preventDefault();
            sendChat($('chat-input').value);
        }
    });
    for (const btn of document.querySelectorAll('[data-insight]')) {
        btn.addEventListener('click', () => runInsight(btn.dataset.insight, btn.textContent.trim()));
    }
    $('quick-ai-filter').addEventListener('click', () => {
        const text = $('chat-input').value.trim();
        if (!text) {
            toast('入力欄に条件を書いてから押してください（例: 東京都RC造7階 内装工事）');
            $('chat-input').focus();
            return;
        }
        runAiFilter(text);
    });
    $('chat-clear').addEventListener('click', async () => {
        $('chat-log').replaceChildren();
        try {
            await call('chatReset');   // AI が覚えている会話も消す
        } catch {
            // 消せなくても表示はクリアする
        }
        addBubble('system', '会話をクリアしました。続けて相談内容を入力してください。');
    });

    addBubble('assistant', 'こんにちは！AIアシスタントです。\n「東京都のRC造7階で内装工事の単価を見たい」のような条件の絞込みはもちろん、使い方の質問や、いまの結果についての相談も気軽にどうぞ。');
}

/** 吹き出しを追加する。role: user / assistant / system / error */
export function addBubble(role, text) {
    const row = document.createElement('div');
    row.className = `bubble-row ${role}`;
    const names = { user: 'あなた', assistant: '🤖 AI', error: '⚠ エラー' };
    if (names[role]) {
        const n = document.createElement('div');
        n.className = 'bubble-name';
        n.textContent = names[role];
        row.appendChild(n);
    }
    const b = document.createElement('div');
    b.className = 'bubble';
    b.textContent = text;
    row.appendChild(b);
    $('chat-log').appendChild(row);
    $('chat-log').scrollTop = $('chat-log').scrollHeight;
    return row;
}

/** DB を調べて答えたとき: 参照したテーブルを DB編集で開くボタン（検索語を検索欄に入れて開く） */
function addTableLink(table) {
    const row = document.createElement('div');
    row.className = 'bubble-row system chat-table-link';
    const btn = document.createElement('button');
    btn.type = 'button';
    btn.className = 'small-btn edit-btn';
    btn.textContent = `🗄 DB編集で「${table.label}」を開く${table.search ? `（検索: ${table.search}）` : ''}`;
    btn.addEventListener('click', () => emit('request:openTable', table));
    row.appendChild(btn);
    $('chat-log').appendChild(row);
    $('chat-log').scrollTop = $('chat-log').scrollHeight;
}

/** 自由入力（C# 側で AI が会話の流れから判断し、会話で答えるか サマリー/確認観点/物件説明/条件設定 を実行する） */
export function sendChat(raw) {
    const text = (raw ?? '').trim();
    if (!text || busy) return;
    $('chat-input').value = '';
    addBubble('user', text);
    return run(() => call('chat', { text }), { needsData: false });   // 挨拶や使い方の質問はデータ読込前でもできる
}

/** ヘッダーの検索バーから: 必ず AI 条件設定を行う */
export function runAiFilter(text) {
    if (!text || busy) return;
    addBubble('user', text);
    return run(() => call('aiFilter', { text }), 'AIが条件を解釈しています...');
}

function runInsight(kind, label) {
    if (busy) return;
    addBubble('user', label);
    return run(() => call('insight', { kind }));
}

async function run(fn, { needsData = true } = {}) {
    if (needsData && !state.loaded) {
        addBubble('error', '先にデータを読込してください。');
        return;
    }
    busy = true;
    const typing = addBubble('assistant', '');
    typing.querySelector('.bubble').classList.add('typing');
    try {
        const res = await fn();
        typing.remove();
        for (const m of res.messages ?? []) addBubble(m.role, m.text);
        if (res.table) addTableLink(res.table);
        if (res.result) emit('result', res.result);
    } catch (e) {
        typing.remove();
        addBubble('error', e.message);
        if (isConnectionError(e.message)) {
            toast('AI（ローカルLLM）への接続に失敗しました。', 'error', { label: '設定を開く', onClick: () => openSettings('llm') });
        }
    } finally {
        busy = false;
    }
}
