namespace ScaffoldAlert.Core.Dashboard;

/// <summary>
/// 画面に出すときの言葉と色をまとめた表。
///
/// データベースには英語の記号 (RAIN / OPEN など) で入っている。
/// 画面には日本語で出したいので、ここで対応を持つ。
/// 1か所にまとめておくと、言い回しを変えたいときにここだけ直せば済む。
/// </summary>
public static class DisplaySettings
{
    /// <summary>事象の呼び名。</summary>
    public static readonly Dictionary<string, string> EventLabel = new()
    {
        ["RAIN"] = "大雨",
        ["WIND"] = "強風",
        ["GUST"] = "暴風",
        ["SNOW"] = "大雪",
        ["EARTHQUAKE"] = "地震",
    };

    /// <summary>
    /// アラートに出す値の単位。地震は震度なので単位を付けない。
    /// </summary>
    public static readonly Dictionary<string, string> EventUnit = new()
    {
        ["RAIN"] = "mm",
        ["WIND"] = "m/s",
        ["GUST"] = "m/s",
        ["SNOW"] = "cm",
        ["EARTHQUAKE"] = "",
    };

    /// <summary>
    /// アラートの進み具合。
    /// OPEN(未対応) → INSTRUCTED(指示済) → COMPLETED(点検完了) の順に進む。
    /// NOT_REQUIRED は足場撤去済み等で点検不要と判断した場合。
    /// </summary>
    public static readonly Dictionary<string, string> StatusLabel = new()
    {
        ["OPEN"] = "未対応",
        ["INSTRUCTED"] = "指示済",
        ["COMPLETED"] = "点検完了",
        ["NOT_REQUIRED"] = "対象外",
    };

    /// <summary>
    /// 部署ごとの地図の丸の色。
    ///
    /// 色は「部署名」に固定で割り当てる。絞り込みで表示件数が変わっても
    /// 色が入れ替わらないようにするため、件数順などで動的に決めてはいけない。
    ///
    /// 注意: 同じ地図上で確実に見分けられる色は4色までで、8色では
    /// 赤とオレンジ、緑とオレンジが判別できない。色だけが手がかりに
    /// ならないよう、丸の吹き出しに必ず部署名を出している。
    /// </summary>
    public static readonly Dictionary<string, string> DepartmentColor = new()
    {
        ["ハウジング工事部"] = "#2a78d6",
        ["総合建設工事部"] = "#eb6834",
        ["名古屋建設"] = "#1baf7a",
        ["解体工事部"] = "#eda100",
        ["リニューアル事業部"] = "#e87ba4",
        ["福岡建設"] = "#008300",
        ["大阪建設"] = "#4a3aa7",
        ["仙台建設"] = "#e34948",
    };

    /// <summary>上の表にない部署・未設定のときの色。</summary>
    public const string OtherColor = "#898781";

    /// <summary>
    /// アラート状況で塗り分けるときの色。
    ///
    /// 地図の色は「部署」か「アラート状況」のどちらか一方しか表せない。
    /// 553点が部署色で塗られていると数点のアラート現場が埋もれるため、
    /// 既定はアラート状況にしている。
    /// </summary>
    public static readonly Dictionary<string, string> AlertColor = new()
    {
        // ★一覧の行の色と同じ考え方にそろえてある
        //     未対応 … 赤   指示すら出していない。いちばん急ぐ
        //     指示済 … 青   連絡は済み、点検結果を待っている
        //     なし   … 灰
        //
        //   赤と黄は隣り合った色で、小さな丸にすると見分けにくい。
        //   とくに色覚によっては、ほとんど同じに見える。
        //   赤と青なら、どんな見え方でも対比がはっきりする。
        //
        //   行の色は薄い色 (dashboard_ダッシュボード.css)、
        //   地図の丸は濃い色。地図は小さいので、薄いと見えない。
        ["未対応"] = "#e34948",
        ["指示済"] = "#2f86cf",
        ["なし"] = "#898781",
    };

    /// <summary>
    /// 背景地図。地理院タイル(淡色地図)を使う。
    ///
    /// OpenStreetMap の既定のタイルは使わない。
    /// ボランティア運営の共有資源であり、業務システムからの
    /// 継続的な利用は規約上推奨されていないため。
    /// </summary>
    public const string TileUrl =
        "https://cyberjapandata.gsi.go.jp/xyz/pale/{z}/{x}/{y}.png";

    /// <summary>
    /// 地図に出す出典表示。地理院タイルの利用条件のため必須。
    /// </summary>
    public const string TileAttribution =
        "<a href=\"https://maps.gsi.go.jp/development/ichiran.html\">"
        + "地理院タイル（国土地理院）</a>";

    /// <summary>事象の呼び名を引く。分からないものはそのまま返す。</summary>
    public static string Event(string? code)
        => code is not null && EventLabel.TryGetValue(code, out var v) ? v
           : code ?? string.Empty;

    /// <summary>単位を引く。</summary>
    public static string Unit(string? code)
        => code is not null && EventUnit.TryGetValue(code, out var v) ? v
           : string.Empty;

    /// <summary>状況の呼び名を引く。</summary>
    public static string Status(string? code)
        => code is not null && StatusLabel.TryGetValue(code, out var v) ? v
           : code ?? string.Empty;

    /// <summary>部署の色を引く。表にない部署は共通の色にする。</summary>
    public static string Department(string? name)
        => name is not null && DepartmentColor.TryGetValue(name, out var v) ? v
           : OtherColor;
}
