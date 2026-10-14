using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ScaffoldAlert.Core.Weather;

/// <summary>
/// 気象庁から地震情報を取ってくる係。
///
/// ★この段階ではデータベースに書き込まない。
///   取ってきた内容を返すだけにして、Python版と突き合わせる。
///
/// 気象庁は同じ地震を続報として何度も配信し直す。
/// 伝送が遅れた観測点の震度が数時間〜数日後に追加されることがあり、
/// そのときに特定の市区町村の震度が 3 から 4 に上がることもある。
/// そのため、取り込み済みの地震でも直近のものは毎回見直す。
/// </summary>
public class EarthquakeFetcher
{
    /// <summary>
    /// 続報を見直す時間の幅。
    /// 24時間では更新を取り逃すため、余裕をみて72時間(3日)分を毎回見直す。
    /// Python版 (fetch_earthquake.py) の REFRESH_HOURS と同じ値。
    /// </summary>
    public const int RefreshHours = 72;

    private readonly IHttpClientFactoryLike _http;

    public EarthquakeFetcher(IHttpClientFactoryLike http)
    {
        _http = http;
    }

    /// <summary>
    /// 地震一覧を取ってきて、取り込みの対象になるものを返す。
    /// </summary>
    /// <param name="knownEventIds">
    /// 既にデータベースに入っている地震のID。
    /// これに含まれ、かつ古いものは対象から外す。
    /// </param>
    public QuakeFetchResult Fetch(HashSet<string> knownEventIds)
    {
        var result = new QuakeFetchResult();

        string json;
        try
        {
            json = _http.GetString(JmaUrls.QuakeList);
        }
        catch (Exception ex)
        {
            result.ErrorMessage = ex.Message;
            return result;
        }

        using var doc = JsonDocument.Parse(json);
        var entries = doc.RootElement;
        result.DeliveredCount = entries.GetArrayLength();

        // この時刻より前の地震は、もう続報が来ないとみなす。
        var border = DateTime.Now.AddHours(-RefreshHours);

        foreach (var element in entries.EnumerateArray())
        {
            var entry = Parse(element);

            // 震度の情報が無いものは対象外。
            // 遠地地震(海外)など、国内の震度が発表されないものが含まれる。
            if (entry.CityIntensities.Count == 0)
            {
                result.SkippedNoIntensity++;
                continue;
            }

            // 取り込み済みで、もう更新されない地震は飛ばす。
            // 毎回すべてを入れ直すと無駄が大きいため。
            bool known = knownEventIds.Contains(entry.EventId);
            bool old = entry.OccurredAt is null || entry.OccurredAt < border;
            if (known && old)
            {
                result.SkippedKnown++;
                continue;
            }

            result.Targets.Add(entry);
        }

        result.TargetCount = result.Targets.Count;
        return result;
    }

    /// <summary>JSONの1件を、扱いやすい形に直す。</summary>
    private static QuakeEntry Parse(JsonElement element)
    {
        var entry = new QuakeEntry
        {
            EventId = GetString(element, "eid") ?? string.Empty,
            OccurredAt = ToDateTime(GetString(element, "at")),
            Hypocenter = GetString(element, "anm"),
            Magnitude = ToDecimal(GetString(element, "mag")),
            DepthKm = ToDepthKm(GetString(element, "cod")),
            MaxIntensity = GetString(element, "maxi"),
        };

        // int は「都道府県 → 市区町村」の入れ子になっている。
        // 判定で使うのは市区町村ごとの震度なので、そこまで降りる。
        if (element.TryGetProperty("int", out var prefs)
            && prefs.ValueKind == JsonValueKind.Array)
        {
            foreach (var pref in prefs.EnumerateArray())
            {
                if (!pref.TryGetProperty("city", out var cities)
                    || cities.ValueKind != JsonValueKind.Array)
                {
                    continue;
                }

                foreach (var city in cities.EnumerateArray())
                {
                    string? code = GetString(city, "code");
                    string? level = GetString(city, "maxi");
                    if (!string.IsNullOrEmpty(code) && !string.IsNullOrEmpty(level))
                    {
                        entry.CityIntensities[code] = level;
                    }
                }
            }
        }

        return entry;
    }

    /// <summary>JSONから文字列を取り出す。数値で入っていても文字にする。</summary>
    private static string? GetString(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.ToString(),
            _ => null,
        };
    }

    /// <summary>
    /// 気象庁の日時表記 (2026-08-06T21:21:00+09:00) を日本時間の日時に直す。
    /// データベースの他の日時と揃えるため、時差の情報は落として保存する。
    /// </summary>
    public static DateTime? ToDateTime(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        if (DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture,
                                    DateTimeStyles.None, out var dto))
        {
            return dto.DateTime;
        }

        return null;
    }

    /// <summary>数値に直す。直せなければ空を返す。</summary>
    public static decimal? ToDecimal(string? text)
    {
        if (decimal.TryParse(text, NumberStyles.Any,
                             CultureInfo.InvariantCulture, out var v))
        {
            return v;
        }
        return null;
    }

    /// <summary>
    /// 震源要素 (+32.3+130.4-10000/) から深さ(km)を取り出す。
    /// 末尾の数値がメートル単位の深さ。取れなければ空を返す。
    /// </summary>
    public static int? ToDepthKm(string? cod)
    {
        if (string.IsNullOrWhiteSpace(cod))
        {
            return null;
        }

        var m = Regex.Match(cod, @"^[+-][\d.]+[+-][\d.]+([+-]\d+)");
        if (!m.Success)
        {
            return null;
        }

        if (int.TryParse(m.Groups[1].Value, NumberStyles.Any,
                         CultureInfo.InvariantCulture, out var meters))
        {
            return Math.Abs(meters) / 1000;
        }
        return null;
    }
}
