using System.Text.Json;
using ScaffoldAlert.Core.Weather;

namespace ScaffoldAlert.Core.Forecast;

/// <summary>1つの区域。市区町村コードから引いた結果。</summary>
public record AreaEntry(string AreaCode, string? AreaName,
                        string OfficeCode, string? OfficeName);

/// <summary>
/// 市区町村コード(5桁) から 一次細分区域・府県予報区 を引ける表。
///
/// 気象庁の区域は 市区町村 → class15 → 一次細分区域 → 府県予報区 の
/// 親子関係になっている。現場側が持っているのは市区町村コード5桁なので、
/// そこから順にたどる。
///
/// 地震のときと同じで、市区町村コードは7桁で配信され、
/// その先頭5桁が現場側の5桁と一致する。
/// </summary>
public class AreaMap
{
    /// <summary>市区町村コード(5桁) → 区域。</summary>
    public Dictionary<string, AreaEntry> ByMunicipality { get; } = new();

    /// <summary>
    /// 一次細分区域コード → 府県予報区。
    /// 緯度経度で区域を決めたときに、その区域がどの府県予報区に
    /// 属するかを知る必要があるため。
    /// </summary>
    public Dictionary<string, (string Code, string? Name)> AreaToOffice { get; }
        = new();

    /// <summary>
    /// 一次細分区域コード → 区域名 (例 130010 → 東京地方)。
    ///
    /// 区域の形(GeoJSON)にも名前は入っているが、そちらは「東京都」のような
    /// 別の表記になっているため、こちらの名前を使う。
    /// 混ぜると、現場によって表示が揃わなくなる。
    /// </summary>
    public Dictionary<string, string?> AreaName { get; } = new();

    /// <summary>気象庁の区域の階層表を読んで、引ける形にする。</summary>
    public static AreaMap Load(IHttpClientFactoryLike http)
    {
        var map = new AreaMap();

        string json = http.GetString(JmaUrls.Area);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var c20 = Section(root, "class20s");
        var c15 = Section(root, "class15s");
        var c10 = Section(root, "class10s");
        var offices = Section(root, "offices");

        foreach (var muni in c20.EnumerateObject())
        {
            // 7桁で配信されるコードの、先頭5桁を鍵にする。
            string key = muni.Name.Length >= 5
                ? muni.Name.Substring(0, 5) : muni.Name;

            string? p15 = Parent(muni.Value);
            if (p15 is null || !c15.TryGetProperty(p15, out var e15))
            {
                continue;
            }

            string? p10 = Parent(e15);
            if (p10 is null || !c10.TryGetProperty(p10, out var e10))
            {
                continue;
            }

            string? office = Parent(e10);
            if (office is null || !offices.TryGetProperty(office, out var eOffice))
            {
                continue;
            }

            string? areaName = Name(e10);
            string? officeName = Name(eOffice);

            // 同じ5桁が複数あれば最初のものを採る。政令市の区などで
            // 起こりうるが、いずれも同じ一次細分区域に属するため実害はない。
            map.ByMunicipality.TryAdd(key,
                new AreaEntry(p10, areaName, office, officeName));

            map.AreaToOffice.TryAdd(p10, (office, officeName));
            map.AreaName.TryAdd(p10, areaName);
        }

        return map;
    }

    /// <summary>区域の階層表の中の1つの区分けを取り出す。</summary>
    private static JsonElement Section(JsonElement root, string name)
        => root.TryGetProperty(name, out var v)
           && v.ValueKind == JsonValueKind.Object
            ? v
            : default;

    /// <summary>親の区域のコードを取り出す。</summary>
    private static string? Parent(JsonElement element)
        => element.ValueKind == JsonValueKind.Object
           && element.TryGetProperty("parent", out var v)
           && v.ValueKind == JsonValueKind.String
            ? v.GetString()
            : null;

    /// <summary>区域の名前を取り出す。</summary>
    private static string? Name(JsonElement element)
        => element.ValueKind == JsonValueKind.Object
           && element.TryGetProperty("name", out var v)
           && v.ValueKind == JsonValueKind.String
            ? v.GetString()
            : null;
}
