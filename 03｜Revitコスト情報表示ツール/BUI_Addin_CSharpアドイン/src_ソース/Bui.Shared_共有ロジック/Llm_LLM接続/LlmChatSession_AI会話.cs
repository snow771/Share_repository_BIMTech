// このファイルの目的:
//   AIとの1往復をまとめます。
//   ①質問から語を挙げさせる → BUIが語・質問文・表記ゆれ表から条件を組み立てて決定的に引く
//   → ②候補を説明 → 回答中のIDを照合。
//   ①がJSONで直接 selections/text（項目名や条件そのもの）を返す設計はもう採っていません。
//   条件の組み立て（ChatQueryBuilder）はBUI側の責務です。
//
// なぜこの順番を固定するのか:
//   検索をBUIが行うことが、AIの答えと【検索】タブの結果が一致する根拠です。
//   LLMに直接データを引かせる形へ滑ると、その根拠が消えます。
//
//   intentがchatのときだけ、本番の絞り込みを飛ばします。
//   飛ばす判断は①の出力にのみ基づき、BUI側で言葉を判定はしません。
//   日本語の言い方は無限にあり、混在した発言を裁けないためです。
//   ChatIntent（search/chat）の定義はここではなく
//   Llm_LLM接続/CandidateQueryPlan_絞り込み計画.cs にあります。
//
// 修正時の影響:
//   絞り込みは必ずICandidateSourceを通してください。
//   ここを迂回すると、AIだけが知っている検索結果が生まれます。

using Bui.Data;

namespace Bui.Llm;

/// <summary>
/// チャットが「データ元がいま読めているか」だけを覗くための細い窓口です。
/// LlmChatSessionにIProductRepository丸ごとを渡すと、検索以外の操作（登録・更新など）まで
/// 見えてしまい、「LLMは検索しかしない」という設計の前提が崩れます。
/// GetStatusAsyncだけを見せることで、チャットは状態を読むだけに制限されます。
/// </summary>
public interface IChatDataSourceStatus
{
    Task<DataSourceStatus> GetStatusAsync(CancellationToken cancellationToken);
}

/// <summary>1往復の結果です。Answerは画面へそのまま出します。</summary>
/// <param name="MatchCount">
///   条件に合った総数（打ち切り前）。CandidateView.TotalCountそのままです。
/// </param>
/// <param name="ShownCount">
///   AIに実際に渡した候補の件数。CandidateView.Candidates.Countそのままです。
///   MatchCountより小さければ、AIは全件を見ずに答えています。
///   この2つを1つに丸めると、「N件中M件」という打ち切りの事実を画面へ出せなくなるため、
///   あえて分けています。
/// </param>
public sealed record ChatTurnResult(string Answer, CandidateQueryPlan Plan, int MatchCount, int ShownCount, string? Notice);

/// <summary>質問1つを、条件への翻訳・絞り込み・説明・照合の順に処理します。</summary>
public sealed class LlmChatSession
{
    private readonly ICandidateSource _candidates;
    private readonly LlmQueryTranslator _translator;
    private readonly LlmSearchAssistant _assistant;
    private readonly IChatDataSourceStatus? _dataSourceStatus;
    private readonly Func<SynonymLoadResult>? _synonymsProvider;

    public LlmChatSession(
        ICandidateSource candidates,
        LlmQueryTranslator translator,
        LlmSearchAssistant assistant,
        IChatDataSourceStatus? dataSourceStatus = null,
        Func<SynonymLoadResult>? synonymsProvider = null)
    {
        _candidates = candidates ?? throw new ArgumentNullException(nameof(candidates));
        _translator = translator ?? throw new ArgumentNullException(nameof(translator));
        _assistant = assistant ?? throw new ArgumentNullException(nameof(assistant));
        _dataSourceStatus = dataSourceStatus;
        _synonymsProvider = synonymsProvider;
    }

    /// <summary>
    /// 表記ゆれ表を毎ターン読み直します。起動時に1回だけ読んで固定すると、
    /// 利用者が表記ゆれ.csvへ行を足しても「次の質問から効きます」という案内どおりに
    /// ならず、Revitの再起動が要る嘘の案内になります（レビュー指摘）。
    /// 読み直しに失敗しても会話は止めません。delegateが例外を投げてもnullを返しても、
    /// CsvSynonymLoader.Loadが読めないときと同じくSynonymLoadResult.Emptyへ倒し、通常どおり答えます。
    /// Tableだけでなく、その読み込みで出たProblemsもここで確定させます。ProblemsをBuildNoticeへ
    /// 渡さないと、起動時1回きりしか読まなかった旧実装と同じく、CSVの見出し誤りなどが
    /// 途中で起きても画面には二度と出せません（レビュー指摘）。
    /// </summary>
    private SynonymLoadResult ReadSynonyms()
    {
        if (_synonymsProvider is null) return SynonymLoadResult.Empty;
        try
        {
            return _synonymsProvider() ?? SynonymLoadResult.Empty;
        }
        catch (Exception)
        {
            return SynonymLoadResult.Empty;
        }
    }

    public async Task<ChatTurnResult> AskAsync(
        string question,
        string selectionContext,
        CancellationToken cancellationToken,
        IReadOnlyList<ChatHistoryTurn>? history = null)
    {
        // 1回目は「いま選べる値」を知るためだけの問い合わせです。
        // これが無いと、LLMの書いた値を実データへ寄せられず、intentも判定できません
        // （intentは①の出力であり、①はこの一覧が無いと質問を条件へ翻訳できません）。
        var available = await _candidates
            .QueryAsync(new CandidateQuery(new Dictionary<string, string>(), string.Empty, 1), cancellationToken)
            .ConfigureAwait(false);

        // LLMは語を挙げるだけです。条件の組み立てはBUIが行います。
        // モデルが何も返せなくても、質問文と表記ゆれ表から条件が作れます。
        var llm = await _translator
            .TranslateTermsAsync(question, selectionContext, cancellationToken, history)
            .ConfigureAwait(false);
        var synonymRead = ReadSynonyms();
        // 小さいローカルモデルは「おはよう」のような挨拶にもintent:"search"を返すことがあります。
        // ①の出力より先に、短い定型句だけの発言かをBUI側で決定的に判定し、
        // そのときだけchatへ倒します（GreetingDetector参照）。
        var intent = GreetingDetector.IsGreetingOrSmallTalk(question) ? ChatIntent.Chat : llm.Intent;
        var plan = ChatQueryBuilder.Build(
            question, available.Filters, synonymRead.Table, llm.Terms, intent);

        // 会話では絞り込みを行いません。ここで本番のQueryAsyncを呼ぶと、
        // 「おはよう」が必ず0件の検索になり、会話が成立しません。
        if (plan.Intent == ChatIntent.Chat)
        {
            var conversation = await _assistant
                .AnswerConversationallyAsync(question, selectionContext, history, cancellationToken)
                .ConfigureAwait(false);

            // 絞り込んでいないため、知らせることも「検索タブへ送る」条件もありません。
            return new ChatTurnResult(conversation.Summary, plan, 0, 0, null);
        }

        // 条件も語も無いなら、探しようがありません。ここで確かめないと、
        // plan.ToQuery()はフィルタ無しのクエリになり、カタログ全件がCandidates>0で返ってきて
        // この案内が発火しないまま先へ進んでしまいます（実データでは常にCount>0のため）。
        // 「条件を1つ減らしてください」は実行できない案内になります。
        // ただし「データ元そのものが読めていない」ときは、それとは別の理由・別の対処なので、
        // 本番のQueryAsyncを呼ばずに済むこの分岐でも先に確かめます。
        if (plan.Selections.Count == 0 && plan.Text.Length == 0)
        {
            var emptyNotice = BuildNotice(plan, 0, 0, synonymRead.Problems);
            var connectionGuidance = await TryBuildConnectionGuidanceAsync(plan, emptyNotice, cancellationToken)
                .ConfigureAwait(false);
            if (connectionGuidance is not null) return connectionGuidance;

            return new ChatTurnResult(
                "質問から絞り込みに使える言葉が見つかりませんでした。\n"
                + "例えば次のように聞いてください。\n"
                + BuildExample(available.Filters)
                + "\n略語や別の言い方は、表記ゆれ.csv に追加すると使えるようになります。",
                plan, 0, 0, emptyNotice);
        }

        var view = await _candidates.QueryAsync(plan.ToQuery(), cancellationToken).ConfigureAwait(false);
        var notice = BuildNotice(plan, view.TotalCount, view.Candidates.Count, synonymRead.Problems);

        if (view.Candidates.Count == 0)
        {
            // 「条件に合う製品が無かった」と「データ元そのものが読めていない」は原因も対処も別です。
            // SqlServerTableSource等は接続失敗でも例外を投げず空の候補集合を返すため、ここで確かめないと
            // 「データ元が落ちている」のに「条件を1つ減らしてください」という実行不可能な案内を出します。
            var connectionGuidance = await TryBuildConnectionGuidanceAsync(plan, notice, cancellationToken)
                .ConfigureAwait(false);
            if (connectionGuidance is not null) return connectionGuidance;

            return new ChatTurnResult(
                $"条件に合う製品がありませんでした。（絞り込み: {plan.Describe()}）"
                + "\n条件を1つ減らすか、別の言い方で聞いてみてください。",
                plan, 0, 0, notice);
        }

        var answer = await _assistant
            .AnswerWithCandidatesAsync(question, selectionContext, view.Candidates, cancellationToken, history)
            .ConfigureAwait(false);

        var marked = ProductIdMentionChecker.MarkUnknownIds(
            answer.Summary, view.Candidates.Select(record => record.ProductId).ToArray());

        // 説明が使えなかった理由も、知らせに混ぜて必ず画面へ出します。
        var combined = answer.FailureUserMessage is null
            ? notice
            : string.Join(" / ", new[] { notice, answer.FailureUserMessage }.Where(x => !string.IsNullOrEmpty(x)));

        return new ChatTurnResult(
            marked, plan, view.TotalCount, view.Candidates.Count, string.IsNullOrEmpty(combined) ? null : combined);
    }

    /// <summary>
    /// データ元そのものが読めていないときの案内を組み立てます。読めているとき（またはこの
    /// 窓口が渡されていないとき）はnullを返し、呼び出し側は通常の0件案内へ進めます。
    /// 「条件に合う製品が無かった」場合と「条件が1つも作れなかった」場合の両方から
    /// 呼ばれます。原因も対処も別だからです。
    /// </summary>
    private async Task<ChatTurnResult?> TryBuildConnectionGuidanceAsync(
        CandidateQueryPlan plan, string? notice, CancellationToken cancellationToken)
    {
        var status = _dataSourceStatus is null
            ? null
            : await _dataSourceStatus.GetStatusAsync(cancellationToken).ConfigureAwait(false);
        if (status is null || status.IsAvailable) return null;

        var reason = status.Problems.Count > 0 ? string.Join(" / ", status.Problems) : "原因不明の接続エラーです。";
        return new ChatTurnResult(
            $"データ元に接続できていないため検索できませんでした。{reason}"
            + "\n【設定】タブでデータ元（CSVフォルダ／SQL Server）の設定を確認し、接続し直してください。",
            plan, 0, 0, notice);
    }

    private static string? BuildNotice(
        CandidateQueryPlan plan, int totalCount, int shownCount, IReadOnlyList<string> synonymReadProblems)
    {
        var parts = new List<string>();

        // 表記ゆれ表の行がマスタと合わないことは、利用者が直せる問題です。
        // 黙って効かない行を残すと、直す機会そのものが無くなります。
        if (plan.Problems.Count > 0)
        {
            parts.Add(string.Join(" / ", plan.Problems));
        }

        // 表記ゆれ.csvは1往復ごとに読み直すため、読み込み自体の問題（見出し誤り・読み取り
        // 失敗など）も毎ターン起こり得ます。ここで混ぜないと、起動時にしか出なかった旧実装
        // と同じく、途中で壊れても画面には二度と理由が出ません（レビュー指摘）。
        if (synonymReadProblems.Count > 0)
        {
            parts.Add(string.Join(" / ", synonymReadProblems));
        }

        // AIには一部しか見せていないため、その事実を必ず画面へ出します。
        // 見せた件数より合計が多いときだけ知らせ、通常の回答では出しません。
        if (totalCount > shownCount)
        {
            parts.Add($"{totalCount}件中{shownCount}件をAIに渡しています。絞り込んでください。");
        }

        return parts.Count == 0 ? null : string.Join(" ", parts);
    }

    /// <summary>
    /// 実データから案内の例を作ります。固定の例文を書くと、
    /// データが変わったときに存在しない値を案内してしまいます。
    /// </summary>
    private static string BuildExample(IReadOnlyList<CandidateFilterOptions> filters)
    {
        var parts = filters
            .Where(filter => filter.Values.Count > 0)
            .Take(3)
            .Select(filter => $"{filter.Column}: {filter.Values[0]}")
            .ToArray();
        return parts.Length == 0 ? "　（選べる項目がありません）" : "　" + string.Join(" / ", parts);
    }
}
