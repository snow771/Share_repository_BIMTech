// このファイルの目的:
//   仕上表出力(Task 6〜8)の各部品(Revit収集器、一般名称解決、集約、Excel出力、確認画面)を
//   一つのコントローラーへ組み立て、Revitコマンド(Task 8)から呼べる同期APIを提供します。
//
// なぜPrepareAsyncはExcelを書かないのか:
//   利用者が確認ダイアログでキャンセルした場合にファイルを作らないためです。
//   件数と警告だけを先に集め、確認が取れてから初めてWriteを呼びます。
//
// なぜコントローラーの技術エラーをErrorMessageへ丸めるのか:
//   IExternalCommand.Executeのmessageやこの後のTaskDialogに生の例外メッセージを出すと、
//   内部パスやスタックトレースが利用者の画面に出てしまいます
//   (Bui.Revit.SharedParameters.InstallIdentifiersCommandと同じ方針)。
//   RevitFinishSourceCollectorが投げるInvalidOperationExceptionは、呼出元が翻訳する前提の
//   利用者向け文言（部屋集計表が無い等）であるため、そのままErrorMessageへ渡してよい設計です
//   (RevitFinishSourceCollector_仕上表元データ取得.csの冒頭コメント参照)。
//   それ以外の未知の例外は、内部詳細を含み得るため定型文へ丸めます。
//
// 修正時の影響:
//   Runの中で生の例外(exception.ToString()やexception.Message全般)をErrorMessageへ
//   そのまま入れないでください。Excelを書いた後に失敗した場合でも、
//   出力先には中途半端なファイルを残さないよう、Write失敗時はOutputPathをnullのままにします。

using System.Reflection;
using Autodesk.Revit.DB;
using Bui.Configuration;
using Bui.Data;
using Bui.FinishSchedules;
using Bui.Logging;
using Bui.Revit.FinishSchedules;
using Bui.UI.FinishSchedules;

namespace Bui.Application;

/// <summary>
/// 出力調整の結果です。Excelはまだ書いていません。確認ダイアログの本文・詳細は
/// Bui.FinishSchedules.FinishScheduleDialogTextへこの値の該当フィールドを渡して組み立てます。
/// </summary>
internal sealed record FinishSchedulePreparedResult(
    FinishScheduleOutput Output,
    int InternalRoomCount,
    int ExternalTypeCount,
    int MissingProductIdCount,
    int AffectedRoomCount,
    int AffectedExternalTypeCount)
{
    public static FinishSchedulePreparedResult From(FinishScheduleOutput output)
    {
        ArgumentNullException.ThrowIfNull(output);
        return new FinishSchedulePreparedResult(
            output,
            output.InternalRows.Count,
            output.ExternalRows.Count,
            CountDistinctMissingIds(output),
            output.InternalRows.Count(HasMissingCell),
            output.ExternalRows.Count(x => x.Finish.HighlightMissing || x.Substrate.HighlightMissing));
    }

    /// <summary>
    /// 内部・外部どちらの行にも、未解決Product_IDは備考(Notes)へ
    /// 「一般名称未登録: {ID}」または「Product_ID未登録: {ID}」の形式で1行ずつ入る
    /// (FinishScheduleAggregator.FormatNote参照)。接頭辞の文字列はFinishScheduleAggregatorの
    /// 公開定数(GeneralNameMissingNotePrefix/ProductMissingNotePrefix)をここでも参照することで、
    /// 文言変更時にこの件数集計が黙って壊れないようにする。
    /// セル自体はHighlightMissingしか持たずIDを保持しないため、件数はNotesから数える。
    /// </summary>
    private static int CountDistinctMissingIds(FinishScheduleOutput output)
    {
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var notes in output.InternalRows.Select(x => x.Notes).Concat(output.ExternalRows.Select(x => x.Notes)))
        {
            AddMissingIds(notes, ids);
        }

        return ids.Count;
    }

    private static void AddMissingIds(string notes, HashSet<string> ids)
    {
        if (string.IsNullOrEmpty(notes))
        {
            return;
        }

        foreach (var line in notes.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries))
        {
            var id = ExtractMissingId(line, FinishScheduleAggregator.GeneralNameMissingNotePrefix)
                ?? ExtractMissingId(line, FinishScheduleAggregator.ProductMissingNotePrefix);
            if (id is not null)
            {
                ids.Add(id);
            }
        }
    }

    private static string? ExtractMissingId(string line, string prefix) =>
        line.StartsWith(prefix, StringComparison.Ordinal) ? line[prefix.Length..] : null;

    private static bool HasMissingCell(InternalFinishRow row) =>
        row.FloorFinish.HighlightMissing || row.FloorSubstrate.HighlightMissing
        || row.WallFinish.HighlightMissing || row.WallSubstrate.HighlightMissing
        || row.CeilingFinish.HighlightMissing || row.CeilingSubstrate.HighlightMissing;
}

/// <summary>収集・分類・解決・集約を行い、Excelはまだ書かない出力調整サービスです。</summary>
internal sealed class FinishScheduleExportService
{
    private readonly RevitFinishSourceCollector _collector;
    private readonly GeneralNameResolver _resolver;

    // 事前確認の段階ではExcelを書かないため、このサービスはwriterを持ちません。
    // 実際のWrite呼び出しはFinishScheduleExportController.Runが確認後に行います。

    public FinishScheduleExportService(RevitFinishSourceCollector collector, GeneralNameResolver resolver)
    {
        _collector = collector ?? throw new ArgumentNullException(nameof(collector));
        _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
    }

    public async Task<FinishSchedulePreparedResult> PrepareAsync(Document document, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(document);

        var snapshot = _collector.Collect(document);
        var classifications = snapshot.Hosts.Select(FinishLayerClassifier.Classify).ToArray();
        var placements = classifications
            .SelectMany(x => x.InternalPlacements.Concat(x.ExternalPlacements))
            .ToArray();
        var resolved = await _resolver.ResolveAsync(placements, token).ConfigureAwait(false);
        var output = FinishScheduleAggregator.Build(
            snapshot.Rooms,
            resolved,
            snapshot.Warnings.Concat(classifications.SelectMany(x => x.Warnings)).ToArray());
        return FinishSchedulePreparedResult.From(output);
    }
}

/// <summary>
/// Task 8のIExternalCommandへ返す実行結果です。Cancelled=trueのときOutputPathは常にnull、
/// ErrorMessageは空文字です(確認・保存先選択の取消はエラーではないため)。
/// </summary>
internal sealed record FinishScheduleCommandResult(bool Cancelled, string? OutputPath, string ErrorMessage);

/// <summary>
/// 出力調整・確認・保存先選択・Excel出力を1回の操作としてまとめる同期コントローラーです。
/// Task 8のIExternalCommandはこのRunを呼ぶだけで、Revit API呼び出しの詳細を知る必要はありません。
/// </summary>
internal sealed class FinishScheduleExportController
{
    private const string PrepareOperationName = "EXPORT_FINISH_SCHEDULE_PREPARE";
    private const string WriteOperationName = "EXPORT_FINISH_SCHEDULE_WRITE";

    private readonly FinishScheduleExportService _service;
    private readonly FinishScheduleWorkbookWriter _writer;
    private readonly FinishScheduleExportDialog _dialog;
    private readonly string _templatePath;
    private readonly IBuiLogger _logger;

    public FinishScheduleExportController(
        FinishScheduleExportService service,
        FinishScheduleWorkbookWriter writer,
        FinishScheduleExportDialog dialog,
        string templatePath,
        IBuiLogger logger)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));
        _writer = writer ?? throw new ArgumentNullException(nameof(writer));
        _dialog = dialog ?? throw new ArgumentNullException(nameof(dialog));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        if (string.IsNullOrWhiteSpace(templatePath))
        {
            throw new ArgumentException("雛型のパスが空です。", nameof(templatePath));
        }

        _templatePath = templatePath;
    }

    /// <summary>
    /// 収集→確認→保存先選択→出力の順に、同期で1回だけ実行します。
    /// Revitコマンドの実行スレッドから直接呼ぶ前提のため、非同期処理は内部で待ち合わせます。
    /// </summary>
    public FinishScheduleCommandResult Run(Document document, IntPtr ownerHandle)
    {
        ArgumentNullException.ThrowIfNull(document);

        FinishSchedulePreparedResult prepared;
        try
        {
            // Executeスレッドから同期で呼ぶため、ここでawaitせず待ち合わせる。
            // GeneralNameResolverはConfigureAwait(false)で継続するため、UIコンテキストへの
            // 掛け戻りが無くデッドロックの心配はない。
            prepared = _service.PrepareAsync(document, CancellationToken.None).GetAwaiter().GetResult();
        }
        catch (InvalidOperationException exception)
        {
            // RevitFinishSourceCollectorが想定内で処理を止めたとき(部屋集計表/列が無い等)の
            // 利用者向け文言。内部パスや例外種別を含まない前提(collector側の設計)であり、
            // メッセージ自体が画面にそのまま出るため、ここは元の挙動どおりログを書かない
            // (想定内の運用エラーであり、未知の障害用の診断ログを汚さないため)。
            return new FinishScheduleCommandResult(false, null, exception.Message);
        }
        catch (Exception exception)
        {
            // 想定外の技術的失敗。画面には内部詳細を出さないが、この例外を握りつぶしたまま
            // 呼出元(ExportFinishScheduleCommand)のBuiRuntimeSafety.Runへ渡さないため、
            // ここで自前にログを書かないと失敗の記録が一切残らない(会社PCでしか再現しない
            // デバッグの唯一の手がかりを失う)。
            LogUnexpected(PrepareOperationName, exception);
            return new FinishScheduleCommandResult(
                false, null, "仕上表の元データ収集中に予期しない問題が発生しました。BUIの管理者へお伝えください。");
        }

        if (!_dialog.ConfirmExport(prepared))
        {
            return new FinishScheduleCommandResult(true, null, string.Empty);
        }

        var outputPath = _dialog.PickSaveFilePath(document.Title, ownerHandle);
        if (string.IsNullOrWhiteSpace(outputPath))
        {
            return new FinishScheduleCommandResult(true, null, string.Empty);
        }

        try
        {
            _writer.Write(_templatePath, outputPath, prepared.Output);
        }
        catch (InvalidOperationException exception)
        {
            // FinishScheduleWorkbookWriter.Writeが投げるInvalidOperationExceptionは、
            // 雛型の同梱ファイル自体が無い/破損している(想定シート・スタイル・行が見つからない)
            // ときの合図。「保存先を確認してください」という汎用文言は原因が違うため、
            // 雛型の再配置が要る旨を別文言で伝える(原因を画面に出す、の教訓を踏襲)。
            LogUnexpected(WriteOperationName, exception);
            return new FinishScheduleCommandResult(
                false, null, "同梱の雛型を読み込めません。BUIの再配置が必要です。");
        }
        catch (Exception exception)
        {
            LogUnexpected(WriteOperationName, exception);
            return new FinishScheduleCommandResult(
                false, null, "仕上表の保存に失敗しました。保存先を確認してもう一度お試しください。");
        }

        return new FinishScheduleCommandResult(false, outputPath, string.Empty);
    }

    /// <summary>
    /// ここで例外をErrorMessageへ丸めて返す前に、ErrorBoundary.CreateEntryと同じ形(操作名+
    /// 例外の型名+安全な技術詳細)でBUIログへ書く。この2つのcatchは呼出元
    /// (ExportFinishScheduleCommand)のBuiRuntimeSafety.Runより内側で例外を最終的に飲み込むため、
    /// ここで書かないとログに何も残らない。
    /// FileBuiLogger.WriteAsync自体がディスク障害等を握りつぶす設計のため、ここでも
    /// ログ書き込み失敗でRun全体を失敗させないようtry/catchで包む。
    /// </summary>
    private void LogUnexpected(string operation, Exception exception)
    {
        try
        {
            var entry = new LogEntry(
                DateTimeOffset.UtcNow, "Error", operation, "UNEXPECTED",
                exception.GetType().Name, SafeTechnicalDetail(exception), null);
            _logger.WriteAsync(entry, CancellationToken.None).GetAwaiter().GetResult();
        }
        catch
        {
            // ログは補助機能。ここでの失敗によりRunの本来の結果(利用者向けメッセージ)を変えない。
        }
    }

    /// <summary>Bui.Logging.ErrorBoundary.SafeTechnicalDetailと同じ考え方の、画面へは出さない技術詳細です。</summary>
    private static string SafeTechnicalDetail(Exception exception) => exception switch
    {
        IOException => "An input/output operation failed.",
        UnauthorizedAccessException => "Access to a required resource was denied.",
        InvalidOperationException => "The operation was invoked in an invalid state.",
        _ => "The operation raised an unexpected exception."
    };
}

/// <summary>仕上表出力コマンド(Task 8)が使う部品一式を組み立てます。</summary>
internal static class FinishScheduleComposition
{
    private const string TemplateFileName = "仕上表_雛型.xlsx";
    private const string TemplateFolderName = "Resources_画像と定義";

    public static FinishScheduleExportController Create(IProductRepository products)
    {
        ArgumentNullException.ThrowIfNull(products);

        // BuiDockPaneComposition.Createと同じ、実行中DLLの配置フォルダーからの相対解決。
        // 雛型はBui.Addin.csprojでこのフォルダーへコピーされる(Task 5)。
        var assemblyDirectory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)
            ?? throw new InvalidOperationException("BUI DLLの配置フォルダーを取得できませんでした。");
        var templatePath = Path.Combine(assemblyDirectory, TemplateFolderName, TemplateFileName);

        var collector = new RevitFinishSourceCollector();
        var resolver = new GeneralNameResolver(products);
        var writer = new FinishScheduleWorkbookWriter();
        var service = new FinishScheduleExportService(collector, resolver);
        var dialog = new FinishScheduleExportDialog();
        var logger = CreateLogger();

        return new FinishScheduleExportController(service, writer, dialog, templatePath, logger);
    }

    /// <summary>
    /// Bui.Application.BuiRuntimeSafetyと同じ保存先(%LOCALAPPDATA%/PoC/BUI/Logs)・
    /// 同じバージョン取得方法のIBuiLoggerを作る。コントローラー内で握りつぶしていた例外を
    /// このロガーへ書くことで、BuiRuntimeSafety.Runが外側で捕まえられない失敗も
    /// 同じログファイルへ残す(2つのログ経路を持たない)。
    /// </summary>
    private static IBuiLogger CreateLogger()
    {
        var localRoot = BuiLocalPaths.Root;
        var assembly = typeof(FinishScheduleComposition).Assembly;
        var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        var version = !string.IsNullOrWhiteSpace(informational)
            ? informational.Split('+', 2)[0]
            : assembly.GetName().Version?.ToString(3) ?? "unknown";
        return new FileBuiLogger(Path.Combine(localRoot, "Logs"), version);
    }
}
