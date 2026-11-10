// このファイルはRevit登録と共有パラメータで使うBUI固有識別子を一か所に固定します。
// 値を変えると既存のパネル配置、アドイン登録、モデル内パラメータとの互換性が失われるため、初回リリース後は絶対に再生成しません。
namespace Bui.Resources;

/// <summary>BUIの公開契約として固定されたGUIDを提供します。</summary>
public static class Identifiers
{
    public static readonly Guid ApplicationAddInId = new("62ba1d8c-426d-45f5-b1b9-9b6407284423");
    public static readonly Guid DockablePaneId = new("b2484bd2-fe75-42b2-8822-7d7bfcf6aa79");

    /// <summary>
    /// 旧Product_ID。2026-09-07以降、BUIは読み書きしません。
    /// 既にモデルへ書き込まれた値を消さないため、定義だけ残します。
    /// </summary>
    public static readonly Guid ProductIdParameterGuid = new("569e8df1-4ca6-448c-8fb9-496cb39b4458");

    public static readonly Guid AssemblyIdParameterGuid = new("883267b2-8771-498a-a77e-a698e06b3453");

    /// <summary>
    /// 複合レイヤーの層ごとのProduct_IDです。並び順は 仕上 → 下地① → … → 下地⑤ で固定します。
    /// この順序と添字が、層スロットの番号そのものです。並べ替えると既存モデルの値がずれます。
    /// GUIDは初回リリース後は絶対に再生成しません。
    /// </summary>
    public static IReadOnlyList<Guid> LayerProductIdParameterGuids { get; } = new[]
    {
        new Guid("e10c429c-9ee1-4813-9e59-9178991af83a"),
        new Guid("3fcefbeb-ddcc-4f74-bd90-d8be05baf555"),
        new Guid("ba9206e7-d7b1-47aa-894f-1eb71ceb4c53"),
        new Guid("0cef352d-edbb-441c-9bd8-43ecf239eb4d"),
        new Guid("5b008bd1-2630-4295-ab09-f7cf4cdbf764"),
        new Guid("79b24248-89df-460d-bbcc-95af64515a63")
    };

    /// <summary>共有パラメータ定義ファイルに書かれている名前です。GUIDと同じ並びです。</summary>
    public static IReadOnlyList<string> LayerProductIdParameterNames { get; } = new[]
    {
        "Product_ID（仕上）",
        "Product_ID（下地①）",
        "Product_ID（下地②）",
        "Product_ID（下地③）",
        "Product_ID（下地④）",
        "Product_ID（下地⑤）"
    };
}
