using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace SimilaritySearchApp.Ocr.Structure;

/// <summary>
/// 構造化ルール（共通ルール JSON ＋ 協力業者差分ルール JSON）。
/// 統合の仕方は Python 版 merge_rules と同じ:
///   辞書は再帰的に統合 / 配列は「共通 → 差分」の順で重複を除いて連結 / 数値・文字列は差分を優先。
/// </summary>
public sealed class OcrRules
{
    /// <summary>同梱のルール（出力フォルダの Ocr\Assets）。</summary>
    public static string AssetsFolder { get; } = Path.Combine(AppContext.BaseDirectory, "Ocr", "Assets");
    public static string DefaultCommonPath { get; } = Path.Combine(AssetsFolder, "RulesCommon_共通ルール.json");

    public List<string> IgnoreValues { get; init; } = new();

    /// <summary>列名候補（項目 → 見出しの候補）。順番を保つ。</summary>
    public List<KeyValuePair<string, List<string>>> ColumnAliases { get; init; } = new();
    public List<string> RequiredColumns { get; init; } = new();
    public int HeaderSearchRows { get; init; } = 5;

    public List<string> SectionPatterns { get; init; } = new();
    public List<string> IgnoreNamePatterns { get; init; } = new();
    public List<string> RemarkReviewPatterns { get; init; } = new();

    public Dictionary<string, string> UnitAliases { get; init; } = new();

    /// <summary>見積ヘッダ（工事名・協力業者名・見積番号・見積金額）のラベル候補</summary>
    public List<KeyValuePair<string, List<string>>> TableLabels { get; init; } = new();
    public List<KeyValuePair<string, List<string>>> ContentPatterns { get; init; } = new();
    public List<string> ExcludeSupplierPatterns { get; init; } = new();

    public List<string> Aliases(string field) =>
        ColumnAliases.FirstOrDefault(kv => kv.Key == field).Value ?? new List<string>();

    // ── 読込 ──

    public static JsonObject LoadJson(string path)
    {
        if (!File.Exists(path)) throw new FileNotFoundException($"ルール JSON が見つかりません: {path}");
        try
        {
            return JsonNode.Parse(File.ReadAllText(path, Encoding.UTF8))?.AsObject()
                   ?? throw new InvalidDataException("空のファイルです。");
        }
        catch (JsonException e)
        {
            throw new InvalidOperationException($"ルール JSON の形式が正しくありません: {Path.GetFileName(path)} / {e.Message}", e);
        }
    }

    public static OcrRules Load(string commonPath, string? supplierPath)
    {
        var common = LoadJson(commonPath);
        var supplier = string.IsNullOrWhiteSpace(supplierPath) ? new JsonObject() : LoadJson(supplierPath);
        return From(MergeRules(common, supplier)!.AsObject());
    }

    /// <summary>ルールの統合（Python 版 merge_rules）。</summary>
    public static JsonNode? MergeRules(JsonNode? @base, JsonNode? @override)
    {
        if (@base is JsonObject b && @override is JsonObject o)
        {
            var merged = (JsonObject)b.DeepClone();
            foreach (var (key, value) in o)
            {
                merged[key] = merged.ContainsKey(key) ? MergeRules(merged[key], value) : value?.DeepClone();
            }
            return merged;
        }
        if (@base is JsonArray ba && @override is JsonArray oa)
        {
            var merged = new JsonArray();
            foreach (var v in ba.Concat(oa))
            {
                if (!merged.Any(m => JsonNode.DeepEquals(m, v))) merged.Add(v?.DeepClone());
            }
            return merged;
        }
        return @override?.DeepClone();
    }

    /// <summary>統合済みの JSON から必要な値を取り出す。</summary>
    public static OcrRules From(JsonObject r)
    {
        var detail = r["detail_table"];
        var cls = r["row_classification"];
        var header = r["header_fields"];
        if (r["column_aliases"] is not JsonObject)
            throw new InvalidOperationException("ルール JSON に column_aliases（列名候補）がありません。共通ルールを指定してください。");
        if (detail?["required_columns"] is not JsonArray)
            throw new InvalidOperationException("ルール JSON に detail_table.required_columns がありません。共通ルールを指定してください。");

        return new OcrRules
        {
            IgnoreValues = Strings(r["ignore_values"]),
            ColumnAliases = Groups(r["column_aliases"]),
            RequiredColumns = Strings(detail!["required_columns"]),
            HeaderSearchRows = detail["header_search_rows"] is JsonValue v && v.TryGetValue<int>(out var n) ? n : 5,
            SectionPatterns = Strings(cls?["section_patterns"]),
            IgnoreNamePatterns = Strings(cls?["ignore_name_patterns"]),
            RemarkReviewPatterns = Strings(cls?["remark_review_patterns"]),
            UnitAliases = (r["normalization"]?["unit_aliases"] as JsonObject ?? new JsonObject())
                .Where(kv => kv.Value is not null)
                .ToDictionary(kv => kv.Key, kv => Str(kv.Value!)),
            TableLabels = Groups(header?["table_labels"]),
            ContentPatterns = Groups(header?["content_patterns"]),
            ExcludeSupplierPatterns = Strings(header?["exclude_supplier_patterns"]),
        };
    }

    private static List<string> Strings(JsonNode? node) =>
        (node as JsonArray ?? new JsonArray()).OfType<JsonNode>().Select(Str).ToList();

    private static string Str(JsonNode x) => x is JsonValue v && v.TryGetValue<string>(out var s) ? s : x.ToJsonString();

    private static List<KeyValuePair<string, List<string>>> Groups(JsonNode? node) =>
        (node as JsonObject ?? new JsonObject()).Select(kv => new KeyValuePair<string, List<string>>(kv.Key, Strings(kv.Value))).ToList();
}
