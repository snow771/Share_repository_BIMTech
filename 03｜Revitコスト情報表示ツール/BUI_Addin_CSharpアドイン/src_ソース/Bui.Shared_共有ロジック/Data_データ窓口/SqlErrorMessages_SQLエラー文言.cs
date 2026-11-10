// このファイルの目的:
//   SQL Serverのエラー番号を、利用者が次に何をすればよいか分かる日本語にします。
//
// なぜ原文を併記するのか:
//   接続まわりは会社PCでしか確かめられません。言い換えた文だけを出すと、
//   言い換えが外れていたときに原因へ辿り着けなくなります。
//   言い換えと原文の両方を出せば、どちらかで届きます。
//
// 修正時の影響:
//   接続文字列は絶対に出さないでください。ここへ渡してよいのは
//   利用者が入力したデータベース名までです。

namespace Bui.Data;

/// <summary>SQL Serverのエラーを利用者向けの日本語にします。</summary>
public static class SqlErrorMessages
{
    public static string Describe(int errorNumber, string databaseName, string originalMessage)
    {
        var database = (databaseName ?? string.Empty).Trim();
        var advice = errorNumber switch
        {
            2 or 53 or 258 or -2 =>
                "SQL Serverに接続できませんでした。"
                + "インスタンス名が正しいか、SQL Server Express が起動しているかを確認してください。",
            4060 =>
                $"データベース「{database}」を開けません。"
                + "名前が正しいか、このWindowsユーザーに参照権限があるかを確認してください。",
            18456 =>
                "このWindowsユーザーではログインできません。"
                + "データベースへの参照権限を確認してください。",
            _ => "SQL Serverへの接続に失敗しました。"
        };

        var original = (originalMessage ?? string.Empty).Trim();
        return original.Length == 0
            ? advice
            : advice + Environment.NewLine + "SQL Serverからの応答: " + original;
    }
}
