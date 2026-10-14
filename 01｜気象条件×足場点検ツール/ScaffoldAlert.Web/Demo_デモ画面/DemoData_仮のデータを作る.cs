using ScaffoldAlert.Core.Data;
using ScaffoldAlert.Core.Forecast;
using ScaffoldAlert.Core.Judgement;

namespace ScaffoldAlert.Web.Demo;

/// <summary>
/// デモ画面で使う、仮のデータを作る係。
///
/// ★このファイルは本番では使わない
///   Demo_デモ画面 フォルダごと消せば、デモ画面はなくなる。
///   本物の画面は何も変わらない。
///   (消したあとは appsettings.json の Demo:Enabled を false にする)
///
/// ★なぜ必要か
///   このアプリは、データベースに気象データが溜まっていないと
///   何も表示されない。そのため
///     ・導入前の説明ができない
///     ・使い方を練習できない
///     ・晴れが続くとアラートが1件も出ず、画面を確かめられない
///   デモ画面は、この仮データで本物と同じ画面を開く。
///
/// ★大事な決まり
///   ここで作るのは、すべてプログラムの中の数字と文字。
///   データベースには一切つながらない。
///   デモ画面で何を押しても、本物の記録は増えも減りもしない。
///
/// ★時刻について
///   日付を固定で書くと、日が経つにつれて「過去の予測」になり、
///   画面から消えてしまう。そのため、いつも「いま」を基準に
///   ずらして作っている。何日後に見ても同じように動く。
/// </summary>
public static class DemoData
{
    // ============================================================
    //  仮の現場
    // ============================================================

    /// <summary>デモ用の現場1つぶん。</summary>
    /// <param name="Id">現場の番号。</param>
    /// <param name="Name">現場名。</param>
    /// <param name="Dept">部署。地図の色は部署ごとに決まっている。</param>
    /// <param name="Address">住所。</param>
    /// <param name="Manager">現場所長。</param>
    /// <param name="Lat">緯度。</param>
    /// <param name="Lon">経度。</param>
    /// <param name="AreaCode">気象庁の一次細分区域コード。</param>
    /// <param name="AreaLabel">その区域の名前。</param>
    public record DemoSite(int Id, string Name, string Dept, string Address,
                           string Manager, decimal Lat, decimal Lon,
                           string AreaCode, string AreaLabel);

    /// <summary>
    /// 仮の現場15件。
    ///
    /// ★部署の名前は、本物と同じものを使っている。
    ///   地図の色は部署名で決まっているので、
    ///   でたらめな名前にすると全部が灰色になってしまう。
    ///
    /// ★区域コードは気象庁の一次細分区域のもの。
    ///   本物の区域の形(地図の塗り)と重ねるために実在のものを使う。
    /// </summary>
    public static readonly List<DemoSite> Sites = new()
    {
        new(9001, "【デモ】東京駅前 新築工事", "ハウジング工事部",
            "東京都千代田区丸の内1-1", "田中 一郎",
            35.6812m, 139.7671m, "130010", "東京地方"),

        new(9002, "【デモ】品川 大規模改修", "リニューアル事業部",
            "東京都港区港南2-16", "佐藤 健二",
            35.6285m, 139.7387m, "130010", "東京地方"),

        new(9003, "【デモ】横浜みなとみらい 事務所棟", "総合建設工事部",
            "神奈川県横浜市西区みなとみらい2-3", "鈴木 三郎",
            35.4600m, 139.6317m, "140010", "神奈川県東部"),

        new(9004, "【デモ】川崎 工場改修", "解体工事部",
            "神奈川県川崎市川崎区殿町3-25", "高橋 四郎",
            35.5308m, 139.7029m, "140010", "神奈川県東部"),

        new(9005, "【デモ】さいたま新都心 商業施設", "ハウジング工事部",
            "埼玉県さいたま市中央区新都心11", "渡辺 五郎",
            35.8944m, 139.6317m, "110010", "埼玉県南部"),

        new(9006, "【デモ】幕張 物流倉庫", "総合建設工事部",
            "千葉県千葉市美浜区中瀬1-3", "伊藤 六郎",
            35.6489m, 140.0344m, "120010", "千葉県北西部"),

        new(9007, "【デモ】梅田 オフィスビル", "大阪建設",
            "大阪府大阪市北区梅田1-1", "山本 七海",
            34.7025m, 135.4959m, "270000", "大阪府"),

        new(9008, "【デモ】堺 プラント新設", "大阪建設",
            "大阪府堺市堺区匠町1", "中村 八重",
            34.5733m, 135.4831m, "270000", "大阪府"),

        new(9009, "【デモ】栄 複合ビル", "名古屋建設",
            "愛知県名古屋市中区栄3-5", "小林 九一",
            35.1681m, 136.9066m, "230010", "愛知県西部"),

        new(9010, "【デモ】清水 倉庫増築", "名古屋建設",
            "静岡県静岡市清水区日の出町9", "加藤 十実",
            35.0167m, 138.4894m, "220020", "静岡県中部"),

        new(9011, "【デモ】仙台駅東 マンション", "仙台建設",
            "宮城県仙台市宮城野区榴岡3-4", "吉田 十一",
            38.2606m, 140.8828m, "040010", "宮城県東部"),

        new(9012, "【デモ】天神 商業ビル改修", "福岡建設",
            "福岡県福岡市中央区天神2-1", "山田 十二",
            33.5904m, 130.3960m, "400010", "福岡県福岡地方"),

        new(9013, "【デモ】札幌北口 再開発", "総合建設工事部",
            "北海道札幌市北区北8条西3", "佐々木 十三",
            43.0686m, 141.3508m, "016010", "石狩地方"),

        new(9014, "【デモ】紙屋町 事務所改修", "リニューアル事業部",
            "広島県広島市中区紙屋町1-2", "松本 十四",
            34.3955m, 132.4596m, "340010", "広島県南部"),

        new(9015, "【デモ】万代 事務所新築", "仙台建設",
            "新潟県新潟市中央区万代1-6", "井上 十五",
            37.9161m, 139.0619m, "150010", "新潟県下越"),
    };

    /// <summary>現場を番号で探す。</summary>
    public static DemoSite? Find(int siteId)
        => Sites.FirstOrDefault(s => s.Id == siteId);

    // ============================================================
    //  ダッシュボード
    // ============================================================

    /// <summary>地図に出す現場。</summary>
    public static List<SiteMarker> SiteMarkers()
        => Sites.Select(s => new SiteMarker
        {
            SiteId = s.Id,
            SiteName = s.Name,
            Department = s.Dept,
            Address = s.Address,
            SiteStatus = "ACTIVE",
            ManagerName = s.Manager,
            StationCount = 3,
            Latitude = s.Lat,
            Longitude = s.Lon,
            AlertState = AlertStates().TryGetValue(s.Id, out var st)
                ? st : "なし",
        }).ToList();

    /// <summary>
    /// 仮のアラート。
    ///
    /// ★わざと色々な状態を混ぜている
    ///   ・事象     … 大雨・強風・暴風・地震
    ///   ・進み具合 … 未対応・指示済
    ///   ・経過時間 … 数時間前から数日前まで
    ///   こうしておかないと、一覧の並べ替えや絞り込みを試せない。
    /// </summary>
    public static List<DashboardAlert> Alerts()
    {
        var now = DateTime.Now;

        // (現場番号, 事象, 何時間前, 観測値, 進み具合)
        var seeds = new (int SiteId, string Event, int HoursAgo,
                         decimal? Value, string? Text, string Status)[]
        {
            (9001, "RAIN",       3, 62.5m,  null, "OPEN"),
            (9003, "WIND",       6, 12.4m,  null, "OPEN"),
            (9005, "GUST",      10, 33.8m,  null, "INSTRUCTED"),
            (9007, "RAIN",      20, 55.0m,  null, "INSTRUCTED"),
            (9009, "EARTHQUAKE", 30, null,   "4", "OPEN"),
            (9011, "SNOW",      44, 28.0m,  null, "OPEN"),
            (9012, "WIND",      52, 11.2m,  null, "INSTRUCTED"),
            (9014, "RAIN",      70, 58.5m,  null, "OPEN"),
        };

        var list = new List<DashboardAlert>();
        int id = 8001;

        foreach (var seed in seeds)
        {
            var site = Find(seed.SiteId);
            if (site is null)
            {
                continue;
            }

            list.Add(new DashboardAlert
            {
                AlertId = id++,
                SiteId = site.Id,
                SiteName = site.Name,
                Department = site.Dept,
                Address = site.Address,
                EventType = seed.Event,
                OccurredAt = now.AddHours(-seed.HoursAgo),
                TriggerValue = seed.Value,
                TriggerText = seed.Text,
                AlertStatus = seed.Status,
                ElapsedHours = seed.HoursAgo,
                ManagerName = site.Manager,
            });
        }

        // 発生が新しい順。本物の一覧と同じ並びにする。
        return list.OrderByDescending(a => a.OccurredAt).ToList();
    }

    /// <summary>現場ごとの対応状況。地図の色に使う。</summary>
    public static Dictionary<int, string> AlertStates()
    {
        var result = new Dictionary<int, string>();

        foreach (var a in Alerts())
        {
            string state = a.AlertStatus == "OPEN" ? "未対応" : "指示済";

            // 1つの現場に複数あるときは、重いほう(未対応)を残す。
            if (state == "未対応" || !result.ContainsKey(a.SiteId))
            {
                result[a.SiteId] = state;
            }
        }

        return result;
    }

    // ============================================================
    //  現場の詳細（地図の丸を押したときのモーダル）
    // ============================================================

    /// <summary>現場そのものの情報。</summary>
    public static SiteInfo? Info(int siteId)
    {
        var site = Find(siteId);
        if (site is null)
        {
            return null;
        }

        return new SiteInfo
        {
            SiteId = site.Id,
            SiteName = site.Name,
            Department = site.Dept,
            Address = site.Address,
            ManagerName = site.Manager,
            SiteStatus = "ACTIVE",
        };
    }

    /// <summary>
    /// その現場に紐付く観測所。
    ///
    /// 本物は距離の近い順に3つほど紐付く。
    /// デモでも同じ形にして、行を押すと推移が開くところまで試せるようにする。
    /// </summary>
    public static List<LinkedStation> Stations(int siteId)
    {
        var site = Find(siteId);
        if (site is null)
        {
            return new List<LinkedStation>();
        }

        // 現場名から都道府県のあたりを取り出す。
        string pref = site.Address.Length >= 3
            ? site.Address[..3] : site.Address;

        return new List<LinkedStation>
        {
            new() { StationId = $"D{siteId}1", StationName = "デモ第一観測所",
                    PrefName = pref, DistanceKm = 2.4m, IsNearest = true },
            new() { StationId = $"D{siteId}2", StationName = "デモ第二観測所",
                    PrefName = pref, DistanceKm = 7.8m, IsNearest = false },
            new() { StationId = $"D{siteId}3", StationName = "デモ第三観測所",
                    PrefName = pref, DistanceKm = 14.1m, IsNearest = false },
        };
    }

    /// <summary>
    /// その現場の観測値。
    ///
    /// アラートが出ている現場は、基準を超える値にしてある。
    /// そうしないと「なぜアラートなのか」が画面から読み取れない。
    /// </summary>
    public static SiteWeather Weather(int siteId)
    {
        var mine = Alerts().Where(a => a.SiteId == siteId).ToList();

        var weather = new SiteWeather
        {
            ObservedAt = DateTime.Now.AddMinutes(-20),
            Precip12h = 4.0m,
            Precip1h = 0.5m,
            MaxWindSpeed = 5.2m,
            MaxGustSpeed = 11.0m,
        };

        foreach (var a in mine)
        {
            switch (a.EventType)
            {
                case "RAIN":
                    weather.Precip12h = a.TriggerValue;
                    weather.Precip1h = 8.5m;
                    break;
                case "WIND":
                    weather.MaxWindSpeed = a.TriggerValue;
                    weather.MaxGustSpeed = 21.0m;
                    break;
                case "GUST":
                    weather.MaxGustSpeed = a.TriggerValue;
                    weather.MaxWindSpeed = 16.5m;
                    break;
            }
        }

        return weather;
    }

    /// <summary>その現場の市区町村で観測された震度。</summary>
    public static List<SiteQuake> Quakes(int siteId)
    {
        var quake = Alerts().FirstOrDefault(
            a => a.SiteId == siteId && a.EventType == "EARTHQUAKE");

        if (quake is null)
        {
            return new List<SiteQuake>();
        }

        return new List<SiteQuake>
        {
            new()
            {
                OccurredAt = quake.OccurredAt,
                Hypocenter = "デモ湾",
                Intensity = quake.TriggerText,
            },
        };
    }

    /// <summary>
    /// 観測所の、直近24時間の推移。
    ///
    /// 山を1つ作ってある。ずっと同じ値だと、
    /// 「いつ基準を超えたのか」を読み取る練習にならない。
    /// </summary>
    public static List<StationTrendRow> Trend(string stationId)
    {
        var rows = new List<StationTrendRow>();
        var start = DateTime.Now.AddHours(-23);

        for (int h = 0; h < 24; h++)
        {
            // 12時間前あたりを山にする。
            decimal 山 = Math.Max(0m, 12m - Math.Abs(h - 11));

            rows.Add(new StationTrendRow
            {
                ObservedAt = start.AddHours(h),
                Precip1h = Math.Round(山 * 0.9m, 1),
                Precip12h = Math.Round(山 * 5.2m, 1),
                MaxWindSpeed = Math.Round(3m + 山 * 0.8m, 1),
                MaxGustSpeed = Math.Round(6m + 山 * 1.9m, 1),

                // 雪は出さない。値が1つも無ければ画面から列ごと消える、
                // という本物の動きをそのまま見せるため。
                Snow24h = null,
            });
        }

        return rows;
    }

    // ============================================================
    //  アラート対応
    // ============================================================

    /// <summary>アラート1件の詳細。</summary>
    public static AlertDetail? Alert(int alertId)
    {
        var a = Alerts().FirstOrDefault(x => x.AlertId == alertId);
        if (a is null)
        {
            return null;
        }

        return new AlertDetail
        {
            AlertId = a.AlertId,
            SiteId = a.SiteId,
            SiteName = a.SiteName,
            Address = a.Address,
            ManagerName = a.ManagerName,
            EventType = a.EventType,
            OccurredAt = a.OccurredAt,
            TriggerValue = a.TriggerValue,
            TriggerText = a.TriggerText,
            AlertStatus = a.AlertStatus,
            LegalBasis = "労働安全衛生規則 第567条第2項",

            // 指示済のものには、指示の記録も入れておく。
            // 「①が済んでいると②の画面がどう見えるか」を試せる。
            InstructedAt = a.AlertStatus == "INSTRUCTED"
                ? a.OccurredAt.AddHours(1) : null,
            InstructedBy = a.AlertStatus == "INSTRUCTED" ? "デモ 太郎" : null,
            InstructionNote = a.AlertStatus == "INSTRUCTED"
                ? "所長へ電話連絡。翌朝の作業開始前に点検するよう指示。" : null,
        };
    }

    // ============================================================
    //  点検記録簿
    // ============================================================

    /// <summary>
    /// 点検記録簿に出す記録。
    ///
    /// ★4つの状態をそろえてある
    ///     未対応     … 指示すら出していない（薄い赤）
    ///     指示済     … 連絡は済み、点検結果待ち（薄い黄）
    ///     点検完了   … 済んでいる（白）
    ///     対象外     … 折りたたみの中
    ///   行の色分け・並べ替え・対象外の折りたたみ・詳細・編集を
    ///   ひととおり試せるようにするため。
    ///
    /// ★日付は「いま」を基準にずらしている
    ///   固定で書くと、日が経つにつれて期間の外へ出てしまい、
    ///   画面に何も出なくなる。
    /// </summary>
    public static List<AuditRow> AuditRows()
    {
        var rows = new List<AuditRow>();

        // ---------- ① まだ対応が終わっていないもの ----------
        // ダッシュボードに出ているアラートと同じもの。
        // 2つの画面で同じ現場が同じ状態に見えるようにする。
        foreach (var a in Alerts())
        {
            rows.Add(new AuditRow
            {
                AlertId = a.AlertId,
                SiteId = a.SiteId,
                SiteName = a.SiteName,
                Address = a.Address,
                EventType = a.EventType,
                OccurredAt = a.OccurredAt,
                TriggerValue = a.TriggerValue,
                TriggerText = a.TriggerText,
                LegalBasis = "労働安全衛生規則 第567条第2項",
                AlertStatus = a.AlertStatus,

                // 指示済のものには、指示の記録も入れておく。
                InstructedAt = a.AlertStatus == "INSTRUCTED"
                    ? a.OccurredAt.AddHours(1) : null,
                InstructedBy = a.AlertStatus == "INSTRUCTED"
                    ? "デモ 太郎" : null,
                InstructionNote = a.AlertStatus == "INSTRUCTED"
                    ? "所長へ電話連絡。翌朝の作業開始前に点検するよう指示。"
                    : null,
            });
        }

        // ---------- ② 点検が終わっているもの ----------
        // (現場番号, 事象, 何日前, 観測値, 点検までの日数, 結果, 指摘)
        var done = new (int SiteId, string Event, int DaysAgo, decimal Value,
                        int ToInspect, string Result, string? Finding)[]
        {
            (9002, "RAIN",  5, 57.5m, 1, "OK", null),
            (9004, "WIND",  8, 13.2m, 1, "NG_FIXED",
             "北面の手すり1か所に緩み。その場で増し締めを実施。"),
            (9006, "RAIN", 12, 51.0m, 2, "OK", null),
            (9008, "GUST", 15, 31.4m, 1, "NG_FIXED",
             "メッシュシート2枚の結束が外れていたため再結束。"),
            (9010, "RAIN", 22, 68.0m, 1, "OK", null),
            (9013, "SNOW", 30, 27.0m, 3, "NG_OPEN",
             "階段部の踏板に変形。使用禁止の表示を行い、"
             + "部材の手配中（次回入荷 未定）。"),
            (9015, "WIND", 40, 10.8m, 2, "OK", null),
        };

        int id = 8100;
        var now = DateTime.Now;

        foreach (var d in done)
        {
            var site = Find(d.SiteId);
            if (site is null)
            {
                continue;
            }

            var occurred = now.AddDays(-d.DaysAgo);
            var inspected = occurred.AddDays(d.ToInspect);

            rows.Add(new AuditRow
            {
                AlertId = id++,
                SiteId = site.Id,
                SiteName = site.Name,
                Address = site.Address,
                EventType = d.Event,
                OccurredAt = occurred,
                TriggerValue = d.Value,
                LegalBasis = "労働安全衛生規則 第567条第2項",
                AlertStatus = "COMPLETED",

                InstructedAt = occurred.AddHours(1),
                InstructedBy = "デモ 太郎",
                InstructionNote = "所長へ電話連絡。作業開始前の点検を指示。",

                InspectedAt = inspected,
                InspectorName = site.Manager,
                InspectorQualify = "足場の組立て等作業主任者",
                ScaffoldType = "くさび緊結式足場",
                InspectionResult = d.Result,
                FindingNote = d.Finding,
                DaysToInspection = d.ToInspect,
                RegisteredBy = "デモ 花子",

                InspectionCreatedAt = inspected.AddHours(2),

                // 1件だけ「あとから直した」ものを混ぜてある。
                // 詳細に「登録のあとで直されています」と出る動きを
                // 確かめられるようにするため。
                InspectionUpdatedAt = d.Result == "NG_OPEN"
                    ? inspected.AddDays(2)
                    : inspected.AddHours(2),
            });
        }

        // ---------- ③ 対象外にしたもの ----------
        // (現場番号, 事象, 何日前, 観測値, 理由)
        var skipped = new (int SiteId, string Event, int DaysAgo,
                           decimal Value, string Why)[]
        {
            (9005, "RAIN", 18, 53.0m,
             "足場は事象発生の前日に解体済み。作業日報で確認。"),
            (9009, "WIND", 25, 11.5m,
             "動作確認のために登録した現場のため、実在しない。"),
            (9011, "RAIN", 35, 55.5m,
             "当該区画は屋内工事のみで、外部足場は設置していない。"),
        };

        foreach (var s in skipped)
        {
            var site = Find(s.SiteId);
            if (site is null)
            {
                continue;
            }

            var occurred = now.AddDays(-s.DaysAgo);

            rows.Add(new AuditRow
            {
                AlertId = id++,
                SiteId = site.Id,
                SiteName = site.Name,
                Address = site.Address,
                EventType = s.Event,
                OccurredAt = occurred,
                TriggerValue = s.Value,
                LegalBasis = "労働安全衛生規則 第567条第2項",
                AlertStatus = "NOT_REQUIRED",

                InstructedAt = occurred.AddHours(3),
                InstructedBy = "デモ 次郎",

                // 対象外の理由は「[対象外] 」を頭に付けて保存する決まり。
                // 本物と同じ形にしておかないと、画面での取り出し方が変わる。
                InstructionNote = "[対象外] " + s.Why,
            });
        }

        // 発生が新しい順。本物の点検記録簿と同じ並びにする。
        return rows.OrderByDescending(r => r.OccurredAt).ToList();
    }

    /// <summary>点検記録簿の1行を、番号で探す。</summary>
    /// <remarks>
    /// 名前を FindAuditRow にしているのは、型名 AuditRow と
    /// 同じ名前のメソッドを作ると、C# がどちらか判断できなくなるため。
    /// </remarks>
    public static AuditRow? FindAuditRow(int alertId)
        => AuditRows().FirstOrDefault(r => r.AlertId == alertId);

    /// <summary>
    /// 「記録を直す」画面に出すアラート。
    ///
    /// 点検記録簿から開くので、記録簿にある全部が対象になる。
    /// ダッシュボードのアラート (Alert) より広い。
    /// </summary>
    public static AlertDetail? EditAlert(int alertId)
    {
        var r = FindAuditRow(alertId);
        if (r is null)
        {
            return null;
        }

        return new AlertDetail
        {
            AlertId = r.AlertId,
            SiteId = r.SiteId,
            SiteName = r.SiteName,
            Address = r.Address,
            ManagerName = Find(r.SiteId)?.Manager,
            EventType = r.EventType,
            OccurredAt = r.OccurredAt,
            TriggerValue = r.TriggerValue,
            TriggerText = r.TriggerText,
            AlertStatus = r.AlertStatus,
            LegalBasis = r.LegalBasis,
            InstructedAt = r.InstructedAt,
            InstructedBy = r.InstructedBy,
            InstructionNote = r.InstructionNote,
        };
    }

    /// <summary>「記録を直す」画面に出す点検記録。無ければ空。</summary>
    public static InspectionDetail? EditInspection(int alertId)
    {
        var r = FindAuditRow(alertId);
        if (r?.InspectedAt is null)
        {
            return null;
        }

        return new InspectionDetail
        {
            InspectionId = alertId,
            AlertId = alertId,
            InspectedAt = r.InspectedAt.Value,
            InspectorName = r.InspectorName ?? string.Empty,
            InspectorQualify = r.InspectorQualify,
            ScaffoldType = r.ScaffoldType,
            InspectionResult = r.InspectionResult ?? "OK",
            FindingNote = r.FindingNote,
            RegisteredBy = r.RegisteredBy ?? string.Empty,
            CreatedAt = r.InspectionCreatedAt ?? r.InspectedAt.Value,
            UpdatedAt = r.InspectionUpdatedAt ?? r.InspectedAt.Value,
        };
    }

    // ============================================================
    //  気象予測
    // ============================================================

    /// <summary>判定と同じ基準の数字。</summary>
    public static Dictionary<string, Threshold> Limits() => new()
    {
        ["RAIN"] = new Threshold("RAIN", "大雨", 50m, null, 12,
                                 "労働安全衛生規則 第567条第2項"),
        ["WIND"] = new Threshold("WIND", "強風", 10m, null, null,
                                 "労働安全衛生規則 第567条第2項"),
        ["GUST"] = new Threshold("GUST", "暴風", 30m, null, null,
                                 "労働安全衛生規則 第567条第2項"),
        ["SNOW"] = new Threshold("SNOW", "大雪", 25m, null, 24,
                                 "労働安全衛生規則 第567条第2項"),
        ["EARTHQUAKE"] = new Threshold("EARTHQUAKE", "中震以上", null, "4",
                                       null, "労働安全衛生規則 第567条第2項"),
    };

    /// <summary>
    /// 仮の予測。
    ///
    /// ★中身
    ///   ・警報級の可能性 … 6時間ごとの区切りで【高】【中】
    ///   ・風の予報       … 今日・明日・明後日 の3つ（文章）
    ///   本物の気象庁のデータと同じ形にしてある。
    ///
    /// ★時間の作り方
    ///   いまが何時でも意味が通るよう、「いまが入っている6時間の区切り」
    ///   から先へ並べている。過ぎた区切りは画面に出ない決まりなので、
    ///   固定の日付で書くと翌日には何も出なくなる。
    /// </summary>
    public static List<SiteForecast> Forecasts()
    {
        var now = DateTime.Now;

        // いまが入っている6時間の区切りの先頭 (0時 / 6時 / 12時 / 18時)。
        var bucket = new DateTime(now.Year, now.Month, now.Day,
                                  now.Hour / 6 * 6, 0, 0);

        // 今日の0時。風の予報は1日ごとなので、こちらを基準にする。
        var today = now.Date;

        // 区域ごとの筋書き。
        //   雨 … (何区切り先, 高中) の並び
        //   風 … 今日・明日・明後日の文章
        var 筋書き = new Dictionary<string, (
            (int Step, string Level)[] Rain,
            (int Step, string Level)[] Snow,
            string[] Wind)>
        {
            // 首都圏 … 雨が強まっていく筋書き
            ["130010"] = (new[] { (0, "中"), (1, "高"), (2, "高"), (3, "中") },
                          Array.Empty<(int, string)>(),
                          new[] { "北の風 後 南の風 やや強く",
                                  "南の風 強く 海上 では 非常に強く",
                                  "南の風 やや強く" }),

            ["140010"] = (new[] { (1, "中"), (2, "高") },
                          Array.Empty<(int, string)>(),
                          new[] { "北東の風", "南の風 やや強く",
                                  "南の風 やや強く" }),

            ["110010"] = (new[] { (1, "中"), (2, "中") },
                          Array.Empty<(int, string)>(),
                          new[] { "北の風", "南の風 やや強く", "北の風" }),

            ["120010"] = (new[] { (2, "中") },
                          Array.Empty<(int, string)>(),
                          new[] { "北東の風 やや強く",
                                  "南の風 海上 では 強く", "南の風" }),

            // 東海・関西 … 風が主役の筋書き
            ["230010"] = (Array.Empty<(int, string)>(),
                          Array.Empty<(int, string)>(),
                          new[] { "西の風 やや強く", "西の風 強く", "西の風" }),

            ["220020"] = (new[] { (3, "中") },
                          Array.Empty<(int, string)>(),
                          new[] { "西の風 海上 では やや強く",
                                  "西の風 非常に強く", "西の風 やや強く" }),

            ["270000"] = (new[] { (2, "中"), (3, "高") },
                          Array.Empty<(int, string)>(),
                          new[] { "西の風", "西の風 やや強く", "西の風" }),

            ["340010"] = (new[] { (0, "高"), (1, "中") },
                          Array.Empty<(int, string)>(),
                          new[] { "西の風 やや強く", "西の風", "西の風" }),

            // 北・日本海側 … 雪の筋書き
            ["040010"] = (Array.Empty<(int, string)>(),
                          new[] { (2, "中"), (3, "中") },
                          new[] { "北西の風 やや強く", "北西の風 強く",
                                  "北西の風 やや強く" }),

            ["016010"] = (Array.Empty<(int, string)>(),
                          new[] { (1, "高"), (2, "高"), (3, "中") },
                          new[] { "北西の風 強く", "北西の風 非常に強く",
                                  "北西の風 やや強く" }),

            ["150010"] = (Array.Empty<(int, string)>(),
                          new[] { (2, "中") },
                          new[] { "北西の風 やや強く", "北西の風 やや強く",
                                  "北西の風" }),

            ["400010"] = (new[] { (3, "中") },
                          Array.Empty<(int, string)>(),
                          new[] { "北の風", "北の風 やや強く", "北の風" }),
        };

        var rows = new List<SiteForecast>();
        var reportedAt = now.AddHours(-1);

        foreach (var site in Sites)
        {
            if (!筋書き.TryGetValue(site.AreaCode, out var 話))
            {
                continue;
            }

            void 足す(DateTime at, string item, string? text)
            {
                rows.Add(new SiteForecast
                {
                    SiteId = site.Id,
                    SiteName = site.Name,
                    Department = site.Dept,
                    AreaLabel = site.AreaLabel,
                    AreaCode = site.AreaCode,
                    ItemType = item,
                    TargetAt = at,
                    Value = null,
                    ValueText = text,
                    ReportedAt = reportedAt,
                });
            }

            foreach (var (step, level) in 話.Rain)
            {
                足す(bucket.AddHours(step * 6),
                     ForecastBoard.KindItem["雨"], level);
            }

            foreach (var (step, level) in 話.Snow)
            {
                足す(bucket.AddHours(step * 6),
                     ForecastBoard.KindItem["雪"], level);
            }

            for (int day = 0; day < 話.Wind.Length; day++)
            {
                足す(today.AddDays(day),
                     ForecastSettings.WindTextItem, 話.Wind[day]);
            }
        }

        return rows;
    }

    /// <summary>1つの現場ぶんの予測。</summary>
    public static List<SiteForecast> ForSite(int siteId)
        => Forecasts().Where(f => f.SiteId == siteId).ToList();

    /// <summary>気象予測の地図に出す現場。</summary>
    public static List<ForecastSiteMarker> ForecastMarkers()
        => Sites.Select(s => new ForecastSiteMarker
        {
            SiteId = s.Id,
            SiteName = s.Name,
            Department = s.Dept,
            Address = s.Address,
            ManagerName = s.Manager,
            AreaCode = s.AreaCode,
            Latitude = s.Lat,
            Longitude = s.Lon,
        }).ToList();

    /// <summary>
    /// 部署ごとの、区域との紐付き具合。
    ///
    /// デモではすべて紐付いている形にする。
    /// 紐付いていない件数の見方は、本物の画面で確かめてもらう。
    /// </summary>
    public static List<ForecastCoverage> Coverage()
        => Sites.GroupBy(s => s.Dept)
                .OrderBy(g => g.Key)
                .Select(g => new ForecastCoverage(g.Key, g.Count(), 0))
                .ToList();
}
