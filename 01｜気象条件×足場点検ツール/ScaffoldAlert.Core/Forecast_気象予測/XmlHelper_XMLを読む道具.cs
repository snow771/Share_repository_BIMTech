using System.Globalization;
using System.Xml.Linq;

namespace ScaffoldAlert.Core.Forecast;

/// <summary>
/// 気象庁のXMLを読むための小さな道具。
///
/// 気象庁のXMLは要素ごとに名前空間が違い、そのまま指定すると
/// 読みにくくなるため、末尾の名前だけで扱う。
/// </summary>
public static class XmlHelper
{
    /// <summary>名前空間を外したタグ名。</summary>
    public static string TagOf(XElement element) => element.Name.LocalName;

    /// <summary>直下の子要素の文字列を返す。無ければ空。</summary>
    public static string? ChildText(XElement element, string name)
    {
        foreach (var child in element.Elements())
        {
            if (TagOf(child) == name)
            {
                return child.Value.Trim();
            }
        }
        return null;
    }

    /// <summary>
    /// 気象庁の日時表記 (2026-08-26T15:00:00+09:00) を日時にする。
    ///
    /// 時差の表記は捨てる。このシステムは日本国内だけを扱い、
    /// データベースにも日本時間で入れているため。
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

    /// <summary>その要素と、その下にあるすべての要素。</summary>
    public static IEnumerable<XElement> SelfAndDescendants(XElement element)
    {
        yield return element;
        foreach (var child in element.Descendants())
        {
            yield return child;
        }
    }
}
