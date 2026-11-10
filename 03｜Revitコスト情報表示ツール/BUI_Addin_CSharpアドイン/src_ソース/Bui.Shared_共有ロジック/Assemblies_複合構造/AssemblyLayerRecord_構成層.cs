// このファイルは複合ホストを構成する一層を不変の値として表すために存在します。
// AssemblyRecord、照合処理、CSV保存に依存されるため、項目や型を変えると構成比較と既存CSVの復元に影響します。
namespace Bui.Assemblies;

/// <summary>複合構造に含まれる一層の順序、機能、厚さ、製品ID、Revit表示名を保持します。</summary>
public sealed record AssemblyLayerRecord(
    int Order,
    string Function,
    decimal ThicknessMillimeters,
    string ProductId,
    string RevitMaterialName,
    long MaterialElementId = -1,
    bool IsMaterialFallback = false);
