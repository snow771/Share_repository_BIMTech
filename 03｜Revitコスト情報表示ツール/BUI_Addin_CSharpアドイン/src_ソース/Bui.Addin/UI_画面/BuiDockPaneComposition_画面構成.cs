// このファイルは、BUIパネルが使う共有ViewModelとRevit/CSVアダプターを一か所で組み立てます。
// メタデータ、CSVリポジトリ、Revit ExternalEventに依存し、Task 11の設定永続化でこの接続部分を更新します。
using System.IO;
using System.Reflection;
using System.Windows.Threading;
using Bui.Configuration;
using Bui.Data;
using Bui.Data.Csv;
using Bui.Data.GoldStandard;
using Bui.Llm;
using Bui.Logging;
using Bui.Metadata;
using Bui.Revit.Audit;
using Bui.Revit.Data;
using Bui.Revit.ExternalEvents;
using Bui.Revit.UI;

namespace Bui.UI;

/// <summary>
/// BuiDockPaneComposition.Createの戻り値です。ViewModelはパネルへ、
/// ProductRepositoryはBuiSession経由で仕上表出力（Task 8）など
/// パネル外のコマンドへ渡します。両方とも同じ構成手順で一度だけ作った同一インスタンスです。
/// </summary>
internal sealed record BuiDockPaneCompositionResult(
    BuiDockPaneViewModel ViewModel,
    IProductRepository ProductRepository);

internal static class BuiDockPaneComposition
{
    public static BuiDockPaneCompositionResult Create(RevitActionDispatcher actionDispatcher, Dispatcher dispatcher)
    {
        var assembly = Assembly.GetExecutingAssembly();
        var assemblyDirectory = Path.GetDirectoryName(assembly.Location)
            ?? throw new InvalidOperationException("BUI DLLの配置フォルダーを取得できませんでした。");
        var metadata = MetadataLoader.Load(Path.Combine(assemblyDirectory, "Config", "AddinMetadata_アドイン情報.json"));
        var localRoot = BuiLocalPaths.Root;
        var version = ReadDisplayVersion(assembly);
        var logger = new FileBuiLogger(Path.Combine(localRoot, "Logs"), version);
        var boundary = new ErrorBoundary(logger);
        var settingsService = SettingsService.CreateForCurrentUser(Path.Combine(assemblyDirectory, "Config", "Settings.Default_共通設定.json"));
        var loadedResult = settingsService.Load();
        if (!loadedResult.IsSuccess)
            throw new InvalidOperationException(loadedResult.Error!.UserMessage);
        var loaded = loadedResult.Value!;
        var providerCheck = DataProviderPolicy.Validate(loaded.Settings.Data.Provider);
        if (!providerCheck.IsSuccess)
            throw new InvalidOperationException(providerCheck.Error!.UserMessage);

        // 出荷JSONのCSVパスは意図的に空欄です。初回起動を失敗させず設定画面を開けるよう、
        // 実行用リポジトリだけはBUI管理下の未作成CSVパスを仮に使います。保存設定の空欄は勝手に書き換えません。
        var dataRoot = Path.Combine(localRoot, "Data");
        // 出荷JSONのパスは意図的に空欄です。初回起動を失敗させず設定画面を開けるよう、
        // 未設定のときはBUI管理下の未作成フォルダを仮に使います。保存設定の空欄は勝手に書き換えません。
        var dataFolder = string.IsNullOrWhiteSpace(loaded.Settings.Data.DataFolderPath)
            ? dataRoot
            : loaded.Settings.Data.DataFolderPath;
        var pricePath = Bui.Configuration.CsvPathResolver.ResolvePricePath(loaded.Settings.Data.PriceCsvPath, dataRoot);
        var assemblyPath = Bui.Configuration.CsvPathResolver.ResolveAssemblyPath(loaded.Settings.Data.AssemblyCsvPath, dataRoot);
        // 起動時は必ずCSVから始めます。SQL用のアセンブリが読めない環境でも
        // BUIパネルが開かなくなることを避けるためです（設計 §5）。
        // 保存された Provider が "SqlServer" の場合は、その旨を起動時の警告に出します。
        var dataSource = DataSourceSwitch.ForCsvFolder(dataFolder);
        var products = new GoldStandardProductRepository(dataSource);
        var productDetails = new GoldStandardDetailSource(dataSource);
        var prices = new SwitchingPriceRepository(
            new CsvPriceRepository(pricePath, loaded.Settings.Data.PriceColumns), dataSource, loaded.Settings.Data.PriceColumns);
        var assemblies = new SwitchingAssemblyRepository(new CsvAssemblyRepository(assemblyPath));
        var startupWarnings = new List<string>(loaded.Warnings);
        if (string.Equals(loaded.Settings.Data.Provider, "SqlServer", StringComparison.OrdinalIgnoreCase))
        {
            startupWarnings.Add(
                "前回はSQL Serverを使う設定でした。起動時はCSVフォルダから始めます。"
                + "設定タブで「切り替えて読み直す」を押してください。");
        }
        var oldIdMappings = LoadOldIdMappings(loaded.Settings.Data.IdAliasCsvPath, startupWarnings);
        // チャット側（BuiChatComposition）が1往復ごとに表記ゆれ.csvを読み直すときも、
        // このパスをそのまま使い回します。ここで解決したパスと違うパスを毎回計算し直すと、
        // 起動時の警告（LastProblems）とチャットで実際に読む場所がずれる恐れがあるためです。
        var synonymCsvResolvedPath = SynonymCsvPathResolver.Resolve(loaded.Settings.Data.SynonymCsvPath, dataFolder);
        var synonyms = LoadSynonyms(synonymCsvResolvedPath, startupWarnings);
        var selection = new RevitSelectionDetailsService(actionDispatcher);
        var audit = new RevitAuditQueryService(new IdentifierAuditService(products, actionDispatcher), oldIdMappings);
        var connectionTester = new SecureLlmConnectionTester(apiKeyProvider: settingsService.GetCurrentApiKey);
        ILlmProvider llm = new DisabledLlmProvider();
        if (loaded.Settings.Llm.Enabled && Uri.TryCreate(loaded.Settings.Llm.BaseUrl, UriKind.Absolute, out var llmUri))
        {
            // 本番経路はTask 5のfactoryを必ず通し、DNS pin・redirect/proxy無効を維持します。
            var provider = connectionTester.CreateProviderAsync(loaded.Settings.Llm, llmUri, CancellationToken.None)
                .GetAwaiter().GetResult();
            if (provider.IsSuccess) llm = provider.Value!;
        }

        // ファイル選択画面と画面更新は同じUIスレッド窓口を共有します。
        var uiDispatcher = new WpfUiDispatcher(dispatcher);

        // RevitIdentifierWorkflowServiceはIIdentifierWorkflowServiceとILayerAssignmentServiceの両方を
        // 実装します。同じインスタンスを両方の窓口へ渡し、選択情報の取得経路を1つにまとめます。
        var identifierWorkflow = new RevitIdentifierWorkflowService(products, assemblies, actionDispatcher);

        var viewModel = new BuiDockPaneViewModel(
            products, prices, selection, audit, uiDispatcher, llm,
            metadata, version, File.GetLastWriteTimeUtc(assembly.Location).ToString("yyyy-MM-dd HH:mm:ss 'UTC'"),
            llmEnabled: loaded.Settings.Llm.Enabled, navigation: new RevitElementNavigationService(actionDispatcher),
            commandErrorHandler: new BuiAsyncCommandErrorHandler(boundary), settingsEditor: settingsService,
            llmConnectionTester: connectionTester, initialSettings: loaded.Settings, initialWarnings: startupWarnings,
            identifierWorkflow: identifierWorkflow,
            confirmationService: new WpfConfirmationService(),
            filePicker: new WpfFilePickerService(uiDispatcher),
            candidateSource: new InMemoryCandidateSource(
                products, loaded.Settings.Data.ProductColumns, loaded.Settings.Data.CandidateFilterColumns),
            productDetails: productDetails,
            layerAssignment: identifierWorkflow,
            dataSourceConnector: new RevitDataSourceConnector(dataSource, prices, assemblies, dataRoot),
            synonyms: synonyms,
            synonymCsvResolvedPath: synonymCsvResolvedPath);

        // productsはBuiDockPaneViewModelへ渡したのと同一インスタンスです。
        // 仕上表出力（Task 8）がBuiSession経由で同じデータソースを参照できるよう、
        // ここで一緒に返します（新しいリポジトリを作り直さない）。
        return new BuiDockPaneCompositionResult(viewModel, products);
    }

    /// <summary>
    /// 表記ゆれ.csv を読み、未設定・不在・不正でも起動もチャットも止めず、空の表で続行します。
    /// 表記ゆれは検索の精度を上げる補助であり必須ではないため（CsvSynonymLoader.Load参照）、
    /// ここで例外を素通しすると、CSVの1行のせいでBUIパネル自体が開けなくなります。
    /// これは起動時に一度だけ読む経路です（起動直後の設定/概要タブの警告用）。
    /// チャットは別途、1往復ごとに同じパス（BuiChatComposition経由）を読み直します。
    /// </summary>
    private static SynonymTable LoadSynonyms(string? resolvedPath, List<string> warnings)
    {
        var loader = new CsvSynonymLoader();
        try
        {
            var table = loader.Load(resolvedPath);
            warnings.AddRange(loader.LastProblems);
            return table;
        }
        catch (Exception exception) when (exception is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            // CsvSynonymLoader.Loadは例外を投げない設計ですが、旧新ID CSVの読み込み（LoadOldIdMappings）と
            // 同じ防御を合わせておきます。例外メッセージにはパスが混じり得るため定型文だけを出します。
            // この文言はTroubleshooting_問題解決.md「表記ゆれCSVを読み込めないため、表記ゆれは使いません。」
            // の行と一致させています。利用者が画面の文言から手順書を引けるようにするためです。
            warnings.Add("表記ゆれCSVを読み込めないため、表記ゆれは使いません。");
            return SynonymTable.Empty;
        }
    }

    /// <summary>
    /// 任意の旧新ID CSVを読み、未設定・不正でも起動を止めず、点検の移行候補だけを無効化します。
    /// ここで例外を素通しすると、BUIパネル自体が開けなくなります。
    /// </summary>
    private static IReadOnlyDictionary<string, string> LoadOldIdMappings(string? aliasCsvPath, List<string> warnings)
    {
        var loader = new CsvIdAliasLoader();
        try
        {
            var map = loader.Load(aliasCsvPath);
            warnings.AddRange(loader.LastStatus.Problems);
            return map.Aliases;
        }
        catch (Exception exception) when (exception is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            warnings.Add($"旧新ID CSVを読み込めないため、旧IDの移行候補は表示しません。{exception.Message}");
            return new Dictionary<string, string>();
        }
    }

    private static string ReadDisplayVersion(Assembly assembly)
    {
        var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        return !string.IsNullOrWhiteSpace(informational)
            ? informational.Split('+', 2)[0]
            : assembly.GetName().Version?.ToString(3) ?? string.Empty;
    }
}
