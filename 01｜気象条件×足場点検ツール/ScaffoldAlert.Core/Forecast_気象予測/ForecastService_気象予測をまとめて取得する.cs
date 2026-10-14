using ScaffoldAlert.Core.Data;
using ScaffoldAlert.Core.Weather;

namespace ScaffoldAlert.Core.Forecast;

/// <summary>
/// 気象予測をまとめて取ってくる係。
///
/// ★この段階ではデータベースに書き込まない。
///   取ってきた内容を返すだけにして、Python版と突き合わせる。
///
/// 手順は Python版 (fetch_forecast.py) と同じ。
///   1. 区域の階層表を取る
///   2. 現場と予報区域を対応付ける
///   3. フィードから電文を選ぶ
///   4. 電文を読み解く
///   5. 風の予報を取る
/// </summary>
public class ForecastService
{
    private readonly IHttpClientFactoryLike _http;
    private readonly FeedReader _feed;
    private readonly WindTextFetcher _windText;
    private readonly SiteAreaRepository _sites;

    public ForecastService(IHttpClientFactoryLike http, FeedReader feed,
                           WindTextFetcher windText,
                           SiteAreaRepository sites)
    {
        _http = http;
        _feed = feed;
        _windText = windText;
        _sites = sites;
    }

    /// <summary>気象予測を取ってくる。</summary>
    public ForecastFetchResult Fetch()
    {
        var result = new ForecastFetchResult();

        // --- 1. 区域の階層表 ---
        AreaMap areaMap;
        try
        {
            areaMap = AreaMap.Load(_http);
            result.MunicipalityCount = areaMap.ByMunicipality.Count;
        }
        catch (Exception ex)
        {
            // ここが取れないと現場と区域を結べず、後が全部できない。
            result.Errors["区域の階層表"] = ex.Message;
            return result;
        }

        // --- 2. 現場と予報区域の対応 ---
        try
        {
            LinkSites(areaMap, result);
        }
        catch (Exception ex)
        {
            result.Errors["現場と区域の対応"] = ex.Message;
            return result;
        }

        if (result.Offices.Count == 0)
        {
            result.Errors["現場"] = "監視対象の現場がありません。"
                + "先に現場を登録してください。";
            return result;
        }

        // --- 3. フィードから電文を選ぶ ---
        List<FeedEntry> entries;
        try
        {
            entries = _feed.Read();
            result.FeedTotal = entries.Count;
        }
        catch (Exception ex)
        {
            result.Errors["フィード"] = ex.Message;
            return result;
        }

        // 自社の現場がある府県予報区のものだけに絞る。
        var wanted = entries
            .Where(e => result.Offices.Contains(e.OfficeCode))
            .ToList();
        result.FeedWanted = wanted.Count;

        // --- 4. 電文を読み解く ---
        foreach (var entry in wanted)
        {
            try
            {
                string xml = _http.GetString(entry.Url);
                result.Rows.AddRange(ForecastParser.Parse(xml));
            }
            catch (Exception ex)
            {
                // 1つの電文が読めなくても、残りは取り込む。
                result.Failed.Add($"{entry.OfficeCode} {entry.Title} {ex.Message}");
            }
        }

        // --- 5. 風の予報 ---
        // 気象庁は風の数値をほとんど発表しないため、
        // 府県天気予報の文章を手掛かりとして取り込む。
        foreach (var office in result.Offices.OrderBy(x => x))
        {
            try
            {
                result.WindTexts.AddRange(_windText.Fetch(office));
            }
            catch (Exception ex)
            {
                result.WindTextFailed.Add($"{office} {ex.Message}");
            }
        }

        return result;
    }

    /// <summary>
    /// 現場と予報区域を対応付ける。
    ///
    /// ★Python版は対応を T_SiteForecastArea に保存してから、
    ///   そこを読んで府県予報区を決めている。
    ///   この画面は書き込まないので、いま計算した対応から直接決める。
    ///   保存を挟まないだけで、結果は同じになる。
    /// </summary>
    private void LinkSites(AreaMap areaMap, ForecastFetchResult result)
    {
        var locations = _sites.LoadLocations();

        // 区域の形は重いので、必要になったときだけ読み込む。
        AreaShapes? shapes = null;

        foreach (var site in locations)
        {
            AreaEntry? hit = null;
            bool byShape = false;

            // まず市区町村コードから引く。
            if (!string.IsNullOrEmpty(site.MunicipalityCode))
            {
                string key = site.MunicipalityCode.Length >= 5
                    ? site.MunicipalityCode.Substring(0, 5)
                    : site.MunicipalityCode;
                areaMap.ByMunicipality.TryGetValue(key, out hit);
            }

            // 引けない現場は、緯度経度がどの区域の中にあるかで決める。
            // 住所の書き方(住居表示か地番か)に左右されない。
            if (hit is null && site.Latitude is not null
                && site.Longitude is not null)
            {
                shapes ??= AreaShapes.Load(_http);

                var found = shapes.AreaOfPoint((double)site.Longitude.Value,
                                               (double)site.Latitude.Value);
                if (found is not null)
                {
                    string code = found.Value.Code;

                    // 名前は区域の階層表のものを使う。区域の形に入っている
                    // 名前は「東京都」のような別表記で、他の現場と表示が
                    // 揃わなくなるため。
                    areaMap.AreaName.TryGetValue(code, out var name);

                    if (areaMap.AreaToOffice.TryGetValue(code, out var office))
                    {
                        hit = new AreaEntry(code, name, office.Code, office.Name);
                    }
                    else
                    {
                        // 府県予報区が引けないときは、区域コードの
                        // 先頭2桁から組み立てる (Python版と同じ扱い)。
                        hit = new AreaEntry(code, name,
                                            code.Substring(0, 2) + "0000", null);
                    }
                    byShape = true;
                }
            }

            if (hit is null)
            {
                // 対応が付かない現場は予測が出ない。黙って落とすと
                // 「あの現場だけ予測が出ない」と後から気づくことになる。
                result.UnknownSiteIds.Add(site.SiteId);
                continue;
            }

            result.Links.Add(new SiteAreaLink
            {
                SiteId = site.SiteId,
                AreaCode = hit.AreaCode,
                AreaName = hit.AreaName,
                OfficeCode = hit.OfficeCode,
                OfficeName = hit.OfficeName,
                ByShape = byShape,
            });

            // 府県予報区は、竣工していない現場のものだけを対象にする。
            // 竣工済み(CLOSED)の現場は点検義務が無いため。
            if (site.SiteStatus == "ACTIVE" || site.SiteStatus == "SUSPENDED")
            {
                result.Offices.Add(hit.OfficeCode);
            }
        }
    }
}
