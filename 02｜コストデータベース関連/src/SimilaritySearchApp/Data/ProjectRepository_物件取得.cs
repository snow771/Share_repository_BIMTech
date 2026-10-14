using Microsoft.Data.SqlClient;
using SimilaritySearchApp.Models;
using SimilaritySearchApp.Services;
using static SimilaritySearchApp.Data.SqlConnectionFactory;

namespace SimilaritySearchApp.Data;

/// <summary>ProjectInformation_物件情報 の取得。構造種別の接頭辞（RC=J / S=I）で絞る。</summary>
public sealed class ProjectRepository
{
    public async Task<List<ProjectRecord>> LoadAsync(
        SqlConnection conn, TableNames tables, StructureType structure, int commandTimeoutSec, CancellationToken ct = default)
    {
        // 列の別名（日本語）は Python 版と同じ。全列を Attributes に保持して詳細表示に使う。
        var sql = $"""
            SELECT
                Project_id                                    AS project_id,
                ProjectName                                   AS 物件名,
                SiteAddress_State_Province                    AS 現場住所_都道府県,
                SiteAddress_City_Town_Village                 AS 現場住所_市区町村,
                SiteAddress_StreetName                        AS 現場住所_町名,
                ContractAmount                                AS 請負金額,
                ProjectedProfitatContractSigning              AS 契約時予定利益,
                ProfitMargin                                  AS 利益率,
                SiteArea                                      AS 敷地面積,
                BuildingFootprint                             AS 建築面積,
                LegalFloorArea                                AS 法廷床面積,
                ConstructionFloorArea                         AS 施工床面積,
                ConstructionTsubo                             AS 施工坪,
                ConstructionPeriod_Start                      AS [工期(着工)],
                ConstructionPeriod_Completion                 AS [工期(竣工)],
                Structure                                     AS 構造,
                NumberofFloors_AboveGround                    AS 階数_地上,
                NumberofFloors_BelowGround                    AS 階数_地下,
                [1R],[1K],[1DK],[1LDK],
                [2K],[2DK],[2LDK],
                [3K],[3DK],[3LDK],
                [4K],[4DK],[4LDK],
                [5K],[5DK],[5LDK],
                [1SK],[2SK],[1SLDK],[2SLDK],[3SLDK],
                [OR],
                Tenant                                        AS テナント,
                Store                                         AS 店舗,
                Office                                        AS 事務所,
                Other                                         AS その他,
                NumberofHouseholds                            AS 世帯数,
                Type                                          AS 形態,
                ELV,
                Concrete                                      AS [ｺﾝｸﾘｰﾄ],
                Formwork                                      AS 型枠,
                ReinforcingBars                               AS [鉄筋（Kg/m2）],
                SteelFrame                                    AS [鉄骨（Kg/m2）],
                ALC,
                ExteriorWallTiles                             AS 外壁タイル,
                RetainingWallConstruction                     AS 山留工法,
                PileWorkTruck                                 AS 杭工事,
                EarthworkTruck                                AS 土工事,
                Formwork_ReinforcingBarsTruck                 AS 型枠・鉄筋,
                SteelFrameTruck                               AS 鉄骨,
                ALCTruck                                      AS [ALC車両],
                Ready_MixConcreteTruck                        AS [生ｺﾝ車],
                PumpTruck                                     AS [ﾎﾟﾝﾌﾟ車]
            FROM {Quote(tables.Projects)}
            WHERE LEFT(CAST(Project_id AS NVARCHAR(50)), 1) = @prefix
            """;

        await using var cmd = new SqlCommand(sql, conn) { CommandTimeout = commandTimeoutSec };
        cmd.Parameters.AddWithValue("@prefix", structure.ProjectIdPrefix());

        var list = new List<ProjectRecord>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
        {
            var attrs = new Dictionary<string, object?>();
            for (var i = 0; i < r.FieldCount; i++)
            {
                var v = Value(r, i);
                attrs[r.GetName(i)] = v is string s ? TextNormalizer.Clean(s) : v;
            }

            object? A(string key) => attrs.TryGetValue(key, out var v) ? v : null;

            var form = ProjectFilterService.CleanForm(A("形態")?.ToString());
            attrs["形態"] = form;

            list.Add(new ProjectRecord
            {
                ProjectId = TextNormalizer.Clean(A("project_id")),
                Name = TextNormalizer.CleanOrNull(A("物件名")),
                Prefecture = TextNormalizer.CleanOrNull(A("現場住所_都道府県")),
                City = TextNormalizer.CleanOrNull(A("現場住所_市区町村")),
                Town = TextNormalizer.CleanOrNull(A("現場住所_町名")),
                Structure = TextNormalizer.CleanOrNull(A("構造")),
                Form = form,
                FloorsAbove = ToDouble(A("階数_地上")),
                FloorsBelow = ToDouble(A("階数_地下")),
                LegalFloorArea = ToDouble(A("法廷床面積")),
                ConstructionFloorArea = ToDouble(A("施工床面積")),
                Formwork = ToDouble(A("型枠")),
                RebarKgPerM2 = ToDouble(A("鉄筋（Kg/m2）")),
                CompletionDate = ToDate(A("工期(竣工)")),
                Attributes = attrs,
            });
        }
        return list;
    }
}
