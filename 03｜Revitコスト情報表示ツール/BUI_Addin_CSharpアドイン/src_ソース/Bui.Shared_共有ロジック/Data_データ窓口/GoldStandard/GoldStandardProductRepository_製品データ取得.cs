// このファイルの目的:
//   GoldStandardのデータフォルダを、既存のIProductRepositoryとして使えるようにします。
//   読み取りはフォルダ全体、書き込みはProducts.csvだけに閉じます。
//
// なぜ書き込みをProducts.csvに限るのか:
//   新規登録は「まだ属性が調べられていない製品」を仮登録する操作です。
//   属性が無いのにProduct_Specsへ空行を足すと、後から本物の属性を入れる際に
//   重複行の掃除が必要になります。PoCではProductsへの1行だけにします。
//
// なぜマスタに無い名前を拒むのか:
//   Products.csvが持つのはmanufacturer_idとmaterial_category_idです。
//   名称からIDを決められない以上、書けば必ず参照切れになります。
//   書いた後に気づくより、書く前に断る方が安全です。
//   ただし「本当に新しいメーカー」という場面は実在するため、
//   IProductMasterSourceでマスタ表へ明示的に1行足せるようにしています。
//   足すのは利用者が画面で「一覧に無いので新しく登録する」を選んだときだけで、
//   自由入力の誤字が黙ってマスタへ入ることはありません。
//
// 修正時の影響:
//   出どころはDataSourceSwitch経由で受け取ります。フォルダパスを直接持たないため、
//   キャッシュはDataSourceSwitch.Currentの参照と版番号(Version)の両方で判定します。
//   ここを外すと、出どころの切り替え後もBUIが古い内容を表示し続けます。

using System.IO;
using Bui.Data.Csv;
using Bui.Products;

namespace Bui.Data.GoldStandard;

/// <summary>GoldStandardのデータフォルダから製品情報を読み書きします。</summary>
public sealed class GoldStandardProductRepository : IProductRepository, IProductMasterSource
{
    private static readonly CsvColumnMapping Mapping = CsvColumnMapping.GoldStandardDefault;

    private readonly DataSourceSwitch _source;
    private readonly CsvFileWriter _writer;
    private readonly SemaphoreSlim _cacheLock = new(1, 1);
    private Snapshot? _cache;

    public GoldStandardProductRepository(DataSourceSwitch source, CsvFileWriter? writer = null)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _writer = writer ?? new CsvFileWriter();
    }

    private string FolderPath => _source.DataFolderPath;

    private string ProductsPath => Path.Combine(FolderPath, GoldStandardTables.Products + ".csv");

    public async Task<DataSourceStatus> GetStatusAsync(CancellationToken cancellationToken) =>
        (await LoadAsync(cancellationToken).ConfigureAwait(false)).Status;

    public async Task<ProductRecord?> GetByIdAsync(string productId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(productId);
        var snapshot = await LoadAsync(cancellationToken).ConfigureAwait(false);
        return snapshot.Records.FirstOrDefault(record => IsSameProductId(record.ProductId, productId));
    }

    public async Task<IReadOnlyList<ProductRecord>> SearchAsync(ProductQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(query.Filters);
        var snapshot = await LoadAsync(cancellationToken).ConfigureAwait(false);
        if (!snapshot.Status.IsAvailable) return Array.Empty<ProductRecord>();

        var text = (query.Text ?? string.Empty).Trim();
        // 画面一覧の暴走を防ぐため既定は200件で頭打ちにしますが、
        // ProductQuery.NoLimitだけは「全件が要る」指定として上限を外します。
        var limit = query.Limit == ProductQuery.NoLimit ? int.MaxValue : Math.Clamp(query.Limit, 1, 200);
        return snapshot.Records
            .Where(record => MatchesText(record, text) && MatchesFilters(record, query.Filters))
            .Take(limit)
            .ToArray();
    }

    public async Task UpsertAsync(ProductRecord record, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(record);
        if (string.IsNullOrWhiteSpace(record.ProductId))
        {
            throw new InvalidDataException("製品IDがありません。");
        }

        EnsureWritableSource();
        var snapshot = await LoadAsync(cancellationToken).ConfigureAwait(false);
        EnsureWritable(snapshot.Status);

        var manufacturerId = ResolveId(
            snapshot.Set, GoldStandardTables.Manufacturers,
            GoldStandardTables.Columns.ManufacturerName, GoldStandardTables.Columns.ManufacturerId,
            record.Manufacturer, "メーカー名");
        var categoryName = record.AdditionalAttributes.TryGetValue(GoldStandardFlattener.CategoryColumn, out var category)
            ? category
            : string.Empty;
        var categoryId = ResolveId(
            snapshot.Set, GoldStandardTables.MaterialCategories,
            GoldStandardTables.Columns.CategoryNameJa, GoldStandardTables.Columns.MaterialCategoryId,
            categoryName, GoldStandardFlattener.CategoryColumn);

        var products = snapshot.Set.Table(GoldStandardTables.Products)
            ?? throw new InvalidDataException($"{GoldStandardTables.Products}.csv を読み取れません。");
        var headers = products.Headers.ToList();
        var rows = products.Rows.Select(row => new Dictionary<string, string>(row, StringComparer.Ordinal)).ToList();

        var target = rows.FirstOrDefault(row =>
            IsSameProductId(row.GetValueOrDefault(GoldStandardTables.Columns.ProductId), record.ProductId));
        if (target is null)
        {
            target = headers.ToDictionary(header => header, _ => string.Empty, StringComparer.Ordinal);
            rows.Add(target);
        }

        target[GoldStandardTables.Columns.ProductId] = record.ProductId.Trim();
        SetWhenHeaderExists(target, headers, GoldStandardTables.Columns.ProductName, record.ProductName);
        SetWhenHeaderExists(target, headers, GoldStandardTables.Columns.ManufacturerId, manufacturerId);
        SetWhenHeaderExists(target, headers, GoldStandardTables.Columns.MaterialCategoryId, categoryId);

        await WriteProductsAsync(headers, rows, products.HasUtf8Bom, snapshot.ProductsWriteTimeUtc, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>指定したProduct_IDの行を削除します。新規登録の取り消しに使います。</summary>
    public async Task RemoveAsync(string productId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(productId);
        EnsureWritableSource();
        var snapshot = await LoadAsync(cancellationToken).ConfigureAwait(false);
        EnsureWritable(snapshot.Status);

        var products = snapshot.Set.Table(GoldStandardTables.Products)
            ?? throw new InvalidDataException($"{GoldStandardTables.Products}.csv を読み取れません。");
        var headers = products.Headers.ToList();
        // 生の行から作り直すため、対象以外は内容が保たれます。
        var rows = products.Rows
            .Where(row => !IsSameProductId(row.GetValueOrDefault(GoldStandardTables.Columns.ProductId), productId))
            .Select(row => new Dictionary<string, string>(row, StringComparer.Ordinal))
            .ToList();

        await WriteProductsAsync(headers, rows, products.HasUtf8Bom, snapshot.ProductsWriteTimeUtc, cancellationToken)
            .ConfigureAwait(false);
    }

    // ------------------------------------------------------------------
    // IProductMasterSource: メーカー名・建材分類の選択肢と、明示的な追加
    // ------------------------------------------------------------------

    public async Task<IReadOnlyList<ProductMasterValue>> GetValuesAsync(
        ProductMasterKind kind, CancellationToken cancellationToken)
    {
        var snapshot = await LoadAsync(cancellationToken).ConfigureAwait(false);
        var layout = MasterLayout.Of(kind);
        return snapshot.Set.Rows(layout.Table)
            .Select(row => new ProductMasterValue(ReadCell(row, layout.IdColumn), ReadCell(row, layout.NameColumn)))
            .Where(value => value.Name.Length > 0)
            .OrderBy(value => value.Name, StringComparer.CurrentCulture)
            .ToArray();
    }

    public async Task<string> AddValueAsync(ProductMasterKind kind, string name, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var trimmed = name.Trim();
        var layout = MasterLayout.Of(kind);
        EnsureWritableSource();
        var snapshot = await LoadAsync(cancellationToken).ConfigureAwait(false);
        EnsureWritable(snapshot.Status);

        var table = snapshot.Set.Table(layout.Table)
            ?? throw new InvalidDataException(
                $"{layout.Table}.csv を読み取れません。{layout.Label}を新しく登録するには、この表が必要です。");

        // 同名が既にあれば足しません。二重登録を作らないためです。
        var existing = table.Rows.FirstOrDefault(row =>
            string.Equals(ReadCell(row, layout.NameColumn), trimmed, StringComparison.Ordinal));
        if (existing is not null) return ReadCell(existing, layout.IdColumn);

        var headers = table.Headers.ToList();
        if (!headers.Contains(layout.IdColumn, StringComparer.Ordinal)
            || !headers.Contains(layout.NameColumn, StringComparer.Ordinal))
        {
            throw new InvalidDataException(
                $"{layout.Table}.csv に {layout.IdColumn} または {layout.NameColumn} の列がありません。");
        }

        // 顧客ごとにIDの付け方が違うため、既存IDの多数派に合わせて採番します。
        var ids = table.Rows.Select(row => ReadCell(row, layout.IdColumn)).ToArray();
        var (prefix, digits) = MasterIdPattern.Derive(ids, layout.FallbackPrefix);
        var allocated = ProductIdAllocator.Next(ids, prefix, digits);
        if (!allocated.IsSuccess)
        {
            throw new InvalidDataException(
                $"{layout.Label}のIDを採番できませんでした。{allocated.Error!.UserMessage}");
        }

        var newId = allocated.Value!;
        var rows = table.Rows.Select(row => new Dictionary<string, string>(row, StringComparer.Ordinal)).ToList();
        var added = headers.ToDictionary(header => header, _ => string.Empty, StringComparer.Ordinal);
        added[layout.IdColumn] = newId;
        added[layout.NameColumn] = trimmed;
        rows.Add(added);

        await WriteTableAsync(layout.Table, headers, rows, table.HasUtf8Bom, cancellationToken).ConfigureAwait(false);
        return newId;
    }

    public async Task RemoveValueAsync(ProductMasterKind kind, string id, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        var trimmed = id.Trim();
        var layout = MasterLayout.Of(kind);
        EnsureWritableSource();
        var snapshot = await LoadAsync(cancellationToken).ConfigureAwait(false);
        EnsureWritable(snapshot.Status);

        var table = snapshot.Set.Table(layout.Table);
        if (table is null) return;

        var headers = table.Headers.ToList();
        var rows = table.Rows
            .Where(row => !string.Equals(ReadCell(row, layout.IdColumn), trimmed, StringComparison.Ordinal))
            .Select(row => new Dictionary<string, string>(row, StringComparer.Ordinal))
            .ToList();
        if (rows.Count == table.Rows.Count) return;

        await WriteTableAsync(layout.Table, headers, rows, table.HasUtf8Bom, cancellationToken).ConfigureAwait(false);
    }

    private static string ReadCell(IReadOnlyDictionary<string, string> row, string column) =>
        row.TryGetValue(column, out var value) ? (value ?? string.Empty).Trim() : string.Empty;

    /// <summary>マスタ表ごとの表名・列名・採番の既定接頭辞です。</summary>
    private readonly record struct MasterLayout(
        string Table, string IdColumn, string NameColumn, string Label, string FallbackPrefix)
    {
        public static MasterLayout Of(ProductMasterKind kind) => kind switch
        {
            ProductMasterKind.Manufacturer => new(
                GoldStandardTables.Manufacturers,
                GoldStandardTables.Columns.ManufacturerId,
                GoldStandardTables.Columns.ManufacturerName,
                "メーカー名",
                "MFR-"),
            ProductMasterKind.MaterialCategory => new(
                GoldStandardTables.MaterialCategories,
                GoldStandardTables.Columns.MaterialCategoryId,
                GoldStandardTables.Columns.CategoryNameJa,
                GoldStandardFlattener.CategoryColumn,
                "CAT-"),
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "未対応のマスタ種別です。")
        };
    }

    private async Task WriteTableAsync(
        string table,
        List<string> headers,
        List<Dictionary<string, string>> rows,
        bool hasUtf8Bom,
        CancellationToken cancellationToken)
    {
        var path = Path.Combine(FolderPath, table + ".csv");
        var expected = File.Exists(path) ? File.GetLastWriteTimeUtc(path) : DateTime.MinValue;
        await _writer.ReplaceWithBackupAsync(
            path, CsvDocumentCodec.Write(headers, rows), expected, hasUtf8Bom, cancellationToken)
            .ConfigureAwait(false);

        _cache = null;
    }

    private async Task WriteProductsAsync(
        List<string> headers,
        List<Dictionary<string, string>> rows,
        bool hasUtf8Bom,
        DateTime expectedWriteTimeUtc,
        CancellationToken cancellationToken)
    {
        await _writer.ReplaceWithBackupAsync(
            ProductsPath, CsvDocumentCodec.Write(headers, rows), expectedWriteTimeUtc, hasUtf8Bom, cancellationToken)
            .ConfigureAwait(false);

        _cache = null;
    }

    /// <summary>名称からIDを引きます。引けない場合は書き込み前に断ります。</summary>
    private static string ResolveId(
        GoldStandardTableSet set, string table, string nameColumn, string idColumn, string? name, string label)
    {
        var value = (name ?? string.Empty).Trim();
        if (value.Length == 0)
        {
            throw new InvalidDataException($"「{label}」が空欄です。マスタに登録されている値を選んでください。");
        }

        foreach (var row in set.Rows(table))
        {
            var candidate = row.TryGetValue(nameColumn, out var raw) ? (raw ?? string.Empty).Trim() : string.Empty;
            if (string.Equals(candidate, value, StringComparison.Ordinal))
            {
                return row.TryGetValue(idColumn, out var id) ? (id ?? string.Empty).Trim() : string.Empty;
            }
        }

        throw new InvalidDataException(
            $"「{label}」の値『{value}』は{table}.csvに登録されていません。既存の値から選んでください。");
    }

    private async Task<Snapshot> LoadAsync(CancellationToken cancellationToken)
    {
        await _cacheLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var source = _source.Current;
            // 出どころそのものが入れ替わった場合も作り直します。
            // 版番号だけを見ると、別の出どころで偶然同じ番号のときに古い内容を返します。
            if (_cache is not null
                && ReferenceEquals(_cache.Source, source)
                && _cache.Version == source.Version)
            {
                return _cache;
            }

            var set = source.Load();
            var records = GoldStandardFlattener.Flatten(set, out var problems);
            // GoldStandardFlattener.FlattenはoutのproblemsをはじめからCollected = set.Problemsのコピーで
            // 埋めるため、problemsは既にset.Problemsを含むスーパーセットです。ここでさらにConcatすると、
            // フォルダ未検出などのCSV由来メッセージが画面に二重表示されます。
            var status = new DataSourceStatus(
                set.HasProducts,
                records.Count,
                null,
                problems);
            var productsWriteTime = source.SupportsWrite && File.Exists(ProductsPath)
                ? File.GetLastWriteTimeUtc(ProductsPath)
                : DateTime.MinValue;

            _cache = new Snapshot(source, source.Version, set, records, status, productsWriteTime);
            return _cache;
        }
        finally
        {
            _cacheLock.Release();
        }
    }

    /// <summary>
    /// 読み取り専用の出どころ（SQL接続中）では、書き込みを始める前に断ります。
    /// 画面のボタンを無効にするだけでは、画面の不具合で押せてしまったときに
    /// 顧客の実データへ書きかけます。最後の砦をここに置きます。
    /// </summary>
    private void EnsureWritableSource()
    {
        if (_source.SupportsWrite) return;

        throw new InvalidDataException(
            "SQL接続中は製品やマスタを登録できません。"
            + "設定タブでデータ元を「CSVフォルダ」に切り替えてください。");
    }

    private static void EnsureWritable(DataSourceStatus status)
    {
        if (!status.IsAvailable)
        {
            throw new InvalidDataException(status.Problems.FirstOrDefault() ?? "製品データを更新できません。");
        }
    }

    private static bool MatchesText(ProductRecord record, string text) =>
        text.Length == 0 || ProductFieldAccessor.Fields(record, Mapping).Values
            .Any(value => value is not null && value.Contains(text, StringComparison.OrdinalIgnoreCase));

    private static bool MatchesFilters(ProductRecord record, IReadOnlyDictionary<string, string> filters)
    {
        var fields = ProductFieldAccessor.Fields(record, Mapping);
        return filters.All(filter => fields.TryGetValue(filter.Key, out var actual)
            && string.Equals(actual.Trim(), filter.Value?.Trim() ?? string.Empty, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// 2つのProduct_IDが同じ製品を指すかを判定します。
    /// GetByIdAsync・UpsertAsync・RemoveAsyncはすべてこのメソッド経由で比較してください。
    /// 比較方法が食い違うと、「取得できるのに更新は別行を足す」「取得できるのに削除できない」
    /// という不整合がCSVに残ります（削除は新規登録の取り消し経路でもあるため、孤立行の原因になります）。
    /// </summary>
    private static bool IsSameProductId(string? left, string? right) =>
        string.Equals((left ?? string.Empty).Trim(), (right ?? string.Empty).Trim(), StringComparison.OrdinalIgnoreCase);

    private static void SetWhenHeaderExists(
        Dictionary<string, string> row, IReadOnlyList<string> headers, string header, string? value)
    {
        if (headers.Contains(header, StringComparer.Ordinal))
        {
            row[header] = value ?? string.Empty;
        }
    }

    private sealed record Snapshot(
        IGoldStandardTableSource Source,
        long Version,
        GoldStandardTableSet Set,
        IReadOnlyList<ProductRecord> Records,
        DataSourceStatus Status,
        DateTime ProductsWriteTimeUtc);
}
