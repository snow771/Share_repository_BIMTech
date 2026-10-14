using ScaffoldAlert.Core.Weather;

namespace ScaffoldAlert.Core.Forecast;

/// <summary>
/// 区域の形(GeoJSON)を、一度取ってきたら覚えておく係。
///
/// このファイルは数MBあり、区域の形は年に何度も変わらない。
/// 画面を開くたびに気象庁へ取りに行くと、表示が遅くなるうえ
/// 気象庁のサーバーにも無駄な負担をかける。
///
/// アプリを起動している間だけ覚えておく。
/// 作り直したいときはアプリを再起動する。
/// </summary>
public class AreaGeoJsonCache
{
    // 覚えておく中身。まだ取っていなければ空。
    private string? _json;

    // 同時に何人が画面を開いても、取りに行くのは1回で済ませる。
    private readonly object _lock = new();

    /// <summary>
    /// 区域の形を返す。まだ取っていなければ取りに行く。
    ///
    /// 取りに行く道具は、持ち続けずに毎回渡してもらう。
    /// この係はアプリ全体で1つだけ作るため、道具を抱えたままにすると、
    /// 使い終わった繋ぎ口をいつまでも離さないことになる。
    /// </summary>
    public string Get(IHttpClientFactoryLike http)
    {
        // 既に持っていれば、鍵をかけずにそのまま返す。
        if (_json is not null)
        {
            return _json;
        }

        lock (_lock)
        {
            // 鍵を待っている間に、他の人が取ってきているかもしれない。
            _json ??= http.GetString(ForecastSettings.AreaGeoJsonUrl);
            return _json;
        }
    }
}
