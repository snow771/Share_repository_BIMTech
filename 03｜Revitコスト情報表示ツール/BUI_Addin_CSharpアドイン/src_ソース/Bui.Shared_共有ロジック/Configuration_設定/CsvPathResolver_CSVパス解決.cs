// このファイルの目的:
//   設定欄が空欄のときに使う既定CSVパスの決め方を、1か所だけで持ちます。
//
// なぜ1か所なのか:
//   起動時（BuiDockPaneComposition.Create）と、設定タブの「切り替えて
//   読み直す」を押したとき（RevitDataSourceConnector）の両方がこの
//   ルールを使います。別々に実装すると、片方だけ直してズレる事故が
//   起きます（2026-09-15のインストーラー作業で、allowlistの二重管理が
//   ズレる不具合を繰り返し踏みました。同じ轍を踏みません）。

namespace Bui.Configuration;

/// <summary>設定欄が空欄のときの既定CSVパスを解決します。</summary>
public static class CsvPathResolver
{
    /// <summary>configuredPathが空欄ならdataRoot配下のfallbackFileNameを、値があればそのまま返します。</summary>
    public static string Resolve(string? configuredPath, string dataRoot, string fallbackFileName) =>
        string.IsNullOrWhiteSpace(configuredPath)
            ? Path.Combine(dataRoot, fallbackFileName)
            : configuredPath;

    /// <summary>単価CSV（Prices.csv）のパスを解決します。configuredPathが空欄ならdataRoot配下の既定ファイルを使います。</summary>
    public static string ResolvePricePath(string? configuredPath, string dataRoot) =>
        Resolve(configuredPath, dataRoot, "Prices.csv");

    /// <summary>構成CSV（Assemblies.csv）のパスを解決します。configuredPathが空欄ならdataRoot配下の既定ファイルを使います。</summary>
    public static string ResolveAssemblyPath(string? configuredPath, string dataRoot) =>
        Resolve(configuredPath, dataRoot, "Assemblies.csv");
}
