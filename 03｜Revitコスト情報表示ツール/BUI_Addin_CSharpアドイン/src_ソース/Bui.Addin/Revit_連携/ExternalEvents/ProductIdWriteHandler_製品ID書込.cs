// このファイルはProduct_ID一括付与の対象収集、事前プレビュー、検証済み書込み、結果報告をRevit ExternalEvent上で行います。
// 固定GUID、製品リポジトリ、BulkAssignmentPlan、Revit DB APIに依存し、対象範囲や上書き規則の変更はモデル値とUndo内容に影響します。
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Bui.Common;
using Bui.Data;
using Bui.Products;
using Bui.Resources;
using Bui.Revit.ExternalEvents;
using Bui.Revit.UI;

namespace Bui.Revit.Products;

/// <summary>一括付与または点検でRevit要素を集める範囲です。</summary>
public enum ProductIdTargetScope
{
    Selection,
    ActiveView,
    FullProject
}

/// <summary>対象範囲と、任意の同タイプ絞込み元を表します。</summary>
public sealed record ProductIdTargetQuery(
    ProductIdTargetScope Scope,
    IReadOnlyList<long> SelectedElementIds,
    long? SameTypeSourceElementId = null);

/// <summary>書込み前に画面へ表示する対象と4分類です。</summary>
public sealed record ProductIdAssignmentPreview(
    Document SourceDocument,
    string SourceDocumentTitle,
    ProductIdTargetQuery Query,
    IReadOnlyList<long> TargetElementIds,
    BulkAssignmentPlan Plan,
    /// <summary>書込不可に分類された理由（重複なし）。件数だけでは何を直せばよいか分かりません。</summary>
    IReadOnlyList<string> NotWritableReasons);

/// <summary>要素単位の書込み失敗理由です。</summary>
public sealed record ProductIdWriteFailure(long ElementId, string Reason);

/// <summary>一括処理後の成功IDと失敗理由です。</summary>
public sealed record ProductIdWriteReport(
    IReadOnlyList<long> WrittenElementIds,
    IReadOnlyList<ProductIdWriteFailure> Failures);

/// <summary>プレビュー済み対象だけを、一つのTransactionGroupと一つのTransactionで更新します。</summary>
public sealed class ProductIdWriteHandler : IRevitActionRequest
{
    private readonly ProductIdAssignmentPreview _preview;
    private readonly bool _overwriteConflictsConfirmedBySecondPrompt;
    private readonly Action<OperationResult<ProductIdWriteReport>> _completed;

    private ProductIdWriteHandler(
        ProductIdAssignmentPreview preview,
        bool overwriteConflictsConfirmedBySecondPrompt,
        Action<OperationResult<ProductIdWriteReport>> completed)
    {
        _preview = preview;
        _overwriteConflictsConfirmedBySecondPrompt = overwriteConflictsConfirmedBySecondPrompt;
        _completed = completed;
    }

    public string Name => "Product_ID一括付与";

    /// <summary>製品リポジトリ確認後、書込みを行わないプレビュー要求を予約します。</summary>
    public static async Task<OperationResult<bool>> EnqueuePreviewAsync(
        IProductRepository repository,
        RevitActionDispatcher dispatcher,
        string productId,
        ProductIdTargetQuery query,
        Action<OperationResult<ProductIdAssignmentPreview>> completed,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(dispatcher);
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(completed);

        var validation = await ProductIdAssignmentValidator
            .ValidateAsync(repository, productId, cancellationToken)
            .ConfigureAwait(false);
        if (!validation.IsSuccess)
        {
            var error = validation.Error!;
            return OperationResult<bool>.Failure(error.Code, error.UserMessage, error.TechnicalDetail);
        }

        return dispatcher.Enqueue(new ProductIdPreviewRequest(
            validation.Value!,
            query,
            completed));
    }

    /// <summary>プレビュー後に製品IDを再確認し、既存値上書きは二回目の明示確認がある場合だけ予約します。</summary>
    public static async Task<OperationResult<bool>> EnqueueWriteAsync(
        IProductRepository repository,
        RevitActionDispatcher dispatcher,
        ProductIdAssignmentPreview preview,
        bool overwriteConflictsConfirmedBySecondPrompt,
        Action<OperationResult<ProductIdWriteReport>> completed,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(dispatcher);
        ArgumentNullException.ThrowIfNull(preview);
        ArgumentNullException.ThrowIfNull(completed);

        var validation = await ProductIdAssignmentValidator
            .ValidateAsync(repository, preview.Plan.TargetProductId, cancellationToken)
            .ConfigureAwait(false);
        if (!validation.IsSuccess)
        {
            var error = validation.Error!;
            return OperationResult<bool>.Failure(error.Code, error.UserMessage, error.TechnicalDetail);
        }

        return dispatcher.Enqueue(new ProductIdWriteHandler(
            preview,
            overwriteConflictsConfirmedBySecondPrompt,
            completed));
    }

    public void Execute(UIApplication application)
    {
        ArgumentNullException.ThrowIfNull(application);
        try
        {
            var document = application.ActiveUIDocument?.Document;
            if (document is null)
            {
                _completed(OperationResult<ProductIdWriteReport>.Failure(
                    "REVIT-DOCUMENT-NOT-FOUND",
                    "Product_IDを書き込むRevitドキュメントがありません。",
                    Name));
                return;
            }

            // 参照比較だけでは、同じモデルでもラッパーが取り直されると不一致になります。
            // 判定の詳細と理由は RevitDocumentMatch を参照してください。
            if (!RevitDocumentMatch.IsSame(document, _preview.SourceDocument))
            {
                _completed(OperationResult<ProductIdWriteReport>.Failure(
                    "REVIT-DOCUMENT-CHANGED",
                    "プレビュー後にRevitドキュメントが切り替わったため、書き込みませんでした。"
                        + RevitDocumentMatch.DescribeMismatch(document, _preview.SourceDocument),
                    $"preview={_preview.SourceDocumentTitle}; active={document.Title}"));
                return;
            }

            WriteBatch(document);
        }
        catch (Exception exception)
        {
            _completed(OperationResult<ProductIdWriteReport>.Failure(
                "PRODUCT-ID-WRITE-FAILED",
                "Product_ID一括付与を開始できませんでした。",
                exception.ToString()));
        }
    }

    private void WriteBatch(Document document)
    {
        var requestedIds = _preview.Plan
            .GetWriteElementIds(_overwriteConflictsConfirmedBySecondPrompt)
            .Distinct()
            .ToArray();
        if (requestedIds.Length == 0)
        {
            _completed(OperationResult<ProductIdWriteReport>.Success(
                new ProductIdWriteReport(Array.Empty<long>(), Array.Empty<ProductIdWriteFailure>())));
            return;
        }

        var written = new List<long>();
        var failures = new List<ProductIdWriteFailure>();
        var group = new TransactionGroup(document, "BUI Product_ID一括付与");
        var transaction = new Transaction(document, "Product_IDを書込み");
        ProductIdTransactionFinalizer? finalizer = null;
        try
        {
            if (group.Start() != TransactionStatus.Started)
            {
                transaction.Dispose();
                group.Dispose();
                _completed(OperationResult<ProductIdWriteReport>.Success(
                    BatchFailure(requestedIds, "TransactionGroupを開始できませんでした。")));
                return;
            }

            if (transaction.Start() != TransactionStatus.Started)
            {
                group.RollBack();
                transaction.Dispose();
                group.Dispose();
                _completed(OperationResult<ProductIdWriteReport>.Success(
                    BatchFailure(requestedIds, "Transactionを開始できませんでした。")));
                return;
            }

            var preprocessor = new ProductIdFailuresPreprocessor();
            finalizer = new ProductIdTransactionFinalizer(
                group,
                transaction,
                requestedIds,
                written,
                failures,
                _completed,
                preprocessor);
            var failureOptions = transaction.GetFailureHandlingOptions();
            failureOptions
                // モデルレス失敗処理はCommitをPendingで返し得るため、この操作だけはモーダル完了を要求します。
                .SetForcedModalHandling(true)
                .SetClearAfterRollback(true)
                .SetFailuresPreprocessor(preprocessor)
                // 万一Pendingになっても、子Transaction終了後にだけGroupを閉じる所有者を明示します。
                .SetTransactionFinalizer(finalizer);
            transaction.SetFailureHandlingOptions(failureOptions);

            var confirmedConflictIds = _overwriteConflictsConfirmedBySecondPrompt
                ? _preview.Plan.Conflicts.ToHashSet()
                : new HashSet<long>();
            foreach (var elementId in requestedIds)
            {
                TryWriteOne(document, elementId, confirmedConflictIds, written, failures);
            }

            var commitStatus = transaction.Commit();
            finalizer.CompleteAfterCommitReturned(commitStatus);
        }
        catch (Exception exception)
        {
            var transactionStatus = transaction.GetStatus();
            if (transactionStatus == TransactionStatus.Pending)
            {
                // Commit呼出しから例外経路へ移っても、Pendingなら親Groupへ触れずfinalizerへ所有権を残します。
                finalizer?.CompleteAfterCommitReturned(TransactionStatus.Pending);
                return;
            }

            if (transactionStatus == TransactionStatus.Started)
            {
                var rollbackStatus = transaction.RollBack();
                if (rollbackStatus == TransactionStatus.Pending)
                {
                    finalizer?.CompleteAfterCommitReturned(TransactionStatus.Pending);
                    return;
                }
            }

            if (group.GetStatus() == TransactionStatus.Started)
            {
                group.RollBack();
            }

            transaction.Dispose();
            group.Dispose();
            _completed(OperationResult<ProductIdWriteReport>.Success(BatchFailure(
                requestedIds,
                $"一括付与を完了できず、全変更を取り消しました: {exception.Message}")));
        }
    }

    private void TryWriteOne(
        Document document,
        long elementId,
        ISet<long> confirmedConflictIds,
        ICollection<long> written,
        ICollection<ProductIdWriteFailure> failures)
    {
        try
        {
            var element = document.GetElement(new ElementId(elementId));
            if (element is null)
            {
                failures.Add(new ProductIdWriteFailure(elementId, "要素が見つかりません。"));
                return;
            }

            var parameter = element.get_Parameter(Identifiers.ProductIdParameterGuid);
            if (parameter is null)
            {
                failures.Add(new ProductIdWriteFailure(elementId, "固定GUIDのProduct_IDパラメータがありません。"));
                return;
            }

            if (document.IsReadOnly || parameter.IsReadOnly || !element.IsModifiable)
            {
                failures.Add(new ProductIdWriteFailure(elementId, "パラメータが読取専用か、要素を編集できません。"));
                return;
            }

            var current = parameter.AsString()?.Trim();
            if (string.Equals(current, _preview.Plan.TargetProductId, StringComparison.OrdinalIgnoreCase))
            {
                // プレビュー後に同値になった要素は再設定せず、既存の履歴を保護します。
                return;
            }

            if (!string.IsNullOrWhiteSpace(current) && !confirmedConflictIds.Contains(elementId))
            {
                failures.Add(new ProductIdWriteFailure(
                    elementId,
                    "プレビュー後に既存Product_IDが変わったため、上書きしませんでした。"));
                return;
            }

            if (!parameter.Set(_preview.Plan.TargetProductId))
            {
                failures.Add(new ProductIdWriteFailure(elementId, "RevitがProduct_IDの設定を受け付けませんでした。"));
                return;
            }

            written.Add(elementId);
        }
        catch (Exception exception)
        {
            failures.Add(new ProductIdWriteFailure(elementId, exception.Message));
        }
    }

    private static ProductIdWriteReport BatchFailure(IEnumerable<long> elementIds, string reason) =>
        new(
            Array.Empty<long>(),
            elementIds
                .Select(elementId => new ProductIdWriteFailure(elementId, reason))
                .ToArray());

    /// <summary>警告だけなら削除し、未解決のエラーは画面待ちにせず全体ロールバックへ送ります。</summary>
    private sealed class ProductIdFailuresPreprocessor : IFailuresPreprocessor
    {
        private readonly SortedSet<string> _errorDescriptions = new(StringComparer.Ordinal);

        /// <summary>ロールバックの原因になったRevit側のエラー文です。無ければ空です。</summary>
        public IReadOnlyList<string> ErrorDescriptions => _errorDescriptions.ToArray();

        public FailureProcessingResult PreprocessFailures(FailuresAccessor failuresAccessor)
        {
            ArgumentNullException.ThrowIfNull(failuresAccessor);

            var severity = failuresAccessor.GetSeverity();

            // Revitは失敗が無い状態でもこの処理を呼びます。そのときの重大度はNoneです。
            // 以前はNoneをエラーと同じ扱いにしてロールバックしていたため、
            // 全要素の書込みに成功していても最後に必ず取り消され、
            // 「0件に書き込みました／失敗170件」になっていました。
            // 何も問題が無いときは、そのまま続行しなければなりません。
            if (severity == FailureSeverity.None)
            {
                return FailureProcessingResult.Continue;
            }

            if (severity == FailureSeverity.Warning)
            {
                failuresAccessor.DeleteAllWarnings();
                return FailureProcessingResult.Continue;
            }

            // ここへ来るのは本当のエラーだけです。何が起きたのかを控えて画面へ返します。
            foreach (var message in failuresAccessor.GetFailureMessages())
            {
                var description = message.GetDescriptionText();
                if (!string.IsNullOrWhiteSpace(description))
                {
                    _errorDescriptions.Add(description.Trim());
                }
            }

            return failuresAccessor.IsTransactionBeingCommitted()
                ? FailureProcessingResult.ProceedWithRollBack
                : FailureProcessingResult.Continue;
        }
    }

    /// <summary>CommitがPendingでも、子Transaction終了前に親Groupを閉じないための所有状態です。</summary>
    private sealed class ProductIdTransactionFinalizer : ITransactionFinalizer
    {
        private readonly object _gate = new();
        private readonly TransactionGroup _group;
        private readonly Transaction _transaction;
        private readonly IReadOnlyList<long> _requestedIds;
        private readonly IReadOnlyList<long> _written;
        private readonly IReadOnlyList<ProductIdWriteFailure> _failures;
        private readonly Action<OperationResult<ProductIdWriteReport>> _completedCallback;
        private readonly ProductIdFailuresPreprocessor _preprocessor;
        private TransactionStatus? _finalStatus;
        private bool _ownsPendingClosure;
        private bool _isCompleted;

        public ProductIdTransactionFinalizer(
            TransactionGroup group,
            Transaction transaction,
            IReadOnlyList<long> requestedIds,
            IReadOnlyList<long> written,
            IReadOnlyList<ProductIdWriteFailure> failures,
            Action<OperationResult<ProductIdWriteReport>> completedCallback,
            ProductIdFailuresPreprocessor preprocessor)
        {
            _group = group;
            _transaction = transaction;
            _requestedIds = requestedIds;
            _written = written;
            _failures = failures;
            _completedCallback = completedCallback;
            _preprocessor = preprocessor;
        }

        /// <summary>
        /// Revitがロールバックを求めた理由を1行に足します。
        /// 「取り消しました」だけでは、利用者にはモデルの何が悪いのか分かりません。
        /// </summary>
        private string DescribeRevitErrors()
        {
            var descriptions = _preprocessor.ErrorDescriptions;
            if (descriptions.Count == 0) return string.Empty;

            const int maximum = 3;
            var shown = string.Join(" / ", descriptions.Take(maximum));
            var omitted = descriptions.Count > maximum ? $" ほか{descriptions.Count - maximum}種類" : string.Empty;
            return $" Revitからの警告・エラー: {shown}{omitted}";
        }

        public void OnCommitted(Document document, string transactionName) =>
            RecordFinalStatus(TransactionStatus.Committed);

        public void OnRolledBack(Document document, string transactionName) =>
            RecordFinalStatus(TransactionStatus.RolledBack);

        public void CompleteAfterCommitReturned(TransactionStatus commitStatus)
        {
            if (commitStatus != TransactionStatus.Pending)
            {
                Complete(commitStatus);
                return;
            }

            TransactionStatus? alreadyFinalizedStatus;
            lock (_gate)
            {
                // Pending中はRollBack/Assimilate/Disposeを呼びません。Revitのfinalizerだけが後続処理を再開します。
                _ownsPendingClosure = true;
                alreadyFinalizedStatus = _finalStatus;
            }

            if (alreadyFinalizedStatus is TransactionStatus finalizedStatus)
            {
                Complete(finalizedStatus);
            }
        }

        private void RecordFinalStatus(TransactionStatus status)
        {
            bool closeDeferredGroup;
            lock (_gate)
            {
                _finalStatus = status;
                closeDeferredGroup = _ownsPendingClosure;
            }

            if (closeDeferredGroup)
            {
                Complete(status);
            }
        }

        private void Complete(TransactionStatus transactionStatus)
        {
            lock (_gate)
            {
                if (_isCompleted)
                {
                    return;
                }

                _isCompleted = true;
            }

            ProductIdWriteReport report;
            try
            {
                if (transactionStatus == TransactionStatus.Committed)
                {
                    // 子Transaction終了後のAssimilateにより、Undo一覧では一つのBUI操作になります。
                    if (_group.Assimilate() == TransactionStatus.Committed)
                    {
                        report = new ProductIdWriteReport(_written.ToArray(), _failures.ToArray());
                    }
                    else
                    {
                        if (_group.GetStatus() == TransactionStatus.Started)
                        {
                            _group.RollBack();
                        }

                        report = BatchFailure(
                            _requestedIds,
                            "TransactionGroupを確定できず、全変更を取り消しました。" + DescribeRevitErrors());
                    }
                }
                else
                {
                    if (_group.GetStatus() == TransactionStatus.Started)
                    {
                        _group.RollBack();
                    }

                    report = BatchFailure(
                        _requestedIds,
                        "Transactionを確定できず、全変更を取り消しました。" + DescribeRevitErrors());
                }
            }
            catch (Exception exception)
            {
                try
                {
                    if (_group.GetStatus() == TransactionStatus.Started)
                    {
                        _group.RollBack();
                    }
                }
                catch
                {
                    // 既にRevitがGroupを終了済みなら、元例外を優先して結果へ残します。
                }

                report = BatchFailure(
                    _requestedIds,
                    $"TransactionGroupを閉じられず、結果を確認できませんでした: {exception.Message}");
            }
            finally
            {
                // 同期CommitではCommit復帰後、Pendingではfinalizer通知後なので、ここで初めて安全に解放できます。
                try
                {
                    _transaction.Dispose();
                }
                catch
                {
                }

                try
                {
                    _group.Dispose();
                }
                catch
                {
                }
            }

            try
            {
                _completedCallback(OperationResult<ProductIdWriteReport>.Success(report));
            }
            catch
            {
                // 画面側コールバック例外をRevitの失敗処理へ逆流させると、確定済みTransactionの状態を壊すため遮断します。
            }
        }
    }

    private sealed class ProductIdPreviewRequest : IRevitActionRequest
    {
        private readonly string _targetProductId;
        private readonly ProductIdTargetQuery _query;
        private readonly Action<OperationResult<ProductIdAssignmentPreview>> _completed;

        public ProductIdPreviewRequest(
            string targetProductId,
            ProductIdTargetQuery query,
            Action<OperationResult<ProductIdAssignmentPreview>> completed)
        {
            _targetProductId = targetProductId;
            _query = query;
            _completed = completed;
        }

        public string Name => "Product_ID一括付与プレビュー";

        public void Execute(UIApplication application)
        {
            try
            {
                var document = application.ActiveUIDocument?.Document;
                if (document is null)
                {
                    _completed(OperationResult<ProductIdAssignmentPreview>.Failure(
                        "REVIT-DOCUMENT-NOT-FOUND",
                        "対象を確認するRevitドキュメントがありません。",
                        Name));
                    return;
                }

                var elements = RevitElementTargetCollector.Collect(document, _query);
                // 書込不可の理由はここでしか分かりません。件数だけを渡すと、
                // 利用者は「書込不可: N件」を見ても何を直せばよいか判断できません。
                var notWritableReasons = new SortedSet<string>(StringComparer.Ordinal);
                var candidates = elements.Select(element =>
                {
                    var parameter = element.get_Parameter(Identifiers.ProductIdParameterGuid);
                    var editability = RevitProductIdEditability.Evaluate(document, element, parameter);
                    if (!editability.CanWriteParameter)
                    {
                        notWritableReasons.Add(RevitProductIdEditability.DescribeBlock(document, parameter));
                    }

                    return new AssignmentCandidate(
                        element.Id.Value,
                        parameter?.AsString(),
                        editability.CanWriteParameter);
                }).ToArray();
                var plan = BulkAssignmentPlan.Build(_targetProductId, candidates);
                _completed(OperationResult<ProductIdAssignmentPreview>.Success(new ProductIdAssignmentPreview(
                    document,
                    document.Title,
                    _query,
                    elements.Select(element => element.Id.Value).ToArray(),
                    plan,
                    notWritableReasons.ToArray())));
            }
            catch (Exception exception)
            {
                _completed(OperationResult<ProductIdAssignmentPreview>.Failure(
                    "PRODUCT-ID-PREVIEW-FAILED",
                    "Product_ID一括付与の対象を確認できませんでした。",
                    exception.ToString()));
            }
        }
    }
}

/// <summary>Revitの永続的な読取専用情報を、共有ロジックの編集可否判定へ変換します。</summary>
internal static class RevitProductIdEditability
{
    public static ProductIdEditabilityDecision Evaluate(
        Document document,
        Element element,
        Parameter? parameter)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(element);

        // GetCheckoutStatusは中央モデルのローカルキャッシュです。プレビュー表示には使えますが、
        // 即時編集可否の保証ではないため、実書込み時にもTransaction内でParameter/Elementを再検証します。
        var isOwnedByOtherUser = document.IsWorkshared
            && WorksharingUtils.GetCheckoutStatus(document, element.Id) == CheckoutStatus.OwnedByOtherUser;
        return ProductIdEditabilityDecision.Evaluate(
            isDocumentReadOnly: document.IsReadOnly,
            hasParameter: parameter is not null,
            isParameterReadOnly: parameter?.IsReadOnly ?? false,
            isWorkshared: document.IsWorkshared,
            isOwnedByOtherUser: isOwnedByOtherUser);
    }

    /// <summary>
    /// 書込不可の理由を、利用者が次の一手を決められる言葉で返します。
    /// 判定と同じ順序で見て、最初に当てはまったものを返します。
    /// </summary>
    public static string DescribeBlock(Document document, Parameter? parameter)
    {
        if (parameter is null)
        {
            return "この要素にはProduct_IDパラメータがありません（このカテゴリに共有パラメータが割り当てられていません）。";
        }

        if (document.IsReadOnly) return "モデルが読み取り専用で開かれています。";
        if (parameter.IsReadOnly) return "Product_IDパラメータが読み取り専用です。";
        if (document.IsWorkshared) return "他の利用者が編集中の要素です。";
        return "この要素には書き込めません。";
    }
}

/// <summary>選択・現在ビュー・プロジェクト全体を同じ規則で集め、必要ならGetTypeIdだけで同タイプに絞ります。</summary>
internal static class RevitElementTargetCollector
{
    public static IReadOnlyList<Element> Collect(Document document, ProductIdTargetQuery query)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(query.SelectedElementIds);

        IEnumerable<Element> elements = query.Scope switch
        {
            ProductIdTargetScope.Selection => query.SelectedElementIds
                .Distinct()
                .Select(value => document.GetElement(new ElementId(value)))
                .Where(element => element is not null)
                .Cast<Element>(),
            ProductIdTargetScope.ActiveView => CollectFromActiveView(document),
            ProductIdTargetScope.FullProject => CollectFromProject(document),
            _ => throw new ArgumentOutOfRangeException(nameof(query), query.Scope, "未対応の対象範囲です。")
        };

        if (query.SameTypeSourceElementId is long sourceElementId)
        {
            var source = document.GetElement(new ElementId(sourceElementId))
                ?? throw new InvalidOperationException($"同タイプ絞込み元の要素 {sourceElementId} が見つかりません。");
            var sourceTypeId = source.GetTypeId();
            if (sourceTypeId == ElementId.InvalidElementId)
            {
                throw new InvalidOperationException($"要素 {sourceElementId} にはタイプIDがありません。");
            }

            // 日本語表示名やFamily/Type文字列は使わず、Revitの不変なElementIdだけを比較します。
            elements = elements.Where(element => element.GetTypeId() == sourceTypeId);
        }

        return elements
            .GroupBy(element => element.Id.Value)
            .Select(group => group.First())
            .OrderBy(element => element.Id.Value)
            .ToArray();
    }

    private static IReadOnlyList<Element> CollectFromActiveView(Document document)
    {
        using var collector = new FilteredElementCollector(document, document.ActiveView.Id);
        return collector
            .WhereElementIsNotElementType()
            .ToElements()
            .ToArray();
    }

    private static IReadOnlyList<Element> CollectFromProject(Document document)
    {
        using var collector = new FilteredElementCollector(document);
        return collector
            .WhereElementIsNotElementType()
            .ToElements()
            .ToArray();
    }
}
