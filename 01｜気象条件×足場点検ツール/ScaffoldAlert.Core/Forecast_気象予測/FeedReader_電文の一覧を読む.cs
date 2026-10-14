using System.Text.RegularExpressions;
using System.Xml.Linq;
using ScaffoldAlert.Core.Weather;

namespace ScaffoldAlert.Core.Forecast;

/// <summary>フィードで見つけた電文1つ。</summary>
public record FeedEntry(string OfficeCode, string Title,
                        DateTime UpdatedAt, string Url);

/// <summary>
/// 防災情報XMLのフィードを読み、取り込む電文を選ぶ係。
/// </summary>
public class FeedReader
{
    private readonly IHttpClientFactoryLike _http;

    public FeedReader(IHttpClientFactoryLike http)
    {
        _http = http;
    }

    /// <summary>
    /// フィードを読み、取り込む電文を府県予報区ごとに1つ選ぶ。
    ///
    /// 同じ府県について新旧の電文が並ぶため、発表の新しいものだけを残す。
    /// </summary>
    public List<FeedEntry> Read()
    {
        // 鍵は (府県予報区コード, 電文の種類)。
        var latest = new Dictionary<(string, string), FeedEntry>();

        foreach (var feedUrl in ForecastSettings.FeedUrls)
        {
            string xml = _http.GetString(feedUrl);
            var root = XDocument.Parse(xml).Root;
            if (root is null)
            {
                continue;
            }

            foreach (var entry in root.Elements())
            {
                if (XmlHelper.TagOf(entry) != "entry")
                {
                    continue;
                }

                string? title = XmlHelper.ChildText(entry, "title");
                if (title is null || !ForecastSettings.WantTitles.Contains(title))
                {
                    continue;
                }

                string? url = XmlHelper.ChildText(entry, "id");
                if (string.IsNullOrEmpty(url))
                {
                    continue;
                }

                var updated = XmlHelper.ToDateTime(
                    XmlHelper.ChildText(entry, "updated")) ?? DateTime.MinValue;

                // 例 20260826054331_0_VPFD60_220000.xml → 府県予報区は 220000
                var m = Regex.Match(url, @"_([0-9]{6})\.xml$");
                if (!m.Success)
                {
                    continue;
                }

                var key = (m.Groups[1].Value, title);
                if (!latest.TryGetValue(key, out var known)
                    || updated > known.UpdatedAt)
                {
                    latest[key] = new FeedEntry(
                        m.Groups[1].Value, title, updated, url);
                }
            }
        }

        return latest.Values
                     .OrderBy(e => e.OfficeCode)
                     .ThenBy(e => e.Title)
                     .ToList();
    }
}
