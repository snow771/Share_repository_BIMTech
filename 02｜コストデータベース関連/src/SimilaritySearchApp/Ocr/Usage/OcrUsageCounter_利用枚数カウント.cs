using System.IO;
using System.Text;
using System.Text.Json;
using SimilaritySearchApp.Models;
using SimilaritySearchApp.Ocr.Models;

namespace SimilaritySearchApp.Ocr.Usage;

/// <summary>
/// AI-OCR の利用枚数（Azure に送って解析されたページ数）を日ごとに数える。
/// 保存先は %AppData%\SimilaritySearchApp\ocr_usage.json（アプリを閉じても残る。90 日より古い日は消す）。
/// 無料枠を超えないよう、上限（既定 500）に対して 300 超で注意、400 超で警告の色を返す。
/// ページは Azure が解析を終えた分だけ数える（失敗した送信は数えない）。
/// </summary>
public sealed class OcrUsageCounter
{
    private const int KeepDays = 90;
    private static readonly object FileLock = new();
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public static string DefaultPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "SimilaritySearchApp", "ocr_usage.json");

    /// <summary>アプリを起動してからの数（ウィンドウを閉じても、アプリを閉じるまでは残る）</summary>
    private static int _sessionPages;
    private static int _sessionFiles;

    private readonly string _path;
    private readonly Func<DateTime> _now;

    public OcrUsageCounter(string? path = null, Func<DateTime>? now = null)
    {
        _path = path ?? DefaultPath;
        _now = now ?? (() => DateTime.Now);
    }

    private sealed class UsageFile
    {
        public List<DayEntry> Days { get; set; } = new();
    }

    private sealed class DayEntry
    {
        public string Date { get; set; } = "";
        public int Pages { get; set; }
        public int Files { get; set; }
    }

    /// <summary>解析が終わったページ数を足す。</summary>
    public void AddPages(int pages)
    {
        if (pages <= 0) return;
        Update(d => d.Pages += pages);
        Interlocked.Add(ref _sessionPages, pages);
    }

    /// <summary>OCR を終えた PDF の数を足す。</summary>
    public void AddFile()
    {
        Update(d => d.Files++);
        Interlocked.Increment(ref _sessionFiles);
    }

    /// <summary>今の状況（期間内の合計・色の段階・直近の日別）。</summary>
    public OcrUsageView Snapshot(OcrSettings s)
    {
        var days = Read().Days;
        var today = _now().Date;
        var inPeriod = days.Where(d => InPeriod(d.Date, today, s.Period)).ToList();
        var used = inPeriod.Sum(d => d.Pages);
        var limit = Math.Max(1, s.Limit);
        var recent = days.OrderByDescending(d => d.Date).Take(14).Select(d => new OcrUsageDay(d.Date, d.Pages, d.Files)).ToList();
        return new OcrUsageView(
            s.Period == "month" ? "month" : "day",
            s.Period == "month" ? "今月" : "本日",
            used, inPeriod.Sum(d => d.Files), limit, s.WarnAt, s.DangerAt,
            LevelOf(used, s), Math.Max(0, limit - used),
            _sessionPages, _sessionFiles, recent);
    }

    /// <summary>色の段階: safe（～注意値）/ caution（注意値超）/ danger（警告値超）/ over（上限以上）。</summary>
    public static string LevelOf(int used, OcrSettings s)
    {
        if (used >= Math.Max(1, s.Limit)) return "over";
        if (used > s.DangerAt) return "danger";
        if (used > s.WarnAt) return "caution";
        return "safe";
    }

    private static bool InPeriod(string date, DateTime today, string period)
    {
        if (!DateTime.TryParse(date, out var d)) return false;
        return period == "month"
            ? d.Year == today.Year && d.Month == today.Month
            : d.Date == today;
    }

    private void Update(Action<DayEntry> change)
    {
        lock (FileLock)
        {
            var file = Read();
            var key = _now().ToString("yyyy-MM-dd");
            var entry = file.Days.FirstOrDefault(d => d.Date == key);
            if (entry is null)
            {
                entry = new DayEntry { Date = key };
                file.Days.Add(entry);
            }
            change(entry);

            var oldest = _now().Date.AddDays(-KeepDays).ToString("yyyy-MM-dd");
            file.Days = file.Days.Where(d => string.CompareOrdinal(d.Date, oldest) >= 0).OrderBy(d => d.Date).ToList();
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllText(_path, JsonSerializer.Serialize(file, Json), Encoding.UTF8);
        }
    }

    private UsageFile Read()
    {
        lock (FileLock)
        {
            try
            {
                if (!File.Exists(_path)) return new UsageFile();
                return JsonSerializer.Deserialize<UsageFile>(File.ReadAllText(_path, Encoding.UTF8)) ?? new UsageFile();
            }
            catch (Exception)
            {
                return new UsageFile();   // 壊れていたら 0 から数え直す
            }
        }
    }
}
