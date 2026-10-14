using System.IO;
using SimilaritySearchApp.Excel;
using YamlDotNet.Serialization;

namespace SimilaritySearchApp.Stencil;

/// <summary>
/// Stencile（元データ Excel）のセル位置の定義（mapping YAML）。
/// 形式: セクション名 → { sheet: シート名, fields: { 項目名: セル番地 } }
/// </summary>
public sealed class StencilMapping
{
    public sealed record Section(string Sheet, Dictionary<string, string> Fields);

    public StencilMapping(string sourcePath, Dictionary<string, Section> sections)
    {
        SourcePath = sourcePath;
        Sections = sections;
    }

    /// <summary>読み込んだ YAML のパス</summary>
    public string SourcePath { get; }

    public Dictionary<string, Section> Sections { get; }

    /// <summary>同梱の既定マッピング（exe と同じ場所の Stencil\Assets）</summary>
    public static string DefaultPath { get; } =
        Path.Combine(AppContext.BaseDirectory, "Stencil", "Assets", "mapping_default_既定マッピング.yaml");

    /// <summary>項目のシート名とセル番地。定義が無ければ null。</summary>
    public (string Sheet, string Address)? Locate(string section, string field)
    {
        if (!Sections.TryGetValue(section, out var sec)) return null;
        return sec.Fields.TryGetValue(field, out var addr) && !string.IsNullOrWhiteSpace(addr) ? (sec.Sheet, addr) : null;
    }

    /// <summary>ブックから項目の値を読む（シート・項目・番地が無い／不正なら null）。</summary>
    public object? Read(IReadOnlyDictionary<string, SheetData> book, string section, string field)
    {
        var loc = Locate(section, field);
        if (loc is null || !book.TryGetValue(loc.Value.Sheet, out var sheet)) return null;
        return sheet.Get(loc.Value.Address);
    }

    /// <summary>必要なシート名（定義に出てくるもの）</summary>
    public IReadOnlyCollection<string> SheetNames => Sections.Values.Select(s => s.Sheet).Distinct().ToList();

    public static StencilMapping Load(string path)
    {
        var yaml = File.ReadAllText(path);
        var raw = new DeserializerBuilder().Build().Deserialize<Dictionary<string, object?>>(yaml)
                  ?? throw new InvalidDataException("マッピング YAML が空です。");

        var sections = new Dictionary<string, Section>(StringComparer.Ordinal);
        foreach (var (name, value) in raw)
        {
            if (value is not Dictionary<object, object?> sec) continue;
            var sheet = sec.TryGetValue("sheet", out var s) ? s?.ToString() ?? name : name;
            var fields = new Dictionary<string, string>(StringComparer.Ordinal);
            if (sec.TryGetValue("fields", out var f) && f is Dictionary<object, object?> map)
            {
                foreach (var (k, v) in map)
                {
                    var key = k?.ToString();
                    if (!string.IsNullOrEmpty(key) && v is not null) fields[key] = v.ToString() ?? "";
                }
            }
            sections[name] = new Section(sheet, fields);
        }
        if (sections.Count == 0) throw new InvalidDataException("マッピング YAML にセクションがありません。");
        return new StencilMapping(path, sections);
    }

    /// <summary>
    /// マッピング YAML を探す（Python 版と同じ考え方）。
    /// 指定パス → 元データフォルダ内の *mapping* / *マッピング* → 同梱の既定マッピング の順。
    /// </summary>
    public static string? Find(string? preferred, string? sourceFolder)
    {
        if (!string.IsNullOrWhiteSpace(preferred) && File.Exists(preferred)) return preferred;
        if (!string.IsNullOrWhiteSpace(sourceFolder) && Directory.Exists(sourceFolder))
        {
            var candidates = Directory.EnumerateFiles(sourceFolder, "*.yaml")
                .Concat(Directory.EnumerateFiles(sourceFolder, "*.yml"))
                .OrderBy(p => p, StringComparer.Ordinal)
                .ToList();
            var hit = candidates.FirstOrDefault(p =>
                Path.GetFileNameWithoutExtension(p).Contains("mapping", StringComparison.OrdinalIgnoreCase) ||
                Path.GetFileNameWithoutExtension(p).Contains("マッピング", StringComparison.Ordinal));
            if (hit is not null) return hit;
        }
        return File.Exists(DefaultPath) ? DefaultPath : null;
    }
}
