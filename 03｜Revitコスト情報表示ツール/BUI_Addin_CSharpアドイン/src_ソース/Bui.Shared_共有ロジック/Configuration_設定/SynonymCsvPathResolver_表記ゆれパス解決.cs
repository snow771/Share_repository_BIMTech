// このファイルの目的:
//   表記ゆれ.csv のパスを決めます。SynonymCsvPathが未設定のときは、
//   データフォルダ配下の 表記ゆれ.csv を既定として使います。
//
// なぜ必要か:
//   利用者に設定項目を1つ増やさせないためです。製品CSVと同じフォルダへ
//   表記ゆれ.csv を置くだけで使えるようにし、パスの入力を求めません。
//   ファイルが実在するかはここでは確かめません（CsvSynonymLoader.Loadの仕事です）。
//   未設定・実在しないの両方とも、この関数は「使うべきパス」を返すだけで、
//   その先の判断（第2層を飛ばすかどうか）はLoad側に委ねます。
//
// 修正時の影響:
//   Bui.Addinはこのマシンでコンパイルできないため、この解決ロジックを
//   Bui.Addin側に置くと単体テストで固定できません。必ずここ（Bui.Shared）に置き、
//   Bui.Addin側は結果を渡すだけの配線にしてください。

namespace Bui.Configuration;

/// <summary>表記ゆれ.csv の既定パスを解決します。</summary>
public static class SynonymCsvPathResolver
{
    private const string DefaultFileName = "表記ゆれ.csv";

    /// <summary>
    /// 設定済みのパスがあればそれを使い、無ければデータフォルダ配下の既定ファイル名を返します。
    /// データフォルダも無ければ解決できないためnullを返します（CsvSynonymLoader.Loadはnullでも
    /// 例外を投げず、第2層を飛ばして続行します）。
    /// </summary>
    public static string? Resolve(string? configuredPath, string dataFolderPath)
    {
        if (!string.IsNullOrWhiteSpace(configuredPath)) return configuredPath;
        if (string.IsNullOrWhiteSpace(dataFolderPath)) return null;
        return Path.Combine(dataFolderPath, DefaultFileName);
    }
}
