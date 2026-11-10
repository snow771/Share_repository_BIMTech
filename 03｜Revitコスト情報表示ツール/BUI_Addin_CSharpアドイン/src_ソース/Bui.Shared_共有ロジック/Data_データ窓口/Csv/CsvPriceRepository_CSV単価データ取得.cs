// このファイルは単価CSVを設定列へ対応付けてキャッシュし、製品・地域・適用日検索と安全な追加更新を提供するために存在します。
// 必須列判定、適用日条件、更新キー、列の保存方法を変えると、見積へ選ばれる単価とCSV互換性に影響します。
using System.Globalization;
using System.IO;
using Bui.Products;

namespace Bui.Data.Csv;

/// <summary>単価CSVを読取り、更新時はバックアップを作ってから置換します。</summary>
public sealed class CsvPriceRepository : IPriceRepository
{
    private readonly string csvPath;
    private readonly PriceColumnMapping mapping;
    private readonly CsvFileWriter writer;
    private readonly SemaphoreSlim cacheLock = new(1, 1);
    private PriceSnapshot? cache;

    public CsvPriceRepository(string csvPath, PriceColumnMapping mapping, CsvFileWriter? writer = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(csvPath);
        this.csvPath = csvPath;
        this.mapping = mapping ?? throw new ArgumentNullException(nameof(mapping));
        this.writer = writer ?? new CsvFileWriter();
    }

    public async Task<DataSourceStatus> GetStatusAsync(CancellationToken cancellationToken) =>
        (await LoadAsync(cancellationToken).ConfigureAwait(false)).Status;

    public async Task<IReadOnlyList<PriceRecord>> SearchAsync(PriceQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var snapshot = await LoadAsync(cancellationToken).ConfigureAwait(false);
        if (!snapshot.Status.IsAvailable)
        {
            return Array.Empty<PriceRecord>();
        }

        var productId = query.ProductId?.Trim();
        var region = query.Region?.Trim();
        var limit = Math.Clamp(query.Limit, 1, 200);
        return snapshot.Records
            .Where(row => string.IsNullOrEmpty(productId)
                || string.Equals(row.ProductId, productId, StringComparison.OrdinalIgnoreCase))
            .Where(row => string.IsNullOrEmpty(region)
                || string.Equals(row.Region.Trim(), region, StringComparison.OrdinalIgnoreCase))
            .Where(row => query.EffectiveOn is null || row.ValidFrom is null || row.ValidFrom <= query.EffectiveOn)
            .Take(limit)
            .ToArray();
    }

    public async Task UpsertAsync(PriceRecord record, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(record);
        ValidateRecordForUpsert(record);
        var snapshot = await LoadAsync(cancellationToken).ConfigureAwait(false);
        if (!snapshot.Status.IsAvailable)
        {
            throw new InvalidDataException(snapshot.Status.Problems.FirstOrDefault() ?? "単価CSVを更新できません。");
        }

        var headers = snapshot.Document.Headers.ToList();
        AddHeaderWhenNeeded(headers, mapping.CurrencyColumn, record.Currency);
        AddHeaderWhenNeeded(headers, mapping.ValidFromColumn, record.ValidFrom?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        AddHeaderWhenNeeded(headers, mapping.RegionColumn, record.Region);
        AddHeaderWhenNeeded(headers, mapping.InformationSourceColumn, record.InformationSource);
        foreach (var key in record.AdditionalAttributes.Keys)
        {
            AddHeaderWhenNeeded(headers, key, "value");
        }

        var rows = snapshot.Document.Rows.Select(row => new Dictionary<string, string>(row, StringComparer.Ordinal)).ToList();
        var target = rows.FirstOrDefault(row => IsSamePriceKey(row, record));
        if (target is null)
        {
            target = headers.ToDictionary(header => header, _ => string.Empty, StringComparer.Ordinal);
            rows.Add(target);
        }

        WriteRecord(target, record, headers);
        await writer.ReplaceWithBackupAsync(
            csvPath,
            CsvDocumentCodec.Write(headers, rows),
            snapshot.TimestampUtc,
            snapshot.Document.HasUtf8Bom,
            cancellationToken).ConfigureAwait(false);
        cache = null;
    }

    private void ValidateRecordForUpsert(PriceRecord record)
    {
        if (string.IsNullOrWhiteSpace(record.ProductId))
        {
            throw new InvalidDataException($"必須項目「{mapping.ProductIdColumn}」に製品IDがありません。");
        }

        var mappedColumns = new HashSet<string>(StringComparer.Ordinal)
        {
            mapping.ProductIdColumn,
            mapping.UnitPriceColumn,
            mapping.CurrencyColumn,
            mapping.ValidFromColumn,
            mapping.RegionColumn,
            mapping.InformationSourceColumn
        };
        var collision = record.AdditionalAttributes.Keys.FirstOrDefault(mappedColumns.Contains);
        if (collision is not null)
        {
            // 単価やIDを追加属性が上書きしないよう、ファイル操作より前に入力契約違反として停止します。
            throw new InvalidDataException($"追加属性「{collision}」は設定済みの単価列と重複しています。");
        }
    }

    private async Task<PriceSnapshot> LoadAsync(CancellationToken cancellationToken)
    {
        await cacheLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var timestamp = File.Exists(csvPath) ? File.GetLastWriteTimeUtc(csvPath) : DateTime.MinValue;
            if (cache is not null && cache.TimestampUtc == timestamp)
            {
                return cache;
            }

            // ファイルが無い場合は、例外本文（保存場所を含む）ではなく専用の説明を返します。
            // 例外本文はパスを含むため画面に出せず、結果として原因不明の
            // 「読み込めません」だけが表示されて利用者が詰まりました。
            if (!File.Exists(csvPath))
            {
                cache = new PriceSnapshot(
                    timestamp,
                    new CsvDocument(Array.Empty<string>(), Array.Empty<Dictionary<string, string>>(), false),
                    Array.Empty<PriceRecord>(),
                    new DataSourceStatus(false, 0, null,
                        new[] { "単価CSVが見つかりません。設定タブのパスを確認してください。" }));
                return cache;
            }

            try
            {
                var document = CsvDocumentCodec.Read(csvPath);
                RequireHeader(document.Headers, mapping.ProductIdColumn);
                RequireHeader(document.Headers, mapping.UnitPriceColumn);
                var records = CsvRowSkipReport.MapRows(document.Rows, row => PriceRowMapper.Map(row, mapping), out var problems);
                var status = new DataSourceStatus(true, records.Count, timestamp, problems);
                cache = new PriceSnapshot(timestamp, document, records, status);
            }
            catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException)
            {
                var status = new DataSourceStatus(false, 0, timestamp == DateTime.MinValue ? null : timestamp, [ex.Message]);
                cache = new PriceSnapshot(timestamp, new CsvDocument(Array.Empty<string>(), Array.Empty<Dictionary<string, string>>(), false), Array.Empty<PriceRecord>(), status);
            }

            return cache;
        }
        finally
        {
            cacheLock.Release();
        }
    }

    private static void RequireHeader(IReadOnlyList<string> headers, string required)
    {
        if (!headers.Contains(required, StringComparer.Ordinal))
        {
            throw new InvalidDataException($"必須ヘッダー「{required}」がありません。列名は大文字・小文字を区別します。");
        }
    }

    private bool IsSamePriceKey(IReadOnlyDictionary<string, string> row, PriceRecord record)
    {
        PriceRecord mapped;
        try
        {
            mapped = PriceRowMapper.Map(row, mapping);
        }
        catch (InvalidDataException)
        {
            // 変換できない行（紐付けや単価が未入力の作業途中の行など）は、上書き対象にしません。
            // ここで例外を投げると、そうした行が1つでもあるだけで書込み自体ができなくなります。
            // 対象外として扱えば、その行は生のまま書き戻され、内容が保たれます。
            return false;
        }

        return string.Equals(mapped.ProductId, record.ProductId.Trim(), StringComparison.Ordinal)
            && string.Equals(mapped.Region, record.Region.Trim(), StringComparison.Ordinal)
            && mapped.ValidFrom == record.ValidFrom;
    }

    private void WriteRecord(Dictionary<string, string> row, PriceRecord record, IReadOnlyList<string> headers)
    {
        row[mapping.ProductIdColumn] = record.ProductId.Trim();
        row[mapping.UnitPriceColumn] = record.UnitPrice.ToString(CultureInfo.InvariantCulture);
        SetWhenHeaderExists(row, headers, mapping.CurrencyColumn, record.Currency);
        SetWhenHeaderExists(row, headers, mapping.ValidFromColumn, record.ValidFrom?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? string.Empty);
        SetWhenHeaderExists(row, headers, mapping.RegionColumn, record.Region);
        SetWhenHeaderExists(row, headers, mapping.InformationSourceColumn, record.InformationSource);
        foreach (var attribute in record.AdditionalAttributes)
        {
            row[attribute.Key] = attribute.Value;
        }
    }

    private static void SetWhenHeaderExists(Dictionary<string, string> row, IReadOnlyList<string> headers, string header, string value)
    {
        if (headers.Contains(header, StringComparer.Ordinal))
        {
            row[header] = value ?? string.Empty;
        }
    }

    private static void AddHeaderWhenNeeded(List<string> headers, string header, string? value)
    {
        if (!string.IsNullOrEmpty(value) && !headers.Contains(header, StringComparer.Ordinal))
        {
            headers.Add(header);
        }
    }

    private sealed record PriceSnapshot(
        DateTime TimestampUtc,
        CsvDocument Document,
        IReadOnlyList<PriceRecord> Records,
        DataSourceStatus Status);
}
