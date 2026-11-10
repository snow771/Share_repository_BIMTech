// このファイルの目的:
//   会話画面とBUIパネルの橋渡しです。
//   会話画面はパネル全体を知らず、この細い契約だけを見ます。
//
// なぜ「候補の一覧」ではなく「絞り込み条件」を返すのか:
//   条件を検索タブへ入れると、タブの項目が実際にその値へ切り替わります。
//   AIが何で絞ったかが画面に残り、人が同じ操作を再現できます。
//   一覧を流し込むと、どうやってその一覧になったのかが消えます。
//
// 修正時の影響:
//   条件に含まれない項目は必ず未選択へ戻してください。
//   前の質問の条件が残ると、新しい条件と混ざって別の結果になります。

using System.Text;
using Bui.Llm;

namespace Bui.UI;

/// <summary>BUIパネルの状態を、会話画面が必要とする形で渡します。</summary>
public sealed class PaneChatContext : IChatContextSource
{
    private readonly BuiDockPaneViewModel _pane;
    private readonly IUiDispatcher _ui;

    public PaneChatContext(BuiDockPaneViewModel pane, IUiDispatcher ui)
    {
        _pane = pane ?? throw new ArgumentNullException(nameof(pane));
        _ui = ui ?? throw new ArgumentNullException(nameof(ui));
    }

    // 会話画面のtranslator/assistant（BuiChatComposition.Create）は起動時固定の設定で動きます。
    // ここを生きた_pane.IsLlmEnabledにすると、設定タブでチェックを変えた直後だけ
    // 「入力欄は使えるのに実際は起動時の設定のまま動く」というずれが生まれます。
    public bool IsLlmEnabled => _pane.ActiveIsLlmEnabled;

    public bool LlmSettingChangedSinceStartup => _pane.IsLlmEnabled != _pane.ActiveIsLlmEnabled;

    public string DataSourceDescription => _pane.DataSourceDescription;

    public string BuildSelectionContext()
    {
        if (_pane.Selection.SelectedCount == 0) return string.Empty;

        var builder = new StringBuilder();
        builder.Append("選択中: ").Append(_pane.Selection.CategoryName)
            .Append(" / ").Append(_pane.Selection.TypeName);

        foreach (var row in _pane.LayerRows)
        {
            builder.Append('\n').Append("  ").Append(row.SlotName)
                .Append(' ').Append(row.MaterialName)
                .Append(' ').Append(row.ThicknessMillimeters).Append("mm")
                .Append(" Product_ID=")
                .Append(row.CurrentProductId.Length == 0 ? "（未設定）" : row.CurrentProductId);
        }

        return builder.ToString();
    }

    public Task ApplyPlanToSearchTabAsync(CandidateQueryPlan plan, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(plan);

        // 列ごとに1つずつSelectedを書き換えると、その都度パネルのカスケード再取得が走り、
        // まだ計画を適用しきっていない途中の状態で他の列の値が消えることがあります
        // （検索タブへ送る計画とチャットが報告した計画が食い違う原因）。
        // ApplyCandidatePlanAsyncは検索語と全列の選択をまとめて書き込み、
        // カスケードを1回だけ、計画を適用し終えた後に走らせます。
        return _pane.ApplyCandidatePlanAsync(plan.Selections, plan.Text, cancellationToken);
    }
}
