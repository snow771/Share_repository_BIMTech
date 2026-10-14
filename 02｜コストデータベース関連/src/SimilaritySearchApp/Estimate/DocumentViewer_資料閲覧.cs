using System.Diagnostics;
using System.IO;
using SimilaritySearchApp.Estimate.Data;

namespace SimilaritySearchApp.Estimate;

/// <summary>
/// DB に保存した資料を一時フォルダに取り出して表示する。
///   ・取り出し先は %LocalAppData%\SimilaritySearchApp\EstimateDocs\{ウィンドウごとのフォルダ}\（アプリが決めた安全な名前）
///   ・PDF は WebView2 の仮想ホスト（https://estimate-docs.local/）経由で画面内に表示（#page=N でページ移動）
///   ・Excel は OS に関連付けられたアプリで開く。外部で編集しても DB には反映しない
///   ・ウィンドウを閉じたら消す。使用中で消せなければ次回起動時に消す（利用者の元ファイルは触らない）
/// </summary>
public sealed class DocumentViewer : IDisposable
{
    public const string VirtualHost = "estimate-docs.local";

    public static string Root { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SimilaritySearchApp", "EstimateDocs");

    /// <summary>このウィンドウ用のフォルダ（仮想ホストに割り当てる）</summary>
    public string Folder { get; } = Path.Combine(Root, Guid.NewGuid().ToString("N"));

    public DocumentViewer() => Directory.CreateDirectory(Folder);

    /// <summary>資料を取り出す。同じ資料は 1 回だけ書き出す。戻り値はファイルのフルパス。</summary>
    public string Extract(long documentId, DocumentRepository.StoredFile file)
    {
        var ext = Path.GetExtension(file.FileName).ToLowerInvariant() is { Length: > 0 and <= 6 } e && e.All(c => char.IsLetterOrDigit(c) || c == '.') ? e : ".bin";
        var path = Path.Combine(Folder, $"doc{documentId}{ext}");
        if (!File.Exists(path))
        {
            File.WriteAllBytes(path, file.Content);
            // 書き出した内容も照合する
            if (!DocumentRepository.Sha256(File.ReadAllBytes(path)).AsSpan().SequenceEqual(file.Hash))
            {
                TryDelete(path);
                throw new InvalidOperationException("資料の取り出しに失敗しました（書き出した内容が一致しません）。");
            }
        }
        return path;
    }

    /// <summary>画面内で開く URL（PDF）。</summary>
    public static string Url(string path, int? page) =>
        $"https://{VirtualHost}/{Uri.EscapeDataString(Path.GetFileName(path))}{(page is > 0 ? $"#page={page}" : "")}";

    /// <summary>関連付けられたアプリ（Excel など）で開く。</summary>
    public static void OpenExternal(string path) => Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(Folder)) Directory.Delete(Folder, recursive: true);
        }
        catch
        {
            // Excel で開いたままなど。次回起動時に消す
        }
    }

    /// <summary>前回までに消せなかった一時フォルダを消す（起動時）。</summary>
    public static void CleanupOld()
    {
        try
        {
            if (!Directory.Exists(Root)) return;
            foreach (var dir in Directory.GetDirectories(Root))
            {
                try
                {
                    if (Directory.GetLastWriteTimeUtc(dir) < DateTime.UtcNow.AddHours(-12)) Directory.Delete(dir, recursive: true);
                }
                catch
                {
                    // 使用中なら次回
                }
            }
        }
        catch
        {
            // 一時フォルダの掃除に失敗してもアプリは続ける
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch
        {
            // 次回起動時に消す
        }
    }
}
