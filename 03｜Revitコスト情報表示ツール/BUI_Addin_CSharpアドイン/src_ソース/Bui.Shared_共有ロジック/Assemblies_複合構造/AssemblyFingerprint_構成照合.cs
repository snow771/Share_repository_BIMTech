// このファイルは複合構造の同一性を、Revitの表示名に依存しないSHA-256フィンガープリントへ変換するために存在します。
// AssemblyRecord と OperationResult に依存し、正規化・含める項目を変えると既存Assembly_IDの再利用判定に影響します。
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Bui.Common;

namespace Bui.Assemblies;

/// <summary>比較に必要な構成項目だけから、安定した構成照合キーを作成します。</summary>
public static class AssemblyFingerprint
{
    /// <summary>
    /// 構成を正規化してSHA-256の16進フィンガープリントを返します。
    /// OrderedLayersは「室内側から外側」の順で渡してください。順序が指紋に含まれます。
    /// </summary>
    public static OperationResult<string> Create(AssemblyRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        ArgumentNullException.ThrowIfNull(record.OrderedLayers);

        var normalizedLayers = new List<NormalizedLayer>(record.OrderedLayers.Count);
        foreach (var layer in record.OrderedLayers)
        {
            ArgumentNullException.ThrowIfNull(layer);
            if (string.IsNullOrWhiteSpace(layer.ProductId))
            {
                return OperationResult<string>.Failure(
                    "ASSEMBLY_PRODUCT_ID_MISSING",
                    "すべての構成層にProduct_IDを設定してください。",
                    $"レイヤー{layer.Order + 1}（内部の層順序 {layer.Order}）のProduct_IDが空です。");
            }

            // 構成の同一性は「どの製品がどの順で入っているか」で決めます。
            // 厚さや層の機能は含めません。同じ製品の並びなら同じ構成として扱う、
            // という取り決め（設計 §4）に合わせるためです。
            // 位置は層の並び順そのもの（室内側からの通し番号）で表します。
            normalizedLayers.Add(new NormalizedLayer(
                normalizedLayers.Count,
                Normalize(layer.ProductId)));
        }

        var normalized = new NormalizedAssembly(
            Normalize(record.HostCategory),
            normalizedLayers);
        var serialized = JsonSerializer.Serialize(normalized);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(serialized));
        return OperationResult<string>.Success(Convert.ToHexString(hash));
    }

    private static string Normalize(string? value) => (value ?? string.Empty).Trim().ToUpperInvariant();

    private sealed record NormalizedAssembly(string HostCategory, IReadOnlyList<NormalizedLayer> Layers);

    private sealed record NormalizedLayer(int Position, string ProductId);
}
