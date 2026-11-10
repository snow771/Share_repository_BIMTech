// このファイルは、既存のProduct_ID点検結果をUI用のグループ表示へ変換します。
// IdentifierAuditServiceと旧新ID対応に依存し、変更は点検タブの件数・ナビゲーション表示に影響します。
using Bui.Products;
using Bui.Revit.Audit;
using Bui.Revit.Products;
using Bui.UI;

namespace Bui.Revit.UI;

public sealed class RevitAuditQueryService : IAuditQueryService
{
    private readonly IdentifierAuditService _auditService;
    private readonly IReadOnlyDictionary<string, string> _oldIdMappings;

    public RevitAuditQueryService(IdentifierAuditService auditService, IReadOnlyDictionary<string, string>? oldIdMappings = null)
    {
        _auditService = auditService ?? throw new ArgumentNullException(nameof(auditService));
        _oldIdMappings = oldIdMappings ?? new Dictionary<string, string>();
    }

    public async Task<IReadOnlyList<AuditIssue>> AuditAsync(CancellationToken cancellationToken)
    {
        // 点検は全プロジェクトを初期値とし、この境界からモデルを書き換えません。
        var result = await _auditService.AuditWithSourceAsync(
            new ProductIdTargetQuery(ProductIdTargetScope.FullProject, Array.Empty<long>()),
            _oldIdMappings,
            cancellationToken).ConfigureAwait(false);
        if (!result.IsSuccess)
        {
            return new[] { new AuditIssue("点検エラー", string.Empty, null, result.Error!.UserMessage) };
        }

        var documentKey = RevitDocumentIdentity.GetKey(result.Value!.SourceDocument);
        return result.Value.Report.Issues.Select(issue => new AuditIssue(
            GroupName(issue.Kind), documentKey, issue.ElementId, issue.Message, $"要素ID {issue.ElementId}を表示")).ToArray();
    }

    private static string GroupName(IdentifierAuditIssueKind kind) => kind switch
    {
        IdentifierAuditIssueKind.ParameterMissing => "パラメータ未作成",
        IdentifierAuditIssueKind.Blank => "Product_ID未設定",
        IdentifierAuditIssueKind.RepositoryMissing => "CSVに存在しないID",
        IdentifierAuditIssueKind.OldIdMappingCandidate => "旧Product_ID",
        IdentifierAuditIssueKind.SameTypeMultipleIds => "同一タイプ内の競合",
        IdentifierAuditIssueKind.ReadOnlyOrNotEditable => "編集不可",
        _ => "その他"
    };
}
