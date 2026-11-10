// このファイルは複合構造のプレビュー、既存Assembly_ID再利用、確認後CSV追加、ホストタイプ書込みと失敗時補償を一つのExternalEvent流程で行うために存在します。
// CompoundStructureReader、AssemblyFingerprint/Planner、IAssemblyRepository、固定GUIDに依存し、処理順序の変更はRevitとCSVの整合性に影響します。
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Bui.Assemblies;
using Bui.Common;
using Bui.Data;
using Bui.Resources;
using Bui.Revit.Assemblies;
using Bui.Revit.ExternalEvents;

using Bui.Revit.UI;
namespace Bui.Revit.AssemblyWrites;

/// <summary>Revit API内で読み取ったドキュメント同一性と構成照合値を保持します。</summary>
public sealed record AssemblyStructurePreview(
    Document SourceDocument,
    string SourceDocumentTitle,
    long HostElementId,
    long HostTypeElementId,
    string HostCategoryIdentity,
    string HostCategoryDisplayName,
    IReadOnlyList<CompoundLayerSnapshot> LayerSnapshots,
    AssemblyRecord DraftRecord,
    string Fingerprint);

/// <summary>表示したAssembly_ID候補と、既存再利用かCSV新規追加かを保持します。</summary>
public sealed record AssemblyIdApplicationPreview(
    AssemblyStructurePreview Structure,
    AssemblyRegistrationPlan Plan);

public sealed record AssemblyIdWriteReport(
    string AssemblyId,
    bool ReusedExistingAssembly,
    bool CsvRecordAdded,
    bool RevitWriteCommitted,
    AssemblyCompensationOutcome CompensationOutcome,
    string Detail);

/// <summary>プレビュー後の構成とDocumentを再確認し、CSVとRevitタイプ書込みを半自動で実行します。</summary>
public sealed class AssemblyIdWriteHandler : IRevitActionRequest
{
    private readonly AssemblyIdApplicationPreview _preview;
    private readonly AssemblyPersistenceCoordinator _persistenceCoordinator;
    private readonly bool _csvRecordAdded;
    private readonly SynchronizationContext? _callbackContext;
    private readonly Action<OperationResult<AssemblyIdWriteReport>> _completed;
    private readonly object _completionGate = new();
    private Task? _completionTask;
    private int _cancelledBeforeExecution;
    private int _completionDelivered;

    private AssemblyIdWriteHandler(
        AssemblyIdApplicationPreview preview,
        AssemblyPersistenceCoordinator persistenceCoordinator,
        bool csvRecordAdded,
        SynchronizationContext? callbackContext,
        Action<OperationResult<AssemblyIdWriteReport>> completed)
    {
        _preview = preview;
        _persistenceCoordinator = persistenceCoordinator;
        _csvRecordAdded = csvRecordAdded;
        _callbackContext = callbackContext;
        _completed = completed;
    }

    public string Name => "Assembly_ID書込み";

    /// <summary>Revit読取りは必ずExternalEvent上で行い、その後のCSV照合に渡す不変プレビューを作ります。</summary>
    public static OperationResult<bool> EnqueueStructurePreview(
        RevitActionDispatcher dispatcher,
        long hostElementId,
        Action<OperationResult<AssemblyStructurePreview>> completed)
    {
        ArgumentNullException.ThrowIfNull(dispatcher);
        ArgumentNullException.ThrowIfNull(completed);
        return dispatcher.Enqueue(new StructurePreviewRequest(hostElementId, completed));
    }

    /// <summary>ExternalEventで読み取った構成を、CSVの既存フィンガープリントと照合します。</summary>
    public static async Task<OperationResult<AssemblyIdApplicationPreview>> PlanAsync(
        IAssemblyRepository repository,
        AssemblyStructurePreview structure,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(structure);
        var plan = await Task.Run(
            () => AssemblyRegistrationPlanner.PlanAsync(
                structure.DraftRecord,
                repository,
                cancellationToken),
            cancellationToken);
        return plan.IsSuccess
            ? OperationResult<AssemblyIdApplicationPreview>.Success(
                new AssemblyIdApplicationPreview(structure, plan.Value!))
            : new OperationResult<AssemblyIdApplicationPreview>(default, plan.Error);
    }

    /// <summary>CSVの直前検査・新規追加は非同期で完了させ、Revit API処理だけをExternalEventへ予約します。</summary>
    public static async Task<OperationResult<bool>> EnqueueWriteAsync(
        IAssemblyRepository repository,
        RevitActionDispatcher dispatcher,
        AssemblyIdApplicationPreview preview,
        bool newRegistrationConfirmed,
        Action<OperationResult<AssemblyIdWriteReport>> completed,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(dispatcher);
        ArgumentNullException.ThrowIfNull(preview);
        ArgumentNullException.ThrowIfNull(completed);
        var callbackContext = SynchronizationContext.Current;
        if (preview.Plan.RequiresRegistrationConfirmation && !newRegistrationConfirmed)
        {
            return OperationResult<bool>.Failure(
                "ASSEMBLY_REGISTRATION_CONFIRMATION_REQUIRED",
                $"Assembly_ID候補「{preview.Plan.Record.AssemblyId}」のCSV追加には明示確認が必要です。",
                preview.Plan.Fingerprint);
        }

        var csvRecordAdded = false;
        try
        {
            if (preview.Plan.ReusesExistingAssembly)
            {
                // プレビューからExternalEvent.RaiseまでにCSV行が変わっていないか、書込み直前に再確認します。
                var reuse = await Task.Run(
                    () => AssemblyReuseValidator.ValidateAsync(
                        preview.Plan,
                        repository,
                        cancellationToken),
                    cancellationToken);
                if (!reuse.IsSuccess)
                {
                    return new OperationResult<bool>(default, reuse.Error);
                }
            }
            else
            {
                var nowExisting = await Task.Run(
                    () => repository.FindByFingerprintAsync(
                        preview.Plan.Fingerprint,
                        cancellationToken),
                    cancellationToken);
                if (nowExisting is not null)
                {
                    return OperationResult<bool>.Failure(
                        "ASSEMBLY_PLAN_STALE",
                        "確認中に同じ構成がCSVへ登録されたため、再度プレビューしてください。",
                        $"existing={nowExisting.AssemblyId}; planned={preview.Plan.Record.AssemblyId}");
                }

                // 新規行は明示確認後にのみ追加し、ここでawaitするためRevit UIスレッドを同期待機させません。
                // 副作用開始後のキャンセルで「追加済みか不明」を作らないため、この1回の原子置換は完遂させます。
                await Task.Run(() => repository.AddAsync(
                    preview.Plan.Record,
                    preview.Plan.Fingerprint,
                    CancellationToken.None));
                csvRecordAdded = true;
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return OperationResult<bool>.Failure(
                "ASSEMBLY_CSV_PREPARE_FAILED",
                "Assembly_IDのCSV準備に失敗したため、Revitへは書き込みません。",
                exception.ToString());
        }

        var coordinator = new AssemblyPersistenceCoordinator(
            repository,
            preview.Plan,
            csvRecordAdded);
        var handler = new AssemblyIdWriteHandler(
            preview,
            coordinator,
            csvRecordAdded,
            callbackContext,
            completed);
        var enqueueOutcome = await AssemblyEnqueueBoundary.EnqueueAsync(
            () => dispatcher.Enqueue(handler),
            handler.CancelBeforeExecution,
            coordinator);
        if (enqueueOutcome.EnqueueResult.IsSuccess)
        {
            return enqueueOutcome.EnqueueResult;
        }

        // Raiseが例外を投げた場合も共通境界で要求を無効化し、新規CSV行の補償後に一度だけ通知します。
        var persistence = enqueueOutcome.PersistenceCompletion
            ?? throw new InvalidOperationException("予約失敗時のAssembly_ID補償結果がありません。");
        handler.DeliverCompletion(CreateReport(
            preview.Plan,
            csvRecordAdded,
            persistence,
            enqueueOutcome.EnqueueResult.Error?.UserMessage));
        return enqueueOutcome.EnqueueResult;
    }

    public void Execute(UIApplication application)
    {
        ArgumentNullException.ThrowIfNull(application);
        if (Volatile.Read(ref _cancelledBeforeExecution) != 0)
        {
            // ExternalEvent.Raiseが例外前に要求を受理していた場合でも、Revitへの遅延書込みを禁止します。
            BeginCompletion(
                revitWriteCommitted: false,
                "ExternalEvent予約失敗後に残った要求を実行しませんでした。",
                new[] { "Request was cancelled before Revit execution." });
            return;
        }

        try
        {
            ExecuteCore(application);
        }
        catch (Exception exception)
        {
            BeginCompletion(
                revitWriteCommitted: false,
                "Assembly_ID書込みを開始できませんでした。",
                new[] { exception.ToString() });
        }
    }

    private void ExecuteCore(UIApplication application)
    {
        var document = application.ActiveUIDocument?.Document;
        if (!RevitDocumentMatch.IsSame(document, _preview.Structure.SourceDocument))
        {
            BeginCompletion(
                revitWriteCommitted: false,
                "プレビュー後にRevitドキュメントが切り替わったため、CSVとRevitのどちらも更新しませんでした。"
                    + RevitDocumentMatch.DescribeMismatch(document, _preview.Structure.SourceDocument),
                new[] { $"preview={_preview.Structure.SourceDocumentTitle}; active={document?.Title}" });
            return;
        }

        var current = ReReadAndValidate(document);
        if (!current.IsSuccess)
        {
            BeginCompletion(
                revitWriteCommitted: false,
                current.Error?.UserMessage ?? "複合構造の再確認に失敗しました。",
                new[] { $"{current.Error?.Code}: {current.Error?.TechnicalDetail}" });
            return;
        }

        var hostType = document.GetElement(new ElementId(current.Value!.HostTypeElementId)) as HostObjAttributes;
        var parameter = hostType?.get_Parameter(Identifiers.AssemblyIdParameterGuid);
        if (hostType is null || parameter is null || parameter.IsReadOnly || document.IsReadOnly)
        {
            BeginCompletion(
                revitWriteCommitted: false,
                "ホストタイプのAssembly_IDパラメータを編集できません。",
                new[] { $"Host type {current.Value.HostTypeElementId}" });
            return;
        }

        var currentAssemblyId = parameter.AsString()?.Trim() ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(currentAssemblyId)
            && !string.Equals(currentAssemblyId, _preview.Plan.Record.AssemblyId, StringComparison.Ordinal))
        {
            BeginCompletion(
                revitWriteCommitted: false,
                "ホストタイプのAssembly_IDがプレビュー後に変更されたため、上書きしませんでした。",
                new[] { $"current={currentAssemblyId}; planned={_preview.Plan.Record.AssemblyId}" });
            return;
        }

        if (string.Equals(currentAssemblyId, _preview.Plan.Record.AssemblyId, StringComparison.Ordinal))
        {
            BeginCompletion(
                revitWriteCommitted: true,
                "Revitタイプはすでに同じAssembly_IDでした。",
                Array.Empty<string>());
            return;
        }

        WriteRevit(document, parameter);
    }

    private OperationResult<CompoundStructureReadResult> ReReadAndValidate(Document document)
    {
        var host = document.GetElement(new ElementId(_preview.Structure.HostElementId));
        if (host is null)
        {
            return OperationResult<CompoundStructureReadResult>.Failure(
                "ASSEMBLY_PREVIEW_STALE",
                "プレビューしたホスト要素が見つかりません。",
                $"Host {_preview.Structure.HostElementId}");
        }

        var read = new CompoundStructureReader().Read(document, host);
        if (!read.IsSuccess)
        {
            return read;
        }

        var converted = CompoundLayerConverter.ToAssemblyLayers(
            read.Value!.Layers,
            read.Value.IsVerticallyCompound);
        if (!converted.IsSuccess)
        {
            return new OperationResult<CompoundStructureReadResult>(default, converted.Error);
        }

        var currentRecord = _preview.Plan.Record with
        {
            HostCategory = read.Value.HostCategoryIdentity,
            OrderedLayers = converted.Value!
        };
        var fingerprint = AssemblyFingerprint.Create(currentRecord);
        if (!fingerprint.IsSuccess)
        {
            return new OperationResult<CompoundStructureReadResult>(default, fingerprint.Error);
        }

        if (read.Value.HostTypeElementId != _preview.Structure.HostTypeElementId
            || !string.Equals(fingerprint.Value, _preview.Plan.Fingerprint, StringComparison.Ordinal))
        {
            return OperationResult<CompoundStructureReadResult>.Failure(
                "ASSEMBLY_PREVIEW_STALE",
                "プレビュー後にホストタイプまたは複合層が変わったため、再度確認してください。",
                $"planned={_preview.Plan.Fingerprint}; current={fingerprint.Value}");
        }

        return read;
    }

    private void WriteRevit(Document document, Parameter parameter)
    {
        Transaction? transaction = null;
        AssemblyTransactionFinalizer? finalizer = null;
        try
        {
            transaction = new Transaction(document, "BUI Assembly_ID書込み");
            if (transaction.Start() != TransactionStatus.Started)
            {
                var cleanupFailures = DisposeSafely(transaction);
                BeginCompletion(
                    revitWriteCommitted: false,
                    "Revitトランザクションを開始できませんでした。",
                    cleanupFailures);
                return;
            }

            finalizer = new AssemblyTransactionFinalizer(
                transaction,
                (committed, detail, cleanupFailures) =>
                    BeginCompletion(committed, detail, cleanupFailures));
            var options = transaction.GetFailureHandlingOptions();
            options
                .SetForcedModalHandling(true)
                .SetClearAfterRollback(true)
                .SetFailuresPreprocessor(new AssemblyFailuresPreprocessor())
                .SetTransactionFinalizer(finalizer);
            transaction.SetFailureHandlingOptions(options);

            if (!parameter.Set(_preview.Plan.Record.AssemblyId))
            {
                RollBackSafely(
                    transaction,
                    finalizer,
                    "RevitがAssembly_IDパラメータの書込みを受け付けませんでした。");
                return;
            }

            var commitStatus = transaction.Commit();
            finalizer.CompleteAfterCommitReturned(
                commitStatus,
                "RevitタイプへAssembly_IDを書き込みました。",
                Array.Empty<string>());
        }
        catch (Exception exception)
        {
            if (transaction is null)
            {
                BeginCompletion(
                    revitWriteCommitted: false,
                    "Revitトランザクションを作成できませんでした。",
                    new[] { exception.ToString() });
                return;
            }

            var cleanupFailures = new List<string> { exception.ToString() };
            var status = GetStatusSafely(transaction, cleanupFailures);
            if (finalizer is null)
            {
                cleanupFailures.AddRange(DisposeSafely(transaction));
                BeginCompletion(
                    revitWriteCommitted: false,
                    "Revitトランザクションの初期化に失敗しました。",
                    cleanupFailures);
                return;
            }

            if (status == TransactionStatus.Pending)
            {
                finalizer.CompleteAfterCommitReturned(
                    TransactionStatus.Pending,
                    "Revitの失敗処理完了を待っています。",
                    cleanupFailures);
                return;
            }

            if (status == TransactionStatus.Started)
            {
                RollBackSafely(
                    transaction,
                    finalizer,
                    $"Revit書込み中に例外が発生しました: {exception.Message}",
                    cleanupFailures);
                return;
            }

            finalizer.CompleteAfterCommitReturned(
                status,
                $"Revit書込み中に例外が発生しました: {exception.Message}",
                cleanupFailures);
        }
    }

    private static void RollBackSafely(
        Transaction transaction,
        AssemblyTransactionFinalizer finalizer,
        string detail,
        IEnumerable<string>? priorFailures = null)
    {
        var cleanupFailures = priorFailures?.ToList() ?? new List<string>();
        try
        {
            var rollbackStatus = transaction.RollBack();
            finalizer.CompleteAfterCommitReturned(rollbackStatus, detail, cleanupFailures);
        }
        catch (Exception rollbackException)
        {
            cleanupFailures.Add($"RollBack failed: {rollbackException}");
            var status = GetStatusSafely(transaction, cleanupFailures);
            if (status == TransactionStatus.Pending)
            {
                finalizer.CompleteAfterCommitReturned(
                    TransactionStatus.Pending,
                    detail,
                    cleanupFailures);
            }
            else
            {
                // RollBack自体が失敗しても、Finalizer内のDisposeと非同期CSV補償へ必ず進みます。
                finalizer.CompleteAfterCommitReturned(status, detail, cleanupFailures);
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
            // APIに「ステータス読取自体が失敗」を表す値はないため、未確定としてUninitializedで安全側に倒します。
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

    /// <summary>予約失敗後に、後から実行される可能性があるExternalEvent要求を無効化します。</summary>
    private void CancelBeforeExecution() =>
        Interlocked.Exchange(ref _cancelledBeforeExecution, 1);

    private void BeginCompletion(
        bool revitWriteCommitted,
        string detail,
        IEnumerable<string> cleanupFailures)
    {
        var copiedFailures = cleanupFailures.ToArray();
        lock (_completionGate)
        {
            // CsvAssemblyRepositoryは先頭に同期ファイル読取りを含むため、Revit Finalizer内でasyncメソッドを直接起動しません。
            _completionTask ??= Task.Run(
                () => CompleteAndDeliverAsync(revitWriteCommitted, detail, copiedFailures));
        }
    }

    private async Task CompleteAndDeliverAsync(
        bool revitWriteCommitted,
        string detail,
        IReadOnlyList<string> cleanupFailures)
    {
        try
        {
            var persistence = await _persistenceCoordinator
                .CompleteAsync(revitWriteCommitted, cleanupFailures)
                .ConfigureAwait(false);
            DeliverCompletion(CreateReport(_preview.Plan, _csvRecordAdded, persistence, detail));
        }
        catch (Exception exception)
        {
            DeliverCompletion(OperationResult<AssemblyIdWriteReport>.Failure(
                "ASSEMBLY_COMPLETION_FAILED",
                "Assembly_IDの後処理結果を通知できませんでした。",
                exception.ToString()));
        }
    }

    private static OperationResult<AssemblyIdWriteReport> CreateReport(
        AssemblyRegistrationPlan plan,
        bool csvRecordAdded,
        AssemblyPersistenceCompletion persistence,
        string? detail = null) =>
        OperationResult<AssemblyIdWriteReport>.Success(new AssemblyIdWriteReport(
            plan.Record.AssemblyId,
            plan.ReusesExistingAssembly,
            csvRecordAdded,
            persistence.RevitWriteCommitted,
            persistence.CompensationOutcome,
            string.IsNullOrWhiteSpace(detail)
                ? persistence.Detail
                : $"{detail} {persistence.Detail}"));

    private void DeliverCompletion(OperationResult<AssemblyIdWriteReport> result)
    {
        if (Interlocked.Exchange(ref _completionDelivered, 1) != 0)
        {
            // 予約失敗通知と残存要求の完了が競合しても、画面へは最初の1回だけ返します。
            return;
        }

        void InvokeSafely()
        {
            try
            {
                _completed(result);
            }
            catch
            {
                // 画面側コールバック例外を、確定済みのRevit/CSV処理へ逆流させません。
            }
        }

        if (_callbackContext is null)
        {
            InvokeSafely();
            return;
        }

        try
        {
            _callbackContext.Post(_ => InvokeSafely(), null);
        }
        catch
        {
            // 画面終了でSynchronizationContextが受付不能でも、補償結果の通知自体は一度試みます。
            InvokeSafely();
        }
    }

    private sealed class StructurePreviewRequest : IRevitActionRequest
    {
        private readonly long _hostElementId;
        private readonly Action<OperationResult<AssemblyStructurePreview>> _completed;

        public StructurePreviewRequest(
            long hostElementId,
            Action<OperationResult<AssemblyStructurePreview>> completed)
        {
            _hostElementId = hostElementId;
            _completed = completed;
        }

        public string Name => "複合構造プレビュー";

        public void Execute(UIApplication application)
        {
            try
            {
                var document = application.ActiveUIDocument?.Document;
                var host = document?.GetElement(new ElementId(_hostElementId));
                if (document is null || host is null)
                {
                    _completed(OperationResult<AssemblyStructurePreview>.Failure(
                        "ASSEMBLY_HOST_NOT_FOUND",
                        "プレビュー対象のホスト要素が見つかりません。",
                        $"Host {_hostElementId}"));
                    return;
                }

                var read = new CompoundStructureReader().Read(document, host);
                if (!read.IsSuccess)
                {
                    _completed(new OperationResult<AssemblyStructurePreview>(default, read.Error));
                    return;
                }

                var converted = CompoundLayerConverter.ToAssemblyLayers(
                    read.Value!.Layers,
                    read.Value.IsVerticallyCompound);
                if (!converted.IsSuccess)
                {
                    _completed(new OperationResult<AssemblyStructurePreview>(default, converted.Error));
                    return;
                }

                var draft = new AssemblyRecord(
                    string.Empty,
                    read.Value.HostCategoryIdentity,
                    converted.Value!,
                    null,
                    new Dictionary<string, string>
                    {
                        // CSVのhost_categoryは照合用安定IDとし、日本語表示名は追加属性に分離します。
                        ["host_category_display_name"] = read.Value.HostCategoryDisplayName,
                        ["source_document"] = document.Title,
                        ["source_host_element_id"] = read.Value.HostElementId.ToString(System.Globalization.CultureInfo.InvariantCulture),
                        ["source_host_type_id"] = read.Value.HostTypeElementId.ToString(System.Globalization.CultureInfo.InvariantCulture)
                    });
                var fingerprint = AssemblyFingerprint.Create(draft);
                if (!fingerprint.IsSuccess)
                {
                    _completed(new OperationResult<AssemblyStructurePreview>(default, fingerprint.Error));
                    return;
                }

                _completed(OperationResult<AssemblyStructurePreview>.Success(new AssemblyStructurePreview(
                    document,
                    document.Title,
                    read.Value.HostElementId,
                    read.Value.HostTypeElementId,
                    read.Value.HostCategoryIdentity,
                    read.Value.HostCategoryDisplayName,
                    read.Value.Layers,
                    draft,
                    fingerprint.Value!)));
            }
            catch (Exception exception)
            {
                _completed(OperationResult<AssemblyStructurePreview>.Failure(
                    "ASSEMBLY_PREVIEW_FAILED",
                    "複合構造のプレビューを作成できませんでした。",
                    exception.ToString()));
            }
        }
    }

    /// <summary>警告だけは削除し、未解決エラーは画面待ちにせずロールバックします。</summary>
    private sealed class AssemblyFailuresPreprocessor : IFailuresPreprocessor
    {
        public FailureProcessingResult PreprocessFailures(FailuresAccessor failuresAccessor)
        {
            var severity = failuresAccessor.GetSeverity();

            // Revitは失敗が無い状態でもこの処理を呼び、そのときの重大度はNoneです。
            // Noneをエラーと同じ扱いにすると、成功した書込みまで必ず取り消されます。
            // Product_ID側で実際にその事故が起きました（0件書込み／全件失敗）。
            if (severity == FailureSeverity.None)
            {
                return FailureProcessingResult.Continue;
            }

            if (severity == FailureSeverity.Warning)
            {
                failuresAccessor.DeleteAllWarnings();
                return FailureProcessingResult.Continue;
            }

            return failuresAccessor.IsTransactionBeingCommitted()
                ? FailureProcessingResult.ProceedWithRollBack
                : FailureProcessingResult.Continue;
        }
    }

    /// <summary>CommitがPendingで返っても、Revitが終了を通知するまでCSV補償とTransaction解放を延期します。</summary>
    private sealed class AssemblyTransactionFinalizer : ITransactionFinalizer
    {
        private readonly object _gate = new();
        private readonly Transaction _transaction;
        private readonly Action<bool, string, IReadOnlyList<string>> _completed;
        private TransactionStatus? _finalStatus;
        private bool _ownsPendingClosure;
        private bool _isCompleted;
        private string _detail = string.Empty;
        private IReadOnlyList<string> _cleanupFailures = Array.Empty<string>();

        public AssemblyTransactionFinalizer(
            Transaction transaction,
            Action<bool, string, IReadOnlyList<string>> completed)
        {
            _transaction = transaction;
            _completed = completed;
        }

        public void OnCommitted(Document document, string transactionName) =>
            RecordFinalStatus(TransactionStatus.Committed);

        public void OnRolledBack(Document document, string transactionName) =>
            RecordFinalStatus(TransactionStatus.RolledBack);

        public void CompleteAfterCommitReturned(
            TransactionStatus status,
            string detail,
            IEnumerable<string> cleanupFailures)
        {
            var copiedFailures = cleanupFailures.ToArray();
            if (status != TransactionStatus.Pending)
            {
                Complete(status, detail, copiedFailures);
                return;
            }

            TransactionStatus? alreadyFinalized;
            lock (_gate)
            {
                _ownsPendingClosure = true;
                _detail = detail;
                _cleanupFailures = copiedFailures;
                alreadyFinalized = _finalStatus;
            }

            if (alreadyFinalized is TransactionStatus finalStatus)
            {
                Complete(finalStatus, detail, copiedFailures);
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
                Complete(status, _detail, _cleanupFailures);
            }
        }

        private void Complete(
            TransactionStatus status,
            string detail,
            IReadOnlyList<string> cleanupFailures)
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

            try
            {
                // Disposeが失敗しても、この通知から非同期CSV補償へ必ず進みます。
                _completed(
                    status == TransactionStatus.Committed,
                    status == TransactionStatus.Committed
                        ? detail
                        : $"{detail} Revitトランザクションは{status}でした。",
                    allCleanupFailures);
            }
            catch
            {
                // 所有元のBeginCompletionは例外を投げない契約ですが、RevitのFinalizer境界では念のため防ぎます。
            }
        }
    }
}
