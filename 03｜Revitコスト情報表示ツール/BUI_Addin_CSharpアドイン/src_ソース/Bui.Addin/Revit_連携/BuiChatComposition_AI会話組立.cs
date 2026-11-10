// このファイルの目的:
//   AIチャットの部品を1か所で組み立てます。
//   LLMの設定（有効・モデル・制限時間）はパネルが読み込んだ設定をそのまま使います。
//
// 修正時の影響:
//   ICandidateSourceはパネルと同じものを使ってください。
//   別に作ると、AIの答えと検索タブの結果が食い違います。

using Bui.Data;
using Bui.Data.Csv;
using Bui.Llm;
using Bui.UI;

namespace Bui.Revit;

internal static class BuiChatComposition
{
    public static BuiChatViewModel Create(BuiDockPaneViewModel pane)
    {
        var context = new PaneChatContext(pane, pane.UiDispatcher);
        // isEnabledはActiveIsLlmEnabled（起動時固定）を使います。pane.IsLlmEnabled（設定タブの生きた値）は
        // Createの後で変わり得ますが、ここで組み立てたtranslator/assistantは以後変わりません。
        // ゲート（PaneChatContext.IsLlmEnabled）も同じActiveIsLlmEnabledを見るため、
        // 両者は常に一致します（Findings 4）。
        var session = new LlmChatSession(
            pane.CandidateSource
                ?? throw new InvalidOperationException(
                    "候補の絞り込みが準備できていません。設定タブでデータフォルダを確認してください。"),
            new LlmQueryTranslator(pane.ActiveLlmProvider, pane.ActiveIsLlmEnabled, pane.ActiveLlmModel, pane.LlmTimeout),
            new LlmSearchAssistant(pane.ActiveLlmProvider, pane.ActiveIsLlmEnabled, pane.ActiveLlmModel, pane.LlmTimeout),
            pane,
            // 表記ゆれ.csv は1往復ごとに読み直します（レビュー指摘）。起動時に1回だけ読んで
            // 固定すると、利用者が表記ゆれ.csvへ行を足しても「次の質問から効きます」という
            // 案内（Troubleshooting_問題解決.md）どおりにならず、Revitの再起動が要る嘘の案内に
            // なってしまいます。パス自体は起動時に解決済みのもの（pane.SynonymCsvResolvedPath）を
            // 使い回し、ファイルの中身だけを毎回読み直します。読み込みが失敗しても
            // LlmChatSession側がSynonymLoadResult.Emptyへ倒すため、会話は止まりません。
            // LastProblemsも一緒に返します。CsvSynonymLoaderはターンごとに使い捨てるため、
            // Tableだけを返すとLastProblemsを誰も読めず、起動後に見出しを壊しても
            // 二度と画面へ理由が出ません（レビュー指摘）。
            synonymsProvider: () =>
            {
                var loader = new CsvSynonymLoader();
                var table = loader.Load(pane.SynonymCsvResolvedPath);
                return new SynonymLoadResult(table, loader.LastProblems);
            });
        return new BuiChatViewModel(session, context, pane.UiDispatcher, pane.CommandErrorHandler);
    }
}
