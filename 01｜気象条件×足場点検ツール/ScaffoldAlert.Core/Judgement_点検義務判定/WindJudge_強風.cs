using Microsoft.Data.SqlClient;
using ScaffoldAlert.Core.Data;

namespace ScaffoldAlert.Core.Judgement;

/// <summary>
/// 強風の判定：10分間平均風速が基準値 (10m/s) 以上か。
/// 労働安全衛生規則 第567条第2項の「強風」に当たるかを見る。
/// </summary>
public class WindJudge : WindJudgeBase
{
    public WindJudge(SqlConnectionFactory factory, AlertRepository alerts)
        : base(factory, alerts) { }

    protected override string EventType => "WIND";
    protected override string SpeedColumn => "MaxWindSpeed";
    protected override string TimeColumn => "MaxWindAt";

    protected override string BuildDetail(decimal value, decimal limit)
        => $"最大風速 {value:F1}m/s (基準 {limit:F0}m/s)";
}
