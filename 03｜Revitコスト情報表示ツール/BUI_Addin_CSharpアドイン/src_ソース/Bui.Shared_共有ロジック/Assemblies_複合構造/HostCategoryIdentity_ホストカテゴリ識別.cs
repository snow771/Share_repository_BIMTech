// このファイルは、Revitのローカライズ表示名と、Assembly照合に使う安定カテゴリIDを分離するために存在します。
// CompoundStructureReaderとAssemblyFingerprintに依存され、接頭辞やID形式を変えるとPoC中の既存フィンガープリント互換性に影響します。
using System.Globalization;

namespace Bui.Assemblies;

/// <summary>照合用の不変キーと、利用者向け表示名を一組で保持します。</summary>
public sealed record HostCategoryIdentity(string FingerprintKey, string DisplayName)
{
    /// <summary>Revit BuiltInCategoryの数値IDから、言語に左右されないキーを作ります。</summary>
    public static HostCategoryIdentity FromBuiltInCategoryId(long categoryId, string? displayName) =>
        new(
            $"BIC:{categoryId.ToString(CultureInfo.InvariantCulture)}",
            displayName?.Trim() ?? string.Empty);
}
