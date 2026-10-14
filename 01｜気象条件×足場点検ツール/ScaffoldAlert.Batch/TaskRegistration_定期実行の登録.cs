using System.Diagnostics;

namespace ScaffoldAlert.Batch;

/// <summary>
/// 定期実行タスクの登録・解除をする係。
///
/// ★なぜ exe の中にあるのか
///   インストーラー(.msi)から呼ぶため。
///
///   Visual Studio の Setup Project は、ファイルを置くことは
///   できるが、タスクスケジューラへの登録ができない。
///   その部分を補うものが要る。
///
///   はじめは VBScript で書いていたが、Windows 11 24H2 以降は
///   VBScript が非推奨になり、無効化されている環境がある。
///   会社のPCでは実行できないことが多い。
///
///   この exe は .NET ごと1つにまとまっているので、
///   追加で何かを入れる必要がない。ここに持たせるのが確実。
///
/// ★手でも使える
///   ScaffoldAlert.Batch.exe install-task
///   ScaffoldAlert.Batch.exe uninstall-task
///   管理者として実行したコマンドプロンプトから叩けば、
///   インストーラーを使わずに登録できる。
/// </summary>
public static class TaskRegistration
{
    /// <summary>
    /// タスクの名前。
    ///
    /// 英数字だけにしてある。インストーラーから schtasks へ
    /// 渡すときに、日本語だとPCの文字コード設定に左右されるため。
    /// </summary>
    public const string TaskName = "ScaffoldAlert_FetchAndJudge";

    /// <summary>
    /// 1日の最初の実行時刻。
    ///
    /// ★なぜ 00:00 ではなく 00:10 なのか
    ///   気象庁は毎正時の観測値を数分あとに公開する。
    ///   00:00 ちょうどに取りに行くと、1つ前の値しか取れない。
    /// </summary>
    public const string StartTime = "00:10";

    /// <summary>
    /// 何時間ごとに動かすか。
    ///
    /// 気象庁が1時間に1回しか更新しないので、1で十分。
    /// 短くしても同じ値を取り直すだけ。
    /// </summary>
    public const int IntervalHours = 1;

    /// <summary>タスクスケジューラに登録する。</summary>
    /// <returns>0 = 成功、1 = 失敗。</returns>
    public static int Install()
    {
        // タスクから呼ぶのは exe ではなく bat。
        // bat が「共有PCの設定を使う」指定とログ出力をしてから
        // exe を呼ぶ作りになっているため。
        string folder = AppContext.BaseDirectory;
        string batPath = Path.Combine(folder, "run_batch.bat");

        Console.WriteLine("定期実行タスクを登録します。");
        Console.WriteLine($"  タスク名 : {TaskName}");
        Console.WriteLine($"  動かすもの: {batPath}");
        Console.WriteLine($"  時刻     : 毎日 {StartTime} から {IntervalHours}時間ごと");
        Console.WriteLine();

        if (!File.Exists(batPath))
        {
            Console.WriteLine($"[NG] {batPath} が見つかりません。");
            Console.WriteLine("     同じフォルダに run_batch.bat が必要です。");
            return 1;
        }

        // 入れ直しを安全にするため、古い登録を先に消す。
        // 初回は「無い」と言われるが、それでよいので結果は見ない。
        Run("schtasks.exe", $"/delete /tn \"{TaskName}\" /f");

        // /ru SYSTEM  … 誰もログインしていなくても動く
        // /rl HIGHEST … 最上位の特権で動く
        // /f          … 同じ名前があれば置き換える
        int code = Run("schtasks.exe",
            $"/create /tn \"{TaskName}\" /tr \"\\\"{batPath}\\\"\" "
            + $"/sc hourly /mo {IntervalHours} /st {StartTime} "
            + "/ru SYSTEM /rl HIGHEST /f");

        if (code != 0)
        {
            Console.WriteLine();
            Console.WriteLine($"[NG] 登録できませんでした (schtasks が {code} を返しました)。");
            Console.WriteLine("     管理者として実行しているか確認してください。");
            return 1;
        }

        Console.WriteLine();
        Console.WriteLine("[OK] 登録しました。");
        return 0;
    }

    /// <summary>タスクスケジューラから消す。</summary>
    /// <returns>常に 0。</returns>
    /// <remarks>
    /// 失敗しても 0 を返す。
    /// すでに手で消されていることもあり、そこで止めると
    /// アンインストールが途中で終わってファイルだけ残るため。
    /// </remarks>
    public static int Uninstall()
    {
        Console.WriteLine($"定期実行タスク {TaskName} を解除します。");

        Run("schtasks.exe", $"/delete /tn \"{TaskName}\" /f");

        Console.WriteLine("[OK] 解除しました。");
        return 0;
    }

    /// <summary>
    /// 外のコマンドを動かして、終了コードを返す。
    ///
    /// 画面を出さずに動かす。インストーラーから呼ばれたときに
    /// 黒い窓が一瞬出るのを防ぐため。
    /// </summary>
    private static int Run(string fileName, string arguments)
    {
        var info = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        try
        {
            using var process = Process.Start(info);
            if (process is null)
            {
                return -1;
            }

            // 出したものは、あとで原因を追えるように残す。
            string output = process.StandardOutput.ReadToEnd();
            string error = process.StandardError.ReadToEnd();

            process.WaitForExit();

            if (!string.IsNullOrWhiteSpace(output))
            {
                Console.WriteLine(output.Trim());
            }

            if (!string.IsNullOrWhiteSpace(error))
            {
                Console.WriteLine(error.Trim());
            }

            return process.ExitCode;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[NG] {fileName} を動かせませんでした: {ex.Message}");
            return -1;
        }
    }
}
