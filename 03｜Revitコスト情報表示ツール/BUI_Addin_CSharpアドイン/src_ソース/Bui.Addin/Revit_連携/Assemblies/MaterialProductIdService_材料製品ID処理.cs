// このファイルはMaterialの固定GUID Product_IDを読み取り、競合上書き前に影響する全ホストタイプをプレビューしてExternalEventだけで書くために存在します。
// RevitActionDispatcher、CompoundStructureReader、固定GUIDに依存し、上書き規則の変更は同一材料を使う複数タイプのAssembly_ID照合に影響します。
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Bui.Common;
using Bui.Resources;
using Bui.Revit.ExternalEvents;

using Bui.Revit.UI;
namespace Bui.Revit.Assemblies;

public sealed record MaterialHostTypeUsage(
    long HostTypeElementId,
    string HostTypeName,
    string HostCategory);

/// <summary>材料上書き前の値と、その材料を明示またはフォールバックで使う全ホストタイプを保持します。</summary>
public sealed record MaterialProductIdPreview(
    Document SourceDocument,
    string SourceDocumentTitle,
    long MaterialElementId,
    string MaterialUniqueId,
    string CurrentProductId,
    string RequestedProductId,
    bool RequiresConflictConfirmation,
    IReadOnlyList<MaterialHostTypeUsage> AffectedHostTypes);

public sealed record MaterialProductIdWriteReport(
    long MaterialElementId,
    string ProductId,
    bool WasWritten,
    int AffectedHostTypeCount,
    string Detail);

/// <summary>Materialの読取りと、ExternalEvent経由のみの安全な書込みを提供します。</summary>
public sealed class MaterialProductIdService
{
    public string Read(Material material)
    {
        ArgumentNullException.ThrowIfNull(material);
        return material.get_Parameter(Identifiers.ProductIdParameterGuid)?.AsString()?.Trim() ?? string.Empty;
    }

    public OperationResult<bool> EnqueuePreview(
        RevitActionDispatcher dispatcher,
        long materialElementId,
        string requestedProductId,
        Action<OperationResult<MaterialProductIdPreview>> completed)
    {
        ArgumentNullException.ThrowIfNull(dispatcher);
        ArgumentException.ThrowIfNullOrWhiteSpace(requestedProductId);
        ArgumentNullException.ThrowIfNull(completed);
        return dispatcher.Enqueue(new PreviewRequest(this, materialElementId, requestedProductId.Trim(), completed));
    }

    /// <summary>直接書込みAPIを公開せず、IRevitActionRequestをディスパッチする入口だけを公開します。</summary>
    public OperationResult<bool> EnqueueWrite(
        RevitActionDispatcher dispatcher,
        MaterialProductIdPreview preview,
        bool conflictingOverwriteConfirmed,
        Action<OperationResult<MaterialProductIdWriteReport>> completed)
    {
        ArgumentNullException.ThrowIfNull(dispatcher);
        ArgumentNullException.ThrowIfNull(preview);
        ArgumentNullException.ThrowIfNull(completed);
        if (preview.RequiresConflictConfirmation && !conflictingOverwriteConfirmed)
        {
            return OperationResult<bool>.Failure(
                "MATERIAL_PRODUCT_ID_CONFIRMATION_REQUIRED",
                "既存の材料Product_IDを上書きするには明示確認が必要です。",
                $"Material {preview.MaterialElementId}: '{preview.CurrentProductId}' -> '{preview.RequestedProductId}'.");
        }

        return dispatcher.Enqueue(new WriteRequest(this, preview, conflictingOverwriteConfirmed, completed));
    }

    private MaterialProductIdPreview CreatePreview(Document document, Material material, string requestedProductId)
    {
        var current = Read(material);
        return new MaterialProductIdPreview(
            document,
            document.Title,
            material.Id.Value,
            material.UniqueId,
            current,
            requestedProductId,
            !string.IsNullOrWhiteSpace(current)
                && !string.Equals(current, requestedProductId, StringComparison.OrdinalIgnoreCase),
            FindAffectedHostTypes(document, material.Id));
    }

    private static IReadOnlyList<MaterialHostTypeUsage> FindAffectedHostTypes(
        Document document,
        ElementId materialId)
    {
        using var collector = new FilteredElementCollector(document);
        return collector
            .WhereElementIsElementType()
            .ToElements()
            .OfType<HostObjAttributes>()
            .Where(hostType => UsesMaterial(document, hostType, materialId))
            .Select(hostType => new MaterialHostTypeUsage(
                hostType.Id.Value,
                hostType.Name,
                hostType.Category?.Name ?? string.Empty))
            .OrderBy(usage => usage.HostTypeElementId)
            .ToArray();
    }

    private static bool UsesMaterial(Document document, HostObjAttributes hostType, ElementId materialId)
    {
        var compound = hostType.GetCompoundStructure();
        return compound is not null
            && compound.GetLayers().Any(layer =>
                CompoundStructureReader.ResolveMaterial(document, hostType, layer).Material?.Id == materialId);
    }

    private sealed class PreviewRequest : IRevitActionRequest
    {
        private readonly MaterialProductIdService _service;
        private readonly long _materialElementId;
        private readonly string _requestedProductId;
        private readonly Action<OperationResult<MaterialProductIdPreview>> _completed;

        public PreviewRequest(
            MaterialProductIdService service,
            long materialElementId,
            string requestedProductId,
            Action<OperationResult<MaterialProductIdPreview>> completed)
        {
            _service = service;
            _materialElementId = materialElementId;
            _requestedProductId = requestedProductId;
            _completed = completed;
        }

        public string Name => "材料Product_ID影響範囲プレビュー";

        public void Execute(UIApplication application)
        {
            try
            {
                var document = application.ActiveUIDocument?.Document;
                if (document?.GetElement(new ElementId(_materialElementId)) is not Material material)
                {
                    _completed(OperationResult<MaterialProductIdPreview>.Failure(
                        "MATERIAL_NOT_FOUND",
                        "対象のRevit材料が見つかりません。",
                        $"Material {_materialElementId}"));
                    return;
                }

                _completed(OperationResult<MaterialProductIdPreview>.Success(
                    _service.CreatePreview(document, material, _requestedProductId)));
            }
            catch (Exception exception)
            {
                _completed(OperationResult<MaterialProductIdPreview>.Failure(
                    "MATERIAL_PRODUCT_ID_PREVIEW_FAILED",
                    "材料Product_IDの影響範囲を確認できませんでした。",
                    exception.ToString()));
            }
        }
    }

    private sealed class WriteRequest : IRevitActionRequest
    {
        private readonly MaterialProductIdService _service;
        private readonly MaterialProductIdPreview _preview;
        private readonly bool _conflictingOverwriteConfirmed;
        private readonly Action<OperationResult<MaterialProductIdWriteReport>> _completed;
        private int _completionDelivered;

        public WriteRequest(
            MaterialProductIdService service,
            MaterialProductIdPreview preview,
            bool conflictingOverwriteConfirmed,
            Action<OperationResult<MaterialProductIdWriteReport>> completed)
        {
            _service = service;
            _preview = preview;
            _conflictingOverwriteConfirmed = conflictingOverwriteConfirmed;
            _completed = completed;
        }

        public string Name => "材料Product_ID書込み";

        public void Execute(UIApplication application)
        {
            try
            {
                ExecuteCore(application);
            }
            catch (Exception exception)
            {
                Deliver(OperationResult<MaterialProductIdWriteReport>.Failure(
                    "MATERIAL_PRODUCT_ID_WRITE_FAILED",
                    "材料Product_IDを書き込めませんでした。",
                    exception.ToString()));
            }
        }

        private void ExecuteCore(UIApplication application)
        {
            var document = application.ActiveUIDocument?.Document;
            if (!RevitDocumentMatch.IsSame(document, _preview.SourceDocument))
            {
                Fail("REVIT-DOCUMENT-CHANGED",
                    "プレビュー後にRevitドキュメントが切り替わったため、書き込みませんでした。"
                        + RevitDocumentMatch.DescribeMismatch(document, _preview.SourceDocument));
                return;
            }

            if (document.GetElement(new ElementId(_preview.MaterialElementId)) is not Material material
                || !string.Equals(material.UniqueId, _preview.MaterialUniqueId, StringComparison.Ordinal))
            {
                Fail("MATERIAL_CHANGED", "プレビューしたRevit材料が変更されたため、書き込みませんでした。");
                return;
            }

            var currentPreview = _service.CreatePreview(document, material, _preview.RequestedProductId);
            if (!currentPreview.AffectedHostTypes.Select(item => item.HostTypeElementId)
                    .SequenceEqual(_preview.AffectedHostTypes.Select(item => item.HostTypeElementId)))
            {
                Fail("MATERIAL_USAGE_PREVIEW_STALE", "この材料を使うホストタイプが変わったため、影響範囲を再表示してください。");
                return;
            }

            if (!string.Equals(currentPreview.CurrentProductId, _preview.CurrentProductId, StringComparison.OrdinalIgnoreCase)
                && !string.Equals(currentPreview.CurrentProductId, _preview.RequestedProductId, StringComparison.OrdinalIgnoreCase))
            {
                Fail("MATERIAL_PRODUCT_ID_PREVIEW_STALE", "材料の現在Product_IDがプレビュー後に変わったため、再度確認してください。");
                return;
            }

            if (currentPreview.RequiresConflictConfirmation && !_conflictingOverwriteConfirmed)
            {
                Fail("MATERIAL_PRODUCT_ID_CONFIRMATION_REQUIRED", "既存の材料Product_IDを上書きするには明示確認が必要です。");
                return;
            }

            if (string.Equals(currentPreview.CurrentProductId, _preview.RequestedProductId, StringComparison.OrdinalIgnoreCase))
            {
                Complete(
                    wasWritten: false,
                    affectedHostTypeCount: currentPreview.AffectedHostTypes.Count,
                    detail: "材料はすでに同じProduct_IDでした。");
                return;
            }

            Transaction? transaction = null;
            MaterialTransactionFinalizer? finalizer = null;
            try
            {
                var parameter = material.get_Parameter(Identifiers.ProductIdParameterGuid);
                if (parameter is null || parameter.IsReadOnly || document.IsReadOnly)
                {
                    Fail("MATERIAL_PRODUCT_ID_NOT_EDITABLE", "材料のProduct_IDパラメータを編集できません。");
                    return;
                }

                transaction = new Transaction(document, "BUI 材料Product_ID書込み");
                if (transaction.Start() != TransactionStatus.Started)
                {
                    var cleanupFailures = DisposeSafely(transaction);
                    Fail(
                        "MATERIAL_PRODUCT_ID_TRANSACTION_FAILED",
                        "Revitトランザクションを開始できませんでした。",
                        cleanupFailures);
                    return;
                }

                finalizer = new MaterialTransactionFinalizer(
                    transaction,
                    _preview,
                    currentPreview.AffectedHostTypes.Count,
                    Deliver);
                var options = transaction.GetFailureHandlingOptions();
                options
                    .SetForcedModalHandling(true)
                    .SetClearAfterRollback(true)
                    .SetTransactionFinalizer(finalizer);
                transaction.SetFailureHandlingOptions(options);
                if (!parameter.Set(_preview.RequestedProductId))
                {
                    RollBackSafely(
                        transaction,
                        finalizer,
                        "Revitが材料Product_IDの書込みを受け付けませんでした。");
                    return;
                }

                // Revitはモーダル失敗処理中にPendingを返すため、その場でDispose/結果通知しません。
                var commitStatus = transaction.Commit();
                finalizer.CompleteAfterCommitReturned(
                    commitStatus,
                    Array.Empty<string>());
            }
            catch (Exception exception)
            {
                if (transaction is null)
                {
                    Fail(
                        "MATERIAL_PRODUCT_ID_WRITE_FAILED",
                        "材料Product_IDを書き込めませんでした。",
                        new[] { exception.ToString() });
                    return;
                }

                var cleanupFailures = new List<string> { exception.ToString() };
                var status = GetStatusSafely(transaction, cleanupFailures);
                if (finalizer is null)
                {
                    cleanupFailures.AddRange(DisposeSafely(transaction));
                    Fail(
                        "MATERIAL_PRODUCT_ID_WRITE_FAILED",
                        "材料Product_ID書込みの初期化に失敗しました。",
                        cleanupFailures);
                    return;
                }

                if (status == TransactionStatus.Pending)
                {
                    finalizer.CompleteAfterCommitReturned(TransactionStatus.Pending, cleanupFailures);
                    return;
                }

                if (status == TransactionStatus.Started)
                {
                    RollBackSafely(
                        transaction,
                        finalizer,
                        "材料Product_ID書込み中に例外が発生しました。",
                        cleanupFailures);
                    return;
                }

                finalizer.CompleteAfterCommitReturned(status, cleanupFailures);
            }
        }

        private void Complete(bool wasWritten, int affectedHostTypeCount, string detail) =>
            Deliver(OperationResult<MaterialProductIdWriteReport>.Success(new MaterialProductIdWriteReport(
                _preview.MaterialElementId,
                _preview.RequestedProductId,
                wasWritten,
                affectedHostTypeCount,
                detail)));

        private void Fail(string code, string userMessage) =>
            Deliver(OperationResult<MaterialProductIdWriteReport>.Failure(code, userMessage, Name));

        private void Fail(string code, string userMessage, IEnumerable<string> technicalDetails) =>
            Deliver(OperationResult<MaterialProductIdWriteReport>.Failure(
                code,
                userMessage,
                string.Join(" | ", technicalDetails)));

        private void Deliver(OperationResult<MaterialProductIdWriteReport> result)
        {
            if (Interlocked.Exchange(ref _completionDelivered, 1) != 0)
            {
                return;
            }

            try
            {
                _completed(result);
            }
            catch
            {
                // 画面側コールバック例外をRevitトランザクションの後処理へ逆流させません。
            }
        }

        private static void RollBackSafely(
            Transaction transaction,
            MaterialTransactionFinalizer finalizer,
            string detail,
            IEnumerable<string>? priorFailures = null)
        {
            var cleanupFailures = priorFailures?.ToList() ?? new List<string>();
            try
            {
                var rollbackStatus = transaction.RollBack();
                finalizer.CompleteAfterCommitReturned(rollbackStatus, cleanupFailures);
            }
            catch (Exception rollbackException)
            {
                cleanupFailures.Add($"{detail} RollBack failed: {rollbackException}");
                var status = GetStatusSafely(transaction, cleanupFailures);
                if (status == TransactionStatus.Pending)
                {
                    finalizer.CompleteAfterCommitReturned(TransactionStatus.Pending, cleanupFailures);
                }
                else
                {
                    finalizer.CompleteAfterCommitReturned(status, cleanupFailures);
                }
            }
        }

        private static TransactionStatus GetStatusSafely(
            Transaction transaction,
            ICollection<string> cleanupFailures)
        {
            try
            {
                return transaction.GetStatus();
            }
            catch (Exception statusException)
            {
                cleanupFailures.Add($"GetStatus failed: {statusException}");
                // Revit APIに読取失敗専用値はないため、未確定としてUninitializedで失敗扱いします。
                return TransactionStatus.Uninitialized;
            }
        }

        private static IReadOnlyList<string> DisposeSafely(Transaction transaction)
        {
            try
            {
                transaction.Dispose();
                return Array.Empty<string>();
            }
            catch (Exception disposeException)
            {
                return new[] { $"Dispose failed: {disposeException}" };
            }
        }

        /// <summary>Pending中のTransactionを閉じず、Revitの最終通知後に一度だけ結果を返します。</summary>
        private sealed class MaterialTransactionFinalizer : ITransactionFinalizer
        {
            private readonly object _gate = new();
            private readonly Transaction _transaction;
            private readonly MaterialProductIdPreview _preview;
            private readonly int _affectedHostTypeCount;
            private readonly Action<OperationResult<MaterialProductIdWriteReport>> _completed;
            private TransactionStatus? _finalStatus;
            private bool _ownsPendingClosure;
            private bool _isCompleted;
            private IReadOnlyList<string> _cleanupFailures = Array.Empty<string>();

            public MaterialTransactionFinalizer(
                Transaction transaction,
                MaterialProductIdPreview preview,
                int affectedHostTypeCount,
                Action<OperationResult<MaterialProductIdWriteReport>> completed)
            {
                _transaction = transaction;
                _preview = preview;
                _affectedHostTypeCount = affectedHostTypeCount;
                _completed = completed;
            }

            public void OnCommitted(Document document, string transactionName) =>
                RecordFinalStatus(TransactionStatus.Committed);

            public void OnRolledBack(Document document, string transactionName) =>
                RecordFinalStatus(TransactionStatus.RolledBack);

            public void CompleteAfterCommitReturned(
                TransactionStatus status,
                IEnumerable<string> cleanupFailures)
            {
                var copiedFailures = cleanupFailures.ToArray();
                if (status != TransactionStatus.Pending)
                {
                    Complete(status, copiedFailures);
                    return;
                }

                TransactionStatus? alreadyFinalized;
                lock (_gate)
                {
                    _ownsPendingClosure = true;
                    _cleanupFailures = copiedFailures;
                    alreadyFinalized = _finalStatus;
                }

                if (alreadyFinalized is TransactionStatus finalStatus)
                {
                    Complete(finalStatus, copiedFailures);
                }
            }

            private void RecordFinalStatus(TransactionStatus status)
            {
                bool closeDeferred;
                lock (_gate)
                {
                    _finalStatus = status;
                    closeDeferred = _ownsPendingClosure;
                }

                if (closeDeferred)
                {
                    Complete(status, _cleanupFailures);
                }
            }

            private void Complete(TransactionStatus status, IReadOnlyList<string> cleanupFailures)
            {
                lock (_gate)
                {
                    if (_isCompleted)
                    {
                        return;
                    }

                    _isCompleted = true;
                }

                var allCleanupFailures = cleanupFailures.ToList();
                try
                {
                    _transaction.Dispose();
                }
                catch (Exception disposeException)
                {
                    allCleanupFailures.Add($"Dispose failed: {disposeException}");
                }

                var cleanupDetail = allCleanupFailures.Count == 0
                    ? string.Empty
                    : $" 後処理の詳細: {string.Join(" | ", allCleanupFailures)}";
                var result = status == TransactionStatus.Committed
                    ? OperationResult<MaterialProductIdWriteReport>.Success(new MaterialProductIdWriteReport(
                        _preview.MaterialElementId,
                        _preview.RequestedProductId,
                        WasWritten: true,
                        AffectedHostTypeCount: _affectedHostTypeCount,
                        Detail: $"材料へProduct_IDを書き込みました。{cleanupDetail}"))
                    : OperationResult<MaterialProductIdWriteReport>.Failure(
                        "MATERIAL_PRODUCT_ID_WRITE_FAILED",
                        "Revitが材料Product_IDの書込みを確定しませんでした。",
                        $"Transaction status: {status}.{cleanupDetail}");
                try
                {
                    _completed(result);
                }
                catch
                {
                    // 画面側の例外を、確定済みのRevit処理へ逆流させません。
                }
            }
        }
    }
}
