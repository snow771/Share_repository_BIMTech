using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows;
using SimilaritySearchApp.Data;
using SimilaritySearchApp.Excel;
using SimilaritySearchApp.Llm;
using SimilaritySearchApp.Models;
using SimilaritySearchApp.Services;
using SimilaritySearchApp.Settings;

namespace SimilaritySearchApp.Bridge;

/// <summary>
/// 画面（JS）からのコマンドを受けて各サービスを呼び、結果を返す。
/// 処理は1件ずつ直列に実行する（状態の競合を防ぐため）。例外は error として画面へ返す。
/// </summary>
public sealed class WebBridge
{
    private readonly AppState _state;
    private readonly SettingsStore _store;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly DatabaseEditor _dbEditor = new();
    private readonly ChangeHistoryService _history = new();

    private readonly Action _openStencilWindow;
    private readonly Action _openOcrWindow;
    private readonly Func<Window?> _owner;

    /// <param name="openStencilWindow">Stencile変換ウィンドウを開く（ヘッダーのボタン）</param>
    /// <param name="openOcrWindow">見積AI-OCRウィンドウを開く（ヘッダー・ホームのボタン）</param>
    /// <param name="owner">ファイル選択ダイアログの親ウィンドウ</param>
    public WebBridge(AppState state, SettingsStore store, Action openStencilWindow, Action openOcrWindow, Func<Window?> owner)
    {
        _state = state;
        _store = store;
        _openStencilWindow = openStencilWindow;
        _openOcrWindow = openOcrWindow;
        _owner = owner;
    }

    public async Task<BridgeResponse> HandleAsync(BridgeRequest req)
    {
        await _gate.WaitAsync();
        try
        {
            object? result = req.Command switch
            {
                "getSettings" => GetSettings(),
                "saveSettings" => SaveSettings(BridgeJson.Read<SettingsView>(req.Payload)),
                "testDbConnection" => await TestDbAsync(BridgeJson.Read<SettingsView>(req.Payload)),
                "listLlmModels" => await ListModelsAsync(BridgeJson.Read<LlmSettings>(req.Payload)),
                "testLlm" => await TestLlmAsync(BridgeJson.Read<LlmSettings>(req.Payload)),
                "loadData" => await LoadDataAsync(BridgeJson.Read<LoadDataRequest>(req.Payload)),
                "analyze" => await AnalyzeAsync(BridgeJson.Read<FilterCriteria>(req.Payload)),
                "selectProject" => SelectProject(BridgeJson.Read<ProjectIdRequest>(req.Payload)),
                "aiFilter" => await AiFilterAsync(BridgeJson.Read<TextRequest>(req.Payload).Text),
                "insight" => Insight(BridgeJson.Read<InsightRequest>(req.Payload).Kind),
                "chat" => await ChatAsync(BridgeJson.Read<TextRequest>(req.Payload).Text),
                "chatReset" => ChatReset(),
                "standardPrices" => StandardPrices(),
                "clearSelection" => ClearSelection(),
                "dbTables" => await _dbEditor.ListTablesAsync(_state.Settings.Db, BridgeJson.Read<DbTablesRequest>(req.Payload).Refresh),
                "dbTable" => await DbTableAsync(BridgeJson.Read<DbTableRequest>(req.Payload)),
                "dbQuery" => await _dbEditor.QueryAsync(_state.Settings.Db, BridgeJson.Read<DbQueryRequest>(req.Payload)),
                "dbSave" => await _dbEditor.SaveAsync(_state.Settings.Db, BridgeJson.Read<DbChangeSet>(req.Payload)),
                "dbDeletePreview" => await _dbEditor.PreviewRelatedDeletesAsync(_state.Settings.Db, BridgeJson.Read<DbDeletePreviewRequest>(req.Payload)),
                "historyList" => await _history.ListAsync(_state.Settings.Db, BridgeJson.Read<HistoryQuery>(req.Payload)),
                "historyDetail" => await _history.DetailAsync(_state.Settings.Db, BridgeJson.Read<HistoryDetailRequest>(req.Payload)),
                "historyRevert" => await RevertAsync(BridgeJson.Read<RevertRequest>(req.Payload)),
                "historyPurge" => new { deleted = await _history.PurgeAsync(_state.Settings.Db, BridgeJson.Read<PurgeRequest>(req.Payload)) },
                "openStencilWindow" => OpenStencilWindow(),
                "openOcrWindow" => OpenOcrWindow(),
                "ocrUsage" => new Ocr.Usage.OcrUsageCounter().Snapshot(_state.Settings.Ocr),
                "pickPath" => new { path = FileDialogs.Pick(BridgeJson.Read<FileDialogs.PickRequest>(req.Payload), _owner()) },
                "excelRead" => await ExcelReadAsync(BridgeJson.Read<ExcelTableReader.Request>(req.Payload)),
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

    // ── 設定 ──────────────────────────────────────────────

    private SettingsView GetSettings()
    {
        var s = _state.Settings;
        return new SettingsView
        {
            Db = new DbSettingsView
            {
                Server = s.Db.Server,
                Database = s.Db.Database,
                UseWindowsAuth = s.Db.UseWindowsAuth,
                User = s.Db.User,
                HasPassword = !string.IsNullOrEmpty(s.Db.EncryptedPassword),
                TrustServerCertificate = s.Db.TrustServerCertificate,
                CommandTimeoutSec = s.Db.CommandTimeoutSec,
                Tables = s.Db.Tables,
            },
            Llm = s.Llm,
        };
    }

    private SettingsView SaveSettings(SettingsView view)
    {
        _state.Settings = Merge(view, _state.Settings);
        _store.Save(_state.Settings);
        _dbEditor.Invalidate();   // 接続先が変わるかもしれないので DB 構造を取り直す
        return GetSettings();
    }

    /// <summary>画面の入力値を既存設定に重ねる。パスワードは null なら現状維持。</summary>
    private static AppSettings Merge(SettingsView view, AppSettings current)
    {
        var v = view.Db;
        var encrypted = v.Password is null ? current.Db.EncryptedPassword : SettingsStore.Protect(v.Password);
        return new AppSettings
        {
            Db = new DbSettings
            {
                Server = v.Server.Trim(),
                Database = v.Database.Trim(),
                UseWindowsAuth = v.UseWindowsAuth,
                User = v.User.Trim(),
                EncryptedPassword = encrypted,
                TrustServerCertificate = v.TrustServerCertificate,
                CommandTimeoutSec = v.CommandTimeoutSec > 0 ? v.CommandTimeoutSec : 120,
                Tables = v.Tables,
            },
            Llm = view.Llm,
            Stencil = current.Stencil,   // 設定画面では触らない項目は引き継ぐ
            Ocr = current.Ocr,
        };
    }

    private async Task<object> TestDbAsync(SettingsView view)
    {
        var message = await new SqlConnectionFactory(Merge(view, _state.Settings).Db).TestAsync();
        return new { message };
    }

    private static async Task<object> ListModelsAsync(LlmSettings llm)
    {
        var models = await new OllamaClient(llm).ListModelsAsync();
        return new { models };
    }

    private static async Task<object> TestLlmAsync(LlmSettings llm)
    {
        var message = await new OllamaClient(llm).TestAsync();
        return new { message };
    }

    // ── データ読込・分析 ──────────────────────────────────

    private async Task<LoadDataResult> LoadDataAsync(LoadDataRequest req)
    {
        var data = await new DataLoader(_state.Settings.Db).LoadAsync(req.StructureType);
        _state.SetData(req.StructureType, data.Projects, data.Assess, data.Revise, data.Materials);
        var result = await AnalyzeAsync(new FilterCriteria());
        return new LoadDataResult(data.Projects.Count, data.Assess.Count, data.Revise.Count, data.Materials.Count, data.Warning, result);
    }

    private async Task<AnalysisResult> AnalyzeAsync(FilterCriteria criteria)
    {
        RequireLoaded();
        NormalizeArea(criteria);
        var result = await Task.Run(() =>
            AnalysisService.Analyze(_state.Projects, _state.Assess, _state.Revise, criteria));

        _state.Criteria = result.Criteria;
        _state.LastResult = result;
        // 選択中の物件が絞込後も残っていれば選択を維持する
        if (_state.SelectedProjectId is not null && result.Projects.All(p => p.ProjectId != _state.SelectedProjectId))
            _state.SelectedProjectId = null;
        return result;
    }

    /// <summary>
    /// 面積レンジが未指定なら全範囲を入れる（Python 版のスライダーと同じく、面積が欠損の物件は対象外になる）。
    /// </summary>
    private void NormalizeArea(FilterCriteria c)
    {
        var bounds = ProjectFilterService.AreaBounds(_state.Projects, c.BasisMode);
        if (bounds is null) return;
        var (mn, mx) = bounds.Value;
        var lo = Math.Clamp(c.AreaMin ?? mn, mn, mx);
        var hi = Math.Clamp(c.AreaMax ?? mx, mn, mx);
        if (lo > hi) hi = lo;
        c.AreaMin = lo;
        c.AreaMax = hi;
    }

    private ProjectDetailResult SelectProject(ProjectIdRequest req)
    {
        RequireLoaded();
        var p = _state.FindProject(req.ProjectId)
                ?? throw new InvalidOperationException($"物件が見つかりません: {req.ProjectId}");
        _state.SelectedProjectId = p.ProjectId;

        var r = _state.LastResult;
        var row = r?.Projects.FirstOrDefault(x => x.ProjectId == p.ProjectId);
        var median = r?.Revise.After.Median;
        double? ratio = row?.ReviseYenPerM2 is double own && median is double med && med != 0
            ? (own / med - 1) * 100
            : null;

        return new ProjectDetailResult(
            p.ProjectId, p.Name,
            p.Attributes.Select(kv => new KeyValueItem(kv.Key, kv.Value)).ToList(),
            row?.ReviseYenPerM2, median, ratio,
            r is null ? "" : InsightTextService.SelectedProject(p, r));
    }

    // ── 標準単価 ─────────────────────────────────────────

    /// <summary>現在の工種条件・選択物件に該当する標準材料（建築材料マスタ）と実績を突き合わせる。</summary>
    private StandardPriceResult StandardPrices()
    {
        RequireLoaded();
        return StandardPriceService.Build(
            _state.Materials, _state.Projects, _state.Assess, _state.Revise,
            _state.Criteria, _state.FindProject(_state.SelectedProjectId));
    }

    private object ClearSelection()
    {
        _state.SelectedProjectId = null;
        return new { cleared = true };
    }

    /// <summary>変更履歴からの取り消し。成功したら DB 構造（行数）のキャッシュを捨てる。</summary>
    private async Task<RevertResult> RevertAsync(RevertRequest req)
    {
        var result = await _history.RevertAsync(_state.Settings.Db, req);
        if (result.Done) _dbEditor.Invalidate();
        return result;
    }

    private object OpenStencilWindow()
    {
        Application.Current.Dispatcher.Invoke(_openStencilWindow);
        return new { opened = true };
    }

    private object OpenOcrWindow()
    {
        Application.Current.Dispatcher.Invoke(_openOcrWindow);
        return new { opened = true };
    }

    /// <summary>DB編集の Excel 取込: シートを表として読む（重いので別スレッド）。</summary>
    private static Task<ExcelTable> ExcelReadAsync(ExcelTableReader.Request req) =>
        Task.Run(() => ExcelTableReader.Read(req.Path, req.Sheet));

    // ── DB編集 ───────────────────────────────────────────

    private Task<DbTableDetail> DbTableAsync(DbTableRequest req) =>
        _dbEditor.GetTableAsync(_state.Settings.Db, req.Schema, req.Name);

    // ── AI ───────────────────────────────────────────────

    private async Task<ChatResult> AiFilterAsync(string text)
    {
        RequireLoaded();
        var request = (text ?? "").Trim();
        if (request.Length == 0) throw new InvalidOperationException("AIへの指示を入力してください。");

        var opts = BuildOptionsSnapshot();
        var client = new OllamaClient(_state.Settings.Llm);
        JsonObject ai;
        try
        {
            ai = await client.ChatJsonAsync(
                AiFilterService.BuildPrompt(request, _state.Criteria, opts, _state.SelectedProjectId),
                AiFilterService.BuildSchema(), temperature: 0.0);
        }
        catch (LlmContextOverflowException)
        {
            // コンテキスト長が足りない（サーバー側で固定されている場合など）→ 候補一覧を省いた短い指示文でやり直す
            ai = await client.ChatJsonAsync(
                AiFilterService.BuildPrompt(request, _state.Criteria, opts, _state.SelectedProjectId, compact: true),
                AiFilterService.BuildSchema(), temperature: 0.0);
        }

        var costs = _state.AllCosts;
        var outcome = AiFilterService.ApplyHints(ai, request, _state.Criteria, opts, _state.Projects, _state.Hierarchy,
            trade => AnalysisService.WorkOptions(costs, trade),
            (trade, work) => AnalysisService.ItemOptions(costs, trade, work));

        var result = await AnalyzeAsync(outcome.Criteria);
        var report = AiFilterService.BuildReport(request, outcome.Reason, result.Criteria, result.ProjectCount);
        var summary = "【判断サマリー】\n" + InsightTextService.Summary(result, result.Criteria);
        return new ChatResult("filter",
            new List<ChatBubble> { new("assistant", report), new("assistant", summary) },
            result);
    }

    private ChatResult Insight(string kind)
    {
        RequireLoaded();
        var r = _state.LastResult ?? throw new InvalidOperationException("統計情報がまだありません。先に条件設定を行ってください。");
        var c = _state.Criteria;
        var text = kind switch
        {
            "review" => "【確認観点の提案】\n" + InsightTextService.ReviewPoints(r, c),
            "project" => "【選択物件を説明】\n" + InsightTextService.SelectedProject(_state.FindProject(_state.SelectedProjectId), r),
            _ => "【判断サマリー】\n" + InsightTextService.Summary(r, c),
        };
        return new ChatResult(kind, new List<ChatBubble> { new("assistant", text) }, null);
    }

    /// <summary>
    /// チャット入力。AI が会話の流れから対応を選ぶ（<see cref="ChatAssistantService"/>）:
    /// 雑談・使い方・今の結果への質問には会話で答え、絞込みや判断サマリーが必要ならその機能を実行する。
    /// 「確認観点」「この物件」などはっきりしたキーワードは AI を通さずにすぐ実行する。
    /// </summary>
    private async Task<ChatResult> ChatAsync(string text)
    {
        text = (text ?? "").Trim();
        if (text.Length == 0) throw new InvalidOperationException("メッセージを入力してください。");
        var history = _state.ChatHistory;
        var loaded = _state.IsLoaded;

        var keyword = InsightTextService.ClassifyChat(text, loaded);
        AssistantDecision decision;
        if (loaded && keyword is ChatIntent.ReviewPoints or ChatIntent.SelectedProject or ChatIntent.Summary)
        {
            decision = new AssistantDecision(keyword switch
            {
                ChatIntent.ReviewPoints => AssistantRoute.Review,
                ChatIntent.SelectedProject => AssistantRoute.Project,
                _ => AssistantRoute.Summary,
            }, "");
        }
        else
        {
            var context = ChatAssistantService.BuildContext(loaded, _state.StructureType, _state.Projects.Count,
                loaded ? _state.LastResult : null, _state.Criteria, _state.FindProject(_state.SelectedProjectId),
                await LookupTableLabelsAsync());
            try
            {
                var ai = await new OllamaClient(_state.Settings.Llm).ChatJsonAsync(
                    ChatAssistantService.BuildPrompt(text, history, context), ChatAssistantService.BuildSchema(),
                    temperature: _state.Settings.Llm.Temperature);
                decision = ChatAssistantService.Parse(ai);
            }
            catch (InvalidOperationException e) when (e.Message.StartsWith("AI応答からJSON", StringComparison.Ordinal))
            {
                // 返答の形が崩れたときは、これまでどおり条件設定として扱う
                decision = new AssistantDecision(AssistantRoute.Filter, "");
            }
        }

        ChatResult result;
        if (decision.Route == AssistantRoute.Lookup)
        {
            result = await LookupAsync(text, history);   // DB を直接調べるので、分析用データの読込は不要
        }
        else if (decision.Route != AssistantRoute.Chat && !loaded)
        {
            result = Say(decision.Reply.Length > 0 && decision.Route == AssistantRoute.Filter
                ? decision.Reply + "\nただ、まだデータが読み込まれていません。ヘッダーの「⚡ データ読込」を押してから、もう一度お願いします。"
                : "まだデータが読み込まれていないため、その操作はできません。ヘッダーの「⚡ データ読込」を押してから、もう一度お願いします。");
        }
        else
        {
            result = decision.Route switch
            {
                AssistantRoute.Filter => await AiFilterAsync(text),
                AssistantRoute.Summary => Insight("summary"),
                AssistantRoute.Review => Insight("review"),
                AssistantRoute.Project => Insight("project"),
                _ => Say(decision.Reply.Length > 0 ? decision.Reply : "すみません、うまく答えられませんでした。言い方を変えてもう一度お願いします。"),
            };
        }

        ChatAssistantService.Remember(history, "user", text);
        ChatAssistantService.Remember(history, "assistant", string.Join("\n", result.Messages.Select(m => m.Text)));
        return result;
    }

    private static ChatResult Say(string text) => new("chat", new List<ChatBubble> { new("assistant", text) }, null);

    /// <summary>AI に見せる「参照できるテーブル」の日本語名（DB に接続できなければ空）。</summary>
    private async Task<List<string>> LookupTableLabelsAsync()
    {
        try
        {
            var (tables, _) = await _dbEditor.CatalogAsync(_state.Settings.Db);
            return tables.Where(t => !DbLookupService.IsExcluded(t, _state.Settings.Db.Tables)).Select(t => t.Label).Distinct().ToList();
        }
        catch (Exception)
        {
            return new List<string>();
        }
    }

    /// <summary>
    /// DB を調べて答える（<see cref="DbLookupService"/>）: AI が調べ方を決める → 読み取り専用で検索 → 結果だけを根拠に AI が答える。
    /// </summary>
    private async Task<ChatResult> LookupAsync(string text, IReadOnlyList<Llm.ChatMessage> history)
    {
        List<DbTableInfo> tables;
        List<ColumnRef> columns;
        try
        {
            (tables, columns) = await _dbEditor.CatalogAsync(_state.Settings.Db);
        }
        catch (Exception e)
        {
            return Say($"データベースに接続できないため、調べられませんでした。設定 → DB接続 を確認してください。\n（{e.Message}）");
        }
        tables = tables.Where(t => !DbLookupService.IsExcluded(t, _state.Settings.Db.Tables)).ToList();
        if (tables.Count == 0) return Say("調べられるテーブルが見つかりませんでした。");

        var client = new OllamaClient(_state.Settings.Llm);
        var planJson = await client.ChatJsonAsync(
            DbLookupService.BuildPlanPrompt(text, history, DbLookupService.BuildCatalog(tables, columns)),
            DbLookupService.BuildPlanSchema(tables.Select(DbLookupService.Key)), temperature: 0.0);
        var plan = DbLookupService.ParsePlan(planJson, tables);
        var rows = await _dbEditor.LookupAsync(_state.Settings.Db, plan);
        var answer = await client.ChatAsync(DbLookupService.BuildAnswerPrompt(text, rows), temperature: _state.Settings.Llm.Temperature);

        return new ChatResult("lookup", new List<ChatBubble>
        {
            new("assistant", LlmResponseParser.StripThink(answer).Trim()),
            new("system", DbLookupService.SourceNote(rows)),
        }, null)
        {
            Table = new ChatTableLink(rows.Table.Schema, rows.Table.Name, rows.Table.Label, rows.Keywords.FirstOrDefault()),
        };
    }

    private object ChatReset()
    {
        _state.ChatHistory.Clear();
        return new { cleared = true };
    }

    // ------------------------------------------------------------------

    private FilterOptionsSnapshot BuildOptionsSnapshot()
    {
        var p = _state.Projects;
        var c = _state.Criteria;
        var costs = _state.AllCosts;
        var bounds = ProjectFilterService.AreaBounds(p, c.BasisMode);
        return new FilterOptionsSnapshot(
            ProjectFilterService.MakeOptions(p.Select(x => x.Prefecture)),
            ProjectFilterService.MakeOptions(p.Select(x => x.City)),
            ProjectFilterService.MakeOptions(p.Select(x => x.Town)),
            ProjectFilterService.MakeOptions(p.Select(x => x.Structure)),
            ProjectFilterService.MakeOptions(p.Select(x => x.Form), excludeNumeric: true),
            ProjectFilterService.MakeOptions(p.Select(x => ProjectFilterService.FormatFloor(x.FloorsAbove))),
            ProjectFilterService.MakeOptions(p.Select(x => ProjectFilterService.FormatFloor(x.FloorsBelow))),
            AnalysisService.TradeOptions(costs),
            AnalysisService.WorkOptions(costs, c.Trade),
            AnalysisService.ItemOptions(costs, c.Trade, c.Work),
            bounds?.Min ?? 0, bounds?.Max ?? 0);
    }

    private void RequireLoaded()
    {
        if (!_state.IsLoaded) throw new InvalidOperationException("先にデータを読込してください。");
    }

    public static string Serialize(BridgeResponse r) => JsonSerializer.Serialize(r, BridgeJson.Options);
}
