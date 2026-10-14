using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using SimilaritySearchApp.Llm;
using SimilaritySearchApp.Models;

namespace SimilaritySearchApp.Services;

/// <summary>AI に渡す・AI の出力を寄せるための、現在の選択肢一覧。</summary>
public sealed record FilterOptionsSnapshot(
    List<string> Prefectures, List<string> Cities, List<string> Towns,
    List<string> Structures, List<string> Forms, List<string> FloorsAbove, List<string> FloorsBelow,
    List<string> Trades, List<string> Works, List<string> Items,
    double AreaMin, double AreaMax);

/// <summary>AI 条件設定の結果。</summary>
public sealed record AiFilterOutcome(FilterCriteria Criteria, string Reason);

/// <summary>
/// 自然文 → 絞込条件（Python の build_filter_schema / build_filter_prompt / _app_apply_ai_filters）。
/// AI には「意図のヒント」だけを出させ、実際の選択肢への当てはめは C# 側で安全に行う。
/// </summary>
public static class AiFilterService
{
    private const string U = FilterCriteria.Unspecified;

    // 指示文はトークン数を節約するため改行・字下げなしの JSON にする
    private static readonly JsonSerializerOptions PromptJson = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,   // 日本語をそのまま出す（ensure_ascii=False 相当）
    };

    private static readonly Regex FloorAboveInText = new(@"(?<!地下\s*)(\d+)\s*階", RegexOptions.Compiled);
    private static readonly Regex FloorBelowInText = new(@"地下\s*(\d+)\s*階", RegexOptions.Compiled);
    private static readonly Regex DigitsOnly = new(@"^\d+$", RegexOptions.Compiled);

    public static JsonObject BuildSchema()
    {
        static JsonObject Str() => new() { ["type"] = "string" };
        static JsonObject Enum(params string[] values) =>
            new() { ["type"] = "string", ["enum"] = new JsonArray(values.Select(v => (JsonNode)v).ToArray()) };

        var props = new JsonObject
        {
            ["pref_hint"] = Str(),
            ["city_hint"] = Str(),
            ["town_hint"] = Str(),
            ["structure_hint"] = Str(),
            ["form_hint"] = Str(),
            ["scale_hint"] = Enum(U, "小規模", "中規模", "大規模"),
            ["floors_up_hint"] = Str(),
            ["floors_down_hint"] = Str(),
            ["basis_mode"] = Enum("法廷床面積", "施工床面積"),
            ["cost_mode"] = Enum("純工事費", "工事原価"),
            ["trade_hint"] = Str(),
            ["work_hint"] = Str(),
            ["item_hint"] = Str(),
            ["reason"] = Str(),
        };
        return new JsonObject
        {
            ["type"] = "object",
            ["properties"] = props,
            ["required"] = new JsonArray(props.Select(kv => (JsonNode)kv.Key).ToArray()),
            ["additionalProperties"] = false,
        };
    }

    /// <summary>候補一覧に載せる最大件数（多すぎると LLM のコンテキスト長を超えるため）。</summary>
    private const int MaxOptions = 40;

    /// <summary>
    /// AI への指示文。候補一覧は参考用（実際の当てはめは <see cref="ApplyHints"/> で全候補から行う）なので、
    /// 件数の多い一覧は先頭だけに絞る。compact=true では市区町村・町名・工事項目・項目の一覧と工種辞書を省いた短い指示文にする
    /// （コンテキスト長が足りないときのやり直し用）。
    /// </summary>
    public static List<ChatMessage> BuildPrompt(string request, FilterCriteria current, FilterOptionsSnapshot opts, string? highlightedProjectId,
        bool compact = false)
    {
        const string system =
            "あなたは建設コスト分析GUIのフィルタ意図抽出AIです。" +
            "あなたの役割は、GUI候補値を直接決めることではなく、ユーザーの意図をヒントとして整理することです。" +
            "明示されていない地域・形態・階数を勝手に補わないでください。" +
            "不明なら '(未指定)' を返してください。" +
            "出力は必ず指定されたJSONスキーマに厳密準拠してください。説明文・コードフェンスは禁止です。";

        var currentState = new Dictionary<string, object?>
        {
            ["pref"] = current.Prefecture,
            ["city"] = current.City,
            ["town"] = current.Town,
            ["structure"] = current.Structure,
            ["form"] = current.Form,
            ["floors_up"] = current.FloorsAbove,
            ["floors_down"] = current.FloorsBelow,
            ["basis_mode"] = current.BasisMode.ToLabel(),
            ["cost_mode"] = current.CostMode.ToLabel(),
            ["area_min"] = current.AreaMin ?? opts.AreaMin,
            ["area_max"] = current.AreaMax ?? opts.AreaMax,
            ["trade"] = current.Trade,
            ["work"] = current.Work,
            ["item"] = current.Item,
            ["highlighted_project_id"] = highlightedProjectId ?? "",
        };

        var options = new Dictionary<string, object?>
        {
            ["pref_options"] = Cap(opts.Prefectures),
            ["structure_options"] = Cap(opts.Structures),
            ["form_options"] = Cap(opts.Forms),
            ["floors_up_options"] = Cap(opts.FloorsAbove),
            ["floors_down_options"] = Cap(opts.FloorsBelow),
            ["basis_mode_options"] = new[] { "法廷床面積", "施工床面積" },
            ["cost_mode_options"] = new[] { "純工事費", "工事原価" },
            ["trade_options"] = Cap(opts.Trades),
            ["area_slider"] = new Dictionary<string, double> { ["min"] = opts.AreaMin, ["max"] = opts.AreaMax },
        };
        if (!compact)
        {
            options["city_options"] = Cap(opts.Cities);
            options["work_options"] = Cap(opts.Works);
            options["item_options"] = Cap(opts.Items);
            options["trade_master_reference"] = TradeMaster.Entries.ToDictionary(
                e => e.Name,
                e => new Dictionary<string, string[]>
                {
                    ["trade_aliases"] = e.TradeAliases,
                    ["work_aliases"] = e.WorkAliases,
                    ["item_aliases"] = e.ItemAliases,
                });
        }

        var user = $"""

ユーザー指示:
{request}

現在値:
{JsonSerializer.Serialize(currentState, PromptJson)}

候補一覧(参考用。ここから直接選ぶのではなく、意図ラベル抽出に使う):
{JsonSerializer.Serialize(options, PromptJson)}

抽出ルール:
- 地域は明示がある場合のみヒントを返す
- RC造/S造 などはそのまま structure_hint に返す
- 共同住宅/マンション/アパート系は form_hint に返す
- 小規模/中規模/大規模 のような規模感は scale_hint に返す
- 内装/外装/躯体/仮設 等の工種感は trade_hint に返す
- 工事項目表の語彙（例: 内装工事 / ﾎﾞｰﾄﾞ / 置床･ﾌﾛｰﾘﾝｸﾞ / OAﾌﾛｱｰ / ｶｰﾃﾝ工事）が見つかる場合は work_hint や item_hint にも反映する
- 不明なら '(未指定)'
""";
        return new List<ChatMessage> { new("system", system), new("user", user) };
    }

    /// <summary>一覧を先頭 MaxOptions 件に絞る（超えた分は「…ほかN件」）。(未指定) は省く。</summary>
    private static List<string> Cap(IReadOnlyList<string> list)
    {
        var items = list.Where(x => x != U).ToList();
        if (items.Count <= MaxOptions) return items;
        var head = items.Take(MaxOptions).ToList();
        head.Add($"…ほか{items.Count - MaxOptions}件");
        return head;
    }

    /// <summary>
    /// 候補値への寄せ（Python _app_apply_ai_filters 内の _pick）。
    /// 完全一致 → 正規化一致 → 部分一致の最初の候補 → (未指定)。
    /// </summary>
    public static string PickOption(IReadOnlyList<string> options, string? candidate)
    {
        var cand = TextNormalizer.Coalesce(candidate, U);
        if (options.Contains(cand)) return cand;
        if (cand != U)
        {
            var nCand = TextNormalizer.NormKey(cand);
            string? partial = null;
            foreach (var opt in options)
            {
                if (opt == U) continue;
                var nOpt = TextNormalizer.NormKey(opt);
                if (nCand == nOpt) return opt;
                if (partial is null && nCand.Length > 0 && nOpt.Length > 0
                    && (nOpt.Contains(nCand, StringComparison.Ordinal) || nCand.Contains(nOpt, StringComparison.Ordinal)))
                {
                    partial = opt;
                }
            }
            if (partial is not null) return partial;
        }
        return options.Contains(U) ? U : (options.Count > 0 ? options[0] : U);
    }

    /// <summary>AI のヒントを、実データの選択肢に寄せた絞込条件に変換する。</summary>
    public static AiFilterOutcome ApplyHints(
        JsonObject ai, string request, FilterCriteria current, FilterOptionsSnapshot opts,
        IReadOnlyList<ProjectRecord> projects, IReadOnlyList<TradePath> hierarchy,
        Func<string, List<string>> worksOf, Func<string, string, List<string>> itemsOf)
    {
        var c = current.Clone();

        c.Prefecture = PickOption(opts.Prefectures, Hint(ai, "pref"));
        c.City = PickOption(opts.Cities, Hint(ai, "city"));
        c.Town = PickOption(opts.Towns, Hint(ai, "town"));
        c.Structure = PickOption(opts.Structures, Hint(ai, "structure"));
        c.Form = PickOption(opts.Forms, Hint(ai, "form"));

        // 階数: ヒントから数字を取り出す。ヒントが無ければ依頼文から拾う（地下の階数を地上と取り違えない）
        var up = ExtractFloor(Hint(ai, "floors_up")) ?? MatchGroup(FloorAboveInText, request);
        var down = ExtractFloor(Hint(ai, "floors_down")) ?? MatchGroup(FloorBelowInText, request);
        c.FloorsAbove = PickOption(opts.FloorsAbove, up);
        c.FloorsBelow = PickOption(opts.FloorsBelow, down);

        if (EnumLabels.ParseBasisMode(Str(ai, "basis_mode")) is BasisMode bm) c.BasisMode = bm;
        if (EnumLabels.ParseCostMode(Str(ai, "cost_mode")) is CostMode cm) c.CostMode = cm;

        // 面積: 規模ヒントがあれば基準面積の 1/3・2/3 分位で区切る。無ければ全範囲。
        var bounds = ProjectFilterService.AreaBounds(projects, c.BasisMode);
        double mn = bounds?.Min ?? opts.AreaMin, mx = bounds?.Max ?? opts.AreaMax;
        double areaMin = mn, areaMax = mx;
        var scale = Str(ai, "scale_hint");
        if (scale is "小規模" or "中規模" or "大規模")
        {
            var s = StatisticsService.SortedFinite(projects
                .Select(p => p.GetArea(c.BasisMode))
                .Where(a => a.HasValue)
                .Select(a => a!.Value));
            if (s.Count > 0)
            {
                var q1 = StatisticsService.Percentile(s, 100.0 / 3);
                var q2 = StatisticsService.Percentile(s, 200.0 / 3);
                (areaMin, areaMax) = scale switch
                {
                    "小規模" => (s[0], q1),
                    "中規模" => (q1, q2),
                    _ => (q2, s[^1]),
                };
            }
        }
        areaMin = Math.Clamp(areaMin, mn, Math.Max(mn, mx));
        areaMax = Math.Clamp(areaMax, mn, Math.Max(mn, mx));
        if (areaMin > areaMax) (areaMin, areaMax) = (mn, mx);
        c.AreaMin = areaMin;
        c.AreaMax = areaMax;

        // 工種: 実データの 工種/工事項目/項目 の階層を逆引きして決める
        var hint = new TradeHint(Hint(ai, "trade") ?? U, Hint(ai, "work") ?? U, Hint(ai, "item") ?? U);
        var path = TradeInferenceService.Resolve(hint, request, hierarchy, opts.Trades);
        c.Trade = PickOption(opts.Trades, path.Trade);
        c.Work = PickOption(worksOf(c.Trade), path.Work);
        c.Item = PickOption(itemsOf(c.Trade, c.Work), path.Item);

        var reason = TextNormalizer.Coalesce(Str(ai, "reason"), "意図ラベルを抽出し、候補値へ安全に反映しました。");
        return new AiFilterOutcome(c, reason);
    }

    /// <summary>【AIで条件設定】の報告文（Python の run_ai_filter_assistant）。</summary>
    public static string BuildReport(string request, string reason, FilterCriteria a, int matchedCount)
    {
        var lines = new List<string>
        {
            "【AIで条件設定】",
            $"指示: {request}",
            $"解釈: {reason}",
            "反映結果:",
            $"  地域: {a.Prefecture} / {a.City} / {a.Town}",
            $"  構造・形態: {a.Structure} / {a.Form}",
            $"  階数: 地上={a.FloorsAbove} / 地下={a.FloorsBelow}",
            $"  面積基準: {a.BasisMode.ToLabel()}  表示モード: {a.CostMode.ToLabel()}",
            string.Create(CultureInfo.InvariantCulture, $"  面積レンジ: {(long)(a.AreaMin ?? 0)}〜{(long)(a.AreaMax ?? 0)}㎡"),
            $"  工種: {a.Trade} / {a.Work} / {a.Item}",
            $"  絞込結果件数: {matchedCount}件",
        };
        if (matchedCount == 0)
        {
            lines.Add("");
            lines.Add("注意:");
            lines.Add("  現在の条件では該当物件が0件です。地域や形態を未指定に戻すと比較しやすくなります。");
        }
        return string.Join("\n", lines);
    }

    // ------------------------------------------------------------------

    /// <summary>"pref" または "pref_hint" の値（Python の ai.get("pref") or ai.get("pref_hint")）。</summary>
    private static string? Hint(JsonObject ai, string key)
    {
        var direct = Str(ai, key);
        return !string.IsNullOrWhiteSpace(direct) ? direct : Str(ai, key + "_hint");
    }

    private static string? Str(JsonObject ai, string key) => ai[key] switch
    {
        JsonValue v when v.TryGetValue<string>(out var s) => s,
        JsonValue v when v.TryGetValue<double>(out var d) => d.ToString(CultureInfo.InvariantCulture),
        _ => null,
    };

    /// <summary>"7階" / "7" / "地上7階" → "7"。未指定・数字なしは null。</summary>
    private static string? ExtractFloor(string? hint)
    {
        if (!FilterCriteria.IsSet(hint)) return null;
        var t = hint!.Trim();
        if (DigitsOnly.IsMatch(t)) return t;
        var m = Regex.Match(t, @"(\d+)\s*階");
        return m.Success ? m.Groups[1].Value : null;
    }

    private static string? MatchGroup(Regex re, string text)
    {
        var m = re.Match(text ?? "");
        return m.Success ? m.Groups[1].Value : null;
    }
}
