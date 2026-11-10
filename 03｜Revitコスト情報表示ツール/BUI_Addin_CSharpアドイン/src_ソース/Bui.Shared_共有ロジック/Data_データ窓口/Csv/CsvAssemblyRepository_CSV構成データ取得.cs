// このファイルは固定スキーマの構成CSVを読み書きし、フィンガープリント検索と安全な追加・補償削除を提供するために存在します。
// CsvDocumentCodec と CsvFileWriter に依存し、行順序・列名・重複判定を変えると既存構成の復元性と更新時の原本保護に影響します。
using System.Globalization;
using System.IO;
using System.Text.Json;
using Bui.Assemblies;

namespace Bui.Data.Csv;

/// <summary>一つのAssembly行と後続するLayer行を固定CSVスキーマへ保存します。</summary>
public sealed class CsvAssemblyRepository : IAssemblyRepository
{
    private static readonly string[] Headers =
    [
        "row_type",
        "assembly_id",
        "fingerprint",
        "host_category",
        "layer_order",
        "function",
        "thickness_mm",
        "product_id",
        "revit_material_name",
        "source_updated_at",
        "additional_attributes_json"
    ];

    private readonly string csvPath;
    private readonly CsvFileWriter writer;
    private readonly Func<CancellationToken, Task> afterDocumentRead;

    public CsvAssemblyRepository(string csvPath, CsvFileWriter? writer = null)
        : this(csvPath, writer, null)
    {
    }

    /// <summary>読取直後の外部更新を実ファイルで検証するための、内部専用の狭い境界です。</summary>
    internal CsvAssemblyRepository(
        string csvPath,
        CsvFileWriter? writer,
        Func<CancellationToken, Task>? afterDocumentRead)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(csvPath);
        this.csvPath = csvPath;
        this.writer = writer ?? new CsvFileWriter();
        this.afterDocumentRead = afterDocumentRead ?? (_ => Task.CompletedTask);
    }

    public async Task<AssemblyRecord?> FindByFingerprintAsync(string fingerprint, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fingerprint);
        var snapshot = await ReadSnapshotAsync(cancellationToken).ConfigureAwait(false);
        return snapshot.Entries
            .FirstOrDefault(entry => string.Equals(entry.Fingerprint, fingerprint.Trim(), StringComparison.Ordinal))
            ?.Record;
    }

    public async Task<IReadOnlyList<string>> GetAllIdsAsync(CancellationToken cancellationToken)
    {
        var snapshot = await ReadSnapshotAsync(cancellationToken).ConfigureAwait(false);
        return snapshot.Entries.Select(entry => entry.Record.AssemblyId).ToArray();
    }

    public async Task AddAsync(AssemblyRecord record, string fingerprint, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(record);
        ArgumentException.ThrowIfNullOrWhiteSpace(fingerprint);
        ValidateRecord(record);

        var snapshot = await ReadSnapshotAsync(cancellationToken).ConfigureAwait(false);
        var assemblyId = record.AssemblyId.Trim();
        var normalizedFingerprint = fingerprint.Trim();
        if (snapshot.Entries.Any(entry => string.Equals(entry.Record.AssemblyId, assemblyId, StringComparison.Ordinal)))
        {
            throw new InvalidDataException($"Assembly_ID「{assemblyId}」は既に登録されています。");
        }

        if (snapshot.Entries.Any(entry => string.Equals(entry.Fingerprint, normalizedFingerprint, StringComparison.Ordinal)))
        {
            throw new InvalidDataException("同じ構成フィンガープリントは既に登録されています。");
        }

        var rows = snapshot.Document.Rows.Select(CloneRow).ToList();
        rows.Add(CreateAssemblyRow(record, normalizedFingerprint));
        foreach (var layer in record.OrderedLayers)
        {
            // Assembly行の直後に入力順のLayer行を並べ、構成順をCSVだけでも復元できるようにします。
            rows.Add(CreateLayerRow(assemblyId, layer));
        }

        await ReplaceAsync(snapshot, rows, cancellationToken).ConfigureAwait(false);
    }

    public async Task RemoveAsync(string assemblyId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(assemblyId);
        var normalizedAssemblyId = assemblyId.Trim();
        var snapshot = await ReadSnapshotAsync(cancellationToken).ConfigureAwait(false);
        if (!snapshot.Entries.Any(entry => string.Equals(entry.Record.AssemblyId, normalizedAssemblyId, StringComparison.Ordinal)))
        {
            return;
        }

        // 行種別でなく同じassembly_idだけを条件にして、対象Assemblyと全Layerを一つの安全置換で除去します。
        var retainedRows = snapshot.Document.Rows
            .Where(row => !string.Equals(row.GetValueOrDefault("assembly_id")?.Trim(), normalizedAssemblyId, StringComparison.Ordinal))
            .Select(CloneRow)
            .ToList();
        await ReplaceAsync(snapshot, retainedRows, cancellationToken).ConfigureAwait(false);
    }

    private async Task<CsvAssemblySnapshot> ReadSnapshotAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // 構成CSVは最初のAssembly_IDを採番するときに初めて作られます。
        // 設定画面も「未作成のファイル名も指定できます」と案内しています。
        // ここで読取例外にすると、初回の採番が必ず失敗し、画面には
        // 「Assembly_ID候補を確認できませんでした。」だけが出て原因が分かりません。
        // 未作成は空の一覧として扱い、AddAsyncが実ファイルを作ります。
        if (!File.Exists(csvPath))
        {
            // 存在しないパスのGetLastWriteTimeUtcは既定値を返します。
            // その値をそのまま版として持たせることで、確認中に誰かがCSVを作った場合は
            // 書込み側の版チェックが食い違いを検出できます。
            var missingTimestamp = File.GetLastWriteTimeUtc(csvPath);
            return new CsvAssemblySnapshot(
                missingTimestamp,
                // BOM付きで作ります。BOMが無いとExcelがShift_JISとして開き、
                // 日本語の材料名や層の機能が文字化けします。
                // 変換スクリプトが出力する他のBUI CSVもすべてBOM付きです。
                new CsvDocument(Headers, Array.Empty<Dictionary<string, string>>(), HasUtf8Bom: true),
                Array.Empty<CsvAssemblyEntry>());
        }

        var sourceTimestamp = File.GetLastWriteTimeUtc(csvPath);
        var document = CsvDocumentCodec.Read(csvPath);
        await afterDocumentRead(cancellationToken).ConfigureAwait(false);

        // 読み取った行と書込み時の版を必ず同じ原本に対応させ、途中の外部更新を古い行で上書きしないようにします。
        if (File.GetLastWriteTimeUtc(csvPath) != sourceTimestamp)
        {
            throw new IOException("CSVが他の処理によって変更されたため、更新を中止しました。");
        }

        RequireExactHeaders(document.Headers);
        var entries = ParseEntries(document.Rows);
        return new CsvAssemblySnapshot(sourceTimestamp, document, entries);
    }

    private async Task ReplaceAsync(CsvAssemblySnapshot snapshot, IReadOnlyList<Dictionary<string, string>> rows, CancellationToken cancellationToken)
    {
        await writer.ReplaceWithBackupAsync(
            csvPath,
            CsvDocumentCodec.Write(Headers, rows),
            snapshot.TimestampUtc,
            snapshot.Document.HasUtf8Bom,
            cancellationToken).ConfigureAwait(false);
    }

    private static IReadOnlyList<CsvAssemblyEntry> ParseEntries(IReadOnlyList<Dictionary<string, string>> rows)
    {
        var entries = new List<CsvAssemblyEntry>();
        AssemblyBuilder? current = null;
        foreach (var row in rows)
        {
            var rowType = row.GetValueOrDefault("row_type");
            if (string.Equals(rowType, "Assembly", StringComparison.Ordinal))
            {
                var assemblyId = RequireValue(row, "assembly_id").Trim();
                var fingerprint = RequireValue(row, "fingerprint").Trim();
                if (entries.Any(entry => string.Equals(entry.Record.AssemblyId, assemblyId, StringComparison.Ordinal))
                    || current is not null && string.Equals(current.AssemblyId, assemblyId, StringComparison.Ordinal))
                {
                    throw new InvalidDataException($"Assembly_ID「{assemblyId}」がCSV内で重複しています。");
                }

                if (entries.Any(entry => string.Equals(entry.Fingerprint, fingerprint, StringComparison.Ordinal))
                    || current is not null && string.Equals(current.Fingerprint, fingerprint, StringComparison.Ordinal))
                {
                    throw new InvalidDataException("構成フィンガープリントがCSV内で重複しています。");
                }

                if (current is not null)
                {
                    entries.Add(current.Build());
                }

                current = new AssemblyBuilder(
                    assemblyId,
                    fingerprint,
                    RequireValue(row, "host_category"),
                    ParseSourceUpdatedAt(row.GetValueOrDefault("source_updated_at")),
                    ParseAdditionalAttributes(row.GetValueOrDefault("additional_attributes_json")));
                continue;
            }

            if (string.Equals(rowType, "Layer", StringComparison.Ordinal))
            {
                if (current is null)
                {
                    throw new InvalidDataException("Assembly行より前にLayer行があります。");
                }

                var layerAssemblyId = RequireValue(row, "assembly_id").Trim();
                if (!string.Equals(current.AssemblyId, layerAssemblyId, StringComparison.Ordinal))
                {
                    throw new InvalidDataException("Layer行のassembly_idは直前のAssembly行と一致する必要があります。");
                }

                current.AddLayer(new AssemblyLayerRecord(
                    ParseLayerOrder(RequireValue(row, "layer_order")),
                    RequireValue(row, "function"),
                    ParseThickness(RequireValue(row, "thickness_mm")),
                    RequireValue(row, "product_id"),
                    row.GetValueOrDefault("revit_material_name") ?? string.Empty));
                continue;
            }

            throw new InvalidDataException("row_typeはAssemblyまたはLayerで指定してください。");
        }

        if (current is not null)
        {
            entries.Add(current.Build());
        }

        return entries;
    }

    private static void RequireExactHeaders(IReadOnlyList<string> headers)
    {
        if (!headers.SequenceEqual(Headers, StringComparer.Ordinal))
        {
            throw new InvalidDataException("構成CSVのヘッダーが定義済みスキーマと一致しません。");
        }
    }

    private static void ValidateRecord(AssemblyRecord record)
    {
        ArgumentNullException.ThrowIfNull(record.OrderedLayers);
        ArgumentNullException.ThrowIfNull(record.AdditionalAttributes);
        if (string.IsNullOrWhiteSpace(record.AssemblyId))
        {
            throw new InvalidDataException("Assembly_IDは空にできません。");
        }

        foreach (var layer in record.OrderedLayers)
        {
            ArgumentNullException.ThrowIfNull(layer);
            if (string.IsNullOrWhiteSpace(layer.ProductId))
            {
                throw new InvalidDataException("構成層のProduct_IDは空にできません。");
            }
        }
    }

    private static Dictionary<string, string> CreateAssemblyRow(AssemblyRecord record, string fingerprint)
    {
        var row = NewRow();
        row["row_type"] = "Assembly";
        row["assembly_id"] = record.AssemblyId.Trim();
        row["fingerprint"] = fingerprint;
        row["host_category"] = record.HostCategory ?? string.Empty;
        row["source_updated_at"] = record.SourceUpdatedAt?.ToString("O", CultureInfo.InvariantCulture) ?? string.Empty;
        row["additional_attributes_json"] = JsonSerializer.Serialize(
            record.AdditionalAttributes.OrderBy(attribute => attribute.Key, StringComparer.Ordinal)
                .ToDictionary(attribute => attribute.Key, attribute => attribute.Value));
        return row;
    }

    private static Dictionary<string, string> CreateLayerRow(string assemblyId, AssemblyLayerRecord layer)
    {
        var row = NewRow();
        row["row_type"] = "Layer";
        row["assembly_id"] = assemblyId;
        row["layer_order"] = layer.Order.ToString(CultureInfo.InvariantCulture);
        row["function"] = layer.Function ?? string.Empty;
        row["thickness_mm"] = layer.ThicknessMillimeters.ToString(CultureInfo.InvariantCulture);
        row["product_id"] = layer.ProductId.Trim();
        row["revit_material_name"] = layer.RevitMaterialName ?? string.Empty;
        return row;
    }

    private static Dictionary<string, string> NewRow() => Headers.ToDictionary(header => header, _ => string.Empty, StringComparer.Ordinal);

    private static Dictionary<string, string> CloneRow(Dictionary<string, string> row) => new(row, StringComparer.Ordinal);

    private static string RequireValue(IReadOnlyDictionary<string, string> row, string header)
    {
        var value = row.GetValueOrDefault(header);
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidDataException($"{header}は空にできません。");
        }

        return value;
    }

    private static int ParseLayerOrder(string value) => int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
        ? parsed
        : throw new InvalidDataException("layer_orderは整数で指定してください。");

    private static decimal ParseThickness(string value) => decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed)
        ? parsed
        : throw new InvalidDataException("thickness_mmは数値で指定してください。");

    private static DateTimeOffset? ParseSourceUpdatedAt(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed)
            ? parsed
            : throw new InvalidDataException("source_updated_atはISO 8601形式で指定してください。");
    }

    private static IReadOnlyDictionary<string, string> ParseAdditionalAttributes(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return new Dictionary<string, string>();
        }

        try
        {
            var parsed = JsonSerializer.Deserialize<Dictionary<string, string?>>(value);
            if (parsed is null)
            {
                throw new InvalidDataException("additional_attributes_jsonはJSONオブジェクトで指定してください。");
            }

            return parsed.ToDictionary(attribute => attribute.Key, attribute => attribute.Value ?? string.Empty, StringComparer.Ordinal);
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("additional_attributes_jsonのJSON形式が正しくありません。", exception);
        }
    }

    private sealed class AssemblyBuilder
    {
        private readonly List<AssemblyLayerRecord> layers = [];

        public AssemblyBuilder(
            string assemblyId,
            string fingerprint,
            string hostCategory,
            DateTimeOffset? sourceUpdatedAt,
            IReadOnlyDictionary<string, string> additionalAttributes)
        {
            AssemblyId = assemblyId;
            Fingerprint = fingerprint;
            HostCategory = hostCategory;
            SourceUpdatedAt = sourceUpdatedAt;
            AdditionalAttributes = additionalAttributes;
        }

        public string AssemblyId { get; }

        public string Fingerprint { get; }

        public string HostCategory { get; }

        public DateTimeOffset? SourceUpdatedAt { get; }

        public IReadOnlyDictionary<string, string> AdditionalAttributes { get; }

        public void AddLayer(AssemblyLayerRecord layer)
        {
            if (layers.Any(existing => existing.Order == layer.Order))
            {
                throw new InvalidDataException($"Assembly_ID「{AssemblyId}」に重複したlayer_orderがあります。");
            }

            layers.Add(layer);
        }

        public CsvAssemblyEntry Build() => new(
            new AssemblyRecord(AssemblyId, HostCategory, layers.ToArray(), SourceUpdatedAt, AdditionalAttributes),
            Fingerprint);
    }

    private sealed record CsvAssemblyEntry(AssemblyRecord Record, string Fingerprint);

    private sealed record CsvAssemblySnapshot(
        DateTime TimestampUtc,
        CsvDocument Document,
        IReadOnlyList<CsvAssemblyEntry> Entries);
}
