// このファイルはRevit APIで読み取った複合層を、Revit非依存の変換・プレビュー境界へ渡すために存在します。
// CompoundStructureReaderとCompoundLayerConverterに依存され、項目変更は材料フォールバック表示とAssembly_ID照合に影響します。
namespace Bui.Assemblies;

/// <summary>Revitの層順序、機能、mm厚、解決済み材料とProduct_IDを保持します。</summary>
public sealed record CompoundLayerSnapshot(
    int Order,
    string Function,
    decimal ThicknessMillimeters,
    long MaterialElementId,
    string MaterialName,
    string ProductId,
    bool IsMaterialFallback);
