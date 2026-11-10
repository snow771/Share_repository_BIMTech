// このファイルの目的:
//   利用者の質問から、建材の言い方を短い単語（terms）として挙げさせます。LLMを使う1回目です。
//   項目名や条件そのものはLLMに決めさせません。語からマスタの項目・値を突き止めて
//   条件を組み立てるのはBUI側（ChatQueryBuilder）です。
//
// なぜ絶対に例外を投げないのか:
//   小さいローカルモデルはJSONを安定して吐きません。
//   壊れた応答で行き止まりになると、機能そのものが使えなくなります。
//   読めなかったときは intent を search、terms を空として返し、先へ進ませます。
//   質問文全体をキーワード検索へ落とす経路はもう採っていません。
//   質問文全体は製品のどの項目の値でもないため、単純な部分一致では
//   まず何にも一致せず、0件の検索を保証するだけでした（旧設計の不具合）。
//   terms が空でも、質問文からの部分一致・表記ゆれ表・LLMの語の3層で
//   BUI（ChatQueryBuilder）が条件を組み立てるため、この経路を消しても検索は動きます。
//
//   intentを読めなかったときも同じ方針です。必ず search として扱い、
//   現状どおり検索を続けます。chatへ倒すと、製品を探している質問が
//   検索されないまま雑談で返ってしまいます。
//
// 修正時の影響:
//   LLMへ渡すのは質問文（と直近のやり取り・選択中の情報）だけです。
//   項目名・選べる値の一覧や製品データそのものは渡しません。
//   渡すとマスタ全体を送ることになり、設計の前提が崩れます。

using System.Text.Json;
using Bui.Data;

namespace Bui.Llm;

/// <summary>
/// 質問に出てくる建材の言い方を、短い語（terms）として挙げさせます。項目名や条件そのものは
/// 決めさせません。読めなくても空のtermsで返し、質問文全体をキーワード検索へ落とすことは
/// しません（質問文はどの項目の値でもなく、部分一致では一致せず0件を保証するだけのため）。
/// </summary>
public sealed class LlmQueryTranslator
{
    /// <summary>
    /// 語の列挙用の指示です。項目名や値の一覧は渡さず、短い単語を挙げさせるだけにします。
    /// 項目名をLLMに決めさせません。語からマスタの項目と値を突き止めるのはBUI側です
    /// （<see cref="Bui.Llm.ChatQueryBuilder"/>）。
    /// </summary>
    private const string SystemInstruction =
        "あなたは、利用者の発言に出てくる建材の言い方を、短い単語として抜き出す補助者です。"
        + "回答はJSONだけを出力してください。形式は "
        + "{\"intent\":\"search\",\"terms\":[\"単語\",\"単語\"]} です。"
        + "製品を探す・比べる・選ぶ話なら intent は \"search\" にし、"
        + "建材の分類・メーカー・製品名・性能・寸法にあたる言葉を terms に入れてください。"
        + "略語や別の言い方は、正式な言い方に直して入れてください（例: PB → せっこうボード）。"
        + "挨拶・雑談・お礼・進め方の相談など、製品を探す話でなければ "
        + "intent を \"chat\" にし、terms を空にしてください。"
        + "助詞や『教えて』のような言い回しは入れないでください。"
        + "説明文やコードブロックの記号は書かないでください。";

    private readonly ILlmProvider? _provider;
    private readonly bool _isEnabled;
    private readonly string _model;
    private readonly TimeSpan _timeout;

    public LlmQueryTranslator(ILlmProvider? provider, bool isEnabled, string model, TimeSpan timeout)
    {
        _provider = provider;
        _isEnabled = isEnabled;
        _model = model ?? string.Empty;
        _timeout = timeout;
    }

    /// <summary>
    /// 質問に出てくる建材の言い方を、短い語として挙げさせます。
    /// 項目名は決めさせません。語からマスタの項目と値を突き止めるのはBUI側です。
    /// 読めなくても空で返します。第1層・第2層で検索は動くため、異常ではありません。
    /// </summary>
    public async Task<(ChatIntent Intent, IReadOnlyList<string> Terms)> TranslateTermsAsync(
        string question,
        string selectionContext,
        CancellationToken cancellationToken,
        IReadOnlyList<ChatHistoryTurn>? history = null)
    {
        var text = (question ?? string.Empty).Trim();
        if (!_isEnabled || _provider is null)
        {
            return (ChatIntent.Search, Array.Empty<string>());
        }

        var historyBlock = ChatHistory.Build(history);
        var prompt = string.IsNullOrWhiteSpace(selectionContext)
            ? historyBlock + "質問: " + text
            : historyBlock + selectionContext + "\n質問: " + text;

        // systemとuserの合計を上限に収めます。userだけを削ると、system（指示文）の分だけ
        // 合計が上限を超えます（旧BuildPromptはこの分を先に引いていました）。
        var budget = Math.Max(0, LlmProviderSupport.MaximumPromptLength - SystemInstruction.Length);
        if (prompt.Length > budget)
        {
            prompt = prompt.Substring(0, budget);
        }

        var request = new LlmChatRequest(
            _model,
            new[]
            {
                new LlmMessage("system", SystemInstruction),
                new LlmMessage("user", prompt)
            },
            _timeout);

        try
        {
            var result = await _provider.ChatAsync(request, cancellationToken).ConfigureAwait(false);
            if (!result.IsSuccess && result.Error!.Code == LlmProviderSupport.CancelledErrorCode)
            {
                throw new OperationCanceledException(cancellationToken);
            }

            if (!result.IsSuccess || string.IsNullOrWhiteSpace(result.Value?.Content))
            {
                return (ChatIntent.Search, Array.Empty<string>());
            }

            return ParseTerms(result.Value.Content);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            // 接続先やパスが混じり得るため、例外の中身は捨てます。
            return (ChatIntent.Search, Array.Empty<string>());
        }
    }

    /// <summary>応答から intent と terms を取り出します。読めなければ search と空を返します。</summary>
    private static (ChatIntent Intent, IReadOnlyList<string> Terms) ParseTerms(string content)
    {
        var start = content.IndexOf('{');
        var end = content.LastIndexOf('}');
        if (start < 0 || end <= start) return (ChatIntent.Search, Array.Empty<string>());

        try
        {
            using var document = JsonDocument.Parse(content[start..(end + 1)]);
            var root = document.RootElement;

            var intent = root.TryGetProperty("intent", out var intentElement)
                && intentElement.ValueKind == JsonValueKind.String
                && string.Equals(intentElement.GetString(), "chat", StringComparison.OrdinalIgnoreCase)
                ? ChatIntent.Chat
                : ChatIntent.Search;

            var terms = new List<string>();
            if (root.TryGetProperty("terms", out var termsElement)
                && termsElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in termsElement.EnumerateArray())
                {
                    if (item.ValueKind != JsonValueKind.String) continue;
                    var term = (item.GetString() ?? string.Empty).Trim();
                    if (term.Length > 0) terms.Add(term);
                }
            }

            return (intent, terms);
        }
        catch (JsonException)
        {
            return (ChatIntent.Search, Array.Empty<string>());
        }
    }
}
