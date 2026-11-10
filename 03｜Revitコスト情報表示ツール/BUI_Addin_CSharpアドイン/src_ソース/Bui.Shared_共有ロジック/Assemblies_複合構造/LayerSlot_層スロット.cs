// このファイルの目的:
//   複合レイヤーの層を入れる「箱」を定義します。
//   箱は6つで、室内側から 仕上 → 下地① → … → 下地⑤ の順です。
//
// なぜ順序が固定なのか:
//   この並びがそのまま共有パラメータの並び（Identifiers.LayerProductIdParameterGuids）です。
//   並べ替えると、既にモデルへ書き込んだ値が別の箱を指すことになります。
//
// 修正時の影響:
//   名前を変える場合は、共有パラメータ定義ファイルの名前も同時に変えてください。
//   食い違うと、画面の表示と実際の書き込み先がずれます。

namespace Bui.Assemblies;

/// <summary>層を入れる箱です。数値はそのまま共有パラメータの添字です。</summary>
public enum LayerSlot
{
    Finish = 0,
    Substrate1 = 1,
    Substrate2 = 2,
    Substrate3 = 3,
    Substrate4 = 4,
    Substrate5 = 5
}

/// <summary>複合構造の層順のうち、どちら端が室内側かを表します。</summary>
public enum InteriorSide
{
    /// <summary>層順の最初が室内側（床など）。</summary>
    FirstLayer,

    /// <summary>層順の最後が室内側（壁・天井・屋根など）。</summary>
    LastLayer
}

/// <summary>スロットの表示名です。共有パラメータの名前と一致させます。</summary>
public static class LayerSlotNames
{
    private static readonly string[] Names =
    {
        "Product_ID（仕上）",
        "Product_ID（下地①）",
        "Product_ID（下地②）",
        "Product_ID（下地③）",
        "Product_ID（下地④）",
        "Product_ID（下地⑤）"
    };

    /// <summary>用意されている箱の数です。</summary>
    public const int Count = 6;

    public static string Of(LayerSlot slot)
    {
        var index = (int)slot;
        if (index is < 0 or >= Count)
        {
            throw new ArgumentOutOfRangeException(nameof(slot), slot, "未対応の層スロットです。");
        }

        return Names[index];
    }
}
