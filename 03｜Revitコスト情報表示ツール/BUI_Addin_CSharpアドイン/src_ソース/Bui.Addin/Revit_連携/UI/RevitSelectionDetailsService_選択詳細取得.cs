// このファイルは、選択要素のRevit情報を共有ViewModel用の値だけに変換します。
// Revit ExternalEvent、固定GUID、複合構造読取に依存し、変更は概要タブの選択表示と警告に影響します。
// 旧・単一Product_ID（569e8df1-…）はBUIが読み書きしなくなったため、ここでは読みません。
// LayerProductIdSummary（6つの層別パラメータの要約）はBuiDockPaneViewModel.SelectedProductIdへ計算しますが、
// 現在どのXAMLにもバインドされておらず画面には出ません。層ごとのProduct_IDは
// 概要タブの複合レイヤー一覧、検索タブの層への割り当てで表示しています。
// ProductId（単一値）は仕上（LayerSlot.Finish）の値を、製品カード読込みと絞り込み初期化のためだけに残します。
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Bui.Assemblies;
using Bui.Common;
using Bui.Resources;
using Bui.Revit.Assemblies;
using Bui.Revit.ExternalEvents;
using Bui.UI;

namespace Bui.Revit.UI;

/// <summary>Revit APIへのアクセスをExternalEvent上に限定する選択詳細取得境界です。</summary>
public sealed class RevitSelectionDetailsService : ISelectionDetailsService
{
    private readonly RevitActionDispatcher _dispatcher;

    public RevitSelectionDetailsService(RevitActionDispatcher dispatcher) =>
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));

    public async Task<SelectionDetails> LoadAsync(SelectionSnapshot snapshot, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (!snapshot.HasDocument || snapshot.ElementIds.Count == 0)
        {
            return new SelectionDetails(snapshot, null, null, Array.Empty<AssemblyLayerRecord>(), Array.Empty<string>());
        }

        var completion = new TaskCompletionSource<OperationResult<SelectionDetails>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var enqueue = _dispatcher.Enqueue(new ReadRequest(snapshot, completion));
        if (!enqueue.IsSuccess)
        {
            return new SelectionDetails(snapshot, null, null, Array.Empty<AssemblyLayerRecord>(),
                new[] { enqueue.Error!.UserMessage });
        }

        var result = await completion.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        return result.IsSuccess
            ? result.Value!
            : new SelectionDetails(snapshot, null, null, Array.Empty<AssemblyLayerRecord>(),
                new[] { $"{result.Error!.Code}: {result.Error.UserMessage}" });
    }

    private sealed class ReadRequest : IRevitActionRequest
    {
        private readonly SelectionSnapshot _snapshot;
        private readonly TaskCompletionSource<OperationResult<SelectionDetails>> _completion;

        public ReadRequest(SelectionSnapshot snapshot, TaskCompletionSource<OperationResult<SelectionDetails>> completion)
        {
            _snapshot = snapshot;
            _completion = completion;
        }

        public string Name => "BUI選択詳細読取";

        public void Execute(UIApplication application)
        {
            try
            {
                var document = application.ActiveUIDocument?.Document;
                var element = document?.GetElement(new ElementId(_snapshot.ElementIds[0]));
                if (document is null || element is null)
                {
                    _completion.TrySetResult(OperationResult<SelectionDetails>.Failure(
                        "REVIT-SELECTION-STALE", "選択要素が切り替わったため再読込みが必要です。", Name));
                    return;
                }

                var warnings = new List<string>();
                if (_snapshot.ElementIds.Count > 1) warnings.Add("複数選択中のため、先頭要素の詳細を表示しています。");
                if (_snapshot.ContainsLinkedElement) warnings.Add("リンク要素は読取表示のみです。");
                // SelectionChangedのコールバック中はRevitが変更を許可しないため、
                // その時点の Document.IsReadOnly は本来編集できる文書でもtrueになり得ます。
                // 誤警告を避けるため、External Event内で取得した現在の文書で判定し直します。
                if (document.IsReadOnly) warnings.Add("現在のドキュメントは読取専用です。");

                // 6つの層別Product_IDを要素本体から読みます（材料でも旧・単一Product_IDでもありません）。
                var layerProductIds = Identifiers.LayerProductIdParameterGuids
                    .Select(guid => element.get_Parameter(guid)?.AsString()?.Trim())
                    .ToArray();
                var layerProductIdSummary = LayerProductIdSummary.Describe(layerProductIds);
                // 製品カード読込み・絞り込み初期化に使う代表値は仕上（LayerSlot.Finish）の値です。
                var productId = layerProductIds[(int)LayerSlot.Finish];
                var typeId = element.GetTypeId();
                var type = typeId == ElementId.InvalidElementId ? null : document.GetElement(typeId);
                var assemblyId = type?.get_Parameter(Identifiers.AssemblyIdParameterGuid)?.AsString()?.Trim();
                IReadOnlyList<AssemblyLayerRecord> layers = Array.Empty<AssemblyLayerRecord>();

                if (element is HostObject)
                {
                    var read = new CompoundStructureReader().Read(document, element);
                    if (read.IsSuccess)
                    {
                        // 表示ではProduct_ID未設定層も消さず、ユーザーが修正対象を追えるようにします。
                        layers = read.Value!.Layers.Select(layer => new AssemblyLayerRecord(
                            layer.Order, layer.Function, layer.ThicknessMillimeters,
                            layer.ProductId, layer.MaterialName, layer.MaterialElementId, layer.IsMaterialFallback)).ToArray();
                        var validation = CompoundLayerConverter.ToAssemblyLayers(read.Value.Layers, read.Value.IsVerticallyCompound);
                        if (!validation.IsSuccess) warnings.Add($"{validation.Error!.Code}: {validation.Error.UserMessage}");
                    }
                    else if (read.Error!.Code != "ASSEMBLY_COMPOUND_STRUCTURE_NOT_FOUND")
                    {
                        warnings.Add($"{read.Error.Code}: {read.Error.UserMessage}");
                    }
                }
                else if (element is Part part)
                {
                    var partRead = new PartMaterialReader().Read(part, partOverrideConfirmed: false);
                    if (partRead.IsSuccess) productId = partRead.Value!.Resolution.EffectiveProductId;
                    else warnings.Add($"{partRead.Error!.Code}: {partRead.Error.UserMessage}");
                }

                var sameTypeCount = typeId == ElementId.InvalidElementId ? 0 : CountSameType(document, typeId);
                _completion.TrySetResult(OperationResult<SelectionDetails>.Success(new SelectionDetails(
                    _snapshot, productId, assemblyId, layers, warnings)
                {
                    ElementDisplayName = element.Name,
                    TypeName = type?.Name ?? string.Empty,
                    CategoryName = element.Category?.Name ?? string.Empty,
                    SameTypeCount = sameTypeCount,
                    LayerProductIdSummary = layerProductIdSummary
                }));
            }
            catch (Exception exception)
            {
                _completion.TrySetResult(OperationResult<SelectionDetails>.Failure(
                    "REVIT-SELECTION-READ-FAILED", "選択要素の詳細を読み込めませんでした。", exception.ToString()));
            }
        }

        private static int CountSameType(Document document, ElementId typeId)
        {
            using var collector = new FilteredElementCollector(document);
            return collector.WhereElementIsNotElementType().ToElements().Count(item => item.GetTypeId() == typeId);
        }
    }
}
