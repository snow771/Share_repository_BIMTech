// このファイルの目的:
//   AIチャットウィンドウの状態を持ちます。WPFには依存しません。
//
// なぜパネルのViewModelと分けるのか:
//   別ウィンドウであり、寿命も表示も違うためです。
//   パネル側の情報はIChatContextSource経由で細く受け取り、
//   会話画面がパネル全体を知らずに済むようにします。
//
// 修正時の影響:
//   送信中の二重送信を止めているのはIsBusyだけです。
//   ここを外すと、同じ質問が2回LLMへ飛びます。
//   回答の件数表示は「一致件数（MatchCount）」と「AIに見せた件数（ShownCount）」を
//   混同しないでください。2つが違うのに1つに丸めると、
//   利用者は回答が何件を見た上でのものか分からなくなります。
//   会話（intent=chat）の発言には絞り込みの要約とボタンを付けないでください。
//   絞り込んでいないのに要約を出すと、挨拶が失敗したように見えます。

using System.Collections.ObjectModel;
using Bui.Common;
using Bui.Llm;

namespace Bui.UI;

/// <summary>会話画面がパネル側から受け取る情報と操作です。</summary>
public interface IChatContextSource
{
    /// <summary>
    /// 起動時に固定したLLM有効フラグです。検索タブの要約と同じ「起動時固定」の値であり、
    /// 設定タブのチェックボックス（生きた値）ではありません。会話画面のtranslator/assistantも
    /// 同じ起動時固定の値で動くため、ここを生きた値にすると、チェックを変えた直後だけ
    /// 「入力欄は使えるのに実際は起動時の設定のまま動く」というずれが生まれます。
    /// </summary>
    bool IsLlmEnabled { get; }

    /// <summary>
    /// 設定タブのLLM有効フラグ（生きた値）が起動時の値と異なるかどうかです。
    /// 真なら「設定を変更した場合、反映にはRevitの再起動が必要です。」を画面へ出します。
    /// </summary>
    bool LlmSettingChangedSinceStartup { get; }

    /// <summary>いま読んでいるデータ元の説明です。</summary>
    string DataSourceDescription { get; }

    /// <summary>選択中の1要素をLLMへ渡す形の文章にします。選択が無ければ空文字。</summary>
    string BuildSelectionContext();

    /// <summary>使った絞り込み条件を検索タブへ反映します。</summary>
    Task ApplyPlanToSearchTabAsync(CandidateQueryPlan plan, CancellationToken cancellationToken);
}

/// <summary>会話1件分の表示です。</summary>
public sealed record ChatMessageViewModel(
    string Speaker, string Text, string FilterSummary, bool CanSendToSearchTab, CandidateQueryPlan? Plan)
{
    /// <summary>話し手の表示名です。この1か所だけで決めます。</summary>
    public const string UserSpeaker = "あなた";

    /// <summary>
    /// 利用者の発言かどうかです。画面の左右と背景を切り替えるために使います。
    /// XAMLで話し手の文字列を比較する書き方にしないでください。
    /// Bui.Addinはこちらでコンパイルできず、間違いに気づけません。
    /// </summary>
    public bool IsFromUser => string.Equals(Speaker, UserSpeaker, StringComparison.Ordinal);
}

/// <summary>AIチャットウィンドウの状態です。</summary>
public sealed class BuiChatViewModel : ObservableObject
{
    private readonly LlmChatSession _session;
    private readonly IChatContextSource _context;
    private readonly IUiDispatcher _ui;
    private readonly IAsyncCommandErrorHandler? _commandErrorHandler;
    private string _input = string.Empty;
    private string _statusMessage = string.Empty;
    private string _selectionContextText = string.Empty;
    private bool _isBusy;

    public BuiChatViewModel(
        LlmChatSession session,
        IChatContextSource context,
        IUiDispatcher ui,
        IAsyncCommandErrorHandler? commandErrorHandler = null)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _ui = ui ?? throw new ArgumentNullException(nameof(ui));
        _commandErrorHandler = commandErrorHandler;
        SendCommand = new AsyncCommand(SendAsync, commandErrorHandler);
        ResetCommand = new AsyncCommand(ResetAsync, commandErrorHandler);
        RefreshContext();
    }

    public ObservableCollection<ChatMessageViewModel> Messages { get; } = new();

    public string Input { get => _input; set => SetProperty(ref _input, value); }

    public string StatusMessage { get => _statusMessage; private set => SetProperty(ref _statusMessage, value); }

    /// <summary>データ元と選択中の情報です。どのデータで答えているかを常に画面へ出します。</summary>
    public string DataSourceDescription => _context.DataSourceDescription;

    public string SelectionContextText
    {
        get => _selectionContextText;
        private set => SetProperty(ref _selectionContextText, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (!SetProperty(ref _isBusy, value)) return;
            RaisePropertyChanged(nameof(CanChat));
        }
    }

    /// <summary>入力欄と送信ボタンを使える状態かどうかです。</summary>
    public bool CanChat => _context.IsLlmEnabled && !IsBusy;

    /// <summary>
    /// LLMが無効かどうかです。画面の表示切替に使います。
    /// 文言の長さで切り替えるとint型がBooleanToVisibilityへ渡り、実行時に落ちます。
    /// </summary>
    public bool IsLlmDisabled => !_context.IsLlmEnabled;

    /// <summary>LLMが無効なときに入力欄の代わりに出す案内です。有効なら空文字。</summary>
    public string LlmDisabledNotice => _context.IsLlmEnabled
        ? string.Empty
        : "LLMが無効になっています。【設定】タブで「LLMを使用する」を有効にし、"
            + "接続URLとモデルを設定してから「接続確認・モデル一覧取得」を押してください。";

    /// <summary>
    /// 設定タブでLLMの有効・無効を切り替えたが、まだRevitを再起動していないときの案内です。
    /// この会話画面はIsLlmEnabled同様、起動時固定の値で動くため、チェックを変えただけでは
    /// 反映されません。そのことを伝えないと、チェックを入れたのに何も変わらないように見えます。
    /// 変更が無ければ空文字。
    /// </summary>
    public string LlmSettingsChangedNotice => _context.LlmSettingChangedSinceStartup
        ? "設定を変更した場合、反映にはRevitの再起動が必要です。"
        : string.Empty;

    /// <summary>
    /// XAMLの表示切替はint型（文字列の長さ）ではなくbool一本で行います
    /// （BooleanToVisibilityConverterへ文字列を渡すと実行時に落ちるため）。
    /// </summary>
    public bool HasLlmSettingsChangedNotice => _context.LlmSettingChangedSinceStartup;

    public AsyncCommand SendCommand { get; }
    public AsyncCommand ResetCommand { get; }

    /// <summary>ウィンドウを開き直したときに呼びます。古い部材について答え続けないためです。</summary>
    public void RefreshContext()
    {
        SelectionContextText = _context.BuildSelectionContext();
        RaisePropertyChanged(nameof(DataSourceDescription));
        RaisePropertyChanged(nameof(CanChat));
        RaisePropertyChanged(nameof(IsLlmDisabled));
        RaisePropertyChanged(nameof(LlmDisabledNotice));
        RaisePropertyChanged(nameof(LlmSettingsChangedNotice));
        RaisePropertyChanged(nameof(HasLlmSettingsChangedNotice));
    }

    public async Task SendAsync(CancellationToken cancellationToken = default)
    {
        if (!_context.IsLlmEnabled)
        {
            await SetStatusAsync(LlmDisabledNotice, cancellationToken).ConfigureAwait(false);
            return;
        }

        var question = (Input ?? string.Empty).Trim();
        if (question.Length == 0)
        {
            await SetStatusAsync("質問を入力してから送信してください。", cancellationToken).ConfigureAwait(false);
            return;
        }

        if (IsBusy)
        {
            await SetStatusAsync("前の質問に応答中です。しばらく待ってください。", cancellationToken).ConfigureAwait(false);
            return;
        }

        // 選択中の情報は毎回読み直します。会話の途中で選択が変わることが普通にあるためです。
        var selectionContext = _context.BuildSelectionContext();

        IReadOnlyList<ChatHistoryTurn> history = Array.Empty<ChatHistoryTurn>();
        await _ui.InvokeAsync(() =>
        {
            SelectionContextText = selectionContext;
            IsBusy = true;
            Input = string.Empty;
            // 履歴は「いま聞いている質問」をMessagesへ追加する前に、必ずここ（UIスレッド上）で取ります。
            // 追加後に取ると、同じ質問が履歴と質問の両方に入り、モデルには同じことを2回言われたように
            // 見えます。また、MessagesはUIが直接束縛するObservableCollectionのため、UIスレッド以外から
            // 列挙すると、追加中のコレクションを覗く競合状態になり得ます。InvokeAsyncの外へ出さないでください。
            history = BuildHistory();
            Messages.Add(new ChatMessageViewModel(ChatMessageViewModel.UserSpeaker, question, string.Empty, false, null));
            StatusMessage = "応答を待っています。";
        }, cancellationToken).ConfigureAwait(false);

        try
        {
            var turn = await _session
                .AskAsync(question, selectionContext, cancellationToken, history)
                .ConfigureAwait(false);
            await _ui.InvokeAsync(() =>
            {
                // 会話では絞り込んでいないため、要約も「検索タブへ送る」も出しません。
                // 出すと、挨拶したはずの利用者に検索が失敗したように見えます。
                var isSearch = turn.Plan.Intent == ChatIntent.Search;
                Messages.Add(new ChatMessageViewModel(
                    "AI",
                    turn.Answer,
                    isSearch ? BuildFilterSummary(turn) : string.Empty,
                    isSearch,
                    isSearch ? turn.Plan : null));
                StatusMessage = turn.Notice ?? string.Empty;
            }, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            await SetStatusAsync("中断しました。", CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception)
        {
            // ここを拾わないとStatusMessageが「応答を待っています。」のまま固まり、
            // 会話にも何も残らない行き止まりになります。例外の中身（接続先やスタック）は
            // 画面へ出さず、AIの発言として次に何をすればよいかだけを伝えます。
            // 例外そのものはSendCommandのIAsyncCommandErrorHandlerへ再スローしてログに残します
            // （ここで握りつぶすと、原因調査に必要な記録が消えます）。
            await _ui.InvokeAsync(() =>
            {
                Messages.Add(new ChatMessageViewModel(
                    "AI", "質問を処理できませんでした。設定タブでLLMの接続設定とデータ元を確認してください。",
                    string.Empty, false, null));
                StatusMessage = "質問を処理できませんでした。";
            }, CancellationToken.None).ConfigureAwait(false);
            throw;
        }
        finally
        {
            await _ui.InvokeAsync(() => IsBusy = false, CancellationToken.None).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// 回答へ添える絞り込みの要約です。
    /// 一致件数（MatchCount）とAIに見せた件数（ShownCount）が同じならその件数だけを出し、
    /// 打ち切りで違うときだけ両方を出します。1つに丸めると、
    /// 利用者は回答が何件を見た上でのものか分からなくなるためです。
    /// </summary>
    private static string BuildFilterSummary(ChatTurnResult turn)
    {
        var countText = turn.MatchCount == turn.ShownCount
            ? $"{turn.MatchCount}件"
            : $"{turn.MatchCount}件（うちAIが見たのは{turn.ShownCount}件）";
        return $"絞り込み: {turn.Plan.Describe()}  → {countText}";
    }

    /// <summary>
    /// LLMへ渡す直近のやり取りです。画面の会話欄をそのまま使い、別に保存はしません。
    /// 長さの上限はChatHistoryが持ちます。ここで件数を絞ると、
    /// 上限の判断が2か所に分かれて片方だけ直したときに前提が崩れます。
    /// </summary>
    private IReadOnlyList<ChatHistoryTurn> BuildHistory() =>
        Messages.Select(message => new ChatHistoryTurn(message.Speaker, message.Text)).ToArray();

    /// <summary>使った絞り込み条件を検索タブへ反映します。候補の一覧は送りません。</summary>
    public async Task SendPlanToSearchTabAsync(
        ChatMessageViewModel message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        if (message.Plan is null)
        {
            await SetStatusAsync("この発言には絞り込み条件がありません。", cancellationToken).ConfigureAwait(false);
            return;
        }

        await _context.ApplyPlanToSearchTabAsync(message.Plan, cancellationToken).ConfigureAwait(false);
        await SetStatusAsync(
            $"検索タブへ送りました。（{message.Plan.Describe()}）", cancellationToken).ConfigureAwait(false);
    }

    public async Task ResetAsync(CancellationToken cancellationToken = default)
    {
        await _ui.InvokeAsync(() =>
        {
            Messages.Clear();
            StatusMessage = "会話をリセットしました。";
            SelectionContextText = _context.BuildSelectionContext();
        }, cancellationToken).ConfigureAwait(false);
    }

    private Task SetStatusAsync(string message, CancellationToken cancellationToken) =>
        _ui.InvokeAsync(() => StatusMessage = message, cancellationToken);

    /// <summary>
    /// AsyncCommandを介さずWPFのイベントハンドラから直接呼ばれる処理（「検索タブへ送る」ボタン）用の
    /// 例外受け皿です。他のすべての操作はAsyncCommandがIAsyncCommandErrorHandlerへ渡しますが、
    /// この操作だけはasync voidのイベントハンドラから呼ばれるため、同じ経路を手動でつなぎます。
    /// 利用者の取消（OperationCanceledException）はここでは扱いません。
    /// </summary>
    public void ReportUnhandledException(Exception exception)
    {
        if (exception is OperationCanceledException) return;
        try
        {
            _commandErrorHandler?.Handle(exception);
        }
        catch
        {
            // ログ出力自体の失敗でRevitのUIスレッドを落とさないため、ここで止めます。
        }
    }
}
