// このファイルは、壁・床・天井・屋根のうち複合構造のどちら端が室内側かを一箇所へ固定するために存在します。
// CompoundStructureReader（旧・単一選択の層読取フロー）とRevitFinishSourceCollector（Task 6・全文書収集フロー）が
// 同じ室内側判定を共有するための単一情報源です。値を変える場合は両方の呼出元への影響を確認してください。
//
// 規則（設計 §3、CompoundStructureReader.ResolveInteriorSideと同じ）:
//   床は複合構造の先頭層（層順の最初）が室内側。壁・天井・屋根は末尾層（層順の最後）が室内側。
//
// この対応はRevitが無い環境では検証できません。誤っていると、
// 仕上のProduct_IDが最も外側の層へ書き込まれ、モデルを見ても気づけません。
using Bui.Assemblies;
using Bui.FinishSchedules;

namespace Bui.Revit.FinishSchedules;

/// <summary>FinishHostKindから、複合構造のどちら端が室内側かを決めます。</summary>
internal static class ExteriorSideResolver
{
    public static InteriorSide InteriorFor(FinishHostKind kind) => kind switch
    {
        FinishHostKind.Floor => InteriorSide.FirstLayer,
        FinishHostKind.Wall or FinishHostKind.Ceiling or FinishHostKind.Roof => InteriorSide.LastLayer,
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };
}
