namespace ScaffoldAlert.Core.Forecast;

/// <summary>
/// 風の強さ1段階。
/// </summary>
/// <param name="Name">画面に出す名前 (やや強い風 など)。</param>
/// <param name="Rank">強さの順位。大きいほど強い。0は該当なし。</param>
/// <param name="MinSpeed">この階級の下限 (m/s)。</param>
/// <param name="MaxSpeed">この階級の上限 (m/s)。上が無ければ空。</param>
/// <param name="Caution">画面での扱い (なし / 注意 / 危険)。</param>
/// <param name="Color">地図で塗る色。</param>
public record WindStrengthLevel(
    string Name,
    int Rank,
    decimal MinSpeed,
    decimal? MaxSpeed,
    string Caution,
    string Color);

/// <summary>
/// 府県天気予報の風の文章から、風の強さを見分ける係。
///
/// ★なぜ必要か
///   気象庁は風の数値予測をほとんど発表していない
///   (「警報級の可能性」電文の最大風速は、実際にはほぼ「値なし」)。
///   届くのは「西の風 やや強く」のような文章だけ。
///
///   ただし、この言い回しは気象庁が予報用語として
///   風速の範囲を定めているので、文章から範囲が分かる。
///
/// ★出典
///   気象庁 予報用語「風」
///   https://www.jma.go.jp/jma/kishou/know/yougo_hp/kaze.html
///     やや強い風   … 10m/s以上 15m/s未満
///     強い風       … 15m/s以上 20m/s未満
///     非常に強い風 … 20m/s以上 30m/s未満
///     猛烈な風     … およそ30m/s以上、または最大瞬間風速50m/s以上
///
/// ★点検基準との関係 (労働安全衛生規則 第567条第2項)
///     強風 … 10分間平均風速 10m/s以上
///     暴風 … 最大瞬間風速 30m/s以上
///   つまり「やや強い風」が予報された時点で、
///   強風の点検義務が生じる水準に届きうる。
/// </summary>
public static class WindStrength
{
    /// <summary>該当する言い回しが無かったとき。</summary>
    public static readonly WindStrengthLevel None =
        new("風の記載なし", 0, 0m, 10m, "なし", "#c9ced2");

    /// <summary>
    /// 強い順に並べた階級。
    ///
    /// 見分けるときは、この順に文章を探す。
    /// 「非常に強い」を先に見ないと、「強い」に引っかかってしまう。
    /// </summary>
    public static readonly WindStrengthLevel[] Levels =
    {
        new("猛烈な風", 4, 30m, null, "危険", "#8b1a1a"),
        new("非常に強い風", 3, 20m, 30m, "危険", "#e34948"),
        new("強い風", 2, 15m, 20m, "注意", "#eda100"),
        new("やや強い風", 1, 10m, 15m, "注意", "#f2d54e"),
    };

    /// <summary>
    /// 文章に出てくる言い回しを、強いほうから順に見る。
    ///
    /// 気象庁は言い切りと連用形の両方を使う。
    ///   「非常に強い風」「非常に強く」
    /// どちらでも拾えるようにしている。
    /// </summary>
    private static readonly (string Word, int Rank)[] Words =
    {
        ("猛烈な風", 4),
        ("猛烈に", 4),
        ("非常に強い風", 3),
        ("非常に強く", 3),
        ("強い風", 2),
        ("強く", 2),          // 「やや強く」を先に見るので、ここへは来ない
        ("やや強い風", 1),
        ("やや強く", 1),
    };

    /// <summary>
    /// 風の文章から、強さの階級を見分ける。
    ///
    /// 1つの文章に複数の言い回しが入ることがある。
    ///   「西の風 海上 では 南西の風 非常に強く」
    /// そのときは最も強いものを採る。安全側に倒すため。
    /// </summary>
    public static WindStrengthLevel Of(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return None;
        }

        int best = 0;

        foreach (var (word, rank) in Words)
        {
            if (rank <= best)
            {
                continue;
            }

            // 「やや強く」を「強く」と取り違えないようにする。
            // 「やや」が付いているところは、先に やや で数えている。
            if (word == "強く" && text.Contains("やや強く"))
            {
                var without = text.Replace("やや強く", string.Empty);
                if (!without.Contains("強く"))
                {
                    continue;
                }
            }

            if (word == "強い風" && text.Contains("やや強い風"))
            {
                var without = text.Replace("やや強い風", string.Empty);
                if (!without.Contains("強い風"))
                {
                    continue;
                }
            }

            if (text.Contains(word))
            {
                best = rank;
            }
        }

        foreach (var level in Levels)
        {
            if (level.Rank == best)
            {
                return level;
            }
        }

        return None;
    }

    /// <summary>
    /// 点検基準との関係を、ひとことで表す。
    ///
    /// 数値そのものは気象庁から出ていないので、
    /// 「どの範囲に入る予報か」を示す言い方にしている。
    /// </summary>
    public static string AgainstLimit(WindStrengthLevel level,
                                      decimal windLimit)
    {
        if (level.Rank == 0)
        {
            return string.Empty;
        }

        string range = level.MaxSpeed is null
            ? $"{level.MinSpeed:0}m/s以上"
            : $"{level.MinSpeed:0}〜{level.MaxSpeed:0}m/s";

        if (level.MinSpeed >= windLimit)
        {
            return $"{range}。強風の点検基準 {windLimit:0}m/s を超えます";
        }

        return $"{range}。強風の点検基準 {windLimit:0}m/s に届きません";
    }
}
