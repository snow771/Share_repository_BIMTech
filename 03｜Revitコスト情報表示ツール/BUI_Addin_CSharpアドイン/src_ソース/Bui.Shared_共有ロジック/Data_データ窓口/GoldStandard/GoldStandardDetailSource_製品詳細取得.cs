// このファイルの目的:
//   GoldStandardの表から、概要タブの製品カード1枚分を組み立てます。
//
// 並び順:
//   タブはDisplay_Groups.display_order、行はSpec_Master.display_orderに従います。
//   ここを固定の順序に変えると、Display_Groupsに行を足しても画面へ反映されません。
//
// 画像を外部から取らない理由:
//   Product_Images.image_urlは外部URLです。LLM接続ではローカル・社内限定の
//   方針を敷いており、画像だけ外部通信するのは一貫しません。
//   データフォルダ内のfile_nameだけを使い、無ければ「画像なし」にします。
//
// 出どころはDataSourceSwitch経由で受け取ります。フォルダパスを直接持たないため、
// キャッシュはDataSourceSwitch.Currentの参照と版番号(Version)の両方で判定します。
// 参照だけ・版番号だけのどちらか一方を見落とすと、出どころの切り替え後もBUIが
// 古い内容を表示し続けます（別の出どころで偶然同じ版番号になる場合があるため）。
//
// 修正時の影響:
//   任意の表（Sources、Reliability_Ranks、Display_Groups）が欠けても
//   詳細を返せる状態を保ってください。欠けたら画面が空になるだけで済みます。

using System.IO;
using Bui.Products;

namespace Bui.Data.GoldStandard;

/// <summary>GoldStandardのデータフォルダから製品カードの内容を作ります。</summary>
public sealed class GoldStandardDetailSource : IProductDetailSource
{
    /// <summary>表示グループの定義が引けない属性をまとめる先です。</summary>
    private const string UngroupedId = "OTHER";
    private const string UngroupedName = "その他";

    private readonly DataSourceSwitch _source;
    private readonly SemaphoreSlim _cacheLock = new(1, 1);
    private GoldStandardTableSet? _cache;
    private IGoldStandardTableSource? _cachedSource;
    private long _cachedVersion = -1;

    public GoldStandardDetailSource(DataSourceSwitch source) =>
        _source = source ?? throw new ArgumentNullException(nameof(source));

    public async Task<ProductDetail?> GetDetailAsync(string productId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(productId)) return null;

        var set = await LoadAsync(cancellationToken).ConfigureAwait(false);
        var id = productId.Trim();
        var product = set.Rows(GoldStandardTables.Products).FirstOrDefault(row =>
            string.Equals(Value(row, GoldStandardTables.Columns.ProductId).Trim(), id, StringComparison.OrdinalIgnoreCase));
        if (product is null) return null;

        var manufacturers = Lookup(set, GoldStandardTables.Manufacturers,
            GoldStandardTables.Columns.ManufacturerId, GoldStandardTables.Columns.ManufacturerName);
        var categories = Lookup(set, GoldStandardTables.MaterialCategories,
            GoldStandardTables.Columns.MaterialCategoryId, GoldStandardTables.Columns.CategoryNameJa);
        var ranks = Lookup(set, GoldStandardTables.ReliabilityRanks,
            GoldStandardTables.Columns.ReliabilityRankId, GoldStandardTables.Columns.RankCode);
        var specs = set.Rows(GoldStandardTables.SpecMaster)
            .Where(row => Value(row, GoldStandardTables.Columns.SpecKey).Trim().Length > 0)
            .ToDictionary<IReadOnlyDictionary<string, string>, string, IReadOnlyDictionary<string, string>>(
                row => Value(row, GoldStandardTables.Columns.SpecKey).Trim(), row => row, StringComparer.Ordinal);
        var sources = set.Rows(GoldStandardTables.Sources)
            .Where(row => Value(row, GoldStandardTables.Columns.SourceId).Trim().Length > 0)
            .ToDictionary<IReadOnlyDictionary<string, string>, string, IReadOnlyDictionary<string, string>>(
                row => Value(row, GoldStandardTables.Columns.SourceId).Trim(), row => row, StringComparer.Ordinal);

        var rows = set.Rows(GoldStandardTables.ProductSpecs)
            .Where(row => string.Equals(Value(row, GoldStandardTables.Columns.ProductId).Trim(), id, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        return new ProductDetail(
            id,
            SpecValueFormatter.Clean(Value(product, GoldStandardTables.Columns.ProductName)),
            Resolve(Value(product, GoldStandardTables.Columns.ManufacturerId), manufacturers),
            Resolve(Value(product, GoldStandardTables.Columns.MaterialCategoryId), categories),
            FindImagePath(set, id),
            SpecValueFormatter.Clean(Value(product, GoldStandardTables.Columns.ProductUrl)),
            BuildGroups(set, rows, specs, sources, ranks),
            BuildSources(rows, sources, ranks),
            BuildRelated(product, set, id));
    }

    private IReadOnlyList<SpecGroup> BuildGroups(
        GoldStandardTableSet set,
        IReadOnlyList<IReadOnlyDictionary<string, string>> rows,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> specs,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> sources,
        IReadOnlyDictionary<string, string> ranks)
    {
        var groupNames = new Dictionary<string, string>(StringComparer.Ordinal);
        var groupOrders = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var row in set.Rows(GoldStandardTables.DisplayGroups))
        {
            var groupId = Value(row, GoldStandardTables.Columns.DisplayGroupId).Trim();
            if (groupId.Length == 0) continue;
            groupNames[groupId] = SpecValueFormatter.Clean(Value(row, GoldStandardTables.Columns.DisplayGroupNameJa));
            groupOrders[groupId] = Order(Value(row, GoldStandardTables.Columns.DisplayOrder));
        }

        var lines = new List<(string GroupId, int Order, SpecLine Line)>();
        foreach (var row in rows)
        {
            var specKey = Value(row, GoldStandardTables.Columns.SpecKey).Trim();
            if (specKey.Length == 0) continue;

            var value = SpecValueFormatter.SelectValue(
                Value(row, GoldStandardTables.Columns.ValueNumber),
                Value(row, GoldStandardTables.Columns.ValueString));
            // 値が無い属性は行を作りません。空欄が並んでも読み手の役に立ちません。
            if (value.Length == 0) continue;

            specs.TryGetValue(specKey, out var definition);
            var groupId = definition is null
                ? UngroupedId
                : Value(definition, GoldStandardTables.Columns.DisplayGroupId).Trim();
            if (groupId.Length == 0) groupId = UngroupedId;

            var displayName = definition is null
                ? specKey
                : SpecValueFormatter.Clean(Value(definition, GoldStandardTables.Columns.DisplayNameJa));
            if (displayName.Length == 0) displayName = specKey;

            // 単位はSpec_Masterの標準単位を使います。属性行のunitは表記ゆれがあります。
            var unit = definition is null
                ? string.Empty
                : SpecValueFormatter.NormalizeUnit(Value(definition, GoldStandardTables.Columns.StandardUnit));

            var sourceId = Value(row, GoldStandardTables.Columns.SourceId).Trim();
            var sourceName = sources.TryGetValue(sourceId, out var source)
                ? SpecValueFormatter.Clean(Value(source, GoldStandardTables.Columns.SourceName))
                : string.Empty;
            var rank = ranks.TryGetValue(Value(row, GoldStandardTables.Columns.ReliabilityRankId).Trim(), out var code)
                ? code
                : string.Empty;

            var order = definition is null ? int.MaxValue : Order(Value(definition, GoldStandardTables.Columns.DisplayOrder));
            lines.Add((groupId, order, new SpecLine(displayName, value, unit, rank, sourceName)));
        }

        return lines
            .GroupBy(item => item.GroupId, StringComparer.Ordinal)
            .OrderBy(group => groupOrders.TryGetValue(group.Key, out var order) ? order : int.MaxValue)
            .ThenBy(group => group.Key, StringComparer.Ordinal)
            .Select(group => new SpecGroup(
                group.Key,
                groupNames.TryGetValue(group.Key, out var name) && name.Length > 0 ? name : UngroupedName,
                group.OrderBy(item => item.Order).Select(item => item.Line).ToArray()))
            .ToArray();
    }

    private static IReadOnlyList<SourceReference> BuildSources(
        IReadOnlyList<IReadOnlyDictionary<string, string>> rows,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> sources,
        IReadOnlyDictionary<string, string> ranks)
    {
        var used = rows
            .Select(row => Value(row, GoldStandardTables.Columns.SourceId).Trim())
            .Where(id => id.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        var references = new List<SourceReference>();
        foreach (var id in used)
        {
            if (!sources.TryGetValue(id, out var source)) continue;
            references.Add(new SourceReference(
                SpecValueFormatter.Clean(Value(source, GoldStandardTables.Columns.SourceName)),
                SpecValueFormatter.Clean(Value(source, GoldStandardTables.Columns.SourceType)),
                SpecValueFormatter.Clean(Value(source, GoldStandardTables.Columns.Publisher)),
                ExcelSerialDate.Parse(Value(source, GoldStandardTables.Columns.PublicationDate)),
                SpecValueFormatter.Clean(Value(source, GoldStandardTables.Columns.Url)),
                ranks.TryGetValue(Value(source, GoldStandardTables.Columns.ReliabilityRankId).Trim(), out var code)
                    ? code
                    : string.Empty));
        }

        return references.OrderBy(item => item.SourceName, StringComparer.Ordinal).ToArray();
    }

    private IReadOnlyList<RelatedResource> BuildRelated(
        IReadOnlyDictionary<string, string> product, GoldStandardTableSet set, string productId)
    {
        var related = new List<RelatedResource>();

        var productUrl = SpecValueFormatter.Clean(Value(product, GoldStandardTables.Columns.ProductUrl));
        if (productUrl.Length > 0)
        {
            related.Add(new RelatedResource("製品ページ", "product_url", string.Empty, productUrl));
        }

        // 画像は必ずローカルのファイルとして扱います。image_urlは意図的に無視します。
        foreach (var row in ImageRows(set, productId))
        {
            var path = LocalImagePath(row);
            if (path.Length == 0) continue;
            var title = SpecValueFormatter.Clean(Value(row, GoldStandardTables.Columns.FileName));
            related.Add(new RelatedResource(
                title.Length > 0 ? title : "製品画像", "image", path, string.Empty));
        }

        return related;
    }

    private string FindImagePath(GoldStandardTableSet set, string productId)
    {
        var rows = ImageRows(set, productId).ToArray();
        // 代表画像があればそれを、無ければ最初に見つかったものを使います。
        var primary = rows.FirstOrDefault(row =>
            string.Equals(Value(row, GoldStandardTables.Columns.IsPrimary).Trim(), "true", StringComparison.OrdinalIgnoreCase));
        foreach (var row in primary is null ? rows : new[] { primary }.Concat(rows))
        {
            var path = LocalImagePath(row);
            if (path.Length > 0) return path;
        }

        return string.Empty;
    }

    private IEnumerable<IReadOnlyDictionary<string, string>> ImageRows(GoldStandardTableSet set, string productId) =>
        set.Rows(GoldStandardTables.ProductImages).Where(row =>
            string.Equals(Value(row, GoldStandardTables.Columns.ProductId).Trim(), productId, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// データフォルダ内に実在する画像ファイルだけを返します。外部URLは使いません。
    ///
    /// なぜ2段階で試すのか:
    ///   file_nameは本来ファイル名（またはデータフォルダ起点のサブフォルダ付き相対パス、
    ///   例: "製品画像\画像.jpg"）を想定しているが、実データには元PCの絶対パス
    ///   （例: "C:\BUI_PoCData\画像フォルダ\画像.jpg"、サブフォルダを含む）がそのまま
    ///   入っていることがある。
    ///   1段階目はfile_nameをそのまま使う。これは今のデータフォルダが元PCと同じ場所
    ///   （絶対パスがそのまま一致する）なら、サブフォルダ構造ごと正しく解決できる。
    ///   2段階目は、1段階目が絶対パスで失敗した場合だけ、ファイル名部分だけを
    ///   データフォルダ直下から探す。これは別PCへデータフォルダごと配った場合の保険で、
    ///   元PCと違う場所に置かれていても、サブフォルダ無しでファイルが直接置かれていれば拾える。
    ///   （2026-09-25、絶対パスを問答無用でファイル名だけに削る1段階の実装をいったん入れたが、
    ///   サブフォルダを含む絶対パスがそのまま一致するはずの元PCまで壊してしまい、
    ///   この2段階の形に直した）。
    /// </summary>
    private string LocalImagePath(IReadOnlyDictionary<string, string> row)
    {
        var rawFileName = SpecValueFormatter.Clean(Value(row, GoldStandardTables.Columns.FileName));
        if (rawFileName.Length == 0) return string.Empty;

        var direct = ResolveWithinDataFolder(rawFileName);
        if (direct.Length > 0) return direct;

        return Path.IsPathRooted(rawFileName)
            ? ResolveWithinDataFolder(Path.GetFileName(rawFileName))
            : string.Empty;
    }

    /// <summary>fileNameをデータフォルダと結合し、その外を指していないか確かめてから存在確認します。</summary>
    private string ResolveWithinDataFolder(string fileName)
    {
        if (fileName.Length == 0) return string.Empty;

        try
        {
            // フォルダの外を指すパスを弾きます。データの誤りで別の場所を読ませないためです。
            // 末尾に区切り文字を付けて比較することで、"C:\data-evil" のような
            // 兄弟フォルダが "C:\data" の前方一致で通ってしまうのを防ぎます。
            // fileNameが絶対パスの場合、Path.Combineは第1引数(データフォルダ)を無視して
            // fileNameをそのまま返す。これは意図的に許容しており、下の閉じ込めチェックが
            // 「そのデータフォルダの中を指しているか」を別途確かめる。
            var candidate = Path.GetFullPath(Path.Combine(_source.DataFolderPath, fileName));
            var root = Path.GetFullPath(_source.DataFolderPath);
            if (!root.EndsWith(Path.DirectorySeparatorChar)) root += Path.DirectorySeparatorChar;
            if (!candidate.StartsWith(root, StringComparison.OrdinalIgnoreCase)) return string.Empty;
            return File.Exists(candidate) ? candidate : string.Empty;
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return string.Empty;
        }
    }

    private async Task<GoldStandardTableSet> LoadAsync(CancellationToken cancellationToken)
    {
        await _cacheLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var source = _source.Current;
            if (_cache is not null && ReferenceEquals(_cachedSource, source) && _cachedVersion == source.Version)
            {
                return _cache;
            }

            _cache = source.Load();
            _cachedSource = source;
            _cachedVersion = source.Version;
            return _cache;
        }
        finally
        {
            _cacheLock.Release();
        }
    }

    private static Dictionary<string, string> Lookup(
        GoldStandardTableSet set, string table, string keyColumn, string valueColumn)
    {
        var lookup = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var row in set.Rows(table))
        {
            var key = Value(row, keyColumn).Trim();
            if (key.Length == 0) continue;
            lookup[key] = SpecValueFormatter.Clean(Value(row, valueColumn));
        }

        return lookup;
    }

    private static string Resolve(string? id, IReadOnlyDictionary<string, string> lookup)
    {
        var key = (id ?? string.Empty).Trim();
        if (key.Length == 0) return string.Empty;
        return lookup.TryGetValue(key, out var name) && name.Length > 0 ? name : key;
    }

    /// <summary>並び順が読めない場合は末尾へ送ります。値が無いことで行が消えるより安全です。</summary>
    private static int Order(string? value) =>
        int.TryParse((value ?? string.Empty).Trim(), out var order) ? order : int.MaxValue;

    private static string Value(IReadOnlyDictionary<string, string> row, string column) =>
        row.TryGetValue(column, out var value) ? value ?? string.Empty : string.Empty;
}
