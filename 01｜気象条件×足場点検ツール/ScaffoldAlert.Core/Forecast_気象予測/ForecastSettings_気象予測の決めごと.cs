namespace ScaffoldAlert.Core.Forecast;

/// <summary>
/// 気象予測の取得先と、取り込むものの決めごと。
///
/// ★ここで取り込むのは「予測」であり、点検義務そのものではない。
///   法令の点検義務は実際に悪天候が起きた後に生じるため、
///   予測を点検義務のアラートに入れてはいけない。
///   臨検で「点検していない事象」が並んで見えてしまう。
///
/// ★取得元について
///   気象庁には同じ内容の JSON 版
///   (bosai/probability/data/probability/*.json) もあるが、
///   2026-05-28 を最後に更新が止まっていることを確認したため使わない
///   (全府県が同じ日付で止まっており、Last-Modified も同日だった)。
///   防災情報XMLのフィードは同日時点でも更新され続けているため、
///   そちらから取る。
/// </summary>
public static class ForecastSettings
{
    /// <summary>
    /// 防災情報XMLのフィード。定時と随時の2本を見る。
    /// </summary>
    public static readonly string[] FeedUrls =
    {
        "https://www.data.jma.go.jp/developer/xml/feed/regular.xml",
        "https://www.data.jma.go.jp/developer/xml/feed/extra.xml",
    };

    /// <summary>
    /// 取り込む電文の種類。
    ///   警報級の可能性（明日まで）   … 数値の予測が入る (最大風速・最大雨量など)
    ///   早期注意情報（明後日まで）   … 同上
    ///   警報級の可能性（明後日以降） … 5日先までの「高」「中」
    /// </summary>
    public static readonly HashSet<string> WantTitles = new()
    {
        "警報級の可能性（明日まで）",
        "早期注意情報（明後日まで）",
        "警報級の可能性（明後日以降）",
    };

    /// <summary>
    /// 区域の形(ポリゴン)。市区町村コードを持たない現場のために使う。
    /// </summary>
    public const string AreaGeoJsonUrl =
        "https://www.jma.go.jp/bosai/common/const/geojson/class10s.json";

    /// <summary>
    /// 府県天気予報。風の予報(文章)がこちらに入っている。
    ///
    /// ★風の数値予測について (2026-08-28 に実際のデータで確認)
    ///   「警報級の可能性」の電文には最大風速の欄があるが、
    ///   全国25電文すべてで「値なし」だった。
    ///   「雨＝中」が出ている区域でも数値は入っていない。
    ///   つまり気象庁は、風の数値をほとんど発表していない。
    ///
    ///   数値が無いままだと風の手掛かりがゼロになるため、
    ///   府県天気予報の文章 (「西の風 やや強く」など) を取り込む。
    ///   数値ではないが、強さの目安にはなる。
    ///
    /// {0} に府県予報区コードを入れて使う。
    /// </summary>
    public const string ForecastUrl =
        "https://www.jma.go.jp/bosai/forecast/data/forecast/{0}.json";

    /// <summary>
    /// 風の予報を保存するときの項目名。気象庁の電文の項目名と混ざらないよう、
    /// こちらで決めた名前を使う。
    /// </summary>
    public const string WindTextItem = "風の予報";

    /// <summary>
    /// 画面に出す予測の種類。
    /// 波や潮位は足場に関係しないので取り込まない。
    /// </summary>
    public static readonly HashSet<string> KeepItems = new()
    {
        "最大風速",
        "１時間最大雨量", "３時間最大雨量", "２４時間最大雨量",
        "６時間最大降雪量", "２４時間最大降雪量",
        "風（風雪）の警報級の可能性", "雨の警報級の可能性", "雪の警報級の可能性",
    };

    /// <summary>
    /// 数値や階級が入っている要素の名前。
    /// </summary>
    public static readonly HashSet<string> ValueTags = new()
    {
        "WindSpeed", "Precipitation", "SnowfallDepth",
        "PossibilityRankOfWarning",
    };

    /// <summary>
    /// 画面と同じ基準。ここに達する予測がある区域を最後に表示する。
    /// </summary>
    public static readonly Dictionary<string, decimal> Limits = new()
    {
        ["最大風速"] = 10.0m,
        ["２４時間最大雨量"] = 50.0m,
        ["２４時間最大降雪量"] = 25.0m,
    };
}
