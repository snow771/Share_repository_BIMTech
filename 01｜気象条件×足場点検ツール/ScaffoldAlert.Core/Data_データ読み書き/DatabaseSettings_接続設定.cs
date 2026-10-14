namespace ScaffoldAlert.Core.Data;

/// <summary>
/// データベースへの接続文字列を持つだけの入れ物。
///
/// 接続先は appsettings.json にだけ書き、ここへ渡す。
/// プログラムの中にサーバー名を直接書かないのは、
/// 24時間稼働PCへ移すときに書き換える場所を1か所にするため。
/// (Python版はサーバー名が8ファイルに散らばっており、
///  移設時に書き換え漏れが起きやすい状態だった)
/// </summary>
public class DatabaseSettings
{
    /// <summary>SQL Server への接続文字列。</summary>
    public string ConnectionString { get; }

    public DatabaseSettings(string connectionString)
    {
        ConnectionString = connectionString;
    }
}
