using System.Text.Json;
using ScaffoldAlert.Core.Weather;

namespace ScaffoldAlert.Core.Forecast;

/// <summary>
/// 府県天気予報から、風の予報(文章)を取り出す係。
///
/// ★なぜ文章なのか
///   気象庁は風の数値予測をほとんど発表していない。
///   「警報級の可能性」の電文にある最大風速の欄は、
///   実際にはほぼ「値なし」で届く (2026-08-28 に全国で確認)。
///
///   数値が無いままでは風の手掛かりがゼロになるため、
///   府県天気予報の文章を取り込む。
///     例 「西の風 海上では 南西の風 やや強く」
///   数値ではないが、強さの目安にはなる。
///
/// 区域は「警報級の可能性」と同じ一次細分区域なので、同じ表に入れられる。
/// </summary>
public class WindTextFetcher
{
    private readonly IHttpClientFactoryLike _http;

    public WindTextFetcher(IHttpClientFactoryLike http)
    {
        _http = http;
    }

    /// <summary>1つの府県予報区の風の予報を取ってくる。</summary>
    public List<ForecastRow> Fetch(string officeCode)
    {
        var rows = new List<ForecastRow>();

        string url = string.Format(ForecastSettings.ForecastUrl, officeCode);
        string json = _http.GetString(url);

        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind != JsonValueKind.Array)
        {
            return rows;
        }

        foreach (var block in doc.RootElement.EnumerateArray())
        {
            DateTime? reportedAt = XmlHelper.ToDateTime(
                Text(block, "reportDatetime"));

            if (!block.TryGetProperty("timeSeries", out var seriesList)
                || seriesList.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var series in seriesList.EnumerateArray())
            {
                // いつの予報かの並び。
                var times = new List<DateTime?>();
                if (series.TryGetProperty("timeDefines", out var defines)
                    && defines.ValueKind == JsonValueKind.Array)
                {
                    foreach (var t in defines.EnumerateArray())
                    {
                        times.Add(XmlHelper.ToDateTime(t.GetString()));
                    }
                }

                if (!series.TryGetProperty("areas", out var areas)
                    || areas.ValueKind != JsonValueKind.Array)
                {
                    continue;
                }

                foreach (var area in areas.EnumerateArray())
                {
                    // 風が入っていない並びもある (気温や降水確率など)。
                    if (!area.TryGetProperty("winds", out var winds)
                        || winds.ValueKind != JsonValueKind.Array)
                    {
                        continue;
                    }

                    string code = string.Empty;
                    string? name = null;
                    if (area.TryGetProperty("area", out var info))
                    {
                        code = Text(info, "code") ?? string.Empty;
                        name = Text(info, "name");
                    }

                    if (string.IsNullOrEmpty(code))
                    {
                        continue;
                    }

                    // 時刻と文章を先頭から順に組にする。
                    var values = winds.EnumerateArray().ToList();
                    int count = Math.Min(times.Count, values.Count);

                    for (int i = 0; i < count; i++)
                    {
                        var at = times[i];
                        string text = (values[i].ValueKind == JsonValueKind.String
                            ? values[i].GetString() ?? string.Empty
                            : string.Empty).Trim();

                        if (at is null || text.Length == 0)
                        {
                            continue;
                        }

                        rows.Add(new ForecastRow
                        {
                            AreaCode = code,
                            AreaName = name,
                            TargetAt = at.Value,
                            ItemType = ForecastSettings.WindTextItem,

                            // 数値ではないので Value は空のまま。
                            Value = null,
                            ValueText = Tidy(text),
                            ReportedAt = reportedAt,
                        });
                    }
                }
            }
        }

        return rows;
    }

    /// <summary>
    /// 読みやすく整える。
    ///
    /// 気象庁の文章は、区切りに全角の空白がいくつも入っている。
    ///   「西の風　　海上　では　南西の風　やや強く」
    /// そのまま出すと間延びするので、1つにまとめる。
    /// </summary>
    private static string Tidy(string text)
    {
        var parts = text.Split(new[] { '\u3000', ' ' },
                               StringSplitOptions.RemoveEmptyEntries);
        return string.Join(" ", parts);
    }

    /// <summary>JSONから文字列を取り出す。数値で入っていても文字にする。</summary>
    private static string? Text(JsonElement element, string name)
    {
        if (element.ValueKind != JsonValueKind.Object
            || !element.TryGetProperty(name, out var v))
        {
            return null;
        }

        return v.ValueKind switch
        {
            JsonValueKind.String => v.GetString()?.Trim(),
            JsonValueKind.Number => v.ToString(),
            _ => null,
        };
    }
}
