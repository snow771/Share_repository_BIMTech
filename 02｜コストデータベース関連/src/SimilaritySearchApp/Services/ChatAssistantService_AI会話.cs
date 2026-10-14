using System.Text.Json.Nodes;
using SimilaritySearchApp.Llm;
using SimilaritySearchApp.Models;

namespace SimilaritySearchApp.Services;

/// <summary>AI アシスタントが選んだ対応。</summary>
public enum AssistantRoute
{
    /// <summary>会話で答える（挨拶・使い方・今の結果についての質問など）</summary>
    Chat,
    /// <summary>絞込条件を設定する（AI条件設定へ）</summary>
    Filter,
    /// <summary>判断サマリー</summary>
    Summary,
    /// <summary>確認観点の提案</summary>
    Review,
    /// <summary>選択物件の説明</summary>
    Project,
    /// <summary>データベースのテーブルを調べて答える（標準単価・協力業者・マスタの値など）</summary>
    Lookup,
}

public sealed record AssistantDecision(AssistantRoute Route, string Reply);

/// <summary>
/// AI アシスタントの会話（自然な会話の流れで、雑談・使い方の質問・今の結果への質問に答え、
/// 条件の絞込みや判断サマリーが必要なときは、その機能に振り分ける）。
///
/// LLM には「今の画面の状況（件数・条件・単価の中央値など）」と直近の会話を渡し、
///   { route: chat / filter / summary / review / project, reply: 返答 }
/// を JSON で返させる。数値は渡した状況に書かれたものだけを使わせる（作り話をさせない）。
/// </summary>
public static class ChatAssistantService
{
    /// <summary>覚えておく会話の数（発言の数。古いものから捨てる）</summary>
    public const int MaxHistory = 12;

    /// <summary>1発言として覚えておく最大文字数（長い集計結果などは先頭だけ）</summary>
    public const int MaxHistoryChars = 400;

    private static readonly string[] Routes = { "chat", "filter", "summary", "review", "project", "lookup" };

    public static JsonObject BuildSchema() => new()
    {
        ["type"] = "object",
        ["properties"] = new JsonObject
        {
            ["route"] = new JsonObject { ["type"] = "string", ["enum"] = new JsonArray(Routes.Select(r => (JsonNode)r).ToArray()) },
            ["reply"] = new JsonObject { ["type"] = "string" },
        },
        ["required"] = new JsonArray("route", "reply"),
        ["additionalProperties"] = false,
    };

    public static List<ChatMessage> BuildPrompt(string text, IReadOnlyList<ChatMessage> history, string context)
    {
        const string system = """
あなたは建設会社の積算・コスト管理担当者を手伝う「AIアシスタント」です。社内ツール「類似物件・実績検索」の右側のチャット欄で会話しています。
丁寧で親しみやすい日本語で、簡潔に（基本は3〜6文程度）答えてください。挨拶や雑談にも自然に応じてください。

このツールでできること:
- 検索・分析: 地域（都道府県・市区町村）・構造（RC造/S造）・形態・階数・面積・工種（工種項目/工事項目/項目）で過去物件を絞り込み、査定単価・修正単価（円/㎡）の分布や中央値、物価補正前後を比べる
- 【標準単価】タブ: 工種コードに対応する標準材料の単価を確認する
- 判断サマリー・確認観点・選択物件の説明（チャット欄上のボタンでも出せる）
- ホームから: DB編集（表の追加・更新・削除、変更履歴と取り消し）、Stencile変換、見積AI-OCR（見積PDFの読取り）

route（対応）の選び方:
- filter: 条件を絞り込みたい・変えたい・物件を探したい（例:「東京都のRC造7階で内装工事」「S造にして」「面積1000㎡以上だけ」「条件をクリア」）
- summary: 今の結果のまとめ・判断材料を求めている
- review: 確認すべき点・注意点を求めている
- project: 選択中の物件について説明を求めている
- lookup: 画面の状況に書かれていない情報を、データベースのテーブルを調べて答える必要がある質問（例:「タイルの標準単価は？」「内装工事の協力業者は？」「物件J02201の住所は？」「ボードの単位は？」）。調べられるテーブルは下の状況の「参照できるテーブル」
- chat: それ以外（挨拶、お礼、雑談、使い方の質問、今の状況や数値についての質問、単価の考え方の相談など）

reply（返答）の書き方:
- chat のときは、会話としての返答をそのまま書く
- filter / summary / review / project / lookup のときは、「条件を設定しますね。」「調べますね。」のような一言だけ
- 数値（件数・単価など）は、下の「いまの画面の状況」に書かれているものだけを使う。書かれていない数値は推測せず、「データ読込」や条件の絞込みなど、確かめ方を案内する
- 分からないことは分からないと言う
出力は指定された JSON（route と reply）だけにしてください。
""";

        var messages = new List<ChatMessage> { new("system", system + "\n# いまの画面の状況\n" + context) };
        messages.AddRange(history);
        messages.Add(new ChatMessage("user", text));
        return messages;
    }

    public static AssistantDecision Parse(JsonObject ai)
    {
        var route = ((string?)(ai["route"] as JsonValue)?.ToString() ?? "chat").Trim().ToLowerInvariant() switch
        {
            "filter" => AssistantRoute.Filter,
            "summary" => AssistantRoute.Summary,
            "review" => AssistantRoute.Review,
            "project" => AssistantRoute.Project,
            "lookup" => AssistantRoute.Lookup,
            _ => AssistantRoute.Chat,
        };
        var reply = (ai["reply"] as JsonValue)?.ToString().Trim() ?? "";
        return new AssistantDecision(route, reply);
    }

    /// <summary>LLM に渡す「いまの画面の状況」。</summary>
    public static string BuildContext(bool loaded, StructureType structure, int totalProjects, AnalysisResult? result, FilterCriteria criteria, ProjectRecord? selected,
        IReadOnlyList<string>? tableLabels = null)
    {
        var tables = tableLabels is { Count: > 0 }
            ? $"- 参照できるテーブル（lookup で調べられる）: {string.Join("、", tableLabels)}"
            : "- 参照できるテーブル: （DB に接続できないため不明）";
        if (!loaded)
            return "- 分析用のデータはまだ読み込まれていません（ヘッダーの「⚡ データ読込」で読み込むと、検索・分析ができます）。\n" + tables;

        var lines = new List<string>
        {
            $"- 構造種別: {structure}造 のデータを読込済み（物件 {totalProjects:N0} 件）",
        };
        if (result is not null)
        {
            lines.Add($"- 絞込後の物件数: {result.ProjectCount:N0} 件");
            lines.Add("- 判断サマリー（いまの条件）:");
            lines.AddRange(InsightTextService.Summary(result, criteria).Split('\n').Select(l => "  " + l));
        }
        lines.Add(selected is null
            ? "- 選択中の物件: なし"
            : $"- 選択中の物件: {selected.ProjectId}{(string.IsNullOrWhiteSpace(selected.Name) ? "" : $"（{selected.Name}）")}");
        lines.Add(tables);
        return string.Join("\n", lines);
    }

    /// <summary>会話を覚える（長い発言は先頭だけ、古い発言は捨てる）。</summary>
    public static void Remember(List<ChatMessage> history, string role, string text)
    {
        var t = text.Trim();
        if (t.Length == 0) return;
        if (t.Length > MaxHistoryChars) t = t[..MaxHistoryChars] + "…";
        history.Add(new ChatMessage(role, t));
        if (history.Count > MaxHistory) history.RemoveRange(0, history.Count - MaxHistory);
    }
}
