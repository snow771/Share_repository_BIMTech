using System.Windows;
using SimilaritySearchApp.Estimate;
using SimilaritySearchApp.Estimate.Data;
using SimilaritySearchApp.Estimate.Models;

namespace SimilaritySearchApp.Bridge;

/// <summary>
/// 見積確認・取決めウィンドウ（estimate-review.html）からのコマンドを受ける。
/// 対象の見積はウィンドウが持つ見積ID に固定し、画面から別の見積ID を指定させない（一覧モードの list / openEstimate を除く）。
/// 資料は資料ID で指定させ、任意のパスやSQLは受け付けない。
///   getContext / list / openEstimate                 … 一覧
///   detail / projectCandidates / supplierCandidates  … 詳細・紐づけ候補
///   saveLink / saveLineReview                        … 紐づけ・明細の確認結果（rowversion で同時編集を検出）
///   preview / saveDraft / confirm / newRevision      … 取決め（計算・確定は C#）
///   showPdf / openDocument / addPdf                  … 原資料
///   deleteEstimate / closeWindow                     … 見積の削除（確定済みは見積ID の入力と理由が必要）・ウィンドウを閉じる
/// </summary>
public sealed class EstimateReviewBridge
{
    private readonly AppState _state;
    private readonly int? _headerId;
    private readonly DocumentViewer _viewer;
    private readonly Func<Window?> _owner;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public EstimateReviewBridge(AppState state, int? headerId, DocumentViewer viewer, Func<Window?> owner)
    {
        _state = state;
        _headerId = headerId;
        _viewer = viewer;
        _owner = owner;
    }

    public sealed class KeywordRequest
    {
        public string? Keyword { get; set; }
    }

    public sealed class HeaderRequest
    {
        public int HeaderId { get; set; }
    }

    public sealed class DocumentRequest
    {
        public long DocumentId { get; set; }
        public int? Page { get; set; }
    }

    private EstimateRepository Estimates => new(_state.Settings.Db);

    private int HeaderId => _headerId ?? throw new InvalidOperationException("見積が選ばれていません。");

    public async Task<BridgeResponse> HandleAsync(BridgeRequest req)
    {
        await _gate.WaitAsync();
        try
        {
            var estimates = Estimates;
            var agreements = new AgreementRepository(estimates);
            object? result = req.Command switch
            {
                "getContext" => new { mode = _headerId is null ? "list" : "detail", headerId = _headerId, @operator = EstimateDb.Operator },
                "list" => await estimates.ListAsync(BridgeJson.Read<EstimateListQuery>(req.Payload)),
                "openEstimate" => OpenEstimate(BridgeJson.Read<HeaderRequest>(req.Payload).HeaderId),
                "detail" => await estimates.DetailAsync(HeaderId, agreements),
                "projectCandidates" => await ProjectCandidatesAsync(estimates, BridgeJson.Read<KeywordRequest>(req.Payload).Keyword),
                "supplierCandidates" => await SupplierCandidatesAsync(estimates, BridgeJson.Read<KeywordRequest>(req.Payload).Keyword),
                "saveLink" => new { rowVersion = await estimates.SaveLinkAsync(WithHeader(BridgeJson.Read<LinkRequest>(req.Payload))) },
                "saveLineReview" => await estimates.SaveLineReviewAsync(HeaderId, BridgeJson.Read<LineReviewRequest>(req.Payload)),
                "preview" => await agreements.PreviewAsync(WithHeader(BridgeJson.Read<AgreementInput>(req.Payload))),
                "saveDraft" => await agreements.SaveDraftAsync(WithHeader(BridgeJson.Read<AgreementInput>(req.Payload))),
                "confirm" => await agreements.ConfirmAsync(WithHeader(BridgeJson.Read<AgreementInput>(req.Payload))),
                "newRevision" => await agreements.NewRevisionAsync(new NewRevisionRequest { HeaderId = HeaderId, Reason = BridgeJson.Read<NewRevisionRequest>(req.Payload).Reason }),
                "showPdf" => await ShowPdfAsync(estimates, BridgeJson.Read<DocumentRequest>(req.Payload)),
                "openDocument" => await OpenDocumentAsync(estimates, BridgeJson.Read<DocumentRequest>(req.Payload).DocumentId),
                "addPdf" => await AddPdfAsync(estimates),
                "deleteEstimate" => await DeleteAsync(BridgeJson.Read<DeleteEstimateRequest>(req.Payload)),
                "closeWindow" => CloseWindow(),
                _ => throw new InvalidOperationException($"未対応のコマンドです: {req.Command}"),
            };
            return new BridgeResponse(req.Id, true, result, null);
        }
        catch (Exception e)
        {
            return new BridgeResponse(req.Id, false, null, e.Message);
        }
        finally
        {
            _gate.Release();
        }
    }

    private LinkRequest WithHeader(LinkRequest r)
    {
        r.HeaderId = HeaderId;
        return r;
    }

    private AgreementInput WithHeader(AgreementInput i)
    {
        i.HeaderId = HeaderId;
        return i;
    }

    private object OpenEstimate(int headerId)
    {
        Application.Current.Dispatcher.Invoke(() => EstimateReviewWindow.Show(_state, headerId, _owner()?.Owner ?? _owner()));
        return new { opened = true };
    }

    private async Task<object> ProjectCandidatesAsync(EstimateRepository estimates, string? keyword)
    {
        string? ocr = null;
        if (_headerId is int id)
        {
            await using var conn = await estimates.OpenAsync();
            ocr = (await estimates.HeaderAsync(conn, null, id, default)).ProjectNameOcr;
        }
        return await estimates.ProjectCandidatesAsync(keyword, ocr);
    }

    private async Task<object> SupplierCandidatesAsync(EstimateRepository estimates, string? keyword)
    {
        string? ocr = null;
        if (_headerId is int id)
        {
            await using var conn = await estimates.OpenAsync();
            ocr = (await estimates.HeaderAsync(conn, null, id, default)).SupplierNameOcr;
        }
        return await estimates.SupplierCandidatesAsync(keyword, ocr);
    }

    private async Task<DocumentRepository.StoredFile> GetDocumentAsync(EstimateRepository estimates, long documentId)
    {
        await using var conn = await estimates.OpenAsync();
        return await new DocumentRepository().GetAsync(conn, HeaderId, documentId, default);
    }

    private async Task<object> ShowPdfAsync(EstimateRepository estimates, DocumentRequest r)
    {
        var file = await GetDocumentAsync(estimates, r.DocumentId);
        if (file.Type != EstimateDb.DocOriginalPdf) throw new InvalidOperationException("PDF ではない資料です。");
        var path = _viewer.Extract(r.DocumentId, file);
        return new { url = DocumentViewer.Url(path, r.Page), fileName = file.FileName };
    }

    private async Task<object> OpenDocumentAsync(EstimateRepository estimates, long documentId)
    {
        var file = await GetDocumentAsync(estimates, documentId);
        DocumentViewer.OpenExternal(_viewer.Extract(documentId, file));
        return new { opened = true, fileName = file.FileName };
    }

    /// <summary>見積をまとめて削除（確定済みの取決めがあれば見積ID の入力と理由が必要）。</summary>
    private async Task<DeleteEstimateResult> DeleteAsync(DeleteEstimateRequest r)
    {
        r.HeaderId = HeaderId;
        return await new EstimateDeleteRepository(_state.Settings.Db).DeleteAsync(r);
    }

    private object CloseWindow()
    {
        Application.Current.Dispatcher.InvokeAsync(() => _owner()?.Close());
        return new { closed = true };
    }

    private async Task<object> AddPdfAsync(EstimateRepository estimates)
    {
        var path = FileDialogs.Pick(new FileDialogs.PickRequest { Kind = "open", Title = "元PDFを選択（この見積の原資料）", Filter = "PDF (*.pdf)|*.pdf" }, _owner());
        if (path is null) return new { added = false, canceled = true };
        var file = DocumentRepository.Load(EstimateDb.DocOriginalPdf, path);
        var pages = Ocr.Pdf.PdfSplitter.CountPages(file.Content);   // PDF として開けるか確かめる
        var added = await estimates.AddDocumentAsync(HeaderId, file);
        return new { added, canceled = false, pages, fileName = file.FileName };
    }
}
