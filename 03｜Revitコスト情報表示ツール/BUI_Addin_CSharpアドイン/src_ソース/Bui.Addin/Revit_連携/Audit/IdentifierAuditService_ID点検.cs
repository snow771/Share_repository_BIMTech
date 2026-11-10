// このファイルはRevit要素から6つの層別Product_IDを層ごとに読取り、製品リポジトリと旧新ID対応に照らして問題だけを報告します。
// Revit ExternalEvent、IProductRepository、IdentifierAuditReportに依存し、読取規則の変更は点検件数と移行候補表示に影響します。
// 旧・単一Product_ID（569e8df1-…）はBUIが読み書きしなくなったため、ここでは点検しません。
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Bui.Common;
using Bui.Data;
using Bui.Products;
using Bui.Resources;
using Bui.Revit.ExternalEvents;
using Bui.Revit.Products;

namespace Bui.Revit.Audit;

/// <summary>点検に使用した実Document参照と共有レポートを、ナビゲーション検証まで保持します。</summary>
public sealed record RevitIdentifierAuditResult(Document SourceDocument, IdentifierAuditReport Report);

/// <summary>1要素・1層スロット分の点検候補です。層スロットの並びはIdentifiers.LayerProductIdParameterGuidsと同じです。</summary>
internal sealed record IdentifierAuditSlotCandidate(int SlotIndex, IdentifierAuditCandidate Candidate);

internal sealed record IdentifierAuditCandidateBatch(
    Document SourceDocument,
    IReadOnlyList<IdentifierAuditSlotCandidate> Candidates,
    IReadOnlyDictionary<long, string> ElementLabels);

/// <summary>Revit読取りとリポジトリ照合を分離し、点検中にモデルを一切変更しないサービスです。</summary>
public sealed class IdentifierAuditService
{
    private readonly IProductRepository _repository;
    private readonly RevitActionDispatcher _dispatcher;

    public IdentifierAuditService(
        IProductRepository repository,
        RevitActionDispatcher dispatcher)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
    }

    public async Task<OperationResult<IdentifierAuditReport>> AuditAsync(
        ProductIdTargetQuery query,
        IReadOnlyDictionary<string, string> oldIdMappings,
        CancellationToken cancellationToken)
    {
        var result = await AuditWithSourceAsync(query, oldIdMappings, cancellationToken).ConfigureAwait(false);
        return result.IsSuccess
            ? OperationResult<IdentifierAuditReport>.Success(result.Value!.Report)
            : new OperationResult<IdentifierAuditReport>(default, result.Error);
    }

    public async Task<OperationResult<RevitIdentifierAuditResult>> AuditWithSourceAsync(
        ProductIdTargetQuery query,
        IReadOnlyDictionary<string, string> oldIdMappings,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(oldIdMappings);

        // CSV等の外部データ読込みはSelectionChangedやExternalEvent内で同期実行せず、先に非同期で可用性を確認します。
        var status = await _repository.GetStatusAsync(cancellationToken).ConfigureAwait(false);
        if (!status.IsAvailable)
        {
            return OperationResult<RevitIdentifierAuditResult>.Failure(
                "PRODUCT-REPOSITORY-UNAVAILABLE",
                "製品データを読み込めないため、Product_ID点検を開始できません。",
                string.Join(Environment.NewLine, status.Problems));
        }

        var completion = new TaskCompletionSource<OperationResult<IdentifierAuditCandidateBatch>>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var enqueue = _dispatcher.Enqueue(new CollectAuditCandidatesRequest(query, completion));
        if (!enqueue.IsSuccess)
        {
            var error = enqueue.Error!;
            return OperationResult<RevitIdentifierAuditResult>.Failure(
                error.Code,
                error.UserMessage,
                error.TechnicalDetail);
        }

        var collected = await completion.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        if (!collected.IsSuccess)
        {
            var error = collected.Error!;
            return OperationResult<RevitIdentifierAuditResult>.Failure(
                error.Code,
                error.UserMessage,
                error.TechnicalDetail);
        }

        var batch = collected.Value!;
        var idsToCheck = batch.Candidates
            .Select(slotCandidate => slotCandidate.Candidate)
            .Where(candidate => candidate.HasParameter && !string.IsNullOrWhiteSpace(candidate.ProductId))
            .Select(candidate => candidate.ProductId!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var products = await Task.WhenAll(idsToCheck.Select(productId =>
            _repository.GetByIdAsync(productId, cancellationToken))).ConfigureAwait(false);
        var knownIds = products
            .Where(product => product is not null)
            .Select(product => product!.ProductId)
            .ToArray();

        // 層ごとに点検します。「同一タイプ内で値が割れている」判定は層スロットをまたいで比べると
        // 常に真になってしまうため（仕上と下地①は元々別の値なので）、スロットごとに独立して評価します。
        var slotCount = Identifiers.LayerProductIdParameterGuids.Count;
        var issuesBySlot = new List<IdentifierAuditIssue>[slotCount];
        for (var slotIndex = 0; slotIndex < slotCount; slotIndex++)
        {
            var slotCandidates = batch.Candidates
                .Where(slotCandidate => slotCandidate.SlotIndex == slotIndex)
                .Select(slotCandidate => slotCandidate.Candidate)
                .ToArray();
            var slotReport = IdentifierAuditReport.Build(slotCandidates, knownIds, oldIdMappings);
            var slotName = Identifiers.LayerProductIdParameterNames[slotIndex];
            issuesBySlot[slotIndex] = slotReport.Issues
                .Select(issue => DecorateIssue(issue, slotName, batch.ElementLabels))
                .ToList();
        }

        var report = MergeSlotIssues(issuesBySlot, batch.ElementLabels);

        return OperationResult<RevitIdentifierAuditResult>.Success(new RevitIdentifierAuditResult(
            batch.SourceDocument,
            report));
    }

    /// <summary>点検は層単位で行うため、どのスロットの問題かをメッセージへ足します。空欄はあとで要素単位にまとめ直します。</summary>
    private static IdentifierAuditIssue DecorateIssue(
        IdentifierAuditIssue issue,
        string slotName,
        IReadOnlyDictionary<long, string> elementLabels)
    {
        if (issue.Kind == IdentifierAuditIssueKind.Blank)
        {
            var label = elementLabels.TryGetValue(issue.ElementId, out var found) ? found : $"要素 [{issue.ElementId}]";
            return issue with { Message = $"Product_ID未設定: {label} の {slotName}" };
        }

        return issue with { Message = $"{issue.Message}（{slotName}）" };
    }

    /// <summary>6層すべてが空欄の要素は、層ごとの空欄報告を1件の「すべての層」報告へまとめます。</summary>
    private static IdentifierAuditReport MergeSlotIssues(
        IReadOnlyList<List<IdentifierAuditIssue>> issuesBySlot,
        IReadOnlyDictionary<long, string> elementLabels)
    {
        var slotCount = issuesBySlot.Count;
        var blankByElement = issuesBySlot
            .SelectMany(issues => issues.Where(issue => issue.Kind == IdentifierAuditIssueKind.Blank))
            .GroupBy(issue => issue.ElementId)
            .ToDictionary(group => group.Key, group => group.ToArray());

        var merged = new List<IdentifierAuditIssue>();
        foreach (var issues in issuesBySlot)
        {
            merged.AddRange(issues.Where(issue => issue.Kind != IdentifierAuditIssueKind.Blank));
        }

        foreach (var (elementId, blankIssues) in blankByElement)
        {
            if (blankIssues.Length >= slotCount)
            {
                var label = elementLabels.TryGetValue(elementId, out var found) ? found : $"要素 [{elementId}]";
                merged.Add(new IdentifierAuditIssue(
                    elementId,
                    IdentifierAuditIssueKind.Blank,
                    $"Product_ID未設定: {label}（すべての層）"));
            }
            else
            {
                merged.AddRange(blankIssues);
            }
        }

        return IdentifierAuditReport.FromIssues(merged);
    }

    private sealed class CollectAuditCandidatesRequest : IRevitActionRequest
    {
        private readonly ProductIdTargetQuery _query;
        private readonly TaskCompletionSource<OperationResult<IdentifierAuditCandidateBatch>> _completion;

        public CollectAuditCandidatesRequest(
            ProductIdTargetQuery query,
            TaskCompletionSource<OperationResult<IdentifierAuditCandidateBatch>> completion)
        {
            _query = query;
            _completion = completion;
        }

        public string Name => "Product_ID点検読取り";

        public void Execute(UIApplication application)
        {
            try
            {
                var document = application.ActiveUIDocument?.Document;
                if (document is null)
                {
                    _completion.TrySetResult(OperationResult<IdentifierAuditCandidateBatch>.Failure(
                        "REVIT-DOCUMENT-NOT-FOUND",
                        "Product_IDを点検するRevitドキュメントがありません。",
                        Name));
                    return;
                }

                var elements = RevitElementTargetCollector.Collect(document, _query);
                var candidates = new List<IdentifierAuditSlotCandidate>();
                var elementLabels = new Dictionary<long, string>();
                foreach (var element in elements)
                {
                    elementLabels[element.Id.Value] = DescribeElement(element);
                    for (var slotIndex = 0; slotIndex < Identifiers.LayerProductIdParameterGuids.Count; slotIndex++)
                    {
                        candidates.Add(new IdentifierAuditSlotCandidate(
                            slotIndex,
                            CreateCandidate(document, element, slotIndex)));
                    }
                }

                _completion.TrySetResult(
                    OperationResult<IdentifierAuditCandidateBatch>.Success(
                        new IdentifierAuditCandidateBatch(document, candidates, elementLabels)));
            }
            catch (Exception exception)
            {
                _completion.TrySetResult(OperationResult<IdentifierAuditCandidateBatch>.Failure(
                    "PRODUCT-ID-AUDIT-READ-FAILED",
                    "Revit要素のProduct_IDを読み取れませんでした。",
                    exception.ToString()));
            }
        }

        private static IdentifierAuditCandidate CreateCandidate(Document document, Element element, int slotIndex)
        {
            var parameter = element.get_Parameter(Identifiers.LayerProductIdParameterGuids[slotIndex]);
            var editability = RevitProductIdEditability.Evaluate(document, element, parameter);

            // タイプ名は言語や利用者編集で変わるため、同タイプ判定はGetTypeIdの値だけを保存します。
            return new IdentifierAuditCandidate(
                element.Id.Value,
                element.GetTypeId().Value,
                parameter is not null,
                parameter?.AsString(),
                !editability.IsReadOnlyOrNotEditable);
        }

        /// <summary>点検結果の表示用ラベルです。カテゴリ名が取れない場合は要素IDだけにします。</summary>
        private static string DescribeElement(Element element)
        {
            var categoryName = element.Category?.Name;
            return string.IsNullOrWhiteSpace(categoryName)
                ? $"要素 [{element.Id.Value}]"
                : $"{categoryName} [{element.Id.Value}]";
        }
    }
}
