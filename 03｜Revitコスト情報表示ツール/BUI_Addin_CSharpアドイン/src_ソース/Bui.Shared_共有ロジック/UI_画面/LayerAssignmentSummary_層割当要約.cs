// このファイルの目的:
//   層ごとのProduct_IDを書き込む前に、利用者が唯一目にする確認文を組み立てます。
//
// なぜこの文が重要か:
//   「複合構造のどちら端が室内側か」はRevitのある環境でしか検証できません（設計 §3）。
//   判定が反転していると、仕上のIDが最も外側の層へ書き込まれ、
//   モデルを見ても間違いに気づけません。
//   この確認文に「どのパラメータへ、どのIDが、どの材料の層として入るか」を並べることが、
//   その事故に対する唯一の歯止めです。
//
// なぜRevitを参照しない場所に置くのか:
//   Revitが無い環境でも文面をテストで固定できるようにするためです。
//   ここが崩れると、誤りが画面に出たまま誰も気づけません。
//
// 修正時の影響:
//   行からパラメータ名・材料名・厚さのいずれかを落とすと、
//   利用者は書き込み先を確認できないまま実行することになります。
using System.Globalization;
using System.Text;

namespace Bui.UI;

/// <summary>書込み前の確認文を組み立てる純粋関数です。</summary>
public static class LayerAssignmentSummary
{
    /// <summary>パラメータ名を揃えるための桁数です。最長は「Product_ID（下地①）」の15文字です。</summary>
    private const int LabelWidth = 16;

    /// <summary>値が選ばれていない層の表示です。空白のままだと見落とされます。</summary>
    public const string UnselectedText = "（未選択）";

    /// <summary>Assembly_IDを付けない場合の表示です。</summary>
    public const string NoAssemblyIdText = "（付与しません）";

    public static string DescribeTarget(ProductAssignmentTarget target) => target switch
    {
        ProductAssignmentTarget.SameTypeAll => "同一タイプすべて",
        ProductAssignmentTarget.SelectionOnly => "選択部材のみ",
        _ => "不明な範囲"
    };

    /// <param name="rows">表示する層の行。スロット順（室内側から外側）に並べて渡してください。</param>
    /// <param name="assemblyId">採番したAssembly_ID。付けない場合は空文字。</param>
    /// <param name="assemblyIdSkipReason">Assembly_IDを付けない理由。付ける場合は空文字。</param>
    /// <param name="layerWarning">6つの箱に収まらなかった層の説明。無ければ空文字。</param>
    public static string Describe(
        int targetCount,
        ProductAssignmentTarget target,
        IReadOnlyList<LayerAssignmentRow> rows,
        string assemblyId,
        string assemblyIdSkipReason,
        string layerWarning)
    {
        ArgumentNullException.ThrowIfNull(rows);

        var builder = new StringBuilder();
        builder.Append("対象: ")
            .Append(targetCount.ToString(CultureInfo.InvariantCulture))
            .Append("件（")
            .Append(DescribeTarget(target))
            .Append("）");

        foreach (var row in rows.OrderBy(item => (int)item.Slot))
        {
            builder.Append(Environment.NewLine).Append(DescribeRow(row));
        }

        builder.Append(Environment.NewLine)
            .Append(Pad("Assembly_ID"))
            .Append(": ")
            .Append(string.IsNullOrWhiteSpace(assemblyId) ? NoAssemblyIdText : assemblyId.Trim());

        if (!string.IsNullOrWhiteSpace(layerWarning))
        {
            builder.Append(Environment.NewLine).Append("※ ").Append(layerWarning.Trim());
        }

        if (!string.IsNullOrWhiteSpace(assemblyIdSkipReason))
        {
            builder.Append(Environment.NewLine).Append("※ ").Append(assemblyIdSkipReason.Trim());
        }

        return builder.ToString();
    }

    /// <summary>
    /// 1行に「書き込み先のパラメータ名」「書き込む値」「その層の実体」を必ず並べます。
    /// 材料名と厚さが無いと、室内側の判定が反転していても利用者は気づけません。
    /// </summary>
    private static string DescribeRow(LayerAssignmentRow row)
    {
        ArgumentNullException.ThrowIfNull(row);

        var value = string.IsNullOrWhiteSpace(row.SelectedProductId)
            ? UnselectedText
            : row.SelectedProductId.Trim();
        var line = new StringBuilder()
            .Append(Pad(row.SlotName))
            .Append(": ")
            .Append(value);

        line.Append("  ← 層順")
            .Append(row.Order.ToString(CultureInfo.InvariantCulture));
        if (!string.IsNullOrWhiteSpace(row.MaterialName))
        {
            line.Append(' ').Append(row.MaterialName.Trim());
        }

        if (row.ThicknessMillimeters > 0m)
        {
            line.Append(' ')
                .Append(row.ThicknessMillimeters.ToString("0.###", CultureInfo.InvariantCulture))
                .Append("mm");
        }

        return line.ToString();
    }

    private static string Pad(string label) => (label ?? string.Empty).PadRight(LabelWidth);
}
