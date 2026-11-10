// このファイルは、BUIの6タブで共有する表示状態と決定的な検索・比較・点検を管理します。
// Revit/WPFに依存せずリポジトリと境界契約だけを使い、変更は6タブの表示と非同期競合制御に影響します。
using System.Collections.ObjectModel;
using Bui.Common;
using Bui.Configuration;
using Bui.Data;
using Bui.Data.GoldStandard;
using Bui.Llm;
using Bui.Metadata;
using Bui.Products;

namespace Bui.UI;

public sealed class BuiDockPaneViewModel : ObservableObject, ISelectionSnapshotSink, IDisposable, IChatDataSourceStatus
{
    private readonly IFilePickerService? _filePicker;
    private readonly IProductRepository _products;
    private readonly IPriceRepository _prices;
    private readonly ISelectionDetailsService _selectionDetails;
    private readonly IAuditQueryService _audit;
    private readonly IUiDispatcher _ui;
    private readonly ILlmProvider _llm;
    private readonly IElementNavigationService? _navigation;
    private readonly ISettingsEditorService? _settingsEditor;
    private readonly ILlmConnectionTester? _llmConnectionTester;
    private readonly IAsyncCommandErrorHandler? _commandErrorHandler;
    private readonly IIdentifierWorkflowService? _identifierWorkflow;
    private readonly IUserConfirmationService? _confirmationService;
    private readonly ICandidateSource? _candidateSource;
    private readonly IProductDetailSource? _productDetails;
    private readonly ILayerAssignmentService? _layerAssignment;
    private readonly LlmSearchAssistant _llmSearchAssistant;
    private readonly LlmSettings _activeLlmSettings;
    private readonly LatestRequestCoordinator _selectionRequests = new();
    private readonly LatestRequestCoordinator _searchRequests = new();
    // 層をクリックしたときの製品カード読み直し用です。連続クリックで古い結果が
    // 後から届いて上書きしないよう、選択・検索と同じ最新要求管理を使います。
    private readonly LatestRequestCoordinator _layerCardRequests = new();
    // メーカー名・建材分類の選択肢と、明示的なマスタ追加の窓口です。
    // 無い環境（旧構成やテスト）では自由入力のままにし、画面は落としません。
    private IProductMasterSource? _masterSource;
    private readonly LatestRequestCoordinator _llmConnectionRequests = new();
    private readonly LatestRequestCoordinator _candidateRequests = new();
    private SelectionSummary _selection = SelectionSummary.FromSnapshot(new(false, Array.Empty<long>(), false, true));
    private ProductRecord? _selectedProduct;
    private string? _selectedProductId;
    private ProductDetail? _productCard;
    private string _productCardMessage = "要素を選択すると製品情報を表示します。";
    private string _selectionWarningText = string.Empty;
    private string? _assemblyId;
    private DataSourceStatus _dataStatus = new(false, 0, null, new[] { "未読込" });
    private bool _isLlmEnabled;
    private string _statusMessage = "準備完了";
    private string _llmSummary = string.Empty;
    private ProductRecord? _selectedSearchProduct;
    private AuditIssue? _selectedAuditIssue;
    private BuiSettings? _loadedSettings;
    private string _dataFolderPath = string.Empty;
    private string _priceCsvPath = string.Empty;
    private string _assemblyCsvPath = string.Empty;
    private string _llmProvider = "Ollama";
    private string _llmBaseUrl = "http://localhost:11434";
    private string _llmModel = string.Empty;
    private int _llmTimeoutSeconds = 30;
    private CompoundLayerViewModel? _selectedLayer;
    // 層の選択を外したときに戻る先です。RefreshSelectionAsyncで仕上のIDを控えます。
    private string _finishProductId = string.Empty;
    private string _productCardLayerLabel = string.Empty;
    private IdentifierWorkflowPreview? _pendingIdentifierPreview;
    private string _identifierPreviewText = "プレビューはまだありません。";
    private string _settingsStatusMessage = "設定を読み込みました。";
    private ProductRecord? _selectedCandidate;
    private string _candidateSummary = "候補0件";
    private readonly ProductRegistrationCoordinator _newProductRegistration;
    private string _newProductIdPreview = string.Empty;
    private bool _canRegisterNewProduct;
    // 絞り込み結果を画面へ書き戻す間だけ真にします。書き戻しはSelectedを触るため、
    // これが無いと「選択が変わった→絞り込み直す→Selectedを書き戻す→…」で無限に回ります。
    // NewProductFieldsのOnNewProductFieldChangedと同じ「自分が起こした変更は無視する」考え方です。
    private bool _isApplyingCandidateView;
    private readonly IDataSourceConnector? _dataSourceConnector;
    private bool _useSqlServer;
    private string _sqlServerName = string.Empty;
    private string _sqlDatabaseName = string.Empty;
    private bool _isDataSourceReadOnly;
    private string _dataSourceDescription = string.Empty;
    private readonly SynonymTable _synonyms;
    private readonly string? _synonymCsvResolvedPath;

    public BuiDockPaneViewModel(
        IProductRepository products,
        IPriceRepository prices,
        ISelectionDetailsService selectionDetails,
        IAuditQueryService audit,
        IUiDispatcher ui,
        ILlmProvider llm,
        AddinMetadata metadata,
        string version,
        string buildTimestampUtc,
        bool llmEnabled = false,
        IElementNavigationService? navigation = null,
        IAsyncCommandErrorHandler? commandErrorHandler = null,
        ISettingsEditorService? settingsEditor = null,
        ILlmConnectionTester? llmConnectionTester = null,
        BuiSettings? initialSettings = null,
        IReadOnlyList<string>? initialWarnings = null,
        IIdentifierWorkflowService? identifierWorkflow = null,
        IUserConfirmationService? confirmationService = null,
        IFilePickerService? filePicker = null,
        ICandidateSource? candidateSource = null,
        IProductDetailSource? productDetails = null,
        ILayerAssignmentService? layerAssignment = null,
        IProductMasterSource? masterSource = null,
        IDataSourceConnector? dataSourceConnector = null,
        SynonymTable? synonyms = null,
        string? synonymCsvResolvedPath = null)
    {
        _products = products ?? throw new ArgumentNullException(nameof(products));
        _prices = prices ?? throw new ArgumentNullException(nameof(prices));
        _selectionDetails = selectionDetails ?? throw new ArgumentNullException(nameof(selectionDetails));
        _audit = audit ?? throw new ArgumentNullException(nameof(audit));
        _ui = ui ?? throw new ArgumentNullException(nameof(ui));
        _llm = llm ?? throw new ArgumentNullException(nameof(llm));
        _navigation = navigation;
        _settingsEditor = settingsEditor;
        _llmConnectionTester = llmConnectionTester;
        _commandErrorHandler = commandErrorHandler;
        _identifierWorkflow = identifierWorkflow;
        _confirmationService = confirmationService;
        _filePicker = filePicker;
        _candidateSource = candidateSource;
        _productDetails = productDetails;
        _layerAssignment = layerAssignment;
        // GoldStandardProductRepositoryはIProductRepositoryとIProductMasterSourceの両方です。
        // 明示的に渡されなければ、製品リポジトリがマスタ窓口も兼ねているかを見ます。
        _masterSource = masterSource ?? products as IProductMasterSource;
        _dataSourceConnector = dataSourceConnector;
        // 読めなくても会話は止めません（Bui.Data.Csv.CsvSynonymLoader.Loadが既にEmptyを返す設計）。
        // ここでもnull合体でEmptyへ倒し、呼び出し側にnull判定を強いません。
        _synonyms = synonyms ?? SynonymTable.Empty;
        // チャット側が「毎ターン読み直す」ための窓口（LlmChatSessionのsynonymsProvider）が
        // 使う、起動時に解決済みのパスです。ここで保持しておけば、チャットの組み立て側
        // （Bui.Addin）でパス解決ロジックを重複させずに済みます。
        _synonymCsvResolvedPath = synonymCsvResolvedPath;
        // 新規登録の採番・追記はこの画面が使う_productsをそのまま使います。専用のリポジトリ差し替えは不要です。
        _newProductRegistration = new ProductRegistrationCoordinator(_products);
        var safeInitialSettings = initialSettings is null ? null : WithoutSecrets(initialSettings);
        _loadedSettings = safeInitialSettings;
        // 実行中のLLM構成は起動時に固定し、編集途中の値で半分だけ切り替わることを防ぎます。
        _activeLlmSettings = safeInitialSettings?.Llm
            ?? new LlmSettings(llmEnabled, llm.ProviderId, string.Empty, string.Empty, 30, null);
        _llmSearchAssistant = new LlmSearchAssistant(
            llm,
            _activeLlmSettings.Enabled,
            _activeLlmSettings.Model,
            TimeSpan.FromSeconds(Math.Clamp(_activeLlmSettings.TimeoutSeconds, 1, 300)));
        VersionInfo = VersionInfoViewModel.FromMetadata(metadata ?? throw new ArgumentNullException(nameof(metadata)), version, buildTimestampUtc);
        _isLlmEnabled = llmEnabled;

        RefreshSelectionCommand = new AsyncCommand(c => RefreshSelectionAsync(LastSnapshot, c), commandErrorHandler);
        SearchCommand = new AsyncCommand(c => SearchAsync(SearchText, BuildSearchFilters(), c), commandErrorHandler);
        AuditCommand = new AsyncCommand(AuditAsync, commandErrorHandler);
        AddComparisonCommand = new AsyncCommand(c => SelectedSearchProduct is null
            ? SetStatusAsync("比較に追加する製品を選択してください。", c)
            : AddComparisonAsync(SelectedSearchProduct, c), commandErrorHandler);
        NavigateAuditIssueCommand = new AsyncCommand(NavigateSelectedAuditIssueAsync, commandErrorHandler);
        SaveSettingsCommand = new AsyncCommand(SaveSettingsAsync, commandErrorHandler);
        ReloadSettingsCommand = new AsyncCommand(ReloadSettingsAsync, commandErrorHandler);
        TestLlmConnectionCommand = new AsyncCommand(TestLlmConnectionAsync, commandErrorHandler);
        ApplyIdentifierPreviewCommand = new AsyncCommand(ApplyIdentifierPreviewAsync, commandErrorHandler);
        BrowseDataFolderCommand = new AsyncCommand(BrowseDataFolderAsync, commandErrorHandler);
        BrowsePriceCsvCommand = new AsyncCommand(BrowsePriceCsvAsync, commandErrorHandler);
        BrowseAssemblyCsvCommand = new AsyncCommand(BrowseAssemblyCsvAsync, commandErrorHandler);
        RefreshCandidatesCommand = new AsyncCommand(RefreshCandidatesAsync, commandErrorHandler);
        ClearCandidateFiltersCommand = new AsyncCommand(ClearCandidateFiltersAsync, commandErrorHandler);
        ResetSearchTabCommand = new AsyncCommand(ResetSearchTabAsync, commandErrorHandler);
        ResetCompareTabCommand = new AsyncCommand(ResetCompareTabAsync, commandErrorHandler);
        ResetAuditTabCommand = new AsyncCommand(ResetAuditTabAsync, commandErrorHandler);
        CheckNewProductCommand = new AsyncCommand(CheckNewProductAsync, commandErrorHandler);
        RegisterNewProductCommand = new AsyncCommand(RegisterNewProductAsync, commandErrorHandler);
        ApplySelectedCandidateToLayerCommand = new AsyncCommand(ApplySelectedCandidateToLayerAsync, commandErrorHandler);
        PreviewLayerAssignmentCommand = new AsyncCommand(PreviewLayerAssignmentAsync, commandErrorHandler);
        TestSqlConnectionCommand = new AsyncCommand(TestSqlConnectionAsync, commandErrorHandler);
        SwitchDataSourceCommand = new AsyncCommand(SwitchDataSourceAsync, commandErrorHandler);
        if (safeInitialSettings is not null) ApplySettings(safeInitialSettings);
        if (initialWarnings is { Count: > 0 })
        {
            // 起動時の警告（例: 前回SQL Server設定だったのに読み直せなかった旨）は、
            // 概要タブのStatusMessageだけでなく、対象操作のボタンがある設定タブにも出します。
            // ここに出さないと、案内文が指す「切り替えて読み直す」ボタンの近くに何も表示されません。
            var combinedWarnings = string.Join(" / ", initialWarnings);
            _statusMessage = $"設定を読み込みました。{combinedWarnings}";
            _settingsStatusMessage = $"設定を読み込みました。{combinedWarnings}";
        }

        // 起動直後から「いま読んでいるデータ元」を画面に出します。非同期にすると、
        // 起動直後は空欄のまま表示され、実際にはCSVで動いているのにSQLのラジオボタンだけが
        // 選ばれて見える瞬間ができてしまいます（コンストラクタからTaskは起こしません）。
        RefreshDataSourceStateCore();
    }

    public SelectionSnapshot LastSnapshot { get; private set; } = new(false, Array.Empty<long>(), false, true);
    public SelectionSummary Selection { get => _selection; private set => SetProperty(ref _selection, value); }
    public ProductRecord? SelectedProduct { get => _selectedProduct; private set => SetProperty(ref _selectedProduct, value); }

    /// <summary>
    /// 6つの層別Product_ID（仕上・下地①〜⑤）を1行に並べた要約であり、単一のIDではありません
    /// （LayerProductIdSummary.Describe参照）。選択のたびに計算していますが、現在どのXAMLにも
    /// バインドされておらず画面には出ません。層ごとのProduct_IDは概要タブの複合レイヤー一覧、
    /// 検索タブの層への割り当てで表示しています。
    /// </summary>
    public string? SelectedProductId { get => _selectedProductId; private set => SetProperty(ref _selectedProductId, value); }

    /// <summary>概要タブの製品カードの内容です。表示できない場合はnullになります。</summary>
    public ProductDetail? ProductCard
    {
        get => _productCard;
        private set
        {
            if (!SetProperty(ref _productCard, value)) return;
            RaisePropertyChanged(nameof(HasProductCard));
            RaisePropertyChanged(nameof(HasProductImage));
            // カードとタブがずれると、前の製品の仕様が新しい製品の名前の下に出ます。
            // 必ず同時に入れ替えます。
            Replace(ProductCardTabs, ProductCardTabBuilder.Build(value));
        }
    }

    /// <summary>カードを表示できるかどうかです。XAMLの表示切替に使います。</summary>
    public bool HasProductCard => ProductCard is not null;

    /// <summary>
    /// 表示できる製品画像があるかどうかです。
    /// Imageのバインドに空文字を渡すと、画面には何も出ないのにバインドエラーだけが出続けます。
    /// 画像の枠ごと出し分けるため、状態として持ちます。
    /// </summary>
    public bool HasProductImage => ProductCard is not null && ProductCard.ImagePath.Length > 0;

    /// <summary>カードを表示できない理由です。空欄のまま何も出さないと利用者が原因を追えません。</summary>
    public string ProductCardMessage
    {
        get => _productCardMessage;
        private set => SetProperty(ref _productCardMessage, value);
    }

    /// <summary>概要タブのカードに並べるタブです。表示グループの数に応じて増減します。</summary>
    public ObservableCollection<ProductCardTab> ProductCardTabs { get; } = new();

    /// <summary>
    /// 選択読込み時に出た警告（複数選択・リンク要素・読取専用・複合構造の読取失敗など）です。
    /// StatusMessageは検索タブでしか表示されなくなったため、概要タブで
    /// 「複合レイヤーが空/違う要素の詳細」の理由を追えるよう、ここに別枠で残します。
    /// </summary>
    public string SelectionWarningText
    {
        get => _selectionWarningText;
        private set
        {
            if (!SetProperty(ref _selectionWarningText, value)) return;
            RaisePropertyChanged(nameof(HasSelectionWarning));
        }
    }

    /// <summary>SelectionWarningTextの有無です。XAMLの表示切替に使います（文字列長を直接バインドしません）。</summary>
    public bool HasSelectionWarning => SelectionWarningText.Length > 0;

    public ObservableCollection<PriceRecord> SelectedPrices { get; } = new();
    public string? AssemblyId { get => _assemblyId; private set => SetProperty(ref _assemblyId, value); }
    public ObservableCollection<CompoundLayerViewModel> Layers { get; } = new();

    /// <summary>層の1行です。選んだIDだけが画面から変わります。</summary>
    public sealed class LayerAssignmentRowViewModel : ObservableObject
    {
        private string _selectedProductId = string.Empty;

        public LayerAssignmentRowViewModel(LayerAssignmentRow row)
        {
            Order = row.Order;
            SlotName = row.SlotName;
            MaterialName = row.MaterialName;
            ThicknessMillimeters = row.ThicknessMillimeters;
            CurrentProductId = row.CurrentProductId;
            Slot = row.Slot;
            // Revitの層の機能です。構成CSVのfunction列へ入ります。画面には出しません。
            // ここで落とすと、CSVのfunction列が空になります。
            LayerFunction = row.LayerFunction;
            _selectedProductId = row.SelectedProductId;
        }

        public int Order { get; }

        /// <summary>
        /// 画面に出す層の見出しです。LayerAssignmentRow.LayerLabel と同じ規則で1始まりにします。
        /// </summary>
        public string LayerLabel => $"レイヤー{Order + 1}";

        public Bui.Assemblies.LayerSlot Slot { get; }
        public string SlotName { get; }
        public string MaterialName { get; }
        public decimal ThicknessMillimeters { get; }
        public string CurrentProductId { get; }
        public string LayerFunction { get; }

        public string SelectedProductId
        {
            get => _selectedProductId;
            set => SetProperty(ref _selectedProductId, value);
        }
    }

    public ObservableCollection<LayerAssignmentRowViewModel> LayerRows { get; } = new();

    private LayerAssignmentRowViewModel? _selectedLayerRow;
    public LayerAssignmentRowViewModel? SelectedLayerRow
    {
        get => _selectedLayerRow;
        set => SetProperty(ref _selectedLayerRow, value);
    }

    private string _layerWarning = string.Empty;
    /// <summary>割り当てられなかった層の説明です。無ければ空文字。</summary>
    public string LayerWarning { get => _layerWarning; private set => SetProperty(ref _layerWarning, value); }

    private ProductAssignmentTarget _assignmentTarget = ProductAssignmentTarget.SameTypeAll;
    /// <summary>
    /// 付与範囲です。既定は同一タイプすべて。設計上そちらが本来の使い方で、
    /// 1件だけ付けたいときに切り替えます。
    /// </summary>
    public ProductAssignmentTarget AssignmentTarget
    {
        get => _assignmentTarget;
        set => SetProperty(ref _assignmentTarget, value);
    }
    public ObservableCollection<ProductRecord> SearchResults { get; } = new();
    public ObservableCollection<ComparisonItemViewModel> CompareItems { get; } = new();
    public ObservableCollection<AuditIssue> AuditIssues { get; } = new();
    public ObservableCollection<LlmModelInfo> AvailableLlmModels { get; } = new();
    public ProductRecord? SelectedSearchProduct { get => _selectedSearchProduct; set => SetProperty(ref _selectedSearchProduct, value); }
    public AuditIssue? SelectedAuditIssue { get => _selectedAuditIssue; set => SetProperty(ref _selectedAuditIssue, value); }
    public DataSourceStatus DataStatus
    {
        get => _dataStatus;
        private set
        {
            if (!SetProperty(ref _dataStatus, value)) return;
            RaisePropertyChanged(nameof(DataConnectionText));
            RaisePropertyChanged(nameof(DataUpdatedText));
            RaisePropertyChanged(nameof(DataProblemsText));
        }
    }
    /// <summary>
    /// 接続状態欄の見出しです。いま読んでいるデータ元に応じて変わります。
    /// このアドインではSQL Serverは読取専用（IsDataSourceReadOnly）で、CSVフォルダは書込み可能なので、
    /// これをそのままデータ元の判定に使えます。
    /// </summary>
    public string DataConnectionLabel => IsDataSourceReadOnly ? "SQL接続" : "CSV接続";

    /// <summary>詳細欄の見出しです。DataConnectionLabelと同じ判定基準を使います。</summary>
    public string DataProblemsLabel => IsDataSourceReadOnly ? "SQL詳細" : "CSV詳細";

    public string DataConnectionText => $"{DataConnectionLabel}: " + (DataStatus.IsAvailable
        ? $"接続正常 / {DataStatus.RowCount}行"
        : "接続できません");
    public string DataUpdatedText => DataStatus.LastUpdatedAt?.ToString("yyyy-MM-dd HH:mm:ss") ?? "更新日時不明";
    // 「読み込めません」だけでは利用者が対処できないため、原因の説明までは画面に出します。
    // ただしCSVのローカルファイルパスを含む文言（IO例外など）は出しません。会社の保存場所が画面に残るためです。
    // 列名の不一致や空のIDといったBUI自身のメッセージにはパスが入らないので、そのまま表示できます。
    // SQL Server接続中はContainsFilePathを適用しません。インスタンス名やSQL自身のメッセージには
    // ごく普通にバックスラッシュが入るため、CSV向けのこのフィルタを通すと理由が丸ごと消えてしまいます。
    public string DataProblemsText
    {
        get
        {
            if (DataStatus.Problems.Count == 0) return $"{DataProblemsLabel}: エラーなし";

            var isSqlActive = IsDataSourceReadOnly;
            var safe = isSqlActive
                ? DataStatus.Problems.ToArray()
                : DataStatus.Problems.Where(p => !ContainsFilePath(p)).ToArray();
            var prefix = isSqlActive ? "SQL Serverからデータを読み込めません。" : "CSVデータを読み込めません。";
            var fallbackSuffix = isSqlActive
                ? "設定タブでサーバー名・データベース名を確認し、「接続を確認」を押してください。"
                : "設定とファイルを確認してください。";
            return safe.Length == 0
                ? $"{DataProblemsLabel}: {prefix}{fallbackSuffix}"
                : $"{DataProblemsLabel}: {prefix}{string.Join(" / ", safe)}";
        }
    }

    /// <summary>パスらしき文字を含むかどうかの判定です。含む場合はCSV接続時に画面へ出しません。</summary>
    private static bool ContainsFilePath(string text) =>
        text.Contains('\\', StringComparison.Ordinal)
        || text.Contains('/', StringComparison.Ordinal)
        || text.Contains(":\\", StringComparison.Ordinal);
    public bool IsLlmEnabled
    {
        get => _isLlmEnabled;
        set
        {
            if (SetProperty(ref _isLlmEnabled, value)) InvalidateModelDiscovery(clearSelectedSuggestion: true);
        }
    }
    public string StatusMessage { get => _statusMessage; private set => SetProperty(ref _statusMessage, value); }

    // 設定タブの操作結果は、検索タブや概要タブへ混ざらないよう別に持ちます。
    // 以前は1つの状態表示を3タブで共有しており、設定を保存した結果が
    // 検索タブの最下段に出るなど、どの操作の結果か分からない表示になっていました。
    public string SettingsStatusMessage { get => _settingsStatusMessage; private set => SetProperty(ref _settingsStatusMessage, value); }
    public string LlmSummary { get => _llmSummary; private set => SetProperty(ref _llmSummary, value); }
    public VersionInfoViewModel VersionInfo { get; }
    // リセットで画面上の入力欄も空に戻す必要があるため、通知付きにしています。
    // 自動プロパティのままだと、コード側で空にしても画面のテキストが残ります。
    private string _searchText = string.Empty;
    private string _manufacturerFilter = string.Empty;
    private string _unitFilter = string.Empty;
    public string SearchText { get => _searchText; set => SetProperty(ref _searchText, value); }
    public IReadOnlyDictionary<string, string> SearchFilters { get; set; } = new Dictionary<string, string>();
    public string ManufacturerFilter { get => _manufacturerFilter; set => SetProperty(ref _manufacturerFilter, value); }
    public string UnitFilter { get => _unitFilter; set => SetProperty(ref _unitFilter, value); }

    // 画面には非秘密値だけを公開し、API keyはプロパティ自体を用意しません。
    /// <summary>GoldStandardのCSV一式が入ったフォルダです。1枚のCSVではありません。</summary>
    public string DataFolderPath { get => _dataFolderPath; set => SetProperty(ref _dataFolderPath, value); }
    public string PriceCsvPath { get => _priceCsvPath; set => SetProperty(ref _priceCsvPath, value); }
    public string AssemblyCsvPath { get => _assemblyCsvPath; set => SetProperty(ref _assemblyCsvPath, value); }
    public string LlmProvider
    {
        get => _llmProvider;
        set
        {
            if (SetProperty(ref _llmProvider, value)) InvalidateModelDiscovery(clearSelectedSuggestion: true);
        }
    }
    public string LlmBaseUrl
    {
        get => _llmBaseUrl;
        set
        {
            if (SetProperty(ref _llmBaseUrl, value)) InvalidateModelDiscovery(clearSelectedSuggestion: true);
        }
    }
    public string LlmModel { get => _llmModel; set => SetProperty(ref _llmModel, value); }
    public int LlmTimeoutSeconds
    {
        get => _llmTimeoutSeconds;
        set
        {
            if (SetProperty(ref _llmTimeoutSeconds, value)) InvalidateModelDiscovery(clearSelectedSuggestion: true);
        }
    }
    /// <summary>
    /// 概要タブの複合レイヤー一覧で選んでいる層です。
    /// 選ぶと製品カードがその層の製品へ切り替わります。
    /// 以前は仕上のIDしかカードに出せず、下地に別のIDが入っていても中身を見られませんでした。
    /// </summary>
    public CompoundLayerViewModel? SelectedLayer
    {
        get => _selectedLayer;
        set
        {
            if (!SetProperty(ref _selectedLayer, value)) return;
            // 画面操作なので待てません。失敗はReportException経由でログと状態表示へ出します。
            _ = ShowSelectedLayerProductCardAsync();
        }
    }

    /// <summary>いま製品カードに出している層の名前です。仕上に戻っているときは空文字。</summary>
    public string ProductCardLayerLabel
    {
        get => _productCardLayerLabel;
        private set => SetProperty(ref _productCardLayerLabel, value);
    }

    /// <summary>
    /// 選んでいる層の製品カードを読み直します。
    /// 層を選んでいない、または層にProduct_IDが無いときは仕上のカードへ戻します。
    /// </summary>
    public async Task ShowSelectedLayerProductCardAsync(CancellationToken outerToken = default)
    {
        using var request = _layerCardRequests.Begin(outerToken);
        try
        {
            var layer = SelectedLayer;
            var layerId = (layer?.ProductId ?? string.Empty).Trim();
            // 層にIDが無ければ仕上へ戻します。空のカードを出すより、
            // 直前まで見ていた仕上の内容が残る方が迷いません。
            var showsLayer = layerId.Length > 0;
            var targetId = showsLayer ? layerId : _finishProductId;
            var label = showsLayer ? DescribeLayer(layer!) : string.Empty;

            var card = await LoadProductCardAsync(targetId, request.Token).ConfigureAwait(false);
            if (!request.IsLatest) return;
            await _ui.InvokeAsync(() =>
            {
                if (!request.IsLatest) return;
                ProductCard = card.Detail;
                ProductCardLayerLabel = label;
                ProductCardMessage = card.Message.Length > 0 || !showsLayer
                    ? card.Message
                    : string.Empty;
                if (showsLayer && card.Detail is null && layerId.Length > 0)
                {
                    ProductCardMessage = $"{label} のProduct_ID『{layerId}』は製品データにありません。";
                }
            }, request.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // 新しい選択に追い越されただけです。
        }
        catch (Exception exception)
        {
            ReportException(exception);
        }
    }

    /// <summary>
    /// 製品カードの見出し・理由文に出す層の呼び名です。
    /// 層一覧と同じ LayerLabel を使います。ここだけ「順序 3」のままにすると、
    /// すぐ隣の一覧が「レイヤー4」と出ていて、同じ層が2つの名前で呼ばれます。
    /// </summary>
    private static string DescribeLayer(CompoundLayerViewModel layer)
    {
        var material = (layer.RevitMaterialName ?? string.Empty).Trim();
        return material.Length > 0
            ? $"{layer.LayerLabel}（{material}）"
            : layer.LayerLabel;
    }
    public IdentifierWorkflowPreview? PendingIdentifierPreview
    {
        get => _pendingIdentifierPreview;
        private set
        {
            // 実行ボタンの有効・無効はこの値だけで決まります。片方だけ通知すると
            // 「押せないはずのボタンが押せる」状態が画面に残るため、必ず同時に通知します。
            if (SetProperty(ref _pendingIdentifierPreview, value)) RaisePropertyChanged(nameof(HasPendingIdentifierPreview));
        }
    }

    // 実行ボタン（この内容で実行／プレビュー内容をRevitへ書き込む）のIsEnabledはここに束ねます。
    // AsyncCommandは「実行中かどうか」しか見ないため、使用済み・未生成のプレビューでも
    // ボタンは押せてしまいます。押した瞬間に顧客のRevitへ書き込む操作なので、
    // 「プレビューが本当に手元にあるときだけ押せる」ことを画面側で保証します。
    public bool HasPendingIdentifierPreview => _pendingIdentifierPreview is not null;

    public string IdentifierPreviewText { get => _identifierPreviewText; private set => SetProperty(ref _identifierPreviewText, value); }

    /// <summary>1つの絞り込み項目の画面状態です。先頭の「(すべて)」が未選択を表します。</summary>
    public sealed class CandidateFilterViewModel : ObservableObject
    {
        private string _selected = AllValuesLabel;

        public CandidateFilterViewModel(string column) => Column = column;

        public string Column { get; }
        public ObservableCollection<string> Values { get; } = new();
        public string Selected { get => _selected; set => SetProperty(ref _selected, value); }
    }

    /// <summary>未選択を表す表示文字列。絞り込みへ渡すときは空文字に変換します。</summary>
    public const string AllValuesLabel = "(すべて)";

    public ObservableCollection<CandidateFilterViewModel> CandidateFilters { get; } = new();
    public ObservableCollection<ProductRecord> Candidates { get; } = new();
    /// <summary>
    /// 候補一覧で選んだ製品です。層の行へ入れる操作（ApplySelectedCandidateToLayerAsync）で使います。
    /// 候補が空になったとき（絞り込み直しなど）は消しません。
    /// 利用者が選んだ値を勝手に失わせないためです。
    /// </summary>
    public ProductRecord? SelectedCandidate
    {
        get => _selectedCandidate;
        set => SetProperty(ref _selectedCandidate, value);
    }
    public string CandidateSummary { get => _candidateSummary; private set => SetProperty(ref _candidateSummary, value); }

    /// <summary>新規登録1項目分の画面状態です。列名は設定のNewProductColumnsから作ります。</summary>
    public sealed class NewProductFieldViewModel : ObservableObject
    {
        private string _value = string.Empty;
        private bool _registerAsNew;

        public NewProductFieldViewModel(string column, bool isRequired, ProductMasterKind? masterKind = null)
        {
            Column = column;
            IsRequired = isRequired;
            MasterKind = masterKind;
        }

        public string Column { get; }
        public bool IsRequired { get; }
        public string Value { get => _value; set => SetProperty(ref _value, value); }

        /// <summary>
        /// マスタ表（Manufacturers / Material_Categories）から値を引く欄かどうかです。
        /// Products.csvはIDしか持たないため、これらの欄は名称からIDを引けないと書き込めません。
        /// nullなら自由入力の欄です。
        /// </summary>
        public ProductMasterKind? MasterKind { get; }

        public bool IsMasterBacked => MasterKind is not null;

        /// <summary>選択肢です。マスタ欄でなければ常に空です。</summary>
        public ObservableCollection<string> AvailableValues { get; } = new();

        /// <summary>
        /// 「一覧に無いので新しく登録する」を選んだ状態です。
        /// 真のときだけ、登録時にマスタ表へ1行足します。
        /// 既定は偽です。自由入力の誤字が黙ってマスタへ入らないようにするためです。
        /// </summary>
        public bool RegisterAsNew
        {
            get => _registerAsNew;
            set
            {
                if (!SetProperty(ref _registerAsNew, value)) return;
                // 選択肢から選んだ値のまま「新しく登録する」に切り替えると、
                // 既にある名前を新規として書きかけます。切り替え時は必ず空に戻します。
                Value = string.Empty;
            }
        }
    }

    public ObservableCollection<NewProductFieldViewModel> NewProductFields { get; } = new();
    public ObservableCollection<DuplicateCandidate> DuplicateCandidates { get; } = new();
    public string NewProductIdPreview { get => _newProductIdPreview; private set => SetProperty(ref _newProductIdPreview, value); }

    // 「内容を確認」を通っていないと真になりません。これが確認前の登録を防ぐ唯一のゲートです。
    // 確認後に必須未入力が見つかった場合もこの値は偽のままになるため、
    // RegisterNewProductAsync側は「確認済みか」と「登録可能か」を別々に持つ必要がありません。
    // 確認後に欄をどれか1つでも編集すると、OnNewProductFieldChangedがこの値をここで偽へ戻します。
    // そのため「真である」ことは常に「現在表示中の欄の値で確認済みである」ことを保証し、
    // 確認結果（重複候補・採番プレビュー）と実際に登録される内容がずれることはありません。
    public bool CanRegisterNewProduct { get => _canRegisterNewProduct; private set => SetProperty(ref _canRegisterNewProduct, value); }

    public AsyncCommand RefreshSelectionCommand { get; }
    public AsyncCommand SearchCommand { get; }
    public AsyncCommand AuditCommand { get; }
    public AsyncCommand AddComparisonCommand { get; }
    public AsyncCommand NavigateAuditIssueCommand { get; }
    public AsyncCommand SaveSettingsCommand { get; }
    public AsyncCommand ReloadSettingsCommand { get; }
    public AsyncCommand TestLlmConnectionCommand { get; }
    public AsyncCommand ApplyIdentifierPreviewCommand { get; }
    public AsyncCommand BrowseDataFolderCommand { get; }
    public AsyncCommand BrowsePriceCsvCommand { get; }
    public AsyncCommand BrowseAssemblyCsvCommand { get; }
    public AsyncCommand RefreshCandidatesCommand { get; }
    public AsyncCommand ClearCandidateFiltersCommand { get; }
    public AsyncCommand ResetSearchTabCommand { get; }
    public AsyncCommand ResetCompareTabCommand { get; }
    public AsyncCommand ResetAuditTabCommand { get; }
    public AsyncCommand CheckNewProductCommand { get; }
    public AsyncCommand RegisterNewProductCommand { get; }
    public AsyncCommand ApplySelectedCandidateToLayerCommand { get; }
    public AsyncCommand PreviewLayerAssignmentCommand { get; }
    public AsyncCommand TestSqlConnectionCommand { get; }
    public AsyncCommand SwitchDataSourceCommand { get; }

    /// <summary>設定タブで「SQL Server」を選んでいるかどうかです。まだ切り替えてはいません。</summary>
    public bool UseSqlServer { get => _useSqlServer; set => SetProperty(ref _useSqlServer, value); }

    public string SqlServerName { get => _sqlServerName; set => SetProperty(ref _sqlServerName, value); }

    public string SqlDatabaseName { get => _sqlDatabaseName; set => SetProperty(ref _sqlDatabaseName, value); }

    /// <summary>
    /// いま読んでいるデータ元が読み取り専用かどうかです。
    /// 真のとき、新規登録とマスタ追加の操作を画面から止めます。
    /// </summary>
    public bool IsDataSourceReadOnly
    {
        get => _isDataSourceReadOnly;
        private set
        {
            if (!SetProperty(ref _isDataSourceReadOnly, value)) return;
            RaisePropertyChanged(nameof(CanRegisterNewProductHere));
            RaisePropertyChanged(nameof(DataSourceReadOnlyNotice));
            // 接続状態欄の見出しと詳細文言も、いま読んでいるデータ元（CSV/SQL）に応じて変わります。
            RaisePropertyChanged(nameof(DataConnectionLabel));
            RaisePropertyChanged(nameof(DataProblemsLabel));
            RaisePropertyChanged(nameof(DataConnectionText));
            RaisePropertyChanged(nameof(DataProblemsText));
        }
    }

    /// <summary>新規登録の操作を出せるかどうかです。</summary>
    public bool CanRegisterNewProductHere => !IsDataSourceReadOnly;

    /// <summary>読み取り専用のときに新規登録の枠へ出す案内です。</summary>
    public string DataSourceReadOnlyNotice => IsDataSourceReadOnly
        ? "SQL接続中は製品を登録できません。設定タブでデータ元を「CSVフォルダ」に切り替えてください。"
        : string.Empty;

    /// <summary>いま読んでいるデータ元の説明です。</summary>
    public string DataSourceDescription
    {
        get => _dataSourceDescription;
        private set => SetProperty(ref _dataSourceDescription, value);
    }

    // CSVパスの手入力は打ち間違いの原因になるため、「参照」ボタンから選べるようにします。
    // 選択画面の実体はWPF側にあり、ここでは契約だけを呼びます。取り消し時は既存の値を変更しません。
    private const string CsvFileFilter = "CSVファイル (*.csv)|*.csv|すべてのファイル (*.*)|*.*";

    /// <summary>
    /// データフォルダを選びます。既存のBrowseCsvAsyncと同じ作法に揃えます。
    /// 状態表示はUIスレッド経由（SetSettingsStatusAsync / _ui.InvokeAsync）で行い、
    /// 取り消し（null）のときは何も変えません。空文字を入れると設定が壊れます。
    /// </summary>
    public async Task BrowseDataFolderAsync(CancellationToken cancellationToken = default)
    {
        if (_filePicker is null)
        {
            await SetSettingsStatusAsync("フォルダ選択画面を利用できません。パスを直接入力してください。", cancellationToken)
                .ConfigureAwait(false);
            return;
        }

        var picked = await _filePicker
            .PickExistingFolderAsync("GoldStandardのデータフォルダを選択", DataFolderPath, cancellationToken)
            .ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(picked)) return;

        await _ui.InvokeAsync(() =>
        {
            DataFolderPath = picked;
            SettingsStatusMessage = "データフォルダを設定しました。「保存」を押すと反映されます。";
        }, cancellationToken).ConfigureAwait(false);
    }

    public Task BrowsePriceCsvAsync(CancellationToken cancellationToken = default) =>
        BrowseCsvAsync("単価CSVを選択", PriceCsvPath, mustExist: true,
            apply: path => PriceCsvPath = path, cancellationToken);

    // 構成CSVはBUIがこれから作るファイルなので、存在しないパスも指定できるようにします。
    public Task BrowseAssemblyCsvAsync(CancellationToken cancellationToken = default) =>
        BrowseCsvAsync("構成CSVを選択（未作成のファイル名も指定できます）", AssemblyCsvPath, mustExist: false,
            apply: path => AssemblyCsvPath = path, cancellationToken);

    private async Task BrowseCsvAsync(
        string title, string? currentPath, bool mustExist, Action<string> apply, CancellationToken cancellationToken)
    {
        if (_filePicker is null)
        {
            await SetSettingsStatusAsync("ファイル選択画面を利用できません。パスを直接入力してください。", cancellationToken)
                .ConfigureAwait(false);
            return;
        }

        var picked = mustExist
            ? await _filePicker.PickExistingFileAsync(title, CsvFileFilter, currentPath, cancellationToken).ConfigureAwait(false)
            : await _filePicker.PickNewOrExistingFileAsync(title, CsvFileFilter, currentPath, cancellationToken).ConfigureAwait(false);

        // 取り消し（null）のときは何も変えません。空文字を入れてしまうと設定が壊れるためです。
        if (string.IsNullOrWhiteSpace(picked)) return;

        await _ui.InvokeAsync(() =>
        {
            apply(picked);
            SettingsStatusMessage = "パスを設定しました。「保存」を押すと反映されます。";
        }, cancellationToken).ConfigureAwait(false);
    }

    public async Task ReloadSettingsAsync(CancellationToken cancellationToken = default)
    {
        if (_settingsEditor is null)
        {
            await SetSettingsStatusAsync("設定の読み込み準備ができていません。", cancellationToken).ConfigureAwait(false);
            return;
        }

        try
        {
            var result = await _settingsEditor.LoadAsync(cancellationToken).ConfigureAwait(false);
            if (!result.IsSuccess)
            {
                await SetSettingsStatusAsync(result.Error!.UserMessage, cancellationToken).ConfigureAwait(false);
                return;
            }

            var loaded = result.Value!;
            var safeSettings = WithoutSecrets(loaded.Settings);
            _loadedSettings = safeSettings;
            await _ui.InvokeAsync(() =>
            {
                ApplySettings(safeSettings);
                SettingsStatusMessage = loaded.Warnings.Count == 0
                    ? "設定を読み込みました。"
                    : $"設定を読み込みました。{string.Join(" / ", loaded.Warnings)}";
            }, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception exception)
        {
            ReportException(exception);
            await SetSettingsStatusAsync("設定を読み込めませんでした。再度実行してください。", CancellationToken.None).ConfigureAwait(false);
        }
    }

    public async Task SaveSettingsAsync(CancellationToken cancellationToken = default)
    {
        if (_settingsEditor is null)
        {
            await SetSettingsStatusAsync("設定の保存準備ができていません。", cancellationToken).ConfigureAwait(false);
            return;
        }

        try
        {
            var current = BuildSettingsFromEditor();
            // モデル一覧の取得は空欄でも可能ですが、LLMを有効化して保存するには選択が必要です。
            if (current.Llm.Enabled && string.IsNullOrWhiteSpace(current.Llm.Model))
            {
                await SetSettingsStatusAsync("LLMをONにする場合はモデルを選択または入力してください。", cancellationToken)
                    .ConfigureAwait(false);
                return;
            }
            var result = await _settingsEditor.SaveLocalAsync(current, cancellationToken).ConfigureAwait(false);
            await SetSettingsStatusAsync(result.IsSuccess
                ? "設定を保存しました。データ接続とLLM設定は次回起動時に反映されます。"
                : result.Error!.UserMessage, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception exception)
        {
            ReportException(exception);
            await SetSettingsStatusAsync("設定を保存できませんでした。元の設定は変更されていません。", CancellationToken.None).ConfigureAwait(false);
        }
    }

    public async Task TestLlmConnectionAsync(CancellationToken cancellationToken = default)
    {
        if (!IsLlmEnabled)
        {
            await SetSettingsStatusAsync("LLMはOFFです。ONにしてから接続を確認してください。", cancellationToken).ConfigureAwait(false);
            return;
        }
        if (_llmConnectionTester is null)
        {
            await SetSettingsStatusAsync("LLM接続確認の準備ができていません。", cancellationToken).ConfigureAwait(false);
            return;
        }

        var snapshot = BuildSettingsFromEditor().Llm;
        using var request = _llmConnectionRequests.Begin(cancellationToken);
        // Sourceが破棄された後でも状態参照できる値を保持し、遅れて戻る接続実装との競合を避けます。
        var requestToken = request.Token;
        try
        {
            // 新しい取得を始めた時点で、以前の接続先から得た候補を画面に残しません。
            await _ui.InvokeAsync(() =>
            {
                if (ApplyDiscoveryCancellationIfNeeded(request, snapshot, requestToken)) return;
                if (!IsCurrentDiscovery(request, snapshot, requestToken)) return;
                ClearModelSuggestions(clearSelectedSuggestion: true);
                SettingsStatusMessage = "LLM接続とモデル一覧を確認しています。";
            }, CancellationToken.None).ConfigureAwait(false);
            if (requestToken.IsCancellationRequested)
            {
                await ShowDiscoveryCancelledAsync(request, snapshot, requestToken).ConfigureAwait(false);
                return;
            }
            if (!IsCurrentDiscovery(request, snapshot, requestToken)) return;

            // モデル一覧はmodel選択前に取得するため、空modelを許可します。
            var result = await _llmConnectionTester.TestAsync(snapshot, requestToken).ConfigureAwait(false);
            if (requestToken.IsCancellationRequested)
            {
                // 接続実装が取消を無視して成功を返しても、その遅い結果を画面へ採用しません。
                await ShowDiscoveryCancelledAsync(request, snapshot, requestToken).ConfigureAwait(false);
                return;
            }
            if (result.IsSuccess)
            {
                await _ui.InvokeAsync(() =>
                {
                    // UI待機中にも設定変更や新しい要求が入り得るため、画面更新の直前に再確認します。
                    if (ApplyDiscoveryCancellationIfNeeded(request, snapshot, requestToken)) return;
                    if (!IsCurrentDiscovery(request, snapshot, requestToken)) return;
                    Replace(AvailableLlmModels, result.Value!);
                    SettingsStatusMessage = $"LLMに接続できました。{result.Value!.Count}件のモデルから選択できます。";
                }, CancellationToken.None).ConfigureAwait(false);
            }
            else
            {
                await _ui.InvokeAsync(() =>
                {
                    if (ApplyDiscoveryCancellationIfNeeded(request, snapshot, requestToken)) return;
                    if (!IsCurrentDiscovery(request, snapshot, requestToken)) return;
                    ClearModelSuggestions(clearSelectedSuggestion: true);
                    SettingsStatusMessage = result.Error!.UserMessage;
                }, CancellationToken.None).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (requestToken.IsCancellationRequested)
        {
            await ShowDiscoveryCancelledAsync(request, snapshot, requestToken).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            if (IsCurrentDiscovery(request, snapshot, requestToken)) ReportException(exception);
            await _ui.InvokeAsync(() =>
            {
                if (ApplyDiscoveryCancellationIfNeeded(request, snapshot, requestToken)) return;
                if (!IsCurrentDiscovery(request, snapshot, requestToken)) return;
                ClearModelSuggestions(clearSelectedSuggestion: true);
                SettingsStatusMessage = "LLM接続を確認できませんでした。設定を確認してください。";
            }, CancellationToken.None).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// SQL Serverへ接続できるかだけを確かめます。データ元は切り替えません。
    /// 切り替えは「切り替えて読み直す」（SwitchDataSourceAsync）の役目です。
    /// </summary>
    public async Task TestSqlConnectionAsync(CancellationToken cancellationToken = default)
    {
        var result = await RunSqlAsync(switchOver: false, cancellationToken).ConfigureAwait(false);
        if (result is null) return;

        await SetSettingsStatusAsync(
            result.IsSuccess ? result.Value! : result.Error!.UserMessage, cancellationToken).ConfigureAwait(false);
        // 確認だけなので、成功していてもCSVへは戻しません。切替器の状態は変えません。
        await RefreshDataSourceStateAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>選んでいるデータ元へ実際に切り替え、読み直します。</summary>
    public async Task SwitchDataSourceAsync(CancellationToken cancellationToken = default)
    {
        if (_dataSourceConnector is null)
        {
            await SetSettingsStatusAsync("データ元を切り替える準備ができていません。", cancellationToken).ConfigureAwait(false);
            return;
        }

        // 単価CSV・構成CSVは、選んでいるデータ元（CSV/SQL）に関わらず、
        // その時点の入力欄の値で読み直します。構成CSVはSQL接続中でも
        // Assembly_IDの書き込み先として使われるため、製品データの
        // 切替が成功したかどうかに関係なく実行します。
        _dataSourceConnector.UsePriceCsv(PriceCsvPath ?? string.Empty);
        _dataSourceConnector.UseAssemblyCsv(AssemblyCsvPath ?? string.Empty);

        if (!UseSqlServer)
        {
            // 編集欄（DataFolderPath）は「保存」を押すまで設定へ反映されません。
            // ここで切り替える先も編集欄の値です。
            var adoptedFolder = DataFolderPath ?? string.Empty;
            _dataSourceConnector.UseCsvFolder(adoptedFolder);
            await RefreshDataSourceStateAsync(cancellationToken).ConfigureAwait(false);
            // 接続状態・製品カードを新しいデータ元の内容に更新します。ここを呼ばないと、
            // 要素をクリックし直すまで概要タブと接続状態が前のデータ元のまま残ります。
            await RefreshSelectionAsync(LastSnapshot, cancellationToken).ConfigureAwait(false);
            var message = $"CSVフォルダに切り替えました（{adoptedFolder}）。{DataSourceDescription}";
            await SetSettingsStatusAsync(message, cancellationToken).ConfigureAwait(false);
            await RefreshCandidatesAsync(cancellationToken).ConfigureAwait(false);
            return;
        }

        var result = await RunSqlAsync(switchOver: true, cancellationToken).ConfigureAwait(false);
        if (result is null) return;

        if (!result.IsSuccess)
        {
            // 失敗したら切り替えません。中途半端な状態を作らないためです。
            await RefreshDataSourceStateAsync(cancellationToken).ConfigureAwait(false);
            await SetSettingsStatusAsync(
                "データ元を切り替えませんでした。" + Environment.NewLine + result.Error!.UserMessage,
                cancellationToken).ConfigureAwait(false);
            return;
        }

        await RefreshDataSourceStateAsync(cancellationToken).ConfigureAwait(false);
        // 接続状態・製品カードを新しいデータ元の内容に更新します（CSV分岐と同じ理由）。
        await RefreshSelectionAsync(LastSnapshot, cancellationToken).ConfigureAwait(false);
        await SetSettingsStatusAsync(
            $"SQL Serverに切り替えました。{result.Value!}", cancellationToken).ConfigureAwait(false);
        await RefreshCandidatesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 入力を検査してからSQLへ接続します。入力が不足していればnullを返します。
    /// switchOverがfalseなら接続を確かめるだけで、データ元は変えません。
    /// </summary>
    private async Task<OperationResult<string>?> RunSqlAsync(bool switchOver, CancellationToken cancellationToken)
    {
        if (_dataSourceConnector is null)
        {
            await SetSettingsStatusAsync("データ元を切り替える準備ができていません。", cancellationToken).ConfigureAwait(false);
            return null;
        }

        // 接続文字列の組み立てと同じ検査をここでも通し、
        // 空欄のまま接続を試みて分かりにくい失敗になるのを防ぎます。
        var connectionString = SqlConnectionStringFactory.Build(SqlServerName, SqlDatabaseName);
        if (!connectionString.IsSuccess)
        {
            await SetSettingsStatusAsync(connectionString.Error!.UserMessage, cancellationToken).ConfigureAwait(false);
            return null;
        }

        var server = SqlServerName.Trim();
        var database = SqlDatabaseName.Trim();
        // 編集欄（DataFolderPath）は「保存」を押すまで設定へ反映されません。CSV分岐(949行目付近)と
        // 同様に、ここで切り替える画像フォルダも編集欄の値をそのまま使います。渡さずに古いデータ元の
        // フォルダを引き継ぐと、フォルダを入力した直後にSQL Serverへ切り替えたときだけ製品画像が
        // 見つからなくなる不具合になります(2026-09-25に実際に発生)。
        return switchOver
            ? await _dataSourceConnector.UseSqlServerAsync(server, database, DataFolderPath ?? string.Empty, cancellationToken).ConfigureAwait(false)
            : await _dataSourceConnector.TestSqlServerAsync(server, database, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// IsDataSourceReadOnly / DataSourceDescriptionを、いまの接続器の状態から取り直します。
    /// UIディスパッチャを経由しない同期版です。コンストラクタから直接呼べるよう分けています
    /// （コンストラクタからTaskを起こすべきではないため）。
    /// </summary>
    private void RefreshDataSourceStateCore()
    {
        IsDataSourceReadOnly = _dataSourceConnector is not null && !_dataSourceConnector.SupportsWrite;
        DataSourceDescription = _dataSourceConnector?.Describe() ?? string.Empty;
    }

    private Task RefreshDataSourceStateAsync(CancellationToken cancellationToken) =>
        _ui.InvokeAsync(RefreshDataSourceStateCore, cancellationToken);

    public void Post(SelectionSnapshot snapshot)
    {
        LastSnapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot));
        // 選択が変わった後に、古い対象の確認済みプレビューを実行させません。
        ClearIdentifierPreview();
        _ = RefreshSelectionAsync(snapshot);
    }

    public async Task ApplyIdentifierPreviewAsync(CancellationToken cancellationToken = default)
    {
        var preview = PendingIdentifierPreview;
        if (_identifierWorkflow is null || _confirmationService is null || preview is null)
        {
            await SetStatusAsync("先にID付与のプレビューを実行してください。", cancellationToken).ConfigureAwait(false);
            return;
        }

        var result = await ConfirmAndApplyAsync(preview, cancellationToken).ConfigureAwait(false);
        await _ui.InvokeAsync(() =>
        {
            StatusMessage = result.IsSuccess ? result.Value! : result.Error!.UserMessage;
            if (result.IsSuccess) ClearIdentifierPreview();
        }, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 仕様8.1の「内訳の確認 → （競合があれば）上書きの確認 → 実行」を1か所にまとめます。
    /// 「手順2: この内容をRevitに書き込む」から呼ばれます。層への付与と新規登録は
    /// どちらも手順1でプレビューを作り、同じこの経路で書き込みます。
    /// 仕様8.4のとおり、2回目の上書き確認に同意しない場合でも中止はせず、
    /// 競合分だけを除いて実行します。空欄への付与という主目的まで巻き添えにしないためです。
    /// </summary>
    private async Task<OperationResult<string>> ConfirmAndApplyAsync(
        IdentifierWorkflowPreview preview, CancellationToken cancellationToken)
    {
        if (_identifierWorkflow is null || _confirmationService is null)
        {
            return OperationResult<string>.Failure(
                "IDENTIFIER_WORKFLOW_UNAVAILABLE", "ID付与の準備ができていません。", "workflow or confirmation service is null");
        }

        if (!await _confirmationService.ConfirmAsync(preview.Title, preview.Summary, cancellationToken).ConfigureAwait(false))
        {
            return OperationResult<string>.Failure(
                "IDENTIFIER_APPLY_CANCELLED", "書込みをキャンセルしました。", "First confirmation declined.");
        }

        var conflictConfirmed = false;
        if (preview.RequiresConflictConfirmation)
        {
            conflictConfirmed = await _confirmationService.ConfirmAsync(
                "既存IDの上書き確認",
                "異なる既存IDを上書きする対象があります。再度確認してください。\n" + preview.Summary,
                cancellationToken).ConfigureAwait(false);
        }

        var result = await _identifierWorkflow.ApplyAsync(preview.Token, conflictConfirmed, cancellationToken).ConfigureAwait(false);
        if (!result.IsSuccess || !preview.RequiresConflictConfirmation || conflictConfirmed) return result;

        // 除いたことは必ず伝えます。黙って一部だけ書くと、利用者は全件書けたと思い込みます。
        return OperationResult<string>.Success($"{result.Value!} 既存IDが入っている対象は上書きせず除きました。");
    }

    private async Task AdoptPreviewAsync(
        Task<OperationResult<IdentifierWorkflowPreview>> pending,
        CancellationToken cancellationToken)
    {
        var result = await pending.ConfigureAwait(false);
        await _ui.InvokeAsync(() =>
        {
            PendingIdentifierPreview = result.IsSuccess ? result.Value : null;
            IdentifierPreviewText = result.IsSuccess ? result.Value!.Summary : result.Error!.UserMessage;
            StatusMessage = result.IsSuccess ? "プレビューを確認し、実行ボタンを押してください。" : result.Error!.UserMessage;
        }, cancellationToken).ConfigureAwait(false);
    }

    private void ClearIdentifierPreview()
    {
        PendingIdentifierPreview = null;
        IdentifierPreviewText = "プレビューはまだありません。";
    }

    public Task RefreshSelectionAsync(SelectionSnapshot snapshot) => RefreshSelectionAsync(snapshot, CancellationToken.None);

    private async Task RefreshSelectionAsync(SelectionSnapshot snapshot, CancellationToken outerToken)
    {
        // Post経由の通知だけでなく、この関数を直接呼んだ場合でも最新の選択として扱います。
        // 付与コマンドはLastSnapshotを対象にするため、ここがずれると誤った要素へ書き込みかねません。
        LastSnapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot));
        using var request = _selectionRequests.Begin(outerToken);

        try
        {
            var details = await _selectionDetails.LoadAsync(snapshot, request.Token).ConfigureAwait(false);
            ProductRecord? product = null;
            IReadOnlyList<PriceRecord> prices = Array.Empty<PriceRecord>();
            if (!string.IsNullOrWhiteSpace(details.ProductId))
            {
                product = await _products.GetByIdAsync(details.ProductId, request.Token).ConfigureAwait(false);
                prices = await _prices.SearchAsync(new(details.ProductId, null, null), request.Token).ConfigureAwait(false);
            }

            var card = await LoadProductCardAsync(details.ProductId, request.Token).ConfigureAwait(false);
            var layerLoad = await LoadLayerAssignmentAsync(snapshot, request.Token).ConfigureAwait(false);
            var status = await _products.GetStatusAsync(request.Token).ConfigureAwait(false);
            if (!request.IsLatest) return;
            var warnings = details.Warnings.ToList();
            if (!string.IsNullOrWhiteSpace(details.ProductId) && product is null)
                warnings.Add($"仕上のProduct_ID「{details.ProductId}」はCSVに登録されていません。");

            await _ui.InvokeAsync(() =>
            {
                if (!request.IsLatest) return;
                Selection = new SelectionSummary(details.Snapshot.HasDocument, details.Snapshot.ElementIds.Count,
                    details.Snapshot.ContainsLinkedElement, details.Snapshot.IsReadOnlyContext,
                    details.ElementDisplayName, details.TypeName, details.CategoryName, details.SameTypeCount);
                SelectedProduct = product;
                // 「現在のProduct_ID」欄は6つの層別パラメータの要約を表示します。
                // details.ProductIdは仕上の代表値で、製品カード読込みと絞り込み初期化だけに使います。
                SelectedProductId = details.LayerProductIdSummary;
                ProductCard = card.Detail;
                ProductCardMessage = card.Message;
                // 層の選択を外したときに戻る先として控えます。
                _finishProductId = details.ProductId ?? string.Empty;
                ProductCardLayerLabel = string.Empty;
                // 直接フィールドを書きます。SelectedLayerのセッターを通すと、
                // いま読み込んだばかりの仕上カードをもう一度読み直すだけになります。
                _selectedLayer = null;
                RaisePropertyChanged(nameof(SelectedLayer));
                AssemblyId = details.AssemblyId;
                Replace(SelectedPrices, prices);
                Replace(Layers, details.Layers.Select(layer => new CompoundLayerViewModel(layer.Order, layer.Function,
                    layer.ThicknessMillimeters, layer.ProductId, layer.RevitMaterialName, string.IsNullOrWhiteSpace(layer.ProductId),
                    layer.MaterialElementId, layer.IsMaterialFallback)));
                Replace(LayerRows, layerLoad.Rows.Select(row => new LayerAssignmentRowViewModel(row)));
                SelectedLayerRow = null;
                LayerWarning = layerLoad.Warning;
                DataStatus = status;
                StatusMessage = warnings.Count == 0 ? "選択情報を更新しました。" : string.Join(" / ", warnings);
                // 前回選択の警告が新しい選択に残らないよう、毎回まず空にしてから積み直します。
                SelectionWarningText = details.Warnings.Count == 0 ? string.Empty : string.Join(" / ", details.Warnings);
            }, request.Token).ConfigureAwait(false);

            // 再選定という別モードは設けません。既存Product_IDを持つ要素を選ぶこと自体が
            // 絞り込みの初期化になります（利用者はモードの違いを意識しません）。
            // 6層のうちどれを使うかはdetails.ProductId（仕上の値）で決めます。仕上を選んだ理由は
            // 複合構造を持たない要素（ドアや什器など）でも唯一存在する層だからです（設計 §8のCreateSingleFinishRowと同じ考え方）。
            if (request.IsLatest)
                await InitializeCandidateFiltersFromSelectionAsync(details.ProductId, product, request.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (request.Token.IsCancellationRequested) { }
        catch (Exception exception)
        {
            ReportException(exception);
            // 既存の正常表示は消さず、例外本文を出さない再試行案内だけを表示します。
            if (request.IsLatest)
            {
                await _ui.InvokeAsync(
                    () => StatusMessage = "選択情報を読み込めませんでした。再度実行してください。",
                    CancellationToken.None).ConfigureAwait(false);
            }
        }
    }

    /// <summary>カード1件の内容と、出せない場合の理由をまとめて返します。</summary>
    private readonly record struct ProductCardLoad(ProductDetail? Detail, string Message);

    /// <summary>
    /// 製品カードを取り直します。
    /// 詳細が取れないことは珍しくありません（IDが未付与、旧IDのまま、データフォルダが未設定）。
    /// どの場合もパネル全体を止めず、理由だけを画面へ返します。
    /// ここで例外を素通しすると、要素を選ぶたびにBUIパネルが落ちます。
    /// </summary>
    private async Task<ProductCardLoad> LoadProductCardAsync(string? productId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(productId))
        {
            return new ProductCardLoad(null, "この要素には仕上のProduct_IDが付与されていません。");
        }

        if (_productDetails is null)
        {
            return new ProductCardLoad(null, "製品データの参照先が設定されていません。設定タブを確認してください。");
        }

        try
        {
            var detail = await _productDetails.GetDetailAsync(productId, cancellationToken).ConfigureAwait(false);
            return detail is null
                ? new ProductCardLoad(null, $"仕上のProduct_ID『{productId}』は製品データにありません。")
                : new ProductCardLoad(detail, string.Empty);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            ReportException(exception);
            // 例外本文はパスを含みうるため画面には出しません。
            return new ProductCardLoad(null, "製品情報を読み込めませんでした。設定タブのデータフォルダを確認してください。");
        }
    }

    /// <summary>1回分の層割当読み込み結果です。窓口が無い・失敗した場合も空の表として返します。</summary>
    private readonly record struct LayerAssignmentLoad(IReadOnlyList<LayerAssignmentRow> Rows, string Warning);

    /// <summary>
    /// 層割当の表を読み直します。
    /// _layerAssignmentが未設定の環境は珍しくないため（Task 7を積んでいない旧インストールなど）、
    /// その場合は空の表を返すだけで、例外を投げません。
    /// ここで例外を素通しすると、要素を選ぶたびにBUIパネルが落ちます（LoadProductCardAsyncと同じ理由）。
    /// </summary>
    private async Task<LayerAssignmentLoad> LoadLayerAssignmentAsync(SelectionSnapshot snapshot, CancellationToken cancellationToken)
    {
        if (_layerAssignment is null) return new LayerAssignmentLoad(Array.Empty<LayerAssignmentRow>(), string.Empty);

        try
        {
            var result = await _layerAssignment.LoadLayersAsync(snapshot, cancellationToken).ConfigureAwait(false);
            return result.IsSuccess
                ? new LayerAssignmentLoad(result.Value!.Rows, result.Value!.LayerWarning)
                : new LayerAssignmentLoad(Array.Empty<LayerAssignmentRow>(), result.Error!.UserMessage);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            ReportException(exception);
            // 例外本文はパスを含みうるため画面には出しません。
            return new LayerAssignmentLoad(Array.Empty<LayerAssignmentRow>(), "層の割当を読み込めませんでした。");
        }
    }

    /// <summary>
    /// 既存Product_IDを持つ要素を選んだときの絞り込み初期化です。
    /// 再選定という専用モードは設けず、この処理だけで再選定を成立させます。
    /// productIdは6つの層別Product_IDのうち仕上（LayerSlot.Finish）の値です（design: 複合構造を持たない要素でも
    /// 唯一存在する層のため、代表値として選びました）。他の5層からは絞り込みを初期化しません。
    /// </summary>
    private async Task InitializeCandidateFiltersFromSelectionAsync(
        string? productId, ProductRecord? product, CancellationToken cancellationToken)
    {
        if (_candidateSource is null || string.IsNullOrWhiteSpace(productId)) return;

        // 再選定の案内は、絞り込み側の警告（「項目○○はCSVにありません」）を消さないよう連結します。
        // 以前は後から単独で上書きしており、候補が0件になった本当の原因が画面から消えていました。
        if (product is null)
        {
            // CSVに無いIDでは絞り込みを初期化せず、一覧だけは未選択状態で表示します。
            var missingWarning = await RefreshCandidatesCoreAsync(new Dictionary<string, string>(), cancellationToken)
                .ConfigureAwait(false);
            await _ui.InvokeAsync(
                () => StatusMessage = Combine($"仕上の現在のProduct_ID『{productId}』は製品データにありません。", missingWarning),
                cancellationToken).ConfigureAwait(false);
            return;
        }

        var mapping = _loadedSettings?.Data.ProductColumns ?? CsvColumnMapping.GoldStandardDefault;
        var filterColumns = _loadedSettings?.Data.CandidateFilterColumns ?? Array.Empty<string>();
        var selections = filterColumns.ToDictionary(
            column => column, column => ProductFieldAccessor.Read(product, mapping, column), StringComparer.Ordinal);

        var warning = await RefreshCandidatesCoreAsync(selections, cancellationToken).ConfigureAwait(false);
        await _ui.InvokeAsync(
            () => StatusMessage = Combine($"仕上の現在のProduct_ID『{productId}』の値で絞り込みを初期化しました。", warning),
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>案内文と絞り込みの警告を、どちらも消さずに1行へまとめます。</summary>
    private static string Combine(string message, string? warning) =>
        string.IsNullOrEmpty(warning) ? message : $"{message} {warning}";

    public Task RefreshCandidatesAsync(CancellationToken cancellationToken = default) =>
        RefreshCandidatesCoreAsync(CurrentCandidateSelections(), cancellationToken);

    /// <summary>
    /// AI会話が出した絞り込み計画を検索タブへ一括で適用します（PaneChatContext専用）。
    ///
    /// 検索語→各列のSelectedの順で1つずつ書き込むと、Selectedのsetterが
    /// OnCandidateFilterChanged経由でカスケード再取得を都度起こし、
    /// まだ計画の途中（半分だけ適用した状態）の選択でカスケードが走ってしまいます。
    /// そのカスケードが計画に含まれる別の列の値を「今は選べない」と判断して
    /// 消してしまうことがあり、チャットが報告した条件と実際に検索タブへ入る条件が食い違います。
    ///
    /// _isApplyingCandidateView（ApplyCandidateViewが使うのと同じ抑止フラグ）を使って
    /// 検索語と全列のSelectedをまとめて書き込み、最後に一度だけRefreshCandidatesAsyncを呼びます。
    /// </summary>
    internal async Task ApplyCandidatePlanAsync(
        IReadOnlyDictionary<string, string> selections, string searchText, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(selections);

        await _ui.InvokeAsync(() =>
        {
            _isApplyingCandidateView = true;
            try
            {
                SearchText = searchText ?? string.Empty;
                foreach (var filter in CandidateFilters)
                {
                    filter.Selected = selections.TryGetValue(filter.Column, out var value)
                        ? value
                        : AllValuesLabel;
                }
            }
            finally
            {
                _isApplyingCandidateView = false;
            }
        }, cancellationToken).ConfigureAwait(false);

        await RefreshCandidatesAsync(cancellationToken).ConfigureAwait(false);
    }

    public Task ClearCandidateFiltersAsync(CancellationToken cancellationToken = default) =>
        RefreshCandidatesCoreAsync(new Dictionary<string, string>(), cancellationToken);

    /// <summary>
    /// 検索タブの表示を初期状態へ戻します。
    /// 絞り込み・文字列検索・検索結果・選択中の候補をまとめて消します。
    /// 消すのは画面の状態だけで、CSVにもRevitにも触れません。
    /// </summary>
    public async Task ResetSearchTabAsync(CancellationToken cancellationToken = default)
    {
        await _ui.InvokeAsync(() =>
        {
            // 絞り込みの再取得はSearchTextを条件に使うため、先に空にします。
            SearchText = string.Empty;
            ManufacturerFilter = string.Empty;
            UnitFilter = string.Empty;
            SearchResults.Clear();
            SelectedSearchProduct = null;
            _selectedCandidate = null;
            RaisePropertyChanged(nameof(SelectedCandidate));
            Candidates.Clear();
            CandidateSummary = "候補0件";
        }, cancellationToken).ConfigureAwait(false);

        await ClearCandidateFiltersAsync(cancellationToken).ConfigureAwait(false);
        await SetStatusAsync("検索タブの表示をリセットしました。", cancellationToken).ConfigureAwait(false);
    }

    /// <summary>比較タブに追加した製品の一覧を空にします。</summary>
    public Task ResetCompareTabAsync(CancellationToken cancellationToken = default) =>
        _ui.InvokeAsync(() =>
        {
            CompareItems.Clear();
            StatusMessage = "比較タブの一覧をリセットしました。";
        }, cancellationToken);

    /// <summary>点検タブの結果表示を空にします。点検し直すには再度実行します。</summary>
    public Task ResetAuditTabAsync(CancellationToken cancellationToken = default) =>
        _ui.InvokeAsync(() =>
        {
            AuditIssues.Clear();
            StatusMessage = "点検タブの結果をリセットしました。";
        }, cancellationToken);


    private Dictionary<string, string> CurrentCandidateSelections() =>
        CandidateFilters.ToDictionary(
            f => f.Column, f => f.Selected == AllValuesLabel ? string.Empty : f.Selected, StringComparer.Ordinal);

    /// <returns>ApplyCandidateViewが出した警告文。無ければnull。呼び出し側が案内文で消さないために返します。</returns>
    private async Task<string?> RefreshCandidatesCoreAsync(IReadOnlyDictionary<string, string> selections, CancellationToken outerToken)
    {
        if (_candidateSource is null)
        {
            await SetStatusAsync("候補選択の準備ができていません。", outerToken).ConfigureAwait(false);
            return null;
        }

        using var request = _candidateRequests.Begin(outerToken);
        try
        {
            var query = new CandidateQuery(selections, SearchText ?? string.Empty);
            var view = await _candidateSource.QueryAsync(query, request.Token).ConfigureAwait(false);
            if (!request.IsLatest) return null;

            string? warning = null;
            await _ui.InvokeAsync(() =>
            {
                if (!request.IsLatest) return;
                warning = ApplyCandidateView(view);
            }, request.Token).ConfigureAwait(false);
            return warning;
        }
        catch (OperationCanceledException) when (request.Token.IsCancellationRequested) { }
        catch (Exception exception)
        {
            ReportException(exception);
            if (request.IsLatest)
            {
                await _ui.InvokeAsync(
                    () => StatusMessage = "候補を取得できませんでした。設定とCSVファイルを確認してください。",
                    CancellationToken.None).ConfigureAwait(false);
            }
        }

        return null;
    }

    /// <summary>絞り込み結果を画面状態へ反映します。選択肢が空の項目は原因特定のため状態表示へ出します。</summary>
    private string? ApplyCandidateView(CandidateView view)
    {
        // ここでSelectedを書き換えます。その変更を「利用者の操作」と取り違えると
        // 絞り込みが自分自身を呼び続けるため、書き戻しの間だけ購読側を止めます。
        _isApplyingCandidateView = true;
        try
        {
            SyncCandidateFilters(view.Filters);
        }
        finally
        {
            _isApplyingCandidateView = false;
        }

        Replace(Candidates, view.Candidates);
        CandidateSummary = view.IsTruncated
            ? $"{view.TotalCount}件中200件を表示しています。絞り込んでください。"
            : $"候補{view.TotalCount}件";

        // 設定の列名と実データがずれていると候補が出ません。候補0件の原因を
        // 利用者が特定できるよう、どの絞り込み項目が空かをここで知らせます。
        var emptyColumns = view.Filters.Where(f => f.Values.Count == 0).Select(f => f.Column).ToArray();
        if (emptyColumns.Length == 0) return null;

        var warning = $"絞り込み項目『{string.Join("、", emptyColumns)}』はCSVにありません。";
        StatusMessage = warning;
        return warning;
    }

    /// <summary>
    /// 絞り込み項目の選択肢と選択値を更新します。列の並びが同じ間は同じ画面オブジェクトを使い回します。
    /// 毎回作り直すと、ComboBoxが作り直されて操作中の項目からフォーカスが外れます。
    /// </summary>
    private void SyncCandidateFilters(IReadOnlyList<CandidateFilterOptions> filters)
    {
        var sameColumns = CandidateFilters.Count == filters.Count
            && CandidateFilters.Zip(filters).All(pair => string.Equals(pair.First.Column, pair.Second.Column, StringComparison.Ordinal));
        if (!sameColumns)
        {
            // 作り直す分だけ購読を外します。外し忘れると破棄した項目がイベントを持ち続けます。
            foreach (var existing in CandidateFilters) existing.PropertyChanged -= OnCandidateFilterChanged;
            CandidateFilters.Clear();
            foreach (var filter in filters)
            {
                var created = new CandidateFilterViewModel(filter.Column);
                created.PropertyChanged += OnCandidateFilterChanged;
                CandidateFilters.Add(created);
            }
        }

        for (var index = 0; index < filters.Count; index++)
        {
            var target = CandidateFilters[index];
            target.Values.Clear();
            target.Values.Add(AllValuesLabel);
            foreach (var value in filters[index].Values) target.Values.Add(value);
            target.Selected = string.IsNullOrEmpty(filters[index].Selected) ? AllValuesLabel : filters[index].Selected;
        }
    }

    /// <summary>
    /// 仕様6.2のカスケードです。1つ選んだ時点で他の項目の選択肢と候補一覧を絞り直します。
    /// 「絞り込む」ボタンを押すまで何も起きない状態は、元のpyRevit版の操作感と違い、
    /// 選んでも画面が変わらないため利用者が選び直しに気づけませんでした。
    /// ボタンは明示的な再取得として残してあります。
    /// </summary>
    private void OnCandidateFilterChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(CandidateFilterViewModel.Selected)) return;
        if (_isApplyingCandidateView) return;

        // 連続操作では_candidateRequestsが古い要求を取り消すため、最後の選択だけが画面に残ります。
        // 例外はRefreshCandidatesCoreAsync内で処理済みですが、拾い残しを未観測にしないため保険を掛けます。
        _ = RefreshCandidatesAsync().ContinueWith(
            task => ReportException(task.Exception!),
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    /// <summary>
    /// 候補一覧で選んだ製品を、選んでいる層の行へ入れます。
    /// IDを手で打たせないための操作です。打ち間違いはそのままモデルへ書き込まれます。
    /// </summary>
    public async Task ApplySelectedCandidateToLayerAsync(CancellationToken cancellationToken = default)
    {
        if (SelectedCandidate is null)
        {
            await SetStatusAsync("先に候補一覧から製品を選んでください。", cancellationToken).ConfigureAwait(false);
            return;
        }

        if (SelectedLayerRow is null)
        {
            await SetStatusAsync("入れる層を一覧から選んでください。", cancellationToken).ConfigureAwait(false);
            return;
        }

        var productId = SelectedCandidate.ProductId;
        var slotName = SelectedLayerRow.SlotName;
        await _ui.InvokeAsync(() =>
        {
            SelectedLayerRow.SelectedProductId = productId;
            StatusMessage = $"{slotName} に {productId} を入れました。";
        }, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 層割当の内容を確認するためのプレビューを作ります。書き込みは行いません。
    /// 概要タブの実行フロー（プレビュー→確認→適用）と同じ経路（AdoptPreviewAsync）に載せます。
    /// </summary>
    public async Task PreviewLayerAssignmentAsync(CancellationToken cancellationToken = default)
    {
        if (_layerAssignment is null)
        {
            await SetStatusAsync("層へのID付与の準備ができていません。", cancellationToken).ConfigureAwait(false);
            return;
        }

        var rows = LayerRows
            .Select(row => new LayerAssignmentRow(
                row.Order, row.Slot, row.SlotName, row.MaterialName, row.ThicknessMillimeters,
                row.CurrentProductId, row.SelectedProductId, row.LayerFunction))
            .ToArray();

        await AdoptPreviewAsync(
            _layerAssignment.PreviewLayerAssignmentAsync(LastSnapshot, rows, AssignmentTarget, cancellationToken),
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 新規登録の入力を確認します。CSVへ書けない場合は入力を待たず即座に断り、
    /// 書ける場合は重複候補と採番予定IDを提示します。重複があっても登録は止めません。
    /// </summary>
    public async Task CheckNewProductAsync(CancellationToken cancellationToken = default)
    {
        // 入力させてから書けないと分かると、入力した内容が無駄になります。先に確認します。
        var status = await _products.GetStatusAsync(cancellationToken).ConfigureAwait(false);
        if (!status.IsAvailable)
        {
            await _ui.InvokeAsync(() =>
            {
                CanRegisterNewProduct = false;
                DuplicateCandidates.Clear();
                NewProductIdPreview = string.Empty;
                StatusMessage = "製品CSVに書き込めないため、新規登録を開始できません。";
            }, cancellationToken).ConfigureAwait(false);
            return;
        }

        var data = _loadedSettings?.Data;
        if (data is null)
        {
            await SetStatusAsync("新規登録設定の読み込みができていません。", cancellationToken).ConfigureAwait(false);
            return;
        }

        var draft = BuildNewProductDraft();
        // 採番は全件を見ないと最大番号を取り違えます。登録時（ProductRegistrationCoordinator）と
        // 同じ条件で採番しないと、ここで見せたIDと実際に書くIDが食い違います。
        var existing = await _products
            .SearchAsync(new ProductQuery(string.Empty, new Dictionary<string, string>(), ProductQuery.NoLimit), cancellationToken)
            .ConfigureAwait(false);
        var duplicates = ProductDuplicateFinder.Find(existing, data.ProductColumns, draft, data.DuplicateCompareColumns);
        var idPreview = ProductIdAllocator.Next(existing.Select(p => p.ProductId), data.NewProductIdPrefix, data.NewProductIdDigits);
        var missing = draft.IsMissingRequired(data.NewProductRequiredColumns);
        // マスタ欄の不整合は、書き込みの直前ではなくここで見つけます。
        // 以前は入力を全部終えてから「Manufacturers.csvに登録されていません」と断られ、
        // その先へ進む道がありませんでした。
        var masterProblem = DescribeMasterFieldProblem();
        var newMasterNames = DescribeNewMasterValues();

        await _ui.InvokeAsync(() =>
        {
            Replace(DuplicateCandidates, duplicates);
            NewProductIdPreview = idPreview.IsSuccess ? idPreview.Value! : string.Empty;
            CanRegisterNewProduct = missing.Count == 0 && masterProblem is null;
            if (missing.Count > 0)
                StatusMessage = $"必須項目（{string.Join("、", missing)}）を入力してください。";
            else if (masterProblem is not null)
                StatusMessage = masterProblem;
            else if (newMasterNames.Length > 0)
                StatusMessage = $"入力内容を確認しました。登録できます。あわせて{newMasterNames}をマスタへ追加します。";
            else if (duplicates.Count > 0)
                StatusMessage = $"重複の疑いがある製品が{duplicates.Count}件あります。内容を確認のうえ登録してください。";
            else
                StatusMessage = "入力内容を確認しました。登録できます。";
        }, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 新規製品をCSVへ追記し、選択中の要素へ採番したIDを付与します。
    /// CheckNewProductAsyncを通っていない（＝CanRegisterNewProductが偽の）場合は何もしません。
    /// これは登録が顧客の実データへ追記する操作だからです。
    /// </summary>
    public async Task RegisterNewProductAsync(CancellationToken cancellationToken = default)
    {
        if (!CanRegisterNewProduct)
        {
            await SetStatusAsync("先に内容を確認してください。", cancellationToken).ConfigureAwait(false);
            return;
        }
        // 付与先が無いまま進むと、CSVへ追記してから付与に失敗し、追記の取り消しまで走ります。
        // 顧客の実データを触る往復を無駄に起こさないよう、ここで止めます（仕様9.5と同じ考え方）。
        if (LastSnapshot.ElementIds.Count == 0)
        {
            await SetStatusAsync("Product_IDを付与する要素を選択してください。", cancellationToken).ConfigureAwait(false);
            return;
        }
        // プレビュー状態（PendingIdentifierPreview／IdentifierPreviewText）は概要タブと共用です。
        // この経路は自前でプレビューを作り直すため、そのまま進むと概要タブで作ったプレビューを
        // 黙って捨ててしまいます。利用者は2つのタブを同時に見られないので、消えたことに気付けません。
        // 「勝手に捨てて別のものへ差し替える」より「始めない」方が安全なので、ここで断ります。
        // 顧客の実データ（製品CSV）へ追記する前に止めるので、巻き戻しも起きません。
        if (PendingIdentifierPreview is not null)
        {
            await SetStatusAsync(
                "未実行のID付与プレビューが残っています。先に「この内容で実行」で片付けてから新規登録してください。",
                cancellationToken).ConfigureAwait(false);
            return;
        }
        // 新規登録した製品は仕上・下地①〜⑤のどの層に当たるか自明ではありません（新規製品は
        // 本質的に仕上でも下地でもないため）。ApplySelectedCandidateToLayerAsyncと同じ理由・同じ文言で、
        // 層の選択を必須にします。ここで断ればCSVへの追記も起きないため、
        // 「付与先が無いまま進む」→「追記してから失敗して巻き戻す」という無駄な往復を避けられます。
        // 選んだ層は層の表とAssignmentTargetにそのまま表示されているため、
        // 何が書かれようとしているかは実行前から画面で確認できます。
        if (SelectedLayerRow is null)
        {
            await SetStatusAsync("入れる層を一覧から選んでください。", cancellationToken).ConfigureAwait(false);
            return;
        }
        if (_layerAssignment is null)
        {
            await SetStatusAsync("層へのID付与の準備ができていません。", cancellationToken).ConfigureAwait(false);
            return;
        }

        var data = _loadedSettings?.Data;
        if (data is null)
        {
            await SetStatusAsync("新規登録設定の読み込みができていません。", cancellationToken).ConfigureAwait(false);
            return;
        }

        // マスタ（メーカー名・建材分類）を先に足します。製品行はマスタのIDを参照するため、
        // 先に足しておかないと必ず参照切れで断られます。
        var addedMasters = await AddNewMasterValuesAsync(cancellationToken).ConfigureAwait(false);
        if (!addedMasters.IsSuccess)
        {
            await SetStatusAsync(addedMasters.Error!.UserMessage, cancellationToken).ConfigureAwait(false);
            return;
        }

        var draft = BuildNewProductDraft();
        // 「内容を確認」で見せた採番予定IDを渡します。登録時に別のIDになっていたら
        // そのまま書かず中止させます。見せたIDと違う番号で登録されると利用者が追跡できません。
        var result = await _newProductRegistration
            .RegisterAsync(draft, data, ApplyNewProductIdAsync, cancellationToken, NewProductIdPreview)
            .ConfigureAwait(false);

        if (!result.IsSuccess)
        {
            // 製品が登録できなかったので、そのために足したマスタ行も戻します。
            // 残すと、どの製品からも参照されないメーカーがマスタに溜まります。
            await RollbackMasterValuesAsync(addedMasters.Value!).ConfigureAwait(false);
            await SetStatusAsync(result.Error!.UserMessage, cancellationToken).ConfigureAwait(false);
            return;
        }

        // 足した値を選択肢へ反映します。次の登録から一覧に出ます。
        await ReloadMasterValuesAsync(cancellationToken).ConfigureAwait(false);

        await _ui.InvokeAsync(() =>
        {
            // 登録が終わったら、次の入力と間違えないよう欄と確認状態を初期化します。
            // 確認状態を先に落としてから欄の値を空にします。OnNewProductFieldChangedは
            // 「既に未確認状態」なら何もしないため、この順序なら直後に設定する成功メッセージを
            // 「再確認してください」で上書きされません。
            DuplicateCandidates.Clear();
            NewProductIdPreview = string.Empty;
            CanRegisterNewProduct = false;
            foreach (var field in NewProductFields)
            {
                // 先にチェックを外します。RegisterAsNewのセッターがValueを空にするため、
                // 逆順にすると空にした値がもう一度空にされるだけで害はありませんが、
                // 「新規登録のまま次の入力を始める」状態を残さないよう明示的に戻します。
                field.RegisterAsNew = false;
                field.Value = string.Empty;
            }
            StatusMessage = result.Value!;
        }, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// マスタ欄（メーカー名・建材分類）の入力が書ける状態かを調べます。
    /// 問題が無ければ null を返します。
    /// </summary>
    private string? DescribeMasterFieldProblem()
    {
        foreach (var field in NewProductFields.Where(f => f.IsMasterBacked))
        {
            var value = (field.Value ?? string.Empty).Trim();
            if (value.Length == 0) continue;

            var known = field.AvailableValues.Contains(value, StringComparer.Ordinal);
            if (field.RegisterAsNew)
            {
                if (known)
                {
                    return $"「{field.Column}」の『{value}』は既に一覧にあります。"
                        + "「一覧に無いので新しく登録する」のチェックを外して、一覧から選んでください。";
                }

                if (_masterSource is null)
                {
                    return $"「{field.Column}」を新しく登録できる状態ではありません。設定タブのデータフォルダを確認してください。";
                }

                continue;
            }

            // 選択肢が1件も読めていない場合は、判定材料が無いので止めません。
            // 書き込み時にリポジトリが理由付きで断ります。
            if (field.AvailableValues.Count > 0 && !known)
            {
                return $"「{field.Column}」の『{value}』は一覧にありません。"
                    + "一覧から選ぶか、「一覧に無いので新しく登録する」にチェックを入れてください。";
            }
        }

        return null;
    }

    /// <summary>新しくマスタへ足す予定の値を、画面へ出す一文にします。無ければ空文字。</summary>
    private string DescribeNewMasterValues()
    {
        var names = NewProductFields
            .Where(field => field.IsMasterBacked && field.RegisterAsNew && (field.Value ?? string.Empty).Trim().Length > 0)
            .Select(field => $"{field.Column}『{field.Value.Trim()}』")
            .ToArray();
        return names.Length == 0 ? string.Empty : string.Join("と", names);
    }

    /// <summary>マスタへ足した1行の記録です。後続が失敗したら取り消します。</summary>
    private readonly record struct AddedMasterValue(ProductMasterKind Kind, string Id);

    /// <summary>
    /// 「一覧に無いので新しく登録する」を選んだ欄について、マスタ表へ行を足します。
    /// 途中で失敗したら、それまでに足した行を取り消してから理由を返します。
    /// </summary>
    private async Task<OperationResult<IReadOnlyList<AddedMasterValue>>> AddNewMasterValuesAsync(
        CancellationToken cancellationToken)
    {
        var targets = NewProductFields
            .Where(field => field.IsMasterBacked && field.RegisterAsNew && (field.Value ?? string.Empty).Trim().Length > 0)
            .ToArray();
        if (targets.Length == 0)
        {
            return OperationResult<IReadOnlyList<AddedMasterValue>>.Success(Array.Empty<AddedMasterValue>());
        }

        if (_masterSource is null)
        {
            return OperationResult<IReadOnlyList<AddedMasterValue>>.Failure(
                "MASTER_SOURCE_UNAVAILABLE",
                "マスタへ登録できる状態ではありません。設定タブのデータフォルダを確認してください。",
                "masterSource is null");
        }

        var added = new List<AddedMasterValue>();
        foreach (var field in targets)
        {
            try
            {
                var id = await _masterSource
                    .AddValueAsync(field.MasterKind!.Value, field.Value.Trim(), cancellationToken)
                    .ConfigureAwait(false);
                added.Add(new AddedMasterValue(field.MasterKind!.Value, id));
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // 直前までに足した行は、製品を1件も書いていない今のうちに戻します。
                await RollbackMasterValuesAsync(added).ConfigureAwait(false);
                return OperationResult<IReadOnlyList<AddedMasterValue>>.Failure(
                    "MASTER_VALUE_ADD_FAILED",
                    $"「{field.Column}」をマスタへ登録できませんでした。{exception.Message}",
                    exception.GetType().FullName ?? "Unknown");
            }
        }

        return OperationResult<IReadOnlyList<AddedMasterValue>>.Success(added);
    }

    /// <summary>足したマスタ行を取り消します。取り消せなかったものは画面へ出します。</summary>
    private async Task RollbackMasterValuesAsync(IReadOnlyList<AddedMasterValue> added)
    {
        if (_masterSource is null || added.Count == 0) return;

        foreach (var value in added)
        {
            try
            {
                // 取り消しは中断させません。取り消し済みトークンでも必ず走らせます。
                await _masterSource.RemoveValueAsync(value.Kind, value.Id, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                ReportException(exception);
                await _ui.InvokeAsync(
                    () => StatusMessage = $"マスタへ追加した『{value.Id}』を取り消せませんでした。CSVを確認してください。",
                    CancellationToken.None).ConfigureAwait(false);
            }
        }
    }

    private NewProductDraft BuildNewProductDraft() =>
        new(NewProductFields.ToDictionary(f => f.Column, f => f.Value, StringComparer.Ordinal));

    /// <summary>
    /// 採番したIDを、選択中の要素の層へ入れる「手順1」の後半です。
    /// 層ごとの割当と同じ ILayerAssignmentService.PreviewLayerAssignmentAsync を使ってプレビューを作り、
    /// Revitへの書込みは共通の「手順2」に任せます（この関数は書き込みません）。
    /// かつては単一のProduct_ID（廃止済み・ProductIdWriteHandler経由）を要素全体へ書いており、
    /// 6つの層別パラメータのどれにも値が入らず、表示からは何も付いていないように見えていました
    /// （層表示は新パラメータしか読まないため）。この修正で、RegisterNewProductAsyncが
    /// 事前に要求したSelectedLayerRow（層の表で選んだ1行）へ新IDを差し込み、
    /// それ以外の行は現在画面にある値のまま（層の表の入力を勝手には消さない）にしてプレビューを作ります。
    /// 対象範囲は画面のAssignmentTarget（既定は同一タイプすべて）をそのまま使います。
    /// 通常の層割当（PreviewLayerAssignmentAsync）と同じ設定を共有するので、
    /// 利用者が検索タブで選んだ範囲がそのまま新規登録にも適用されます。
    /// </summary>
    private async Task<OperationResult<string>> ApplyNewProductIdAsync(string productId, CancellationToken cancellationToken)
    {
        if (_layerAssignment is null)
        {
            return OperationResult<string>.Failure(
                "IDENTIFIER_WORKFLOW_UNAVAILABLE", "層へのID付与の準備ができていません。", "layerAssignment is null");
        }

        var targetRow = SelectedLayerRow;
        if (targetRow is null)
        {
            // RegisterNewProductAsyncが呼出し前に確認済みのはずですが、CSV追記中の非同期区間で
            // 選択が変わり得るため、ここでも防御的に確認します。
            return OperationResult<string>.Failure(
                "LAYER_ASSIGNMENT_ROW_NOT_SELECTED", "入れる層を一覧から選んでください。", "SelectedLayerRow is null");
        }

        var rows = LayerRows
            .Select(row => new LayerAssignmentRow(
                row.Order, row.Slot, row.SlotName, row.MaterialName, row.ThicknessMillimeters,
                row.CurrentProductId, ReferenceEquals(row, targetRow) ? productId : row.SelectedProductId,
                row.LayerFunction))
            .ToArray();

        var preview = await _layerAssignment
            .PreviewLayerAssignmentAsync(LastSnapshot, rows, AssignmentTarget, cancellationToken)
            .ConfigureAwait(false);
        if (!preview.IsSuccess)
            return OperationResult<string>.Failure(preview.Error!.Code, preview.Error.UserMessage, preview.Error.TechnicalDetail);

        // ここで内訳を画面へ残し、Revitへの書込みは「手順2」に任せます。
        //
        // かつてはこの中で確認ダイアログを出して書込みまで済ませ、最後にプレビューを捨てていました。
        // その作りだと「手順2」を押す場面が無いのに、手順書は押すよう案内していたため、
        // 利用者はダイアログを閉じて手順2を探し、閉じたせいでCSVの追記まで巻き戻る、
        // という詰み方をしました。層への付与と操作の形を揃えます。
        //
        // 追記したCSV行はここで巻き戻しません。手順2を押し忘れても製品はマスタに残り、
        // 書込みだけを後から実行できます。プレビューは必ず実在するCSV行を指します。
        await _ui.InvokeAsync(() =>
        {
            PendingIdentifierPreview = preview.Value;
            IdentifierPreviewText = preview.Value!.Summary;
        }, cancellationToken).ConfigureAwait(false);

        return OperationResult<string>.Success(
            $"製品「{productId}」を登録しました。内訳を確認して「手順2: この内容をRevitに書き込む」を押してください。");
    }

    public Task SearchAsync(string text, IReadOnlyDictionary<string, string> filters) =>
        SearchAsync(text, filters, CancellationToken.None);

    private async Task SearchAsync(string text, IReadOnlyDictionary<string, string> filters, CancellationToken outerToken)
    {
        using var request = _searchRequests.Begin(outerToken);

        try
        {
            // 常にCSV等の信頼できるリポジトリを先に検索し、LLMは結果の要約だけに限ります。
            var candidates = await _products.SearchAsync(
                new ProductQuery(text ?? string.Empty, new Dictionary<string, string>()), request.Token).ConfigureAwait(false);
            var results = ApplyLogicalFilters(candidates, filters);
            // 送信件数・属性長・不信データ指示は、共通の安全化済み検索補助だけで管理します。
            // 要約が使えなかったことは黙って隠さず、状態表示とログの両方へ必ず出します。
            var summary = string.Empty;
            string? llmWarning = null;
            if (_activeLlmSettings.Enabled && results.Count > 0)
            {
                var summaryResult = await _llmSearchAssistant
                    .SummarizeWithDiagnosticsAsync(results, request.Token).ConfigureAwait(false);
                summary = summaryResult.Summary;
                llmWarning = summaryResult.FailureUserMessage;
                if (summaryResult.Failure is not null) ReportException(summaryResult.Failure);
            }

            if (!request.IsLatest) return;
            await _ui.InvokeAsync(() =>
            {
                if (!request.IsLatest) return;
                Replace(SearchResults, results);
                LlmSummary = summary;
                StatusMessage = llmWarning ?? $"{results.Count}件の製品を検索しました。";
            }, request.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (request.Token.IsCancellationRequested) { }
        catch (Exception exception)
        {
            ReportException(exception);
            if (request.IsLatest)
            {
                await _ui.InvokeAsync(
                    () => StatusMessage = "製品を検索できませんでした。設定とCSVファイルを確認してください。",
                    CancellationToken.None).ConfigureAwait(false);
            }
        }
    }

    public async Task AddComparisonAsync(ProductRecord product, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(product);
        if (CompareItems.Any(x => string.Equals(x.Product.ProductId, product.ProductId, StringComparison.OrdinalIgnoreCase)))
        {
            await _ui.InvokeAsync(() => StatusMessage = "同じ製品はすでに比較中です。", cancellationToken);
            return;
        }

        if (CompareItems.Count >= 4)
        {
            await _ui.InvokeAsync(() => StatusMessage = "比較は4製品までです。", cancellationToken);
            return;
        }

        var prices = await _prices.SearchAsync(new(product.ProductId, null, null), cancellationToken).ConfigureAwait(false);
        var price = prices.OrderByDescending(x => x.ValidFrom).FirstOrDefault();
        var missing = MissingFields(product, price);
        await _ui.InvokeAsync(() => CompareItems.Add(new(product, price, missing)), cancellationToken).ConfigureAwait(false);
    }

    public async Task AuditAsync(CancellationToken cancellationToken = default)
    {
        var issues = await _audit.AuditAsync(cancellationToken).ConfigureAwait(false);
        await _ui.InvokeAsync(() =>
        {
            Replace(AuditIssues, issues);
            StatusMessage = $"{issues.Count}件の点検結果を表示しました。自動修正は行いません。";
        }, cancellationToken).ConfigureAwait(false);
    }

    public async Task NavigateSelectedAuditIssueAsync(CancellationToken cancellationToken = default)
    {
        if (SelectedAuditIssue?.ElementId is not long elementId)
        {
            await SetStatusAsync("表示する点検結果を選択してください。", cancellationToken).ConfigureAwait(false);
            return;
        }
        if (_navigation is null)
        {
            await SetStatusAsync("Revit要素表示の準備ができていません。", cancellationToken).ConfigureAwait(false);
            return;
        }

        await _navigation.NavigateAsync(SelectedAuditIssue.DocumentKey, elementId, cancellationToken).ConfigureAwait(false);
        await SetStatusAsync($"要素ID {elementId} をRevitで表示しました。", cancellationToken).ConfigureAwait(false);
    }

    private void ApplySettings(BuiSettings settings)
    {
        DataFolderPath = settings.Data.DataFolderPath;
        PriceCsvPath = settings.Data.PriceCsvPath;
        AssemblyCsvPath = settings.Data.AssemblyCsvPath;
        IsLlmEnabled = settings.Llm.Enabled;
        LlmProvider = settings.Llm.Provider;
        LlmBaseUrl = settings.Llm.BaseUrl;
        LlmModel = settings.Llm.Model;
        LlmTimeoutSeconds = settings.Llm.TimeoutSeconds;
        SqlServerName = settings.Data.SqlServerName ?? string.Empty;
        SqlDatabaseName = settings.Data.SqlDatabaseName ?? string.Empty;
        UseSqlServer = string.Equals(settings.Data.Provider, "SqlServer", StringComparison.OrdinalIgnoreCase);
        InitializeNewProductFields(settings.Data);
    }

    /// <summary>設定のNewProductColumnsから新規登録欄を作り直します。既存の入力値は保持しません。</summary>
    private void InitializeNewProductFields(DataSettings data)
    {
        // 作り直す欄の分だけ購読しているため、古い欄への購読を先に外します。
        // 外し忘れると、破棄済みの欄がイベントを持ち続けてリークします。
        foreach (var field in NewProductFields) field.PropertyChanged -= OnNewProductFieldChanged;

        NewProductFields.Clear();
        // 更新前に保存されたPC個別設定にはこれらの項目がありません。SettingsMergerが出荷設定で
        // 補いますが、ここでも空として扱います。設定の欠落で画面全体（＝BUIの起動）を
        // 落とすのは割に合いません。列が無ければ新規登録欄が空になるだけで済ませます。
        var columns = data.NewProductColumns ?? Array.Empty<string>();
        var required = data.NewProductRequiredColumns ?? Array.Empty<string>();
        foreach (var column in columns)
        {
            var isRequired = required.Contains(column, StringComparer.Ordinal);
            var field = new NewProductFieldViewModel(column, isRequired, ResolveMasterKind(column, data));
            field.PropertyChanged += OnNewProductFieldChanged;
            NewProductFields.Add(field);
        }

        // 選択肢の読み込みは待ちません。読めなくても欄は使えます（自由入力に見えるだけ）。
        _ = ReloadMasterValuesAsync();
        // 列構成が変わり得るため、古い確認結果を新しい欄構成に残しません。
        DuplicateCandidates.Clear();
        NewProductIdPreview = string.Empty;
        CanRegisterNewProduct = false;
    }

    /// <summary>
    /// この列がマスタ表を参照する欄かどうかを、設定の列対応から決めます。
    /// 列名を定数で持たないのは、顧客ごとにCSVの列名が違うためです。
    /// </summary>
    private static ProductMasterKind? ResolveMasterKind(string column, DataSettings data)
    {
        if (string.Equals(column, data.ProductColumns?.ManufacturerColumn, StringComparison.Ordinal))
            return ProductMasterKind.Manufacturer;
        if (string.Equals(column, GoldStandardFlattener.CategoryColumn, StringComparison.Ordinal))
            return ProductMasterKind.MaterialCategory;
        return null;
    }

    /// <summary>マスタ欄の選択肢を読み直します。読めない場合は空のままにし、例外は投げません。</summary>
    public async Task ReloadMasterValuesAsync(CancellationToken cancellationToken = default)
    {
        var source = _masterSource;
        if (source is null) return;

        foreach (var field in NewProductFields.Where(f => f.MasterKind is not null).ToArray())
        {
            try
            {
                var values = await source.GetValuesAsync(field.MasterKind!.Value, cancellationToken).ConfigureAwait(false);
                await _ui.InvokeAsync(() =>
                {
                    field.AvailableValues.Clear();
                    foreach (var value in values.Select(v => v.Name).Distinct(StringComparer.Ordinal))
                    {
                        field.AvailableValues.Add(value);
                    }
                }, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception exception)
            {
                // 選択肢が出せなくても新規登録以外は使えます。画面全体は落としません。
                ReportException(exception);
            }
        }
    }

    /// <summary>
    /// 「内容を確認」の後で欄が編集されたら、確認結果を無効にします。
    /// 確認時の値と登録時の値がずれると、設計者が見ていない内容がCSVへ追記されるためです。
    /// このハンドラーはUIのバインディングから直接呼ばれる想定（既にUIスレッド）のため、
    /// _ui.InvokeAsyncへは回さず直接プロパティを更新します（回すと自己デッドロックの恐れがあります）。
    /// </summary>
    private void OnNewProductFieldChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        // 「一覧に無いので新しく登録する」の切り替えも入力の変更として扱います。
        // 切り替えるとマスタへ行を足すかどうかが変わるため、確認をやり直させます。
        if (e.PropertyName != nameof(NewProductFieldViewModel.Value)
            && e.PropertyName != nameof(NewProductFieldViewModel.RegisterAsNew)) return;
        if (!CanRegisterNewProduct && DuplicateCandidates.Count == 0 && NewProductIdPreview.Length == 0) return;

        CanRegisterNewProduct = false;
        DuplicateCandidates.Clear();
        NewProductIdPreview = string.Empty;
        StatusMessage = "入力内容が変更されました。再度「内容を確認」を押してください。";
    }

    private BuiSettings BuildSettingsFromEditor()
    {
        // 列対応と旧IDパス、候補選択・新規登録用の列設定は画面で変更しないため、読込済み設定を維持します。
        var priorData = _loadedSettings?.Data;
        return new BuiSettings(
            new DataSettings(UseSqlServer ? "SqlServer" : "GoldStandard",
                DataFolderPath ?? string.Empty, PriceCsvPath ?? string.Empty,
                AssemblyCsvPath ?? string.Empty,
                priorData?.IdAliasCsvPath, priorData?.ProductColumns ?? CsvColumnMapping.GoldStandardDefault,
                priorData?.PriceColumns ?? PriceColumnMapping.PocDefault,
                priorData?.CandidateFilterColumns ?? Array.Empty<string>(),
                priorData?.NewProductColumns ?? Array.Empty<string>(),
                priorData?.NewProductRequiredColumns ?? Array.Empty<string>(),
                priorData?.DuplicateCompareColumns ?? Array.Empty<string>(),
                priorData?.NewProductIdPrefix ?? string.Empty,
                priorData?.NewProductIdDigits ?? 0,
                SqlServerName ?? string.Empty,
                SqlDatabaseName ?? string.Empty),
            new LlmSettings(IsLlmEnabled, LlmProvider ?? "Ollama", LlmBaseUrl ?? string.Empty,
                LlmModel ?? string.Empty, Math.Clamp(LlmTimeoutSeconds, 1, 300), null));
    }

    private bool DiscoveryEditorMatches(LlmSettings snapshot) =>
        snapshot.Enabled == IsLlmEnabled &&
        string.Equals(snapshot.Provider, LlmProvider, StringComparison.Ordinal) &&
        string.Equals(snapshot.BaseUrl, LlmBaseUrl, StringComparison.Ordinal) &&
        snapshot.TimeoutSeconds == Math.Clamp(LlmTimeoutSeconds, 1, 300);

    private bool IsCurrentDiscovery(
        LatestRequestCoordinator.RequestLease request,
        LlmSettings snapshot,
        CancellationToken requestToken) =>
        !requestToken.IsCancellationRequested && request.IsLatest && DiscoveryEditorMatches(snapshot);

    private Task ShowDiscoveryCancelledAsync(
        LatestRequestCoordinator.RequestLease request,
        LlmSettings snapshot,
        CancellationToken requestToken) =>
        _ui.InvokeAsync(() =>
        {
            ApplyDiscoveryCancellationIfNeeded(request, snapshot, requestToken);
        }, CancellationToken.None);

    private bool ApplyDiscoveryCancellationIfNeeded(
        LatestRequestCoordinator.RequestLease request,
        LlmSettings snapshot,
        CancellationToken requestToken)
    {
        if (!requestToken.IsCancellationRequested) return false;
        // 新しい要求や編集が既に始まっていれば、その画面状態を古い取消で上書きしません。
        if (request.IsLatest && DiscoveryEditorMatches(snapshot))
        {
            ClearModelSuggestions(clearSelectedSuggestion: true);
            StatusMessage = "LLM接続確認をキャンセルしました。";
        }
        return true;
    }

    private void InvalidateModelDiscovery(bool clearSelectedSuggestion)
    {
        _llmConnectionRequests.CancelCurrent();
        var hadSuggestions = AvailableLlmModels.Count > 0;
        ClearModelSuggestions(clearSelectedSuggestion);
        if (hadSuggestions)
            StatusMessage = "LLM接続設定が変更されました。再度接続を確認してください。";
    }

    private void ClearModelSuggestions(bool clearSelectedSuggestion)
    {
        // 直接入力したモデル名は維持し、現在の一覧から選んだ候補だけを接続先変更時に外します。
        var selectedWasSuggestion = clearSelectedSuggestion && AvailableLlmModels.Any(model =>
            string.Equals(model.Id, LlmModel, StringComparison.Ordinal));
        AvailableLlmModels.Clear();
        if (selectedWasSuggestion) SetProperty(ref _llmModel, string.Empty, nameof(LlmModel));
    }

    private static BuiSettings WithoutSecrets(BuiSettings settings) =>
        settings with { Llm = settings.Llm with { ApiKey = null } };

    private Task SetStatusAsync(string message, CancellationToken cancellationToken) =>
        _ui.InvokeAsync(() => StatusMessage = message, cancellationToken);

    /// <summary>設定タブの操作結果だけを更新します。他タブの表示は変えません。</summary>
    private Task SetSettingsStatusAsync(string message, CancellationToken cancellationToken) =>
        _ui.InvokeAsync(() => SettingsStatusMessage = message, cancellationToken);

    private void ReportException(Exception exception)
    {
        // 画面内で例外を安全な文言へ変換する場合、async commandまで再throwせずここで1度だけログ境界へ渡します。
        try { _commandErrorHandler?.Handle(exception); } catch { }
    }

    private static IReadOnlyList<string> MissingFields(ProductRecord product, PriceRecord? price)
    {
        var result = new List<string>();
        if (string.IsNullOrWhiteSpace(product.Manufacturer)) result.Add("メーカー");
        if (string.IsNullOrWhiteSpace(product.ModelNumber)) result.Add("型番");
        if (string.IsNullOrWhiteSpace(product.Unit)) result.Add("単位");
        if (price is null) result.Add("単価");
        else
        {
            if (string.IsNullOrWhiteSpace(price.InformationSource)) result.Add("情報源");
            if (price.ValidFrom is null) result.Add("価格基準日");
        }
        return result.AsReadOnly();
    }

    private IReadOnlyDictionary<string, string> BuildSearchFilters()
    {
        var filters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(ManufacturerFilter)) filters["Manufacturer"] = ManufacturerFilter.Trim();
        if (!string.IsNullOrWhiteSpace(UnitFilter)) filters["Unit"] = UnitFilter.Trim();
        foreach (var pair in SearchFilters) filters[pair.Key] = pair.Value;
        return filters;
    }

    private static IReadOnlyList<ProductRecord> ApplyLogicalFilters(
        IEnumerable<ProductRecord> candidates,
        IReadOnlyDictionary<string, string> filters) => candidates.Where(product => filters.All(filter =>
    {
        var value = filter.Key.ToUpperInvariant() switch
        {
            "PRODUCTID" => product.ProductId,
            "PRODUCTNAME" => product.ProductName,
            "MANUFACTURER" => product.Manufacturer,
            "MODELNUMBER" => product.ModelNumber,
            "UNIT" => product.Unit,
            _ => product.AdditionalAttributes.GetValueOrDefault(filter.Key) ?? string.Empty
        };
        return string.Equals(value.Trim(), filter.Value?.Trim() ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    })).ToArray();

    private static void Replace<T>(ObservableCollection<T> target, IEnumerable<T> values)
    {
        target.Clear();
        foreach (var value in values) target.Add(value);
    }

    // 以下6つ（UiDispatcher〜CommandErrorHandler）は、AIチャットの組み立てが同じ部品を
    // 使うための公開です。別に作ると結果が食い違います。設定タブの編集用プロパティ
    // （LlmProvider/LlmModel、文字列・編集可）とは別物で、こちらは起動時に固定した
    // 実行用の接続先・モデル・有効フラグ（_llm/_activeLlmSettings）を返します。
    // ※XML docコメント（<summary>）にすると1つ目のメンバー（UiDispatcher）にしか
    // 付かないため、複数メンバーへの説明はここでは通常コメントで書きます。
    public IUiDispatcher UiDispatcher => _ui;
    public ICandidateSource? CandidateSource => _candidateSource;

    /// <summary>
    /// 起動時に解決済みの表記ゆれ.csvのパスです（SynonymCsvPathResolver.Resolveの結果）。
    /// チャット側（LlmChatSessionのsynonymsProvider）が毎ターン読み直すときに使います。
    /// 未設定・データフォルダも無い場合はnullで、そのときはCsvSynonymLoader.Load(null)が
    /// Emptyを返す既存の挙動に委ねます。
    /// </summary>
    public string? SynonymCsvResolvedPath => _synonymCsvResolvedPath;
    public ILlmProvider ActiveLlmProvider => _llm;
    public string ActiveLlmModel => _activeLlmSettings.Model;
    public TimeSpan LlmTimeout => TimeSpan.FromSeconds(Math.Clamp(_activeLlmSettings.TimeoutSeconds, 1, 300));

    /// <summary>
    /// 起動時に固定したLLM有効フラグです。設定タブの生きたIsLlmEnabledとは別物です。
    /// AIチャットのゲート（入力欄を使えるか）は、ActiveLlmProvider/ActiveLlmModel/LlmTimeoutと
    /// 同じ「起動時固定」のこの値に合わせてください。IsLlmEnabled（生きた値）に合わせると、
    /// 設定タブでチェックを変えた直後は入力欄が使えるように見えても、translator/assistantは
    /// まだ起動時の値のままで動くため、画面の見た目と実際の動作がずれます。
    /// </summary>
    public bool ActiveIsLlmEnabled => _activeLlmSettings.Enabled;

    public IAsyncCommandErrorHandler? CommandErrorHandler => _commandErrorHandler;

    /// <summary>
    /// AIチャット（IChatDataSourceStatus）専用の窓口です。製品リポジトリそのものを渡さず、
    /// 「いま読めているか」だけを見せます。チャットに登録・更新の操作まで見せないためです。
    /// </summary>
    Task<DataSourceStatus> IChatDataSourceStatus.GetStatusAsync(CancellationToken cancellationToken) =>
        _products.GetStatusAsync(cancellationToken);

    public void Dispose()
    {
        _selectionRequests.Dispose();
        _searchRequests.Dispose();
        _layerCardRequests.Dispose();
        _llmConnectionRequests.Dispose();
        _candidateRequests.Dispose();
        foreach (var field in NewProductFields) field.PropertyChanged -= OnNewProductFieldChanged;
        foreach (var filter in CandidateFilters) filter.PropertyChanged -= OnCandidateFilterChanged;
    }
}
