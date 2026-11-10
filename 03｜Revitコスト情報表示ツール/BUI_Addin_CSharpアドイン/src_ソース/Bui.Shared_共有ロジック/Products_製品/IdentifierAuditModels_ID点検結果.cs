// このファイルはProduct_ID点検の候補・問題・集計をRevit非依存で表し、読取専用の判定を行います。
// 要素ID・タイプID・製品リポジトリID一覧・旧新ID対応に依存し、判定の変更は点検UI、CSV出力、将来の修正候補提示に影響します。
namespace Bui.Products;

/// <summary>Revit要素から取得した点検用の不変スナップショットです。</summary>
public sealed record IdentifierAuditCandidate(
    long ElementId,
    long TypeId,
    bool HasParameter,
    string? ProductId,
    bool IsWritable);

/// <summary>Product_ID点検で報告する問題種別です。</summary>
public enum IdentifierAuditIssueKind
{
    ParameterMissing,
    Blank,
    RepositoryMissing,
    OldIdMappingCandidate,
    SameTypeMultipleIds,
    ReadOnlyOrNotEditable
}

/// <summary>要素ごとの点検問題と任意の移行候補を表します。</summary>
public sealed record IdentifierAuditIssue(
    long ElementId,
    IdentifierAuditIssueKind Kind,
    string Message,
    string? CurrentProductId = null,
    string? SuggestedProductId = null);

/// <summary>自動修正を行わないProduct_ID点検結果です。</summary>
public sealed class IdentifierAuditReport
{
    private IdentifierAuditReport(IReadOnlyList<IdentifierAuditIssue> issues)
    {
        Issues = issues;
    }

    public IReadOnlyList<IdentifierAuditIssue> Issues { get; }

    /// <summary>
    /// 層ごとにBuildした問題をまとめ直す場合など、既に確定した問題一覧から直接組み立てます。
    /// 分類ロジックはBuildにしかないため、判定を新たに行いたい場合はBuildを使ってください。
    /// </summary>
    public static IdentifierAuditReport FromIssues(IEnumerable<IdentifierAuditIssue> issues)
    {
        ArgumentNullException.ThrowIfNull(issues);
        return new IdentifierAuditReport(issues.ToArray());
    }

    /// <summary>Revitから切り離した候補を分類し、修正せず問題だけを返します。</summary>
    public static IdentifierAuditReport Build(
        IEnumerable<IdentifierAuditCandidate> candidates,
        IEnumerable<string> knownProductIds,
        IReadOnlyDictionary<string, string> oldIdMappings)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentNullException.ThrowIfNull(knownProductIds);
        ArgumentNullException.ThrowIfNull(oldIdMappings);

        var snapshot = candidates.ToArray();
        var knownIds = new HashSet<string>(
            knownProductIds.Where(id => !string.IsNullOrWhiteSpace(id)).Select(id => id.Trim()),
            StringComparer.OrdinalIgnoreCase);
        var mappings = new Dictionary<string, string>(oldIdMappings, StringComparer.OrdinalIgnoreCase);
        var issues = new List<IdentifierAuditIssue>();

        foreach (var candidate in snapshot)
        {
            if (!candidate.IsWritable)
            {
                issues.Add(new IdentifierAuditIssue(
                    candidate.ElementId,
                    IdentifierAuditIssueKind.ReadOnlyOrNotEditable,
                    "Product_IDパラメータが読取専用か、要素を編集できません。",
                    candidate.ProductId));
            }

            if (!candidate.HasParameter)
            {
                issues.Add(new IdentifierAuditIssue(
                    candidate.ElementId,
                    IdentifierAuditIssueKind.ParameterMissing,
                    "固定GUIDのProduct_IDパラメータがありません。"));
                continue;
            }

            var productId = candidate.ProductId?.Trim();
            if (string.IsNullOrWhiteSpace(productId))
            {
                issues.Add(new IdentifierAuditIssue(
                    candidate.ElementId,
                    IdentifierAuditIssueKind.Blank,
                    "Product_IDが空欄です。"));
                continue;
            }

            if (!knownIds.Contains(productId))
            {
                issues.Add(new IdentifierAuditIssue(
                    candidate.ElementId,
                    IdentifierAuditIssueKind.RepositoryMissing,
                    "Product_IDが製品リポジトリにありません。",
                    productId));
            }

            if (mappings.TryGetValue(productId, out var mappedId)
                && !string.IsNullOrWhiteSpace(mappedId)
                && !string.Equals(productId, mappedId.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                issues.Add(new IdentifierAuditIssue(
                    candidate.ElementId,
                    IdentifierAuditIssueKind.OldIdMappingCandidate,
                    "旧Product_IDに対する現行ID候補があります。自動修正は行いません。",
                    productId,
                    mappedId.Trim()));
            }
        }

        AddSameTypeIssues(snapshot, issues);
        return new IdentifierAuditReport(issues.AsReadOnly());
    }

    private static void AddSameTypeIssues(
        IEnumerable<IdentifierAuditCandidate> candidates,
        ICollection<IdentifierAuditIssue> issues)
    {
        var inconsistentGroups = candidates
            .Where(candidate =>
                candidate.TypeId >= 0
                && candidate.HasParameter
                && !string.IsNullOrWhiteSpace(candidate.ProductId))
            .GroupBy(candidate => candidate.TypeId)
            .Where(group => group
                .Select(candidate => candidate.ProductId!.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Skip(1)
                .Any());

        foreach (var group in inconsistentGroups)
        {
            foreach (var candidate in group)
            {
                issues.Add(new IdentifierAuditIssue(
                    candidate.ElementId,
                    IdentifierAuditIssueKind.SameTypeMultipleIds,
                    $"同じタイプID {group.Key} に複数のProduct_IDがあります。",
                    candidate.ProductId?.Trim()));
            }
        }
    }
}
