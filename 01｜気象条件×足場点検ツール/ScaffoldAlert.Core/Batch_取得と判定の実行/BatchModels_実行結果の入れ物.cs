namespace ScaffoldAlert.Core.Batch;

/// <summary>1つの手順の結果。</summary>
public class BatchStep
{
    /// <summary>手順の名前 (気象データの取得 など)。</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>うまくいったかどうか。</summary>
    public bool Succeeded { get; set; }

    /// <summary>画面に出す1行の要約。</summary>
    public string Summary { get; set; } = string.Empty;

    /// <summary>詳しい中身。折りたたんで出す。</summary>
    public List<string> Details { get; set; } = new();

    /// <summary>うまくいかなかったときの理由。</summary>
    public string? ErrorMessage { get; set; }

    /// <summary>この手順で扱った件数。呼び出し元が使う。</summary>
    public int Count { get; set; }
}

/// <summary>取得と判定をひととおり動かした結果。</summary>
public class BatchResult
{
    /// <summary>始めた時刻。</summary>
    public DateTime StartedAt { get; set; } = DateTime.Now;

    /// <summary>かかった時間 (秒)。</summary>
    public double Seconds { get; set; }

    /// <summary>手順ごとの結果。</summary>
    public List<BatchStep> Steps { get; set; } = new();

    /// <summary>すべてうまくいったかどうか。</summary>
    public bool Succeeded => Steps.All(s => s.Succeeded);

    /// <summary>新しく出たアラートの件数。</summary>
    public int NewAlerts { get; set; }
}
