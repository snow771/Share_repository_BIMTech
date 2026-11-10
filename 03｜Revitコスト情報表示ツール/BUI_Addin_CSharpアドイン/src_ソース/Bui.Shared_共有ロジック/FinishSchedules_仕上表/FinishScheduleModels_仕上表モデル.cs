namespace Bui.FinishSchedules;

public sealed record NormalizedRoomName(string DisplayName, string Key);

public enum FinishHostKind { Wall, Floor, Ceiling, Roof }

public enum FinishColumn
{
    FloorFinish, FloorSubstrate, WallFinish, WallSubstrate,
    CeilingFinish, CeilingSubstrate, ExteriorFinish, ExteriorSubstrate
}

public sealed record FinishLayerInput(
    int Order, string Function, int DepthFromInterior, string ProductId);

public sealed record HostFinishInput(
    long ElementId, FinishHostKind Kind, string Category, string FamilyName, string TypeName,
    string InnerRoomName, string OuterRoomName, string HorizontalRoomName,
    IReadOnlyList<FinishLayerInput> Layers, string LayerWarning);

public sealed record ProductPlacement(
    string RoomName, string RoomKey, FinishColumn Column, string ProductId,
    int Depth, long ElementId, string Category, string FamilyName, string TypeName);

public sealed record FinishScheduleWarning(string Code, string Message, long? ElementId = null);

public sealed record HostClassification(
    IReadOnlyList<ProductPlacement> InternalPlacements,
    IReadOnlyList<ProductPlacement> ExternalPlacements,
    IReadOnlyList<FinishScheduleWarning> Warnings);

/// <summary>Product_IDを一般名称へ解決できたかどうかを表します。</summary>
public enum ProductResolution { Resolved, ProductMissing, GeneralNameMissing }

/// <summary>配置1件に、解決した一般名称と解決状態を添えたものです。</summary>
public sealed record ResolvedProductPlacement(
    ProductPlacement Placement, string GeneralName, ProductResolution Resolution);

/// <summary>仕上表1セル分の表示テキストと、未解決を含むかどうかを表します。</summary>
public sealed record FinishScheduleCell(string Text, bool HighlightMissing);

/// <summary>内部仕上表の1部屋分の行です。</summary>
public sealed record InternalFinishRow(
    string RoomName,
    FinishScheduleCell FloorFinish, FinishScheduleCell FloorSubstrate,
    FinishScheduleCell WallFinish, FinishScheduleCell WallSubstrate,
    FinishScheduleCell CeilingFinish, FinishScheduleCell CeilingSubstrate,
    string Notes);

/// <summary>外部仕上表のカテゴリ・ファミリ・タイプ単位の1行です。</summary>
public sealed record ExternalFinishRow(
    string Category, string FamilyName, string TypeName,
    FinishScheduleCell Finish, FinishScheduleCell Substrate,
    int ElementCount, string Notes);

/// <summary>集約済みの内部仕上表・外部仕上表・警告一式です。</summary>
public sealed record FinishScheduleOutput(
    IReadOnlyList<InternalFinishRow> InternalRows,
    IReadOnlyList<ExternalFinishRow> ExternalRows,
    IReadOnlyList<FinishScheduleWarning> Warnings);

/// <summary>
/// Revitドキュメントから収集した、仕上表の元データ一式です（部屋集計表由来の部屋一覧、
/// 壁・床・天井・屋根の層スナップショット、収集時点の警告）。RevitFinishSourceCollectorが生成します。
/// </summary>
public sealed record FinishSourceSnapshot(
    IReadOnlyList<NormalizedRoomName> Rooms,
    IReadOnlyList<HostFinishInput> Hosts,
    IReadOnlyList<FinishScheduleWarning> Warnings);
