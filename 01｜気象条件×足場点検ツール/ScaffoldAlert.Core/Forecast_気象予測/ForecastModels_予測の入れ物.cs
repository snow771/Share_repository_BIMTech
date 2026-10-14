namespace ScaffoldAlert.Core.Forecast;

/// <summary>予測1行。区域・対象日時・項目で1つに決まる。</summary>
public class ForecastRow
{
    /// <summary>一次細分区域コード (例 130010)。</summary>
    public string AreaCode { get; set; } = string.Empty;

    /// <summary>区域名 (例 東京地方)。</summary>
    public string? AreaName { get; set; }

    /// <summary>いつの予測か。</summary>
    public DateTime TargetAt { get; set; }

    /// <summary>項目 (例 最大風速)。</summary>
    public string ItemType { get; set; } = string.Empty;

    /// <summary>数値。数値でない予測 (「高」など) のときは空。</summary>
    public decimal? Value { get; set; }

    /// <summary>数値以外の中身。階級 (「高」) や単位 (「%」) が入る。</summary>
    public string? ValueText { get; set; }

    /// <summary>気象庁がこの予測を発表した日時。</summary>
    public DateTime? ReportedAt { get; set; }
}

/// <summary>現場と予報区域の対応。</summary>
public class SiteAreaLink
{
    public int SiteId { get; set; }

    /// <summary>一次細分区域コード。</summary>
    public string AreaCode { get; set; } = string.Empty;

    /// <summary>区域名。</summary>
    public string? AreaName { get; set; }

    /// <summary>府県予報区コード。</summary>
    public string OfficeCode { get; set; } = string.Empty;

    /// <summary>府県名。</summary>
    public string? OfficeName { get; set; }

    /// <summary>
    /// 緯度経度から割り出したかどうか。
    /// 市区町村コードを持たない現場はこちらになる。
    /// </summary>
    public bool ByShape { get; set; }
}

/// <summary>
/// 気象予測の取得結果。画面に出して Python版と比べるために使う。
/// </summary>
public class ForecastFetchResult
{
    /// <summary>区域の階層表から作った市区町村コードの件数。</summary>
    public int MunicipalityCount { get; set; }

    /// <summary>現場と区域の対応が付いたもの。</summary>
    public List<SiteAreaLink> Links { get; set; } = new();

    /// <summary>区域を割り出せなかった現場。</summary>
    public List<int> UnknownSiteIds { get; set; } = new();

    /// <summary>緯度経度から割り出した件数。</summary>
    public int ByShapeCount => Links.Count(l => l.ByShape);

    /// <summary>現場がある府県予報区。</summary>
    public HashSet<string> Offices { get; set; } = new();

    /// <summary>フィードに入っていた該当電文の件数。</summary>
    public int FeedTotal { get; set; }

    /// <summary>うち自社の現場がある区域の件数。</summary>
    public int FeedWanted { get; set; }

    /// <summary>電文から取り出した予測。</summary>
    public List<ForecastRow> Rows { get; set; } = new();

    /// <summary>府県天気予報から取り出した風の予報(文章)。</summary>
    public List<ForecastRow> WindTexts { get; set; } = new();

    /// <summary>取り込めなかった電文。</summary>
    public List<string> Failed { get; set; } = new();

    /// <summary>風の予報を取得できなかった府県予報区。</summary>
    public List<string> WindTextFailed { get; set; } = new();

    /// <summary>うまくいかなかったときの理由。</summary>
    public Dictionary<string, string> Errors { get; set; } = new();

    /// <summary>
    /// 基準に達する予測。これから先のものだけを対象にする。
    /// Python版がバッチの最後に表示している一覧と同じもの。
    /// </summary>
    public List<ForecastRow> OverLimit(DateTime now)
        => Rows.Where(r => r.TargetAt >= now
                           && r.Value.HasValue
                           && ForecastSettings.Limits.TryGetValue(
                                  r.ItemType, out var limit)
                           && r.Value.Value >= limit)
               .OrderBy(r => r.TargetAt)
               .ThenBy(r => r.AreaName)
               .ToList();
}
