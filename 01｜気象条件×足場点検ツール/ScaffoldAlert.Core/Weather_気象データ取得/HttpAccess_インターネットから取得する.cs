namespace ScaffoldAlert.Core.Weather;

/// <summary>
/// インターネットからファイルを取ってくる機能の窓口。
///
/// 直接 HttpClient を使わずに一段はさんでいるのは、
/// 取得の仕方を後から差し替えられるようにするため。
/// (社内の制限で経由先が必要になった場合など)
/// </summary>
public interface IHttpClientFactoryLike
{
    /// <summary>指定したURLの中身を文字として取ってくる。</summary>
    string GetString(string url);

    /// <summary>
    /// 指定したURLの中身をそのままのバイト列で取ってくる。
    /// 気象庁のCSVは Shift_JIS なので、文字に直す前の形が必要になる。
    /// </summary>
    byte[] GetBytes(string url);
}

/// <summary>
/// 実際にインターネットへ取りに行く実装。
///
/// HttpClient は使い捨てにせず1つを使い回す。
/// 毎回作ると、繋ぎ口を使い切って繋がらなくなることがあるため。
/// </summary>
public class HttpAccess : IHttpClientFactoryLike
{
    private readonly HttpClient _client;

    public HttpAccess(HttpClient client)
    {
        _client = client;
        // 気象庁のファイルは大きいものでも数MB。
        // 応答が返らないまま画面が固まらないよう、待つ時間に上限を置く。
        _client.Timeout = TimeSpan.FromSeconds(60);
    }

    public string GetString(string url)
    {
        var response = _client.GetAsync(url).GetAwaiter().GetResult();
        response.EnsureSuccessStatusCode();
        return response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
    }

    public byte[] GetBytes(string url)
    {
        var response = _client.GetAsync(url).GetAwaiter().GetResult();
        response.EnsureSuccessStatusCode();
        return response.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult();
    }
}
