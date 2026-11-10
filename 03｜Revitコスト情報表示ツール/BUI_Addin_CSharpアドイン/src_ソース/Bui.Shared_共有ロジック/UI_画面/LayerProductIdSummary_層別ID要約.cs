// このファイルの目的:
//   選択要素に今入っている6つの層別Product_ID（仕上・下地①〜⑤）を、1行の要約にします。
//   BuiDockPaneViewModel.SelectedProductIdへ計算はしていますが、現在どのXAMLにも
//   バインドされておらず画面には出ません。層ごとのProduct_IDは概要タブの複合レイヤー一覧、
//   検索タブの層への割り当てで表示しています。
//
// なぜ単一の値を返さないのか:
//   旧・単一Product_IDパラメータは廃止され、値は6つの層別パラメータに分かれています。
//   1つだけを選んで返すと、利用者は残り5つの状態を見落とします。
//   そのため必ず6つ全部を、どのパラメータの値かが分かる形で並べます。
//
// 修正時の影響:
//   このファイルはRevitに依存しません。Revit無し環境でもテストで文面を固定してください。
namespace Bui.UI;

/// <summary>層別Product_IDの現在値を、1行の要約文にする純粋関数です。</summary>
public static class LayerProductIdSummary
{
    /// <summary>値が入っていない層の表示です。空白のままだと見落とされます。</summary>
    public const string UnsetText = "未設定";

    /// <param name="layerProductIds">
    /// Bui.Assemblies.LayerSlotNamesと同じ並び（仕上→下地①→…→下地⑤）で6件渡してください。
    /// 未設定の層はnullまたは空文字で構いません。
    /// </param>
    public static string Describe(IReadOnlyList<string?> layerProductIds)
    {
        ArgumentNullException.ThrowIfNull(layerProductIds);
        if (layerProductIds.Count != Bui.Assemblies.LayerSlotNames.Count)
        {
            throw new ArgumentException(
                $"layerProductIdsは{Bui.Assemblies.LayerSlotNames.Count}件必要です。実際: {layerProductIds.Count}件。",
                nameof(layerProductIds));
        }

        return string.Join(" / ", layerProductIds.Select((value, index) =>
        {
            var slotName = Bui.Assemblies.LayerSlotNames.Of((Bui.Assemblies.LayerSlot)index);
            var display = string.IsNullOrWhiteSpace(value) ? UnsetText : value.Trim();
            return $"{slotName}: {display}";
        }));
    }
}
