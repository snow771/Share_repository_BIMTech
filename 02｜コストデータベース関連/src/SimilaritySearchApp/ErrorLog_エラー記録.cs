using System.IO;
using System.Text;

namespace SimilaritySearchApp;

/// <summary>
/// 想定外のエラーを %AppData%\SimilaritySearchApp\error.log に書き残す（原因調査用。詳しい場所＝スタックトレースも残す）。
/// 1MB を超えたら古い内容を error.old.log に移す。書けなくてもアプリは止めない。
/// </summary>
public static class ErrorLog
{
    private static readonly object Lock = new();

    public static string PathName { get; } = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SimilaritySearchApp", "error.log");

    public static void Write(string where, Exception ex)
    {
        try
        {
            lock (Lock)
            {
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(PathName)!);
                if (File.Exists(PathName) && new FileInfo(PathName).Length > 1_000_000)
                    File.Move(PathName, System.IO.Path.ChangeExtension(PathName, ".old.log"), overwrite: true);
                File.AppendAllText(PathName, $"===== {DateTime.Now:yyyy-MM-dd HH:mm:ss} {where}{Environment.NewLine}{ex}{Environment.NewLine}{Environment.NewLine}", Encoding.UTF8);
            }
        }
        catch
        {
            // 記録できなくても続ける
        }
    }
}
