// このファイルの目的:
//   複合レイヤーの層ごとのProduct_IDを、選んだ要素へまとめて書き込みます。
//   併せて、Assembly_IDが採番済みならタイプへも同じTransactionで書き込みます
//   （Assembly_IDはタイプに付きます。設計 §4.1）。
//   Ctrl+Z 1回で全部戻せるよう、TransactionGroupと単一Transactionで囲みます。
//
// 既存のProductIdWriteHandlerと同じ規律を守ること:
//   1. 文書の一致はRevitDocumentMatchで判定する。ReferenceEqualsは使わない。
//      同じ文書でも別のラッパーが返り、書き込めなくなる。
//   2. FailureSeverity.Noneは「問題なし」。続行する。
//      これをロールバック扱いにすると、成功した書込みまで全部取り消される。
//   3. 失敗理由は必ず呼び出し側へ返す。件数だけ返すと原因が追えない。
//
// 修正時の影響:
//   上の3つはいずれも過去に事故を起こした箇所です。外さないでください。
//
// RevitActionDispatcherの実際の予約メソッドは同期の Enqueue(IRevitActionRequest) です
// （EnqueueAsyncという名前のメソッドは存在しません）。
// このハンドラの公開契約はTask<OperationResult<bool>>を返す必要があるため、
// 同期Enqueueの結果をTask.FromResultで包んでいます。
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Bui.Assemblies;
using Bui.Common;
using Bui.Products;
using Bui.Resources;
using Bui.Revit.ExternalEvents;
using Bui.Revit.UI;

namespace Bui.Revit.Products;

/// <summary>1つの層スロットへ書き込むProduct_IDです。</summary>
public sealed record LayerAssignmentRequest(LayerSlot Slot, string ProductId);

/// <summary>書込み前に画面へ見せる内容です。</summary>
public sealed record LayerProductIdPreview(
    Document SourceDocument,
    string SourceDocumentTitle,
    IReadOnlyList<long> TargetElementIds,
    IReadOnlyList<LayerAssignmentRequest> Assignments,
    string LayerWarning,
    /// <summary>採番済みのAssembly_ID。付けない場合は空文字。</summary>
    string AssemblyId,
    /// <summary>Assembly_IDを付けない理由。付ける場合は空文字。</summary>
    string AssemblyIdSkipReason);

/// <summary>書込みの結果です。</summary>
public sealed record LayerProductIdWriteReport(
    int WrittenElementCount,
    IReadOnlyList<string> FailureReasons,
    bool AssemblyIdWritten,
    /// <summary>既存値と選択値が異なり、上書き確認が無かったために書かなかった要素数です。</summary>
    int ConflictsSkippedElementCount);

/// <summary>層ごとのProduct_IDを1つのTransactionで書き込みます。</summary>
public sealed class LayerProductIdWriteHandler : IRevitActionRequest
{
    private readonly LayerProductIdPreview _preview;
    private readonly bool _conflictOverwriteConfirmed;
    private readonly Action<OperationResult<LayerProductIdWriteReport>> _completed;

    private LayerProductIdWriteHandler(
        LayerProductIdPreview preview,
        bool conflictOverwriteConfirmed,
        Action<OperationResult<LayerProductIdWriteReport>> completed)
    {
        _preview = preview;
        _conflictOverwriteConfirmed = conflictOverwriteConfirmed;
        _completed = completed;
    }

    public string Name => "層Product_ID書込み";

    /// <summary>
    /// RevitActionDispatcherの予約メソッドは同期のEnqueueです（EnqueueAsyncは存在しません）。
    /// 呼び出し側の契約（Task<OperationResult<bool>>）は保ったまま、その結果をそのまま包んで返します。
    /// </summary>
    /// <param name="conflictOverwriteConfirmed">
    /// 既存値と選択値が異なる層への上書きを、利用者が明示的に確認したかどうかです。
    /// falseの場合、そのような層は書かずに件数だけ報告します（ProductIdWriteHandlerと同じ規律）。
    /// </param>
    public static Task<OperationResult<bool>> EnqueueWriteAsync(
        RevitActionDispatcher dispatcher,
        LayerProductIdPreview preview,
        bool conflictOverwriteConfirmed,
        Action<OperationResult<LayerProductIdWriteReport>> completed,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(dispatcher);
        ArgumentNullException.ThrowIfNull(preview);
        ArgumentNullException.ThrowIfNull(completed);
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(dispatcher.Enqueue(
            new LayerProductIdWriteHandler(preview, conflictOverwriteConfirmed, completed)));
    }

    public void Execute(UIApplication application)
    {
        ArgumentNullException.ThrowIfNull(application);
        try
        {
            var document = application.ActiveUIDocument?.Document;

            // 参照比較だけでは、同じモデルでもラッパーが取り直されると不一致になります。
            // 判定の詳細と理由は RevitDocumentMatch を参照してください。
            if (!RevitDocumentMatch.IsSame(document, _preview.SourceDocument))
            {
                _completed(OperationResult<LayerProductIdWriteReport>.Failure(
                    "REVIT-DOCUMENT-CHANGED",
                    "プレビュー後にRevitドキュメントが切り替わったため、書き込みませんでした。"
                        + RevitDocumentMatch.DescribeMismatch(document, _preview.SourceDocument),
                    Name));
                return;
            }

            WriteBatch(document!);
        }
        catch (Exception exception)
        {
            _completed(OperationResult<LayerProductIdWriteReport>.Failure(
                "LAYER-PRODUCT-ID-WRITE-FAILED",
                "層Product_IDの書込みを開始できませんでした。",
                exception.ToString()));
        }
    }

    private void WriteBatch(Document document)
    {
        // SortedSetで重複を排除します。170要素×6スロットで同じ理由が並ぶと、
        // 利用者には同じ一文が千行以上渡り、原因を読み取れなくなります。
        var failures = new SortedSet<string>(StringComparer.Ordinal);
        var written = 0;
        // 既存値と選択値が異なり、上書き確認が無かったために書かなかった要素です。
        // 「書込不可の理由」とは別に数えます。パラメータが無い等の失敗ではなく、
        // 利用者の確認待ちで意図的に見送ったものだからです。
        var conflictSkippedElementIds = new HashSet<long>();

        using var group = new TransactionGroup(document, "BUI 層Product_ID付与");
        if (group.Start() != TransactionStatus.Started)
        {
            _completed(OperationResult<LayerProductIdWriteReport>.Success(
                new LayerProductIdWriteReport(0, new[] { "TransactionGroupを開始できませんでした。" }, false, 0)));
            return;
        }

        using var transaction = new Transaction(document, "層Product_IDを書込み");
        if (transaction.Start() != TransactionStatus.Started)
        {
            group.RollBack();
            _completed(OperationResult<LayerProductIdWriteReport>.Success(
                new LayerProductIdWriteReport(0, new[] { "Transactionを開始できませんでした。" }, false, 0)));
            return;
        }

        var preprocessor = new LayerFailuresPreprocessor();
        var options = transaction.GetFailureHandlingOptions();
        options.SetForcedModalHandling(true).SetClearAfterRollback(true).SetFailuresPreprocessor(preprocessor);
        transaction.SetFailureHandlingOptions(options);

        foreach (var elementId in _preview.TargetElementIds.Distinct())
        {
            if (TryWriteOne(document, elementId, failures, conflictSkippedElementIds)) written++;
        }

        // Assembly_IDはタイプに付きます（設計 §4.1）。
        // 層IDは要素ごとなので、同じタイプの要素で揃っていないと番号が嘘になります。
        // 揃っているかの判定は画面側で済ませ、ここでは書くかどうかだけを見ます。
        // AssemblyIdSkipReasonは失敗ではなく「採番を見送った」という設計どおりの結果なので、
        // failuresには入れません（プレビュー画面のSummaryで既に伝えています）。
        var assemblyIdWritten = false;
        if (_preview.AssemblyId.Length > 0)
        {
            assemblyIdWritten = TryWriteAssemblyId(document, failures);
        }

        if (transaction.Commit() != TransactionStatus.Committed)
        {
            if (group.GetStatus() == TransactionStatus.Started) group.RollBack();
            failures.Add("Transactionを確定できず、全変更を取り消しました。" + preprocessor.DescribeErrors());
            _completed(OperationResult<LayerProductIdWriteReport>.Success(
                new LayerProductIdWriteReport(0, failures.ToArray(), false, 0)));
            return;
        }

        // 子Transaction終了後のAssimilateで、Undo一覧では一つのBUI操作になります。
        if (group.Assimilate() != TransactionStatus.Committed)
        {
            // Commitは成功済みです。ここでロールバックしないと、モデルには値が残ったまま
            // 「0件書き込みました」と報告することになり、画面とモデルが食い違います。
            if (group.GetStatus() == TransactionStatus.Started) group.RollBack();
            failures.Add("TransactionGroupを確定できませんでした。");
            _completed(OperationResult<LayerProductIdWriteReport>.Success(
                new LayerProductIdWriteReport(0, failures.ToArray(), false, 0)));
            return;
        }

        _completed(OperationResult<LayerProductIdWriteReport>.Success(
            new LayerProductIdWriteReport(written, failures.ToArray(), assemblyIdWritten, conflictSkippedElementIds.Count)));
    }

    /// <summary>対象要素のタイプへAssembly_IDを書きます。タイプは要素間で共有されます。</summary>
    private bool TryWriteAssemblyId(Document document, SortedSet<string> failures)
    {
        // ElementId(0)はRevit 2025のlong基盤のIDでは「無効」の予約値ではありません
        // （無効値は ElementId.InvalidElementId、Value == -1）。0は理論上有効なIDになり得るため、
        // 「対象が無い」判定は先頭要素の値では行わず、リストが空かどうかで直接判定します。
        if (_preview.TargetElementIds.Count == 0)
        {
            failures.Add("Assembly_IDを書く対象がありません。");
            return false;
        }

        var firstElementId = _preview.TargetElementIds[0];
        var element = document.GetElement(new ElementId(firstElementId));
        var typeId = element?.GetTypeId() ?? ElementId.InvalidElementId;
        if (typeId == ElementId.InvalidElementId || document.GetElement(typeId) is not Element type)
        {
            failures.Add("Assembly_IDを書くタイプを取得できませんでした。");
            return false;
        }

        var parameter = type.get_Parameter(Identifiers.AssemblyIdParameterGuid);
        if (parameter is null)
        {
            failures.Add("Assembly_IDパラメータがありません（タイプに共有パラメータが割り当てられていません）。");
            return false;
        }

        if (parameter.IsReadOnly)
        {
            failures.Add("Assembly_IDパラメータが読み取り専用です。");
            return false;
        }

        if (!parameter.Set(_preview.AssemblyId))
        {
            failures.Add("RevitがAssembly_IDの設定を受け付けませんでした。");
            return false;
        }

        return true;
    }

    private bool TryWriteOne(
        Document document,
        long elementId,
        SortedSet<string> failures,
        ISet<long> conflictSkippedElementIds)
    {
        try
        {
            var element = document.GetElement(new ElementId(elementId));
            if (element is null)
            {
                failures.Add("要素が見つかりません。");
                return false;
            }

            var wroteAny = false;
            foreach (var assignment in _preview.Assignments)
            {
                var slotIndex = (int)assignment.Slot;
                if (slotIndex < 0 || slotIndex >= Identifiers.LayerProductIdParameterGuids.Count)
                {
                    // C#はどんな整数もLayerSlotへキャストできてしまいます。ガード無しでは
                    // 説明の無いIndexOutOfRangeExceptionになり、次のUIタスクに罠を残します。
                    failures.Add($"未対応の層スロットです（値: {slotIndex}）。");
                    continue;
                }

                var guid = Identifiers.LayerProductIdParameterGuids[slotIndex];
                var parameter = element.get_Parameter(guid);
                if (parameter is null)
                {
                    failures.Add(
                        $"「{LayerSlotNames.Of(assignment.Slot)}」パラメータがありません"
                            + "（このカテゴリに共有パラメータが割り当てられていません）。");
                    continue;
                }

                if (parameter.IsReadOnly)
                {
                    failures.Add($"「{LayerSlotNames.Of(assignment.Slot)}」が読み取り専用です。");
                    continue;
                }

                // 行はアンカー要素の現在値で初期化されるため、触っていない層でも値を持ちます（Task 7）。
                // 「同一タイプすべて」では、その値が異なる既存値を無条件に上書きしかねません。
                // プレビュー側の確認（LayerTargetSurvey.HasConflictingExistingValue）を、ここで実際に守ります。
                var outcome = LayerSlotWriteDecision.Classify(parameter.AsString(), assignment.ProductId);
                if (outcome == LayerSlotWriteOutcome.AlreadySame)
                {
                    // 既に同じ値です。書き直さず既存の履歴を保護します。
                    continue;
                }

                if (outcome == LayerSlotWriteOutcome.Conflict && !_conflictOverwriteConfirmed)
                {
                    conflictSkippedElementIds.Add(elementId);
                    continue;
                }

                if (!parameter.Set(assignment.ProductId))
                {
                    failures.Add($"Revitが「{LayerSlotNames.Of(assignment.Slot)}」の設定を受け付けませんでした。");
                    continue;
                }

                wroteAny = true;
            }

            return wroteAny;
        }
        catch (Exception exception)
        {
            // ここで捕まえないと、1要素の例外がforeach全体を止め、
            // それまでに集めた失敗理由を全部捨ててしまいます。
            failures.Add(exception.Message);
            return false;
        }
    }

    /// <summary>
    /// FailureSeverity.Noneは「失敗が無い」状態です。続行しなければなりません。
    /// これをロールバック扱いにすると、成功した書込みまで全部取り消されます。
    /// 過去に実際その事故を起こしました。
    /// </summary>
    private sealed class LayerFailuresPreprocessor : IFailuresPreprocessor
    {
        private readonly SortedSet<string> _errors = new(StringComparer.Ordinal);

        public FailureProcessingResult PreprocessFailures(FailuresAccessor failuresAccessor)
        {
            ArgumentNullException.ThrowIfNull(failuresAccessor);
            var severity = failuresAccessor.GetSeverity();
            if (severity == FailureSeverity.None) return FailureProcessingResult.Continue;
            if (severity == FailureSeverity.Warning)
            {
                failuresAccessor.DeleteAllWarnings();
                return FailureProcessingResult.Continue;
            }

            foreach (var message in failuresAccessor.GetFailureMessages())
            {
                var description = message.GetDescriptionText();
                if (!string.IsNullOrWhiteSpace(description)) _errors.Add(description.Trim());
            }

            return failuresAccessor.IsTransactionBeingCommitted()
                ? FailureProcessingResult.ProceedWithRollBack
                : FailureProcessingResult.Continue;
        }

        public string DescribeErrors() =>
            _errors.Count == 0 ? string.Empty : $" Revitからのエラー: {string.Join(" / ", _errors.Take(3))}";
    }
}
