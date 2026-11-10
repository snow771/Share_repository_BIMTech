// このファイルの目的:
//   壁・床・天井・屋根1要素分の層構成を、部屋ごとの内部仕上表(InternalPlacements)と
//   部屋の定まらない外部仕上候補(ExternalPlacements)へ仕分けます。
//
// 規則の要点（詳細は仕上表出力の設計ドキュメント／task-2-brief.mdを参照）:
//   壁:
//     - 内側部屋名があれば、DepthFromInterior==0の層をその部屋の仕上とする（機能は問わない）。
//     - 外側部屋名があれば、反対端（最大深度）がFinish1/Finish2のときだけその部屋の仕上とする。
//     - 外側部屋名が空なら反対端は昇格させず、内側の下地として残す。
//     - 外側部屋名があるのに反対端を外側仕上として特定できない（層が無い/Product_IDが空/
//       Finish1でもFinish2でもない）場合は、Code="WALL_OUTER_FINISH_UNRESOLVED"の警告を1件出す
//       （その層は内側の下地として残るだけで、外側仕上が欠落したことが利用者に見えなくなるため）。
//     - 内外どちらも空なら、内部仕上表には入れず外部仕上候補（ExteriorFinish/ExteriorSubstrate）にする。
//     - 両側に部屋がある場合、仕上に採用しなかった残りの層は両方の部屋の下地へ重複して入れる
//       （同じ物理層を、内側からの下地列・外側からの下地列としてそれぞれ持つ）。
//   床・天井（横要素）:
//     - 天井・床部屋要素IDが非空なら深度0を仕上、残りを下地として内部配置にする。
//     - 空なら外部仕上候補にする。カテゴリ別の外部端は床=深度0、天井=最大深度。
//   屋根:
//     - 常に外部仕上候補。最大深度の仕上機能層をExteriorFinish、残りをExteriorSubstrateとする。
//   共通:
//     - Product_IDが空欄の層は配置にも警告にも一切含めない。
//     - input.LayerWarningが非空なら、Code="LAYER_OVERFLOW"の警告を1件Warningsへ追加する。
//     - 外部配置にも要素ID・カテゴリ・ファミリ名・タイプ名は常にinputからコピーする。
//
// 修正時の影響:
//   ここがずれると、仕上/下地の欄に誤った製品IDが入り、後続タスク（帳票組立）まで壊れます。
//   必ずテストで固定してから変更してください。

namespace Bui.FinishSchedules;

public static class FinishLayerClassifier
{
    private const string LayerOverflowWarningCode = "LAYER_OVERFLOW";

    /// <summary>壁の外側部屋名はあるが、反対端(最大深度)の層から外側仕上を特定できなかったときの警告コードです。</summary>
    public const string WallOuterFinishUnresolvedWarningCode = "WALL_OUTER_FINISH_UNRESOLVED";

    private static readonly NormalizedRoomName EmptyRoom = new(string.Empty, string.Empty);

    public static HostClassification Classify(HostFinishInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        var body = input.Kind switch
        {
            FinishHostKind.Wall => ClassifyWall(input),
            FinishHostKind.Floor => ClassifyHorizontal(input, FinishColumn.FloorFinish, FinishColumn.FloorSubstrate, exteriorDepthIsMax: false),
            FinishHostKind.Ceiling => ClassifyHorizontal(input, FinishColumn.CeilingFinish, FinishColumn.CeilingSubstrate, exteriorDepthIsMax: true),
            FinishHostKind.Roof => ClassifyExternalByFinishFunction(input),
            _ => throw new ArgumentOutOfRangeException(nameof(input.Kind), input.Kind, null)
        };

        // 分類本体(ClassifyWall等)が生成した固有の警告(例: WALL_OUTER_FINISH_UNRESOLVED)を
        // 上書きせず、共通のLAYER_OVERFLOW判定と合算する。
        return body with { Warnings = body.Warnings.Concat(BuildWarnings(input)).ToArray() };
    }

    private static bool HasProduct(FinishLayerInput layer) => !string.IsNullOrEmpty(layer.ProductId);

    private static bool IsFinishFunction(FinishLayerInput layer) => layer.Function is "Finish1" or "Finish2";

    private static ProductPlacement MakePlacement(
        HostFinishInput input, NormalizedRoomName room, FinishColumn column, FinishLayerInput layer) =>
        new(room.DisplayName, room.Key, column, layer.ProductId, layer.DepthFromInterior,
            input.ElementId, input.Category, input.FamilyName, input.TypeName);

    private static IReadOnlyList<FinishScheduleWarning> BuildWarnings(HostFinishInput input) =>
        string.IsNullOrEmpty(input.LayerWarning)
            ? Array.Empty<FinishScheduleWarning>()
            : new[] { new FinishScheduleWarning(LayerOverflowWarningCode, input.LayerWarning, input.ElementId) };

    // --- 壁 ---

    private static HostClassification ClassifyWall(HostFinishInput input)
    {
        var innerPresent = !string.IsNullOrEmpty(input.InnerRoomName);
        var outerPresent = !string.IsNullOrEmpty(input.OuterRoomName);

        if (!innerPresent && !outerPresent)
        {
            return ClassifyExternalByFinishFunction(input);
        }

        var layers = input.Layers;

        FinishLayerInput? innerFinish = null;
        if (innerPresent)
        {
            var candidate = layers.FirstOrDefault(l => l.DepthFromInterior == 0);
            if (candidate is not null && HasProduct(candidate))
            {
                innerFinish = candidate;
            }
        }

        FinishLayerInput? outerFinish = null;
        if (outerPresent && layers.Count > 0)
        {
            var maxDepth = layers.Max(l => l.DepthFromInterior);
            var candidate = layers.FirstOrDefault(l => l.DepthFromInterior == maxDepth);
            if (candidate is not null && HasProduct(candidate) && IsFinishFunction(candidate))
            {
                // 単層壁など、内側の深度0層と外側の最大深度層が同一物理層(Orderも同じ)になる場合でも、
                // 両側の部屋名が入力されていれば各側の規則を独立に適用し、同じ層を両方の仕上として採用する。
                // (consumedOrdersはHashSetのため重複追加されず、remainingの下地列にもこの層は含まれない)
                outerFinish = candidate;
            }
        }

        // 外側部屋名はあるのに、反対端(最大深度)の層が無い・Product_IDが空・Finish1/Finish2でない
        // いずれかの理由で外側仕上を特定できなかった場合、その層は下地(内側の下地列)へ残るだけで
        // 静かに欠落しているように見える。設計の警告表(壁の外側部屋名はあるが外側仕上を特定できない)
        // どおり、ここで警告を1件出す。
        var wallWarnings = outerPresent && outerFinish is null
            ? new[]
            {
                new FinishScheduleWarning(
                    WallOuterFinishUnresolvedWarningCode,
                    $"壁(ID:{input.ElementId})の外側部屋名「{input.OuterRoomName}」はありますが、外側の仕上層を特定できませんでした。",
                    input.ElementId)
            }
            : Array.Empty<FinishScheduleWarning>();

        var consumedOrders = new HashSet<int>();
        if (innerFinish is not null) consumedOrders.Add(innerFinish.Order);
        if (outerFinish is not null) consumedOrders.Add(outerFinish.Order);

        var remaining = layers.Where(HasProduct).Where(l => !consumedOrders.Contains(l.Order)).ToList();

        var placements = new List<ProductPlacement>();

        if (innerPresent)
        {
            var room = RoomNameNormalizer.NormalizeForRoomMatching(input.InnerRoomName);
            if (innerFinish is not null)
            {
                placements.Add(MakePlacement(input, room, FinishColumn.WallFinish, innerFinish));
            }
            // 内側部屋の面（深度0）から構造側へ向かう順序 = 深度昇順
            foreach (var layer in remaining.OrderBy(l => l.DepthFromInterior))
            {
                placements.Add(MakePlacement(input, room, FinishColumn.WallSubstrate, layer));
            }
        }

        if (outerPresent)
        {
            var room = RoomNameNormalizer.NormalizeForRoomMatching(input.OuterRoomName);
            if (outerFinish is not null)
            {
                placements.Add(MakePlacement(input, room, FinishColumn.WallFinish, outerFinish));
            }
            // 外側部屋の面（最大深度）から構造側へ向かう順序 = 深度降順
            foreach (var layer in remaining.OrderByDescending(l => l.DepthFromInterior))
            {
                placements.Add(MakePlacement(input, room, FinishColumn.WallSubstrate, layer));
            }
        }

        return new HostClassification(placements, Array.Empty<ProductPlacement>(), wallWarnings);
    }

    // 壁の内外両方空欄、および屋根で使う: 最大深度の仕上機能層をExteriorFinish、残りをExteriorSubstrateとする。
    private static HostClassification ClassifyExternalByFinishFunction(HostFinishInput input)
    {
        var candidates = input.Layers.Where(HasProduct).ToList();
        var finish = candidates.Where(IsFinishFunction)
            .OrderByDescending(l => l.DepthFromInterior)
            .FirstOrDefault();

        var placements = new List<ProductPlacement>();
        if (finish is not null)
        {
            placements.Add(MakePlacement(input, EmptyRoom, FinishColumn.ExteriorFinish, finish));
        }

        var remaining = candidates.Where(l => finish is null || l.Order != finish.Order)
            .OrderByDescending(l => l.DepthFromInterior);
        foreach (var layer in remaining)
        {
            placements.Add(MakePlacement(input, EmptyRoom, FinishColumn.ExteriorSubstrate, layer));
        }

        return new HostClassification(Array.Empty<ProductPlacement>(), placements, Array.Empty<FinishScheduleWarning>());
    }

    // --- 床・天井（横要素） ---

    private static HostClassification ClassifyHorizontal(
        HostFinishInput input, FinishColumn internalFinishColumn, FinishColumn internalSubstrateColumn, bool exteriorDepthIsMax)
    {
        if (!string.IsNullOrEmpty(input.HorizontalRoomName))
        {
            var room = RoomNameNormalizer.NormalizeForRoomMatching(input.HorizontalRoomName);
            var layers = input.Layers;

            var finish = layers.FirstOrDefault(l => l.DepthFromInterior == 0);
            if (finish is not null && !HasProduct(finish))
            {
                finish = null;
            }

            var placements = new List<ProductPlacement>();
            if (finish is not null)
            {
                placements.Add(MakePlacement(input, room, internalFinishColumn, finish));
            }

            var remaining = layers.Where(HasProduct)
                .Where(l => finish is null || l.Order != finish.Order)
                .OrderBy(l => l.DepthFromInterior);
            foreach (var layer in remaining)
            {
                placements.Add(MakePlacement(input, room, internalSubstrateColumn, layer));
            }

            return new HostClassification(placements, Array.Empty<ProductPlacement>(), Array.Empty<FinishScheduleWarning>());
        }

        return ClassifyHorizontalExternal(input, exteriorDepthIsMax);
    }

    // 天井・床部屋要素IDが空欄: カテゴリ別の外部端（床=深度0、天井=最大深度）を仕上、残りを下地とする。
    private static HostClassification ClassifyHorizontalExternal(HostFinishInput input, bool exteriorDepthIsMax)
    {
        var layers = input.Layers;

        FinishLayerInput? exteriorEnd = exteriorDepthIsMax
            ? (layers.Count > 0 ? layers.First(l => l.DepthFromInterior == layers.Max(x => x.DepthFromInterior)) : null)
            : layers.FirstOrDefault(l => l.DepthFromInterior == 0);

        if (exteriorEnd is not null && !HasProduct(exteriorEnd))
        {
            exteriorEnd = null;
        }

        var placements = new List<ProductPlacement>();
        if (exteriorEnd is not null)
        {
            placements.Add(MakePlacement(input, EmptyRoom, FinishColumn.ExteriorFinish, exteriorEnd));
        }

        var candidates = layers.Where(HasProduct).Where(l => exteriorEnd is null || l.Order != exteriorEnd.Order);
        var ordered = exteriorDepthIsMax
            ? candidates.OrderByDescending(l => l.DepthFromInterior)
            : candidates.OrderBy(l => l.DepthFromInterior);
        foreach (var layer in ordered)
        {
            placements.Add(MakePlacement(input, EmptyRoom, FinishColumn.ExteriorSubstrate, layer));
        }

        return new HostClassification(Array.Empty<ProductPlacement>(), placements, Array.Empty<FinishScheduleWarning>());
    }
}
