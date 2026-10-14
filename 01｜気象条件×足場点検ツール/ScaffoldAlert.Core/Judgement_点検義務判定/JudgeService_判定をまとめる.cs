using Microsoft.Data.SqlClient;
using ScaffoldAlert.Core.Data;

namespace ScaffoldAlert.Core.Judgement;

/// <summary>
/// 5つの判定をまとめて実行する係。
///
/// ★この段階ではデータベースに書き込まない。
///   「もし判定したらこうなる」を返すだけ。
///   Python版と結果が一致することを確認してから、書き込みへ進む。
///   そうすることで、稼働中のPython版に影響を与えずに検証できる。
/// </summary>
public class JudgeService
{
    private readonly ThresholdRepository _thresholds;
    private readonly RainJudge _rain;
    private readonly WindJudge _wind;
    private readonly GustJudge _gust;
    private readonly SnowJudge _snow;
    private readonly EarthquakeJudge _earthquake;

    public JudgeService(
        ThresholdRepository thresholds,
        RainJudge rain,
        WindJudge wind,
        GustJudge gust,
        SnowJudge snow,
        EarthquakeJudge earthquake)
    {
        _thresholds = thresholds;
        _rain = rain;
        _wind = wind;
        _gust = gust;
        _snow = snow;
        _earthquake = earthquake;
    }

    /// <summary>
    /// 判定を実行して、発報の対象になる現場を返す。
    /// Python版 judge_alert.py の main() と同じ順番で呼ぶ。
    /// </summary>
    public JudgeResult Judge()
    {
        var result = new JudgeResult();
        var thresholds = _thresholds.LoadEnabled();

        // 基準がデータベースに無い事象は判定しない。
        // (M_Threshold の IsEnabled を落とせば、その事象だけ止められる)
        if (thresholds.TryGetValue("RAIN", out var rain))
        {
            result.Candidates.AddRange(_rain.Judge(rain));
        }
        if (thresholds.TryGetValue("WIND", out var wind))
        {
            result.Candidates.AddRange(_wind.Judge(wind));
        }
        if (thresholds.TryGetValue("GUST", out var gust))
        {
            result.Candidates.AddRange(_gust.Judge(gust));
        }
        if (thresholds.TryGetValue("SNOW", out var snow))
        {
            result.Candidates.AddRange(_snow.Judge(snow));
        }
        if (thresholds.TryGetValue("EARTHQUAKE", out var eq))
        {
            result.Candidates.AddRange(_earthquake.Judge(eq, result.Warnings));
        }

        return result;
    }

    /// <summary>
    /// 今どの基準で判定しているかを画面に出すために返す。
    /// </summary>
    public Dictionary<string, Threshold> LoadThresholds()
    {
        return _thresholds.LoadEnabled();
    }
}
