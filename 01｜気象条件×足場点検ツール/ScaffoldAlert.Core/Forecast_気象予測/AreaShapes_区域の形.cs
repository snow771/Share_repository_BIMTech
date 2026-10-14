using System.Text.Json;
using ScaffoldAlert.Core.Weather;

namespace ScaffoldAlert.Core.Forecast;

/// <summary>
/// 予報区域の形(多角形)。
///
/// 市区町村コードから区域を割り出せない現場のために使う。
/// 現場の緯度経度がどの区域の中にあるかで決められるので、
/// 住所の書き方(住居表示か地番か)に左右されない。
/// </summary>
public class AreaShapes
{
    /// <summary>多角形1つ。外枠と、その中の穴。</summary>
    private record Ring(double[][] Outer, List<double[][]> Holes);

    /// <summary>1つの区域。囲む四角と、多角形の集まり。</summary>
    private record Shape(string Code, string? Name,
                         double MinX, double MinY, double MaxX, double MaxY,
                         List<Ring> Rings);

    private readonly List<Shape> _shapes = new();

    /// <summary>読み込んだ区域の数。</summary>
    public int Count => _shapes.Count;

    /// <summary>区域の形を読み込む。</summary>
    public static AreaShapes Load(IHttpClientFactoryLike http)
    {
        var result = new AreaShapes();

        string json = http.GetString(ForecastSettings.AreaGeoJsonUrl);
        using var doc = JsonDocument.Parse(json);

        if (!doc.RootElement.TryGetProperty("features", out var features))
        {
            return result;
        }

        foreach (var feature in features.EnumerateArray())
        {
            string code = string.Empty;
            string? name = null;

            if (feature.TryGetProperty("properties", out var props))
            {
                if (props.TryGetProperty("code", out var c))
                {
                    code = c.ValueKind == JsonValueKind.String
                        ? c.GetString() ?? string.Empty
                        : c.ToString();
                }
                if (props.TryGetProperty("name", out var n)
                    && n.ValueKind == JsonValueKind.String)
                {
                    name = n.GetString();
                }
            }

            if (!feature.TryGetProperty("geometry", out var geometry)
                || geometry.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            string type = geometry.TryGetProperty("type", out var t)
                ? t.GetString() ?? string.Empty : string.Empty;

            if (!geometry.TryGetProperty("coordinates", out var coords))
            {
                continue;
            }

            // Polygon は多角形1つ、MultiPolygon は複数。
            // 扱いを揃えるため、Polygon も「1つだけの集まり」にする。
            List<JsonElement> polygons = new();
            if (type == "Polygon")
            {
                polygons.Add(coords);
            }
            else if (type == "MultiPolygon")
            {
                polygons.AddRange(coords.EnumerateArray());
            }
            else
            {
                continue;
            }

            var rings = new List<Ring>();
            double minX = double.MaxValue, minY = double.MaxValue;
            double maxX = double.MinValue, maxY = double.MinValue;

            foreach (var polygon in polygons)
            {
                var parts = polygon.EnumerateArray().ToList();
                if (parts.Count == 0)
                {
                    continue;
                }

                var outer = ToPoints(parts[0]);
                var holes = parts.Skip(1).Select(ToPoints).ToList();
                rings.Add(new Ring(outer, holes));

                foreach (var p in outer)
                {
                    if (p[0] < minX) { minX = p[0]; }
                    if (p[0] > maxX) { maxX = p[0]; }
                    if (p[1] < minY) { minY = p[1]; }
                    if (p[1] > maxY) { maxY = p[1]; }
                }
            }

            if (rings.Count > 0)
            {
                result._shapes.Add(new Shape(code, name,
                                             minX, minY, maxX, maxY, rings));
            }
        }

        return result;
    }

    /// <summary>座標の並びを、扱いやすい形に直す。</summary>
    private static double[][] ToPoints(JsonElement ring)
        => ring.EnumerateArray()
               .Select(p => p.EnumerateArray()
                             .Select(v => v.GetDouble()).ToArray())
               .Where(p => p.Length >= 2)
               .ToArray();

    /// <summary>
    /// 緯度経度がどの予報区域に入るかを返す。見つからなければ空。
    /// </summary>
    public (string Code, string? Name)? AreaOfPoint(double lon, double lat)
    {
        foreach (var shape in _shapes)
        {
            // 先に「囲む四角」で弾いて、計算を減らす。
            if (lon < shape.MinX || lon > shape.MaxX
                || lat < shape.MinY || lat > shape.MaxY)
            {
                continue;
            }

            foreach (var ring in shape.Rings)
            {
                if (!InRing(lon, lat, ring.Outer))
                {
                    continue;
                }

                // 穴(湖や飛び地の切り抜き)の中なら、その区域ではない。
                if (ring.Holes.Any(h => InRing(lon, lat, h)))
                {
                    continue;
                }

                return (shape.Code, shape.Name);
            }
        }

        return null;
    }

    /// <summary>
    /// 点が多角形の中にあるかを調べる (交差数判定)。
    ///
    /// 点から右へ線を伸ばし、辺と交わる回数が奇数なら内側。
    /// 外部の部品を増やさずに済ませるため、ここで持つ。
    /// </summary>
    public static bool InRing(double x, double y, double[][] ring)
    {
        bool inside = false;
        int n = ring.Length;
        int j = n - 1;

        for (int i = 0; i < n; i++)
        {
            double xi = ring[i][0], yi = ring[i][1];
            double xj = ring[j][0], yj = ring[j][1];

            if ((yi > y) != (yj > y))
            {
                if (x < (xj - xi) * (y - yi) / (yj - yi) + xi)
                {
                    inside = !inside;
                }
            }
            j = i;
        }

        return inside;
    }
}
