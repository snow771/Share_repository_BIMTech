using System.Globalization;
using System.Xml.Linq;

namespace ScaffoldAlert.Core.Forecast;

/// <summary>
/// 1つの電文から、保存する行を取り出す係。
/// </summary>
public static class ForecastParser
{
    /// <summary>電文を読み解いて、予測の行にする。</summary>
    public static List<ForecastRow> Parse(string xml)
    {
        var rows = new List<ForecastRow>();

        var root = XDocument.Parse(xml).Root;
        if (root is null)
        {
            return rows;
        }

        // 発表日時。電文の中に1つだけある。
        DateTime? reportedAt = null;
        foreach (var el in XmlHelper.SelfAndDescendants(root))
        {
            if (XmlHelper.TagOf(el) == "ReportDateTime")
            {
                reportedAt = XmlHelper.ToDateTime(el.Value.Trim());
                break;
            }
        }

        foreach (var series in XmlHelper.SelfAndDescendants(root))
        {
            if (XmlHelper.TagOf(series) != "TimeSeriesInfo")
            {
                continue;
            }

            // 「timeId」と、それが指す日時の対応。
            var times = new Dictionary<string, DateTime>();
            foreach (var td in XmlHelper.SelfAndDescendants(series))
            {
                if (XmlHelper.TagOf(td) != "TimeDefine")
                {
                    continue;
                }

                string? id = td.Attribute("timeId")?.Value;
                var at = XmlHelper.ToDateTime(XmlHelper.ChildText(td, "DateTime"));
                if (!string.IsNullOrEmpty(id) && at is not null)
                {
                    times[id] = at.Value;
                }
            }

            foreach (var item in series.Elements())
            {
                if (XmlHelper.TagOf(item) != "Item")
                {
                    continue;
                }

                // どの区域についての予測か。
                string? areaCode = null;
                string? areaName = null;
                foreach (var a in item.Elements())
                {
                    if (XmlHelper.TagOf(a) == "Area")
                    {
                        areaCode = XmlHelper.ChildText(a, "Code");
                        areaName = XmlHelper.ChildText(a, "Name");
                    }
                }

                if (string.IsNullOrEmpty(areaCode))
                {
                    continue;
                }

                foreach (var kind in item.Elements())
                {
                    if (XmlHelper.TagOf(kind) != "Kind")
                    {
                        continue;
                    }

                    foreach (var prop in kind.Elements())
                    {
                        if (XmlHelper.TagOf(prop) != "Property")
                        {
                            continue;
                        }

                        string? itemType = XmlHelper.ChildText(prop, "Type");
                        if (itemType is null
                            || !ForecastSettings.KeepItems.Contains(itemType))
                        {
                            continue;
                        }

                        ReadProperty(prop, itemType, areaCode, areaName,
                                     times, reportedAt, rows);
                    }
                }
            }
        }

        return rows;
    }

    /// <summary>1つの項目の中から、時刻の付いた値をすべて取り出す。</summary>
    private static void ReadProperty(
        XElement prop, string itemType, string areaCode, string? areaName,
        Dictionary<string, DateTime> times, DateTime? reportedAt,
        List<ForecastRow> rows)
    {
        foreach (var el in XmlHelper.SelfAndDescendants(prop))
        {
            string? refId = el.Attribute("refID")?.Value;
            if (string.IsNullOrEmpty(refId)
                || !times.TryGetValue(refId, out var targetAt))
            {
                continue;
            }

            var target = ForecastSettings.ValueTags.Contains(XmlHelper.TagOf(el))
                ? el
                : ValueElement(el, itemType);

            if (target is null)
            {
                continue;
            }

            string condition = (target.Attribute("condition")?.Value ?? string.Empty)
                .Trim();
            string text = target.Value.Trim();

            // 「値なし」は予測が無いという意味なので保存しない。
            // 保存すると画面に空の行が並ぶだけになる。
            if (condition == "値なし" || text.Length == 0 || text == "なし")
            {
                continue;
            }

            decimal? value = null;
            string? valueText;

            if (decimal.TryParse(text, NumberStyles.Any,
                                 CultureInfo.InvariantCulture, out var num))
            {
                value = num;
                valueText = string.IsNullOrEmpty(condition) ? null : condition;
            }
            else
            {
                // 数値でないもの (「高」「中」など) は、そのまま文字で残す。
                valueText = text;
            }

            rows.Add(new ForecastRow
            {
                AreaCode = areaCode,
                AreaName = areaName,
                TargetAt = targetAt,
                ItemType = itemType,
                Value = value,
                ValueText = valueText,
                ReportedAt = reportedAt,
            });
        }
    }

    /// <summary>
    /// 予測の値が入っている要素を選ぶ。
    ///
    /// 電文には2つの形がある。
    ///   ・部分要素(refID付き)の中に値の要素が入っている形
    ///   ・値の要素そのものに refID が付いている形
    ///
    /// 最大風速は「陸上」と「海上」に分かれるため、陸上を採る。
    /// 足場は陸上にあるので、海上の値で判断すると実態と合わない。
    /// </summary>
    public static XElement? ValueElement(XElement part, string itemType)
    {
        if (itemType == "最大風速")
        {
            var locals = XmlHelper.SelfAndDescendants(part)
                .Where(l => XmlHelper.TagOf(l) == "Local")
                .ToList();

            var land = locals.FirstOrDefault(
                l => (XmlHelper.ChildText(l, "AreaName") ?? string.Empty) == "陸上");

            // 陸上が無ければ最初のものを使う。
            part = land ?? locals.FirstOrDefault() ?? part;
        }

        foreach (var c in XmlHelper.SelfAndDescendants(part))
        {
            if (ForecastSettings.ValueTags.Contains(XmlHelper.TagOf(c)))
            {
                return c;
            }
        }

        return null;
    }
}
