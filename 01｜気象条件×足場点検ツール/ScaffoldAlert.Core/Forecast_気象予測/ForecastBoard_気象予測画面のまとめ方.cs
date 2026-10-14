using ScaffoldAlert.Core.Data;

namespace ScaffoldAlert.Core.Forecast;

/// <summary>基準に達する予測1行。</summary>
public class OverLimitRow
{
    public string SiteName { get; set; } = string.Empty;
    public string Department { get; set; } = string.Empty;
    public string AreaLabel { get; set; } = string.Empty;
    public string ItemType { get; set; } = string.Empty;
    public decimal? Value { get; set; }
    public string? ValueText { get; set; }
    public DateTime TargetAt { get; set; }
}

/// <summary>
/// 警報級の可能性を、現場と種類ごとに1行へまとめたもの。
///
/// 気象庁は時間帯ごとに可能性を発表するため、そのまま並べると
/// 同じ現場が「風・6時間ごと」「雨・6時間ごと」…と何行も並ぶ。
/// 対策会議で見たいのは「どの現場がいつ、どれくらい危ないか」なので、
/// 最も高い可能性と、それが最初に来る時間帯を1行で出す。
/// 時間帯ごとの細かい動きは、行を押したときに見られるようにする。
/// </summary>
public class PossibilityRow
{
    public int SiteId { get; set; }
    public string SiteName { get; set; } = string.Empty;
    public string Department { get; set; } = string.Empty;
    public string AreaLabel { get; set; } = string.Empty;

    /// <summary>風 / 雨 / 雪。</summary>
    public string Kind { get; set; } = string.Empty;

    /// <summary>高 / 中。</summary>
    public string Level { get; set; } = string.Empty;

    /// <summary>最も高くなる時間帯。</summary>
    public DateTime TargetAt { get; set; }

    /// <summary>
    /// その時間帯に効いている風の予報(文章)。
    ///
    /// 気象庁は風の数値をほとんど発表しないため、
    /// 「やや強く」などの言い回しが唯一の手掛かりになる。
    /// </summary>
    public string? WindText { get; set; }

    /// <summary>風の文章から見分けた強さ。</summary>
    public WindStrengthLevel Wind => WindStrength.Of(WindText);
}

/// <summary>
/// 風の予報の一覧の1行（現場ごと）。
///
/// 「警報級の可能性」とは別の表にする。
/// あちらは気象庁が【高】【中】と発表したもの、
/// こちらは府県天気予報の文章から見分けた風の強さ。
/// 根拠が違うものを1つの表に混ぜると、読み手が取り違える。
/// </summary>
public class WindRow
{
    public int SiteId { get; set; }
    public string SiteName { get; set; } = string.Empty;
    public string Department { get; set; } = string.Empty;
    public string AreaLabel { get; set; } = string.Empty;
    public string AreaCode { get; set; } = string.Empty;

    /// <summary>いつの予報か。</summary>
    public DateTime TargetAt { get; set; }

    /// <summary>気象庁の文章。「西の風 やや強く」など。</summary>
    public string Text { get; set; } = string.Empty;

    /// <summary>文章から見分けた強さ。</summary>
    public WindStrengthLevel Strength { get; set; } = WindStrength.None;
}

/// <summary>ある時間帯の風の予報1つ。</summary>
public class AreaWind
{
    /// <summary>いつの予報か。</summary>
    public DateTime TargetAt { get; set; }

    /// <summary>気象庁の文章。</summary>
    public string Text { get; set; } = string.Empty;

    /// <summary>文章から見分けた強さ。</summary>
    public WindStrengthLevel Strength => WindStrength.Of(Text);
}

/// <summary>1つの区域の、地図に塗る色を決める材料。</summary>
public class AreaLevel
{
    public string AreaCode { get; set; } = string.Empty;
    public string AreaLabel { get; set; } = string.Empty;

    /// <summary>高 / 中 / 空 (発表なし)。</summary>
    public string Level { get; set; } = string.Empty;

    /// <summary>
    /// その区域の風の予報。今日・明日・明後日ぶん。
    ///
    /// 気象庁の府県天気予報には、1つの区域につき3つ入っている。
    /// 「いま」の1つだけを見ると、明日以降に強い風があっても
    /// 分からなくなる。
    /// </summary>
    public List<AreaWind> Winds { get; } = new();

    /// <summary>
    /// この先で最も強い風。地図の色はこれで決める。
    ///
    /// ★予測を見る画面なので、いまより「この先どうなるか」が要る。
    ///   いまの状況は、現場にいる監督が自分で判断できる。
    /// </summary>
    public AreaWind? Strongest
        => Winds.OrderByDescending(w => w.Strength.Rank)
                .ThenBy(w => w.TargetAt)
                .FirstOrDefault();

    /// <summary>この先で最も強い風の階級。無ければ「記載なし」。</summary>
    public WindStrengthLevel Wind
        => Strongest?.Strength ?? WindStrength.None;
}

/// <summary>
/// 気象予測を、画面に出せる形にまとめる係。
///
/// ★ここに出るのは予測であり、点検義務そのものではない。
///   法令の点検義務は実際に悪天候が起きた後に生じるため、
///   ダッシュボードのアラートとは別の画面に分けている。
///   混ぜると、臨検で「点検していない事象」が並んで見えてしまう。
/// </summary>
public static class ForecastBoard
{
    /// <summary>
    /// 「警報級の可能性」の高さの順位。
    /// 文字のままでは大小を比べられないため、数に置き換える。
    /// </summary>
    public static readonly Dictionary<string, int> PossibilityRank = new()
    {
        ["中"] = 1,
        ["高"] = 2,
    };

    /// <summary>「警報級の可能性」で塗り分けるときの色。</summary>
    public static readonly Dictionary<string, string> PossibilityColor = new()
    {
        ["高"] = "#e34948",
        ["中"] = "#eda100",
    };

    /// <summary>発表が無い区域の色。</summary>
    public const string NoLevelColor = "#c9ced2";

    /// <summary>
    /// 予測1つが受け持つ時間の長さ (時間)。
    ///
    /// 気象庁の「警報級の可能性」は6時間ごとに区切って発表される。
    /// 12:00 の区切りは 18:00 まで効いている。
    /// </summary>
    public const int BucketHours = 6;

    /// <summary>風の予報を保存するときの項目名。</summary>
    public const string WindTextItem = "風の予報";

    /// <summary>種類 (風/雨/雪) と、電文の項目名の対応。</summary>
    public static readonly Dictionary<string, string> KindItem = new()
    {
        ["風"] = "風（風雪）の警報級の可能性",
        ["雨"] = "雨の警報級の可能性",
        ["雪"] = "雪の警報級の可能性",
    };

    /// <summary>
    /// 地図に塗る、区域ごとの警戒度を求める。
    ///
    /// これから48時間のうち最も高い可能性を採る。
    /// 時間帯ごとに出すと地図を何枚も見ることになり、
    /// 対策会議で「どこが危ないか」を一目で見る用途に合わない。
    /// </summary>
    public static Dictionary<string, AreaLevel> AreaLevels(
        List<SiteForecast> forecasts, string kind, DateTime now)
    {
        var limit = now.AddHours(48);
        string item = KindItem.TryGetValue(kind, out var v) ? v : string.Empty;

        var result = new Dictionary<string, AreaLevel>();

        foreach (var f in forecasts)
        {
            if (f.TargetAt > limit)
            {
                continue;
            }

            // 終わった区切りは見ない。
            //
            // 区切りの長さは種類で違う。
            //   警報級の可能性 … 6時間ごと
            //   風の予報       … 今日・明日・明後日 の3つ（1日ぶん）
            // 同じ物差しで切ると、風の明日の分まで落ちてしまう。
            int hours = f.ItemType == WindTextItem ? 24 : BucketHours;
            if (f.TargetAt.AddHours(hours) <= now)
            {
                continue;
            }

            if (!result.TryGetValue(f.AreaCode, out var area))
            {
                area = new AreaLevel
                {
                    AreaCode = f.AreaCode,
                    AreaLabel = f.AreaLabel,
                };
                result[f.AreaCode] = area;
            }

            if (f.ItemType == item)
            {
                string level = (f.ValueText ?? string.Empty).Trim();
                if (RankOf(level) > RankOf(area.Level))
                {
                    area.Level = level;
                }
            }
            else if (f.ItemType == WindTextItem
                     && !string.IsNullOrEmpty(f.ValueText))
            {
                // 今日・明日・明後日ぶんを、すべて集める。
                // 地図の色は、この中で最も強いもので決める。
                if (!area.Winds.Any(w => w.TargetAt == f.TargetAt))
                {
                    area.Winds.Add(new AreaWind
                    {
                        TargetAt = f.TargetAt,
                        Text = f.ValueText,
                    });
                }
            }
        }

        return result;
    }

    /// <summary>
    /// 基準に達する予測を取り出す。
    ///
    /// 基準の数字は判定と同じものを使う。片方だけ変えると食い違うため、
    /// データベース (M_Threshold) の1か所から読む。
    /// </summary>
    public static List<OverLimitRow> OverLimit(
        List<SiteForecast> forecasts,
        decimal windLimit, decimal rainLimit, decimal snowLimit)
    {
        var result = new List<OverLimitRow>();

        foreach (var f in forecasts)
        {
            if (f.Value is null)
            {
                continue;
            }

            bool over =
                (f.ItemType == "最大風速" && f.Value >= windLimit)
                || (f.ItemType == "２４時間最大雨量" && f.Value >= rainLimit)
                || (f.ItemType == "２４時間最大降雪量" && f.Value >= snowLimit);

            if (!over)
            {
                continue;
            }

            result.Add(new OverLimitRow
            {
                SiteName = f.SiteName,
                Department = f.Department,
                AreaLabel = f.AreaLabel,
                ItemType = f.ItemType,
                Value = f.Value,
                ValueText = f.ValueText,
                TargetAt = f.TargetAt,
            });
        }

        return result.OrderBy(r => r.TargetAt)
                     .ThenBy(r => r.SiteName)
                     .ToList();
    }

    /// <summary>
    /// 警報級の可能性を、現場と種類ごとに1行へまとめる。
    /// 高い順、同じ高さなら現場名の順に並べる。
    /// </summary>
    /// <summary>
    /// 風の予報を、現場ごとの一覧にまとめる。
    ///
    /// ★なぜ「警報級の可能性」と別にするか
    ///   気象庁は風の【高】【中】をほとんど発表しない。
    ///   そのため、警報級の可能性の一覧には風の行がほぼ出ない。
    ///
    ///   一方で地図は、風の強さ（府県天気予報の文章）で色を塗っている。
    ///   結果として「地図は色が付いているのに一覧に無い」となり、
    ///   風が取れていないように見えてしまう。
    ///
    ///   地図の色と同じ根拠で一覧を作れば、この食い違いが消える。
    ///
    /// ★出すのは「注意」以上のものだけ
    ///   風の記載が無い区域まで並べると、ほぼ全現場が出てしまい、
    ///   どこが危ないのかが読み取れない。
    /// </summary>
    public static List<WindRow> SummarizeWind(List<SiteForecast> forecasts,
                                              DateTime now)
    {
        // 区域ごとに、この先で最も強い風を1つ選ぶ。
        // 地図の色を決めているのと同じ選び方。
        var strongest = new Dictionary<string, SiteForecast>();

        foreach (var f in forecasts)
        {
            if (f.ItemType != WindTextItem
                || string.IsNullOrEmpty(f.ValueText))
            {
                continue;
            }

            // 終わった日は見ない。風の予報は1日ぶんの区切り。
            if (f.TargetAt.AddHours(24) <= now)
            {
                continue;
            }

            if (!strongest.TryGetValue(f.AreaCode, out var best))
            {
                strongest[f.AreaCode] = f;
                continue;
            }

            int now_ = WindStrength.Of(f.ValueText).Rank;
            int old_ = WindStrength.Of(best.ValueText).Rank;

            // 強いほうを採る。同じ強さなら早いほうを採る。
            // 「いつから危ないか」を先に知りたいため。
            if (now_ > old_ || (now_ == old_ && f.TargetAt < best.TargetAt))
            {
                strongest[f.AreaCode] = f;
            }
        }

        // 現場ごとに1行。同じ区域の現場は同じ内容になる。
        var rows = new List<WindRow>();
        var seen = new HashSet<int>();

        foreach (var f in forecasts)
        {
            if (seen.Contains(f.SiteId))
            {
                continue;
            }

            if (!strongest.TryGetValue(f.AreaCode, out var wind))
            {
                continue;
            }

            var level = WindStrength.Of(wind.ValueText);

            // 「注意」以上のものだけ出す。
            if (level.Rank == 0)
            {
                continue;
            }

            seen.Add(f.SiteId);

            rows.Add(new WindRow
            {
                SiteId = f.SiteId,
                SiteName = f.SiteName,
                Department = f.Department,
                AreaLabel = f.AreaLabel,
                AreaCode = f.AreaCode,
                TargetAt = wind.TargetAt,
                Text = wind.ValueText ?? string.Empty,
                Strength = level,
            });
        }

        // 強い順。同じなら早い順。
        return rows
            .OrderByDescending(r => r.Strength.Rank)
            .ThenBy(r => r.TargetAt)
            .ThenBy(r => r.SiteName)
            .ToList();
    }

    public static List<PossibilityRow> Summarize(List<SiteForecast> forecasts,
                                                DateTime now)
    {
        // 区域ごとの風の予報。あとで組み合わせるために先に作る。
        //
        // 風の予報は「今日」「今夜」「明日」のような大きな刻みで、
        // 警報級の可能性の時間帯とはぴったり合わない。
        // そこで、その時刻に効いている(その時刻以前で最も新しい)
        // ものを選ぶ。
        var windTexts = new Dictionary<string, List<SiteForecast>>();
        foreach (var f in forecasts)
        {
            if (f.ItemType != WindTextItem
                || string.IsNullOrEmpty(f.ValueText))
            {
                continue;
            }

            if (!windTexts.TryGetValue(f.AreaCode, out var list))
            {
                list = new List<SiteForecast>();
                windTexts[f.AreaCode] = list;
            }
            list.Add(f);
        }

        foreach (var list in windTexts.Values)
        {
            list.Sort((a, b) => a.TargetAt.CompareTo(b.TargetAt));
        }

        var summary = new Dictionary<(int, string), PossibilityRow>();

        foreach (var f in forecasts)
        {
            string? kind = KindOf(f.ItemType);
            if (kind is null)
            {
                continue;
            }

            string level = (f.ValueText ?? string.Empty).Trim();
            if (RankOf(level) == 0)
            {
                continue;
            }

            // 終わった区切りは出さない。
            // 「この先いつ危ないか」を見る画面なので、
            // 済んだ話が混ざると読み取れなくなる。
            if (!StillAhead(f.TargetAt, now))
            {
                continue;
            }

            // 一覧に出す風は「この先で最も強いもの」。
            // 可能性の時間帯に合わせると、その瞬間の風しか見えず、
            // 明日以降に強い風があっても気づけない。
            string? windText = StrongestWind(windTexts, f.AreaCode);

            var key = (f.SiteId, kind);

            if (!summary.TryGetValue(key, out var row))
            {
                summary[key] = new PossibilityRow
                {
                    SiteId = f.SiteId,
                    SiteName = f.SiteName,
                    Department = f.Department,
                    AreaLabel = f.AreaLabel,
                    Kind = kind,
                    Level = level,
                    TargetAt = f.TargetAt,
                    WindText = windText,
                };
                continue;
            }

            // より高い可能性が来たら差し替える。
            // 同じ高さなら、先に来る時間帯を残す。
            // 終わった区切りは上で外してあるので、単純に早い順でよい。
            if (RankOf(level) > RankOf(row.Level)
                || (RankOf(level) == RankOf(row.Level)
                    && f.TargetAt < row.TargetAt))
            {
                row.Level = level;
                row.TargetAt = f.TargetAt;
            }

            // 風は区域ごとに1つ (この先で最も強いもの) なので、
            // 差し替える必要はない。
        }

        return summary.Values
                      .OrderByDescending(r => RankOf(r.Level))
                      .ThenBy(r => r.SiteName)
                      .ThenBy(r => r.Kind)
                      .ToList();
    }

    /// <summary>
    /// その区切りが、まだ終わっていないか。
    ///
    /// ★開始時刻だけを見て「過ぎた」と決めてはいけない。
    ///   12:00 の区切りは 18:00 まで効いているので、
    ///   16時に見たときの 12:00 は「いま」であって「過去」ではない。
    ///   ここを取り違えると、いま危ない区域を画面から消してしまう。
    /// </summary>
    public static bool StillAhead(DateTime targetAt, DateTime now)
        => targetAt.AddHours(BucketHours) > now;

    /// <summary>いま効いている区切りか (もう始まっていて、まだ終わっていない)。</summary>
    public static bool IsNow(DateTime targetAt, DateTime now)
        => targetAt <= now && StillAhead(targetAt, now);

    /// <summary>
    /// その区域の、この先で最も強い風の予報を返す。
    ///
    /// ★予測を見る画面なので、いまより「この先どうなるか」が要る。
    ///   いまの状況は、現場にいる監督が自分で判断できる。
    /// </summary>
    public static string? StrongestWind(
        Dictionary<string, List<SiteForecast>> windTexts, string areaCode)
    {
        if (!windTexts.TryGetValue(areaCode, out var list) || list.Count == 0)
        {
            return null;
        }

        return list
            .OrderByDescending(r => WindStrength.Of(r.ValueText).Rank)
            .ThenBy(r => r.TargetAt)
            .First()
            .ValueText;
    }

    /// <summary>
    /// その時刻に効いている風の予報を返す。
    ///
    /// 風の予報は「今日」「今夜」「明日」のような大きな刻みなので、
    /// その時刻以前で最も新しいものを選ぶ。
    /// それより前のものが無ければ、いちばん最初のものを使う。
    /// </summary>
    public static string? WindTextAt(
        Dictionary<string, List<SiteForecast>> windTexts,
        string areaCode, DateTime at)
    {
        if (!windTexts.TryGetValue(areaCode, out var list) || list.Count == 0)
        {
            return null;
        }

        string? found = null;

        foreach (var row in list)
        {
            if (row.TargetAt <= at)
            {
                found = row.ValueText;
            }
            else
            {
                break;
            }
        }

        return found ?? list[0].ValueText;
    }

    /// <summary>
    /// 「高」「中」が何を意味するかの説明。
    ///
    /// ★気象庁は【高】【中】に％を公表していない。
    ///   公式の解説ページにあるのは言葉の説明だけなので、
    ///   それをそのまま画面に出す。
    ///   こちらで「◯％以上」と決めて出すと、
    ///   気象庁が言っていないことを気象庁の予測として
    ///   表示することになり、臨検で根拠を示せない。
    ///
    /// 出典 気象庁「早期注意情報（警報級の可能性）」
    ///   https://www.jma.go.jp/jma/kishou/know/bosai/prob_warning.html
    /// </summary>
    public static string PossibilityNote(string? level)
    {
        return (level ?? string.Empty).Trim() switch
        {
            "高" => "警報などが既に発表されているか、"
                    + "発表される可能性が高まっています。",
            "中" => "［高］ほど確度は高くないものの、"
                    + "命に危険が及ぶような警報級の現象となり得ます。",
            _ => string.Empty,
        };
    }

    /// <summary>可能性の高さを数にする。分からないものは0。</summary>
    public static int RankOf(string? level)
        => level is not null && PossibilityRank.TryGetValue(level, out var v)
           ? v : 0;

    /// <summary>
    /// 項目名から種類 (風/雨/雪) を取り出す。
    /// 警報級の可能性でなければ空を返す。
    /// </summary>
    public static string? KindOf(string itemType)
    {
        foreach (var pair in KindItem)
        {
            if (pair.Value == itemType)
            {
                return pair.Key;
            }
        }
        return null;
    }
}
