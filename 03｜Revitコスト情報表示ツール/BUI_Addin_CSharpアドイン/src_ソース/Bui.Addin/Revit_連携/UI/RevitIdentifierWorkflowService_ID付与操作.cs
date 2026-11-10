// このファイルは、既存の4つのRevit書込み機能を、画面の「プレビュー→確認後の実行」契約へ接続します。
// ProductIdWriteHandler、MaterialProductIdService、AssemblyIdWriteHandler、LayerProductIdWriteHandler、CSVリポジトリに依存し、変更時はExternalEvent外でRevit APIを触らないことに注意してください。
//
// 層ごとのProduct_ID（ILayerAssignmentService）について、外してはいけない規律:
//   1. 書込み対象は必ず「1つのタイプだけ」にすること。
//      Assembly_IDはタイプに付くため（設計 §4.1）、対象が2タイプにまたがると
//      LayerProductIdWriteHandlerは先頭要素のタイプにしか番号を書けず、
//      もう一方は無印のまま、警告も出ずに残ります。
//      そのため、選択に複数タイプが混ざっている場合はプレビュー自体を断ります。
//   2. Summaryには必ず「どのパラメータへ、どのIDが、どの材料の層として入るか」を並べること。
//      室内側がどちら端かはRevitのある環境でしか検証できません。この表示が唯一の歯止めです。
using System.Collections.Concurrent;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Bui.Assemblies;
using Bui.Common;
using Bui.Resources;
using Bui.Products;
using Bui.Data;
using Bui.Revit.Assemblies;
using Bui.Revit.AssemblyWrites;
using Bui.Revit.ExternalEvents;
using Bui.Revit.Products;
using Bui.UI;

namespace Bui.Revit.UI;

/// <summary>プレビューした実体を一回限り保持し、表示していない書込みを拒否します。</summary>
public sealed class RevitIdentifierWorkflowService : IIdentifierWorkflowService, ILayerAssignmentService
{
    private readonly IProductRepository _products;
    private readonly IAssemblyRepository _assemblies;
    private readonly RevitActionDispatcher _dispatcher;
    private readonly MaterialProductIdService _materials = new();
    private readonly ConcurrentDictionary<string, object> _pending = new(StringComparer.Ordinal);

    public RevitIdentifierWorkflowService(
        IProductRepository products,
        IAssemblyRepository assemblies,
        RevitActionDispatcher dispatcher)
    {
        _products = products ?? throw new ArgumentNullException(nameof(products));
        _assemblies = assemblies ?? throw new ArgumentNullException(nameof(assemblies));
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
    }

    public async Task<OperationResult<IdentifierWorkflowPreview>> PreviewProductAsync(
        SelectionSnapshot selection,
        string productId,
        ProductAssignmentScope scope,
        bool sameTypeOnly,
        CancellationToken cancellationToken)
    {
        if (!selection.HasDocument)
            return FailPreview("PRODUCT_ID_DOCUMENT_REQUIRED", "Revitでプロジェクトを開いてから実行してください。");
        // 表示中ビュー・プロジェクト全体は収集器で対象を決めるため選択を必須にしません。
        // 選択範囲と同タイプ絞込みだけが、起点となる選択要素を必要とします。
        if ((scope == ProductAssignmentScope.Selection || sameTypeOnly) && selection.ElementIds.Count == 0)
            return FailPreview("PRODUCT_ID_SELECTION_REQUIRED", "対象範囲「選択中の要素」と「同じタイプだけ」には、要素の選択が必要です。");

        var query = new ProductIdTargetQuery(
            scope switch
            {
                ProductAssignmentScope.Selection => ProductIdTargetScope.Selection,
                ProductAssignmentScope.ActiveView => ProductIdTargetScope.ActiveView,
                _ => ProductIdTargetScope.FullProject
            },
            selection.ElementIds,
            sameTypeOnly ? selection.ElementIds[0] : null);
        var completion = NewCompletion<ProductIdAssignmentPreview>();
        var enqueue = await ProductIdWriteHandler.EnqueuePreviewAsync(
            _products, _dispatcher, productId, query, result => completion.TrySetResult(result), cancellationToken).ConfigureAwait(false);
        if (!enqueue.IsSuccess) return CopyFailure<IdentifierWorkflowPreview>(enqueue.Error!);
        var result = await completion.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        if (!result.IsSuccess) return CopyFailure<IdentifierWorkflowPreview>(result.Error!);

        var preview = result.Value!;
        var token = Remember(preview);
        var plan = preview.Plan;
        return OperationResult<IdentifierWorkflowPreview>.Success(new(
            token,
            IdentifierWorkflowKind.InstanceProductId,
            "Product_ID付与の確認",
            $"提案ID: {plan.TargetProductId}{Environment.NewLine}"
                + $"空欄へ付与: {plan.ToWrite.Count}件 / 同一ID: {plan.AlreadySame.Count}件 / "
                + $"異なるID: {plan.Conflicts.Count}件 / 書込不可: {plan.NotWritable.Count}件"
                // 書込不可の件数だけでは何を直せばよいか分かりません。理由も出します。
                + IdentifierWriteSummary.DescribeNotWritable(preview.NotWritableReasons)
                + DescribeScope(scope, sameTypeOnly),
            plan.Conflicts.Count > 0));
    }

    /// <summary>
    /// 対象範囲がプロジェクト全体で、同じタイプへの限定が無い場合は、
    /// プロジェクト内の全要素へ同じProduct_IDを書こうとしています。
    /// 件数だけ見て「はい」を押すと取り返しがつかないため、明示的に警告します。
    /// </summary>
    private static string DescribeScope(ProductAssignmentScope scope, bool sameTypeOnly)
    {
        if (scope != ProductAssignmentScope.FullProject || sameTypeOnly) return string.Empty;
        return $"{Environment.NewLine}※ 対象範囲がプロジェクト全体で、同じタイプへの限定がありません。"
            + "プロジェクト内のすべての対象要素に同じProduct_IDを書き込もうとしています。";
    }

    public async Task<OperationResult<IdentifierWorkflowPreview>> PreviewMaterialAsync(
        long materialElementId,
        string productId,
        CancellationToken cancellationToken)
    {
        var product = await _products.GetByIdAsync(productId.Trim(), cancellationToken).ConfigureAwait(false);
        if (product is null)
            return FailPreview("PRODUCT_ID_NOT_FOUND", "指定したProduct_IDは製品CSVにありません。");

        var completion = NewCompletion<MaterialProductIdPreview>();
        var enqueue = _materials.EnqueuePreview(
            _dispatcher, materialElementId, product.ProductId, result => completion.TrySetResult(result));
        if (!enqueue.IsSuccess) return CopyFailure<IdentifierWorkflowPreview>(enqueue.Error!);
        var result = await completion.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        if (!result.IsSuccess) return CopyFailure<IdentifierWorkflowPreview>(result.Error!);

        var preview = result.Value!;
        var token = Remember(preview);
        return OperationResult<IdentifierWorkflowPreview>.Success(new(
            token,
            IdentifierWorkflowKind.MaterialProductId,
            "材料Product_ID付与の確認",
            $"提案ID: {preview.RequestedProductId}\n現在値: {(string.IsNullOrWhiteSpace(preview.CurrentProductId) ? "(空欄)" : preview.CurrentProductId)}\nこの材料を使うホストタイプ: {preview.AffectedHostTypes.Count}件",
            preview.RequiresConflictConfirmation));
    }

    public async Task<OperationResult<IdentifierWorkflowPreview>> PreviewAssemblyAsync(
        long hostElementId,
        CancellationToken cancellationToken)
    {
        var structureCompletion = NewCompletion<AssemblyStructurePreview>();
        var enqueue = AssemblyIdWriteHandler.EnqueueStructurePreview(
            _dispatcher, hostElementId, result => structureCompletion.TrySetResult(result));
        if (!enqueue.IsSuccess) return CopyFailure<IdentifierWorkflowPreview>(enqueue.Error!);
        var structure = await structureCompletion.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        if (!structure.IsSuccess) return CopyFailure<IdentifierWorkflowPreview>(structure.Error!);
        var plan = await AssemblyIdWriteHandler.PlanAsync(_assemblies, structure.Value!, cancellationToken).ConfigureAwait(false);
        if (!plan.IsSuccess) return CopyFailure<IdentifierWorkflowPreview>(plan.Error!);

        var preview = plan.Value!;
        var token = Remember(preview);
        return OperationResult<IdentifierWorkflowPreview>.Success(new(
            token,
            IdentifierWorkflowKind.AssemblyId,
            "Assembly_ID付与の確認",
            $"提案ID: {preview.Plan.Record.AssemblyId}\n判定: {(preview.Plan.ReusesExistingAssembly ? "既存構成を再利用" : "新規構成をCSV登録")}\nレイヤー: {preview.Plan.Record.OrderedLayers.Count}層\n対象タイプID: {preview.Structure.HostTypeElementId}",
            false));
    }

    public async Task<OperationResult<string>> ApplyAsync(
        string previewToken,
        bool conflictOverwriteConfirmed,
        CancellationToken cancellationToken)
    {
        // tokenは最初の実行試行で破棄し、再実行時は必ず最新値のプレビューを求めます。
        if (!_pending.TryRemove(previewToken ?? string.Empty, out var stored))
            return OperationResult<string>.Failure("IDENTIFIER_PREVIEW_EXPIRED", "プレビューが古いため、再度確認してください。", "Unknown one-shot token.");

        return stored switch
        {
            ProductIdAssignmentPreview product => await ApplyProductAsync(product, conflictOverwriteConfirmed, cancellationToken).ConfigureAwait(false),
            MaterialProductIdPreview material => await ApplyMaterialAsync(material, conflictOverwriteConfirmed, cancellationToken).ConfigureAwait(false),
            AssemblyIdApplicationPreview assembly => await ApplyAssemblyAsync(assembly, cancellationToken).ConfigureAwait(false),
            LayerAssignmentApplication layer => await ApplyLayerAsync(layer, conflictOverwriteConfirmed, cancellationToken).ConfigureAwait(false),
            _ => OperationResult<string>.Failure("IDENTIFIER_PREVIEW_INVALID", "プレビューを実行できません。", stored.GetType().FullName ?? stored.GetType().Name)
        };
    }

    private async Task<OperationResult<string>> ApplyProductAsync(ProductIdAssignmentPreview preview, bool confirmed, CancellationToken cancellationToken)
    {
        var completion = NewCompletion<ProductIdWriteReport>();
        var enqueue = await ProductIdWriteHandler.EnqueueWriteAsync(
            _products, _dispatcher, preview, confirmed, result => completion.TrySetResult(result), cancellationToken).ConfigureAwait(false);
        if (!enqueue.IsSuccess) return CopyFailure<string>(enqueue.Error!);
        var result = await completion.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        // 失敗の件数だけを返すと、原因が分かっているのに利用者へ伝わりません。
        // 書込み処理が持っている理由をそのまま画面へ渡します。
        return result.IsSuccess
            ? OperationResult<string>.Success(IdentifierWriteSummary.Describe(
                result.Value!.WrittenElementIds.Count,
                result.Value.Failures.Select(failure => failure.Reason).ToArray()))
            : CopyFailure<string>(result.Error!);
    }

    private async Task<OperationResult<string>> ApplyMaterialAsync(MaterialProductIdPreview preview, bool confirmed, CancellationToken cancellationToken)
    {
        var completion = NewCompletion<MaterialProductIdWriteReport>();
        var enqueue = _materials.EnqueueWrite(_dispatcher, preview, confirmed, result => completion.TrySetResult(result));
        if (!enqueue.IsSuccess) return CopyFailure<string>(enqueue.Error!);
        var result = await completion.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        return result.IsSuccess
            ? OperationResult<string>.Success(result.Value!.WasWritten
                ? $"材料Product_IDを書き込みました。影響ホストタイプ: {result.Value.AffectedHostTypeCount}件"
                : "材料Product_IDは同じ値のため変更しませんでした。")
            : CopyFailure<string>(result.Error!);
    }

    private async Task<OperationResult<string>> ApplyAssemblyAsync(AssemblyIdApplicationPreview preview, CancellationToken cancellationToken)
    {
        var completion = NewCompletion<AssemblyIdWriteReport>();
        var enqueue = await AssemblyIdWriteHandler.EnqueueWriteAsync(
            _assemblies, _dispatcher, preview, newRegistrationConfirmed: true,
            result => completion.TrySetResult(result), cancellationToken).ConfigureAwait(false);
        if (!enqueue.IsSuccess) return CopyFailure<string>(enqueue.Error!);
        var result = await completion.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        return result.IsSuccess
            ? OperationResult<string>.Success($"Assembly_ID「{result.Value!.AssemblyId}」の処理が完了しました。{result.Value.Detail}")
            : CopyFailure<string>(result.Error!);
    }

    // ---- ILayerAssignmentService（層ごとのProduct_ID） ----

    public async Task<OperationResult<LayerAssignmentView>> LoadLayersAsync(
        SelectionSnapshot selection,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(selection);
        if (!selection.HasDocument)
        {
            return OperationResult<LayerAssignmentView>.Failure(
                "LAYER_ASSIGNMENT_DOCUMENT_REQUIRED",
                "Revitでプロジェクトを開いてから実行してください。",
                "LAYER_ASSIGNMENT_DOCUMENT_REQUIRED");
        }

        if (selection.ElementIds.Count == 0)
        {
            return OperationResult<LayerAssignmentView>.Failure(
                "LAYER_ASSIGNMENT_SELECTION_REQUIRED",
                "層ごとのProduct_IDを付けるには、壁・床・天井・屋根を1つ選択してください。",
                "LAYER_ASSIGNMENT_SELECTION_REQUIRED");
        }

        var completion = NewCompletion<LayerAssignmentView>();
        var enqueue = _dispatcher.Enqueue(new LayerLoadRequest(
            selection.ElementIds[0], result => completion.TrySetResult(result)));
        if (!enqueue.IsSuccess) return CopyFailure<LayerAssignmentView>(enqueue.Error!);
        return await completion.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<OperationResult<IdentifierWorkflowPreview>> PreviewLayerAssignmentAsync(
        SelectionSnapshot selection,
        IReadOnlyList<LayerAssignmentRow> rows,
        ProductAssignmentTarget target,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(selection);
        ArgumentNullException.ThrowIfNull(rows);
        if (!selection.HasDocument)
            return FailPreview("LAYER_ASSIGNMENT_DOCUMENT_REQUIRED", "Revitでプロジェクトを開いてから実行してください。");
        if (selection.ElementIds.Count == 0)
            return FailPreview("LAYER_ASSIGNMENT_SELECTION_REQUIRED", "層ごとのProduct_IDを付けるには、壁・床・天井・屋根を1つ選択してください。");
        if (rows.Count == 0)
            return FailPreview("LAYER_ASSIGNMENT_ROWS_REQUIRED", "層の表がありません。先に層を読み取ってください。");
        if (rows.All(row => string.IsNullOrWhiteSpace(row.SelectedProductId)))
            return FailPreview("LAYER_ASSIGNMENT_PRODUCT_ID_REQUIRED", "書き込むProduct_IDが1つも選ばれていません。");

        // 以降はすべて「室内側から外側」の順で扱います。
        // AssemblyIdEligibilityとAssemblyFingerprintがこの順序を前提にしています。
        var ordered = rows.OrderBy(row => (int)row.Slot).ToArray();
        if (ordered.Select(row => row.Slot).Distinct().Count() != ordered.Length)
        {
            return FailPreview(
                "LAYER_ASSIGNMENT_DUPLICATE_SLOT",
                "同じパラメータへ2つの層が割り当てられています。層を読み直してください。");
        }

        var query = target == ProductAssignmentTarget.SameTypeAll
            ? new ProductIdTargetQuery(ProductIdTargetScope.FullProject, selection.ElementIds, selection.ElementIds[0])
            : new ProductIdTargetQuery(ProductIdTargetScope.Selection, selection.ElementIds, null);

        var completion = NewCompletion<LayerTargetSurvey>();
        var enqueue = _dispatcher.Enqueue(new LayerTargetSurveyRequest(
            query, selection.ElementIds[0], ordered, result => completion.TrySetResult(result)));
        if (!enqueue.IsSuccess) return CopyFailure<IdentifierWorkflowPreview>(enqueue.Error!);
        var surveyResult = await completion.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        if (!surveyResult.IsSuccess) return CopyFailure<IdentifierWorkflowPreview>(surveyResult.Error!);
        var survey = surveyResult.Value!;

        var (assemblyId, skipReason, registration) =
            await PlanAssemblyIdAsync(ordered, survey, cancellationToken).ConfigureAwait(false);

        // 値が空の行は書きません。空を書くと、既に入っている値を消すことになります。
        var assignments = ordered
            .Where(row => !string.IsNullOrWhiteSpace(row.SelectedProductId))
            .Select(row => new LayerAssignmentRequest(row.Slot, row.SelectedProductId.Trim()))
            .ToArray();

        var preview = new LayerProductIdPreview(
            survey.SourceDocument,
            survey.SourceDocumentTitle,
            survey.TargetElementIds,
            assignments,
            survey.LayerWarning,
            assemblyId,
            skipReason);
        var token = Remember(new LayerAssignmentApplication(preview, registration));
        return OperationResult<IdentifierWorkflowPreview>.Success(new(
            token,
            IdentifierWorkflowKind.LayerProductId,
            "層Product_ID付与の確認",
            LayerAssignmentSummary.Describe(
                survey.TargetElementIds.Count,
                target,
                ordered,
                assemblyId,
                skipReason,
                survey.LayerWarning),
            survey.HasConflictingExistingValue));
    }

    /// <summary>
    /// 採番してよいかを判定し、よければ既存構成の再利用か新規候補かを決めます。
    /// 同じ構成が既にCSVにあるのに新しい番号を振ると、同じ構成へ2つの番号が付きます。
    /// そのためAssemblyIdAllocatorを直接呼ばず、照合を含むAssemblyRegistrationPlannerを通します。
    /// </summary>
    private async Task<(string AssemblyId, string SkipReason, AssemblyRegistrationPlan? Registration)> PlanAssemblyIdAsync(
        IReadOnlyList<LayerAssignmentRow> ordered,
        LayerTargetSurvey survey,
        CancellationToken cancellationToken)
    {
        var decision = AssemblyIdEligibility.Evaluate(survey.ProjectedLayerIdsPerElement);
        if (!decision.CanAssign) return (string.Empty, decision.Reason, null);

        if (survey.HostCategoryIdentity.Length == 0)
        {
            return (
                string.Empty,
                "ホストカテゴリの安定IDを読み取れないため、Assembly_IDは付けませんでした。",
                null);
        }

        // CanAssignは「全対象要素の層IDが一致する」ことを意味するため、先頭の並びが構成そのものです。
        var projected = survey.ProjectedLayerIdsPerElement[0];
        // function列にはRevitの層の機能（Finish1・Substrateなど）を入れます。
        // 以前はここにSlotName（"Product_ID（仕上）"）を渡しており、
        // 構成CSVのfunction列にパラメータ名が並んでいました。
        var layers = ordered
            .Select((row, index) => new AssemblyLayerRecord(
                index,
                row.LayerFunction,
                row.ThicknessMillimeters,
                projected[index],
                row.MaterialName))
            .ToArray();
        var draft = new AssemblyRecord(
            string.Empty,
            survey.HostCategoryIdentity,
            layers,
            null,
            new Dictionary<string, string>(StringComparer.Ordinal));
        var plan = await AssemblyRegistrationPlanner
            .PlanAsync(draft, _assemblies, cancellationToken)
            .ConfigureAwait(false);
        return plan.IsSuccess
            ? (plan.Value!.Record.AssemblyId, string.Empty, plan.Value)
            : (string.Empty, plan.Error!.UserMessage, null);
    }

    private async Task<OperationResult<string>> ApplyLayerAsync(
        LayerAssignmentApplication application,
        bool conflictOverwriteConfirmed,
        CancellationToken cancellationToken)
    {
        var preview = application.Preview;
        var registration = application.Registration;
        var csvRecordAdded = false;

        if (registration is not null)
        {
            try
            {
                if (registration.ReusesExistingAssembly)
                {
                    var reuse = await AssemblyReuseValidator
                        .ValidateAsync(registration, _assemblies, cancellationToken)
                        .ConfigureAwait(false);
                    if (!reuse.IsSuccess) return CopyFailure<string>(reuse.Error!);
                }
                else
                {
                    var nowExisting = await _assemblies
                        .FindByFingerprintAsync(registration.Fingerprint, cancellationToken)
                        .ConfigureAwait(false);
                    if (nowExisting is not null)
                    {
                        // 確認中に同じ構成が登録された場合は、番号を増やさず既存を使います。
                        preview = preview with { AssemblyId = nowExisting.AssemblyId };
                    }
                    else
                    {
                        // 副作用開始後のキャンセルで「追加済みか不明」を作らないため、この追加は完遂させます。
                        await _assemblies
                            .AddAsync(registration.Record, registration.Fingerprint, CancellationToken.None)
                            .ConfigureAwait(false);
                        csvRecordAdded = true;
                    }
                }
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                return OperationResult<string>.Failure(
                    "ASSEMBLY_CSV_PREPARE_FAILED",
                    "Assembly_IDのCSV準備に失敗したため、Revitへは書き込みません。",
                    exception.ToString());
            }
        }

        var completion = NewCompletion<LayerProductIdWriteReport>();
        var enqueue = await LayerProductIdWriteHandler.EnqueueWriteAsync(
            _dispatcher, preview, conflictOverwriteConfirmed, result => completion.TrySetResult(result), cancellationToken).ConfigureAwait(false);
        if (!enqueue.IsSuccess)
        {
            await CompensateAssemblyRowAsync(csvRecordAdded, registration).ConfigureAwait(false);
            return CopyFailure<string>(enqueue.Error!);
        }

        var result = await completion.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        if (!result.IsSuccess)
        {
            await CompensateAssemblyRowAsync(csvRecordAdded, registration).ConfigureAwait(false);
            return CopyFailure<string>(result.Error!);
        }

        var report = result.Value!;
        var compensated = false;
        if (csvRecordAdded && !report.AssemblyIdWritten)
        {
            // Revitへ書けなかった新規行をCSVへ残すと、モデルに存在しない構成が採番済みとして居座ります。
            compensated = await CompensateAssemblyRowAsync(true, registration).ConfigureAwait(false);
        }

        var message = $"層Product_IDを{report.WrittenElementCount}件へ書き込みました。";
        if (report.AssemblyIdWritten)
        {
            message += $" Assembly_ID「{preview.AssemblyId}」をタイプへ書き込みました。";
        }

        if (compensated)
        {
            message += " Assembly_IDは書き込めなかったため、CSVへ追加した行を取り消しました。";
        }

        return OperationResult<string>.Success(
            message
            + IdentifierWriteSummary.DescribeConflictSkip(report.ConflictsSkippedElementCount)
            + IdentifierWriteSummary.DescribeNotWritable(report.FailureReasons));
    }

    /// <summary>Revitへ書けなかった新規Assembly_ID行だけを取り消します。既存再利用行は決して消しません。</summary>
    private async Task<bool> CompensateAssemblyRowAsync(bool csvRecordAdded, AssemblyRegistrationPlan? registration)
    {
        if (!csvRecordAdded || registration is null || registration.ReusesExistingAssembly) return false;
        try
        {
            await _assemblies.RemoveAsync(registration.Record.AssemblyId, CancellationToken.None).ConfigureAwait(false);
            return true;
        }
        catch (Exception)
        {
            // 補償に失敗しても、書込み結果の報告そのものは利用者へ返します。
            return false;
        }
    }

    private string Remember(object preview)
    {
        var token = Guid.NewGuid().ToString("N");
        _pending[token] = preview;
        return token;
    }

    private static TaskCompletionSource<OperationResult<T>> NewCompletion<T>() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private static OperationResult<IdentifierWorkflowPreview> FailPreview(string code, string message) =>
        OperationResult<IdentifierWorkflowPreview>.Failure(code, message, code);

    private static OperationResult<T> CopyFailure<T>(OperationError error) =>
        OperationResult<T>.Failure(error.Code, error.UserMessage, error.TechnicalDetail);

    /// <summary>選択要素1つの複合構造を読み、層とパラメータの対応表を作ります。書き込みはしません。</summary>
    private sealed class LayerLoadRequest : IRevitActionRequest
    {
        private readonly long _elementId;
        private readonly Action<OperationResult<LayerAssignmentView>> _completed;

        public LayerLoadRequest(long elementId, Action<OperationResult<LayerAssignmentView>> completed)
        {
            _elementId = elementId;
            _completed = completed;
        }

        public string Name => "層Product_ID表の読取";

        public void Execute(UIApplication application)
        {
            ArgumentNullException.ThrowIfNull(application);
            try
            {
                var document = application.ActiveUIDocument?.Document;
                if (document is null)
                {
                    _completed(OperationResult<LayerAssignmentView>.Failure(
                        "REVIT-DOCUMENT-NOT-FOUND", "層を読み取るRevitドキュメントがありません。", Name));
                    return;
                }

                var element = document.GetElement(new ElementId(_elementId));
                if (element is null)
                {
                    _completed(OperationResult<LayerAssignmentView>.Failure(
                        "LAYER_ASSIGNMENT_ELEMENT_NOT_FOUND",
                        "選択した要素が見つかりません。選び直してください。",
                        $"Element {_elementId}"));
                    return;
                }

                _completed(LayerAssignmentReader.BuildView(document, element));
            }
            catch (Exception exception)
            {
                _completed(OperationResult<LayerAssignmentView>.Failure(
                    "LAYER_ASSIGNMENT_LOAD_FAILED", "層の読み取りに失敗しました。", exception.ToString()));
            }
        }
    }

    /// <summary>
    /// 書込み対象を集め、1タイプに収まっているかを確かめ、書込み後の層IDの並びを作ります。
    /// Revit APIを触るのはここだけです。
    /// </summary>
    private sealed class LayerTargetSurveyRequest : IRevitActionRequest
    {
        private readonly ProductIdTargetQuery _query;
        private readonly long _anchorElementId;
        private readonly IReadOnlyList<LayerAssignmentRow> _rows;
        private readonly Action<OperationResult<LayerTargetSurvey>> _completed;

        public LayerTargetSurveyRequest(
            ProductIdTargetQuery query,
            long anchorElementId,
            IReadOnlyList<LayerAssignmentRow> rows,
            Action<OperationResult<LayerTargetSurvey>> completed)
        {
            _query = query;
            _anchorElementId = anchorElementId;
            _rows = rows;
            _completed = completed;
        }

        public string Name => "層Product_ID対象の確認";

        public void Execute(UIApplication application)
        {
            ArgumentNullException.ThrowIfNull(application);
            try
            {
                var document = application.ActiveUIDocument?.Document;
                if (document is null)
                {
                    _completed(OperationResult<LayerTargetSurvey>.Failure(
                        "REVIT-DOCUMENT-NOT-FOUND", "対象を確認するRevitドキュメントがありません。", Name));
                    return;
                }

                var elements = RevitElementTargetCollector.Collect(document, _query);
                if (elements.Count == 0)
                {
                    _completed(OperationResult<LayerTargetSurvey>.Failure(
                        "LAYER_ASSIGNMENT_NO_TARGET",
                        "書き込む対象の要素がありません。",
                        $"scope={_query.Scope}"));
                    return;
                }

                var mixedType = DescribeMixedTypes(document, elements);
                if (mixedType is not null)
                {
                    _completed(OperationResult<LayerTargetSurvey>.Failure(
                        "LAYER_ASSIGNMENT_MIXED_TYPES", mixedType.Value.UserMessage, mixedType.Value.Detail));
                    return;
                }

                var anchor = elements.FirstOrDefault(element => element.Id.Value == _anchorElementId) ?? elements[0];
                var context = ReadAnchorContext(document, anchor);
                if (!context.IsSuccess)
                {
                    _completed(new OperationResult<LayerTargetSurvey>(default, context.Error));
                    return;
                }

                var projected = new List<IReadOnlyList<string>>(elements.Count);
                var hasConflict = false;
                foreach (var element in elements)
                {
                    var ids = new string[_rows.Count];
                    for (var index = 0; index < _rows.Count; index++)
                    {
                        var row = _rows[index];
                        var current = LayerAssignmentReader.ReadCurrent(element, row.Slot);
                        var selected = (row.SelectedProductId ?? string.Empty).Trim();
                        if (selected.Length == 0)
                        {
                            // 書かない層は、書込み後も今の値のままです。採番の判定はその前提で行います。
                            ids[index] = current;
                            continue;
                        }

                        if (current.Length > 0
                            && !string.Equals(current, selected, StringComparison.OrdinalIgnoreCase))
                        {
                            hasConflict = true;
                        }

                        ids[index] = selected;
                    }

                    projected.Add(ids);
                }

                _completed(OperationResult<LayerTargetSurvey>.Success(new LayerTargetSurvey(
                    document,
                    document.Title,
                    elements.Select(element => element.Id.Value).ToArray(),
                    context.Value!.HostCategoryIdentity,
                    context.Value.LayerWarning,
                    projected,
                    hasConflict)));
            }
            catch (Exception exception)
            {
                _completed(OperationResult<LayerTargetSurvey>.Failure(
                    "LAYER_ASSIGNMENT_TARGET_FAILED", "書き込む対象を確認できませんでした。", exception.ToString()));
            }
        }

        /// <summary>
        /// Assembly_IDはタイプに付きます。対象が2タイプにまたがると、
        /// 書込み側は先頭要素のタイプにしか番号を書けず、もう一方は無印のまま黙って残ります。
        /// さらに、層の表は1タイプの構成から作られているため、別タイプへ書くと別の層のIDを書きます。
        /// どちらも実行後には気づけないので、ここで断ります。
        /// </summary>
        private static (string UserMessage, string Detail)? DescribeMixedTypes(
            Document document,
            IReadOnlyList<Element> elements)
        {
            var typeIds = elements
                .Select(element => element.GetTypeId().Value)
                .Distinct()
                .OrderBy(value => value)
                .ToArray();
            if (typeIds.Length <= 1) return null;

            var names = typeIds
                .Select(value => value == ElementId.InvalidElementId.Value
                    ? "（タイプなし）"
                    : document.GetElement(new ElementId(value))?.Name ?? $"タイプ{value}")
                .ToArray();
            return (
                $"選択に{typeIds.Length}種類のタイプが含まれています（{string.Join("、", names.Take(4))}"
                    + $"{(names.Length > 4 ? " ほか" : string.Empty)}）。"
                    + "層の表は1つのタイプの構成から作られているため、別のタイプへ書くと別の層のIDを書き込みます。"
                    + "またAssembly_IDはタイプに付くため、複数タイプでは片方だけが無印のまま残ります。"
                    + "同じタイプの要素だけを選び直すか、付与範囲を「同一タイプすべて」にしてください。",
                $"typeIds={string.Join(",", typeIds)}");
        }

        /// <summary>溢れた層の警告と、採番に使う安定カテゴリキーを、代表要素から読みます。</summary>
        private static OperationResult<LayerAnchorContext> ReadAnchorContext(Document document, Element anchor)
        {
            var read = new CompoundStructureReader().Read(document, anchor);
            if (read.IsSuccess)
            {
                return OperationResult<LayerAnchorContext>.Success(new LayerAnchorContext(
                    read.Value!.HostCategoryIdentity,
                    LayerAssignmentReader.MapLayers(read.Value).Warning));
            }

            // 複合構造を持たない要素と、安定カテゴリIDを読めない要素は、層の書込み自体は止めません。
            // Assembly_IDだけを見送り、理由は呼出側がSummaryへ出します。
            var code = read.Error!.Code;
            if (code is LayerAssignmentReader.HostNotSupportedCode
                or LayerAssignmentReader.CompoundStructureMissingCode
                or "ASSEMBLY_HOST_CATEGORY_UNSTABLE")
            {
                return OperationResult<LayerAnchorContext>.Success(
                    new LayerAnchorContext(LayerAssignmentReader.DescribeStableCategory(anchor), string.Empty));
            }

            return new OperationResult<LayerAnchorContext>(default, read.Error);
        }
    }

    private sealed record LayerAnchorContext(string HostCategoryIdentity, string LayerWarning);
}

/// <summary>プレビューで確定した書込み内容と、それに伴うCSV登録計画の組です。</summary>
internal sealed record LayerAssignmentApplication(
    LayerProductIdPreview Preview,
    AssemblyRegistrationPlan? Registration);

/// <summary>ExternalEvent上で読み取った、書込み対象の実態です。</summary>
internal sealed record LayerTargetSurvey(
    Document SourceDocument,
    string SourceDocumentTitle,
    IReadOnlyList<long> TargetElementIds,
    /// <summary>採番の照合に使う安定カテゴリキー。読めない場合は空文字。</summary>
    string HostCategoryIdentity,
    string LayerWarning,
    /// <summary>書込み後に各要素が持つことになる層IDの並び（室内側から外側）。</summary>
    IReadOnlyList<IReadOnlyList<string>> ProjectedLayerIdsPerElement,
    /// <summary>既に別の値が入っている層を上書きするかどうか。</summary>
    bool HasConflictingExistingValue);

/// <summary>
/// 複合構造から「どの層がどのパラメータへ入るか」の表を作ります。
/// CompoundLayerConverterは使いません。あちらは材料側のProduct_IDが空だと変換を止めますが、
/// この機能では層のIDを要素のパラメータへ入れるため、材料側が空でも表を出す必要があります。
/// </summary>
internal static class LayerAssignmentReader
{
    /// <summary>壁・床・天井・屋根以外を選んだ場合のコードです。</summary>
    public const string HostNotSupportedCode = "ASSEMBLY_HOST_NOT_SUPPORTED";

    /// <summary>タイプに複合構造が無い場合のコードです。</summary>
    public const string CompoundStructureMissingCode = "ASSEMBLY_COMPOUND_STRUCTURE_NOT_FOUND";

    public static OperationResult<LayerAssignmentView> BuildView(Document document, Element element)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(element);

        var read = new CompoundStructureReader().Read(document, element);
        if (!read.IsSuccess)
        {
            // 複合構造を持たない要素は「仕上」の1つだけを扱います（設計 §8）。
            if (read.Error!.Code is HostNotSupportedCode or CompoundStructureMissingCode)
            {
                return OperationResult<LayerAssignmentView>.Success(new LayerAssignmentView(
                    new[] { CreateSingleFinishRow(document, element) }, string.Empty));
            }

            return new OperationResult<LayerAssignmentView>(default, read.Error);
        }

        var plan = MapLayers(read.Value!);
        var rows = plan.Assigned.Select(assignment => CreateRow(element, assignment)).ToArray();
        return OperationResult<LayerAssignmentView>.Success(new LayerAssignmentView(rows, plan.Warning));
    }

    /// <summary>Revitの層順とInteriorSideから、6つの箱への割当を作ります。</summary>
    public static LayerSlotPlan MapLayers(CompoundStructureReadResult read)
    {
        ArgumentNullException.ThrowIfNull(read);
        var layers = read.Layers
            .Select(snapshot => new AssemblyLayerRecord(
                snapshot.Order,
                snapshot.Function,
                snapshot.ThicknessMillimeters,
                snapshot.ProductId,
                snapshot.MaterialName,
                snapshot.MaterialElementId,
                snapshot.IsMaterialFallback))
            .ToArray();
        return LayerSlotMapper.Map(layers, read.InteriorSide);
    }

    /// <summary>要素に今入っている層Product_IDを読みます。未設定なら空文字です。</summary>
    public static string ReadCurrent(Element element, LayerSlot slot)
    {
        ArgumentNullException.ThrowIfNull(element);
        var index = (int)slot;
        if (index < 0 || index >= Identifiers.LayerProductIdParameterGuids.Count) return string.Empty;
        return element.get_Parameter(Identifiers.LayerProductIdParameterGuids[index])?.AsString()?.Trim()
            ?? string.Empty;
    }

    /// <summary>言語に左右されない安定カテゴリキーを返します。読めない場合は空文字です。</summary>
    public static string DescribeStableCategory(Element element)
    {
        ArgumentNullException.ThrowIfNull(element);
        var category = element.Category;
        // BuiltInCategoryのIDは負の値です。0以上は利用者が作ったカテゴリで、モデル間で一致しません。
        return category is not null && category.Id != ElementId.InvalidElementId && category.Id.Value < 0
            ? HostCategoryIdentity.FromBuiltInCategoryId(category.Id.Value, category.Name).FingerprintKey
            : string.Empty;
    }

    private static LayerAssignmentRow CreateRow(Element element, LayerSlotAssignment assignment)
    {
        var current = ReadCurrent(element, assignment.Slot);
        return new LayerAssignmentRow(
            assignment.Layer.Order,
            assignment.Slot,
            LayerSlotNames.Of(assignment.Slot),
            assignment.Layer.RevitMaterialName,
            assignment.Layer.ThicknessMillimeters,
            current,
            // 既存値を初期選択にします。利用者が触らなかった層は同じ値が書き直されるだけで、
            // 空を書いて既存値を消すことがありません。
            current,
            assignment.Layer.Function);
    }

    private static LayerAssignmentRow CreateSingleFinishRow(Document document, Element element)
    {
        var typeId = element.GetTypeId();
        var typeName = typeId == ElementId.InvalidElementId
            ? element.Name
            : document.GetElement(typeId)?.Name ?? element.Name;
        var current = ReadCurrent(element, LayerSlot.Finish);
        return new LayerAssignmentRow(
            0,
            LayerSlot.Finish,
            LayerSlotNames.Of(LayerSlot.Finish),
            typeName,
            0m,
            current,
            current,
            // 複合構造を持たない要素には層の機能がありません。CSVでは空欄になります。
            string.Empty);
    }
}
