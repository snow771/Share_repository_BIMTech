// このファイルは、Revit/WPF固有処理とテスト可能な画面状態を分離する契約です。
// 製品・単価・複合層の共有モデルに依存し、項目変更はRevitアダプターとUIテストの両方に影響します。
using Bui.Assemblies;
using Bui.Data;
using Bui.Metadata;
using Bui.Products;

namespace Bui.UI;

public sealed record SelectionSummary(
    bool HasDocument,
    int SelectedCount,
    bool ContainsLinkedElement,
    bool IsReadOnlyContext,
    string ElementDisplayName,
    string TypeName,
    string CategoryName,
    int SameTypeCount)
{
    public static SelectionSummary FromSnapshot(SelectionSnapshot snapshot, int sameTypeCount = 0) => new(
        snapshot.HasDocument,
        snapshot.ElementIds.Count,
        snapshot.ContainsLinkedElement,
        snapshot.IsReadOnlyContext,
        string.Empty,
        string.Empty,
        string.Empty,
        sameTypeCount);
}

// ProductIdは仕上（LayerSlot.Finish）の現在値です。旧・単一Product_IDの後継として、
// 製品カードの読込みや絞り込み初期化など「代表となる1つのID」が必要な場面だけに使います。
// こちらは単一値のため、他の5層の状態を映しません（複数層の値はLayerProductIdSummaryで要約します）。
public sealed record SelectionDetails(
    SelectionSnapshot Snapshot,
    string? ProductId,
    string? AssemblyId,
    IReadOnlyList<AssemblyLayerRecord> Layers,
    IReadOnlyList<string> Warnings)
{
    public string ElementDisplayName { get; init; } = string.Empty;
    public string TypeName { get; init; } = string.Empty;
    public string CategoryName { get; init; } = string.Empty;
    public int SameTypeCount { get; init; }

    /// <summary>
    /// 6つの層別Product_IDを並べた1行要約です（LayerProductIdSummary.Describe参照）。
    /// BuiDockPaneViewModel.SelectedProductIdへ計算はしていますが、現在どのXAMLにも
    /// バインドされておらず画面には出ません。層ごとのProduct_IDは概要タブの複合レイヤー一覧、
    /// 検索タブの層への割り当てで表示しています。空文字は「まだ読み取っていない」ことを意味します
    /// （既定値。Revit読取実装は必ず設定してください）。
    /// </summary>
    public string LayerProductIdSummary { get; init; } = string.Empty;
}

public sealed record CompoundLayerViewModel(
    int Order,
    string Function,
    decimal ThicknessMillimeters,
    string ProductId,
    string RevitMaterialName,
    bool HasMissingProductId,
    long MaterialElementId = -1,
    bool IsMaterialFallback = false)
{
    /// <summary>
    /// 画面に出す層の見出しです。Revitの添字は0始まりですが、
    /// 「順序 0」は読み手に伝わりません。表示だけ1始まりにします。
    /// Orderそのものは変えません。共有パラメータの箱の番号と対応しており、
    /// 内部まで1始まりにすると書き込み先がずれます。
    /// </summary>
    public string LayerLabel => $"レイヤー{Order + 1}";
}

public sealed record ComparisonItemViewModel(
    ProductRecord Product,
    PriceRecord? Price,
    IReadOnlyList<string> MissingFields);

public sealed record AuditIssue(string Group, string DocumentKey, long? ElementId, string Message, string? NavigationLabel = null);

public sealed record VersionInfoViewModel(
    string DisplayName,
    string EnglishName,
    string DescriptionJa,
    string Author,
    string Department,
    string Company,
    string RevitVersion,
    string Version,
    string BuildTimestampUtc)
{
    public static VersionInfoViewModel FromMetadata(AddinMetadata metadata, string version, string buildTimestampUtc) =>
        new(metadata.DisplayName, metadata.EnglishName, metadata.DescriptionJa, metadata.Author,
            metadata.Department, metadata.Company, metadata.RevitVersion, version, buildTimestampUtc);
}

public interface ISelectionDetailsService
{
    Task<SelectionDetails> LoadAsync(SelectionSnapshot snapshot, CancellationToken cancellationToken);
}

public interface IAuditQueryService
{
    Task<IReadOnlyList<AuditIssue>> AuditAsync(CancellationToken cancellationToken);
}

/// <summary>点検結果からRevit要素を表示するが、要素を変更しない境界です。</summary>
public interface IElementNavigationService
{
    Task NavigateAsync(string documentKey, long elementId, CancellationToken cancellationToken);
}

public interface IUiDispatcher
{
    Task InvokeAsync(Action action, CancellationToken cancellationToken);
}

/// <summary>ID付与の3種類を、Revit型を画面に漏らさず識別します。</summary>
public enum IdentifierWorkflowKind
{
    InstanceProductId,
    MaterialProductId,
    AssemblyId,

    /// <summary>複合レイヤーの層ごとのProduct_ID付与。Assembly_IDの採番を同時に伴います。</summary>
    LayerProductId
}

/// <summary>Product_ID一括付与の対象範囲です。初期値はプロジェクト全体です。</summary>
public enum ProductAssignmentScope
{
    Selection,
    ActiveView,
    FullProject
}

/// <summary>画面が表示する安全な事前確認と、アダプター内の一回限りトークンです。</summary>
public sealed record IdentifierWorkflowPreview(
    string Token,
    IdentifierWorkflowKind Kind,
    string Title,
    string Summary,
    bool RequiresConflictConfirmation);

/// <summary>Revitの既存ハンドラーを「読取プレビュー→確認済み書込み」として画面へ公開する契約です。</summary>
public interface IIdentifierWorkflowService
{
    Task<Bui.Common.OperationResult<IdentifierWorkflowPreview>> PreviewProductAsync(
        SelectionSnapshot selection, string productId, ProductAssignmentScope scope, bool sameTypeOnly, CancellationToken cancellationToken);
    Task<Bui.Common.OperationResult<IdentifierWorkflowPreview>> PreviewMaterialAsync(
        long materialElementId, string productId, CancellationToken cancellationToken);
    Task<Bui.Common.OperationResult<IdentifierWorkflowPreview>> PreviewAssemblyAsync(
        long hostElementId, CancellationToken cancellationToken);
    Task<Bui.Common.OperationResult<string>> ApplyAsync(
        string previewToken, bool conflictOverwriteConfirmed, CancellationToken cancellationToken);
}

/// <summary>実際の書込み前に利用者の明示操作を受け取る画面境界です。</summary>
public interface IUserConfirmationService
{
    Task<bool> ConfirmAsync(string title, string message, CancellationToken cancellationToken);
}

/// <summary>
/// CSVの場所を手入力させず、Windowsのファイル選択画面から選ばせる境界です。
/// 画面側はWPFに依存しますが、ViewModelはこの契約だけを見るためテストで差し替えられます。
/// 取り消し時はnullを返します。
/// </summary>
public interface IFilePickerService
{
    /// <summary>既に存在するファイルを選びます。製品CSV・単価CSVで使います。</summary>
    Task<string?> PickExistingFileAsync(string title, string filter, string? initialPath, CancellationToken cancellationToken);

    /// <summary>まだ無いファイルも指定できます。これから作られる構成CSVで使います。</summary>
    Task<string?> PickNewOrExistingFileAsync(string title, string filter, string? initialPath, CancellationToken cancellationToken);

    /// <summary>既に存在するフォルダを選びます。GoldStandardのデータフォルダで使います。</summary>
    Task<string?> PickExistingFolderAsync(string title, string? initialPath, CancellationToken cancellationToken);
}

/// <summary>
/// 層とパラメータの対応を1行で表します。画面はこの形だけを見ます。
/// SlotNameは「この層がどのパラメータへ書かれるか」を利用者へ見せるための情報です。
/// 落とすと、仕上のIDが下地へ入っても実行前に気づけません。
/// </summary>
/// <param name="Order">Revitの複合構造が返した層順です。Revitの構造編集画面と突き合わせるために残します。</param>
/// <param name="Slot">書き込み先の箱。数値がそのまま共有パラメータの添字です。</param>
/// <param name="SlotName">書き込み先のパラメータ名。利用者が実行前に確認するための情報です。</param>
/// <param name="MaterialName">Revitが返した層の材料名。室内側の判定が反転していれば、ここで気づけます。</param>
/// <param name="ThicknessMillimeters">層の厚さ（mm）。複合構造を持たない要素では0です。</param>
/// <param name="CurrentProductId">現在モデルに入っている値。空文字なら未設定です。</param>
/// <param name="SelectedProductId">これから書き込む値。空文字の層は書き込みません（既存値を消しません）。</param>
public sealed record LayerAssignmentRow(
    int Order,
    Bui.Assemblies.LayerSlot Slot,
    string SlotName,
    string MaterialName,
    decimal ThicknessMillimeters,
    string CurrentProductId,
    string SelectedProductId,
    /// <summary>
    /// Revitの層の機能（Finish1・Substrate・Structure など）です。
    /// 構成CSVのfunction列へそのまま入ります。パラメータ名（Product_ID（仕上））とは別物です。
    /// 読めない場合は空文字です。
    /// </summary>
    string LayerFunction = "")
{
    /// <summary>
    /// 画面に出す層の見出しです。概要タブと同じ規則で1始まりにします
    /// （CompoundLayerViewModel.LayerLabel と揃えてください。片方だけ直すと表記が食い違います）。
    /// </summary>
    public string LayerLabel => $"レイヤー{Order + 1}";
}

/// <summary>層割当の表と、割り当てられなかった層の説明です。</summary>
public sealed record LayerAssignmentView(IReadOnlyList<LayerAssignmentRow> Rows, string LayerWarning);

/// <summary>Product_IDを付ける範囲です。</summary>
public enum ProductAssignmentTarget
{
    /// <summary>プロジェクト全体にある、選択要素と同じタイプの要素すべて。</summary>
    SameTypeAll,

    /// <summary>Revitで選択中の要素だけ。</summary>
    SelectionOnly
}

/// <summary>
/// 層ごとのProduct_ID付与の窓口です。画面はこの契約だけを見るため、
/// Revitの型が画面側へ漏れません。テストでは偽実装に差し替えられます。
/// </summary>
public interface ILayerAssignmentService
{
    /// <summary>選択要素の複合レイヤーを読み、どの層がどのパラメータへ入るかを返します。</summary>
    Task<Bui.Common.OperationResult<LayerAssignmentView>> LoadLayersAsync(
        SelectionSnapshot selection, CancellationToken cancellationToken);

    /// <summary>書き込む内容を確認するためのプレビューを作ります。書き込みは行いません。</summary>
    Task<Bui.Common.OperationResult<IdentifierWorkflowPreview>> PreviewLayerAssignmentAsync(
        SelectionSnapshot selection,
        IReadOnlyList<LayerAssignmentRow> rows,
        ProductAssignmentTarget target,
        CancellationToken cancellationToken);
}
