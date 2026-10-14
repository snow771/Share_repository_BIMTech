using System.IO;
using System.Runtime.ExceptionServices;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;

namespace SimilaritySearchApp.Ocr.Pdf;

/// <summary>
/// PDF のページ数を数え、指定ページ数ごとに分割する（PDFsharp）。元の PDF は変更しない。
/// Azure の無料枠（F0）は 1 回の解析で先頭 2 ページまでしか読まないため、2 ページずつ送る。
///
/// PDF の解析は、中身の入れ子が深い PDF だと既定のスタック（1MB）を使い切ってアプリごと終了することがある
/// （スタック不足は try/catch で捕まえられない）。そのため大きなスタックの専用スレッドで読む。
/// </summary>
public static class PdfSplitter
{
    /// <summary>分割した1ブロック（ページ番号は元 PDF の 1 始まり）。</summary>
    public sealed record Chunk(int Index, int StartPage, int EndPage, byte[] Bytes)
    {
        public int PageCount => EndPage - StartPage + 1;
    }

    private const int StackSize = 256 * 1024 * 1024;

    public static int CountPages(string path) => OnLargeStack(() =>
    {
        using var doc = Open(path);
        return doc.PageCount;
    });

    /// <summary>メモリ上の PDF のページ数（DB に保存した元PDFの確認用）。</summary>
    public static int CountPages(byte[] pdf) => OnLargeStack(() =>
    {
        try
        {
            using var ms = new MemoryStream(pdf, writable: false);
            using var doc = PdfReader.Open(ms, PdfDocumentOpenMode.Import);
            return doc.PageCount;
        }
        catch (Exception e)
        {
            throw new InvalidOperationException($"PDF を開けませんでした（パスワード付き・破損の可能性があります）: {e.Message}", e);
        }
    });

    /// <summary>chunkPages ページずつに分けた PDF（メモリ上）の一覧。</summary>
    public static List<Chunk> SplitAll(string path, int chunkPages) => OnLargeStack(() => Split(path, chunkPages).ToList());

    /// <summary>大きなスタックのスレッドで実行して結果を返す（例外はそのまま呼び出し元へ）。</summary>
    private static T OnLargeStack<T>(Func<T> work)
    {
        T result = default!;
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try
            {
                result = work();
            }
            catch (Exception e)
            {
                error = e;
            }
        }, StackSize) { IsBackground = true, Name = "PdfReader" };
        thread.Start();
        thread.Join();
        if (error is not null) ExceptionDispatchInfo.Capture(error).Throw();
        return result;
    }

    private static IEnumerable<Chunk> Split(string path, int chunkPages)
    {
        if (chunkPages < 1) throw new ArgumentOutOfRangeException(nameof(chunkPages), "分割ページ数は 1 以上にしてください。");
        using var src = Open(path);
        var total = src.PageCount;
        if (total == 0) throw new InvalidOperationException("PDF にページがありません。");

        var index = 0;
        for (var start = 0; start < total; start += chunkPages)
        {
            var end = Math.Min(start + chunkPages, total);
            using var part = new PdfDocument();
            for (var p = start; p < end; p++) part.AddPage(src.Pages[p]);
            using var ms = new MemoryStream();
            part.Save(ms, false);
            yield return new Chunk(++index, start + 1, end, ms.ToArray());
        }
    }

    private static PdfDocument Open(string path)
    {
        if (!File.Exists(path)) throw new FileNotFoundException($"PDF が見つかりません: {path}");
        try
        {
            return PdfReader.Open(path, PdfDocumentOpenMode.Import);
        }
        catch (Exception e) when (e is not FileNotFoundException)
        {
            throw new InvalidOperationException($"PDF を開けませんでした（パスワード付き・破損の可能性があります）: {Path.GetFileName(path)} / {e.Message}", e);
        }
    }
}
