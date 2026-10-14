using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ScaffoldAlert.Batch;
using ScaffoldAlert.Core.Batch;
using ScaffoldAlert.Core.Data;
using ScaffoldAlert.Core.Forecast;
using ScaffoldAlert.Core.Judgement;
using ScaffoldAlert.Core.Weather;

// ============================================================
//  bat から動かす取得と判定
// ============================================================
//  実行の中身は Web アプリの「取得と判定」の画面と同じもの。
//  同じ係(BatchService)を呼んでいるので、片方だけ直して
//  動きがずれる、ということが起きない。
//
//  使い方
//    ScaffoldAlert.Batch.exe                 … 取得して判定まで
//    ScaffoldAlert.Batch.exe fetch           … 取得だけ
//    ScaffoldAlert.Batch.exe install-task    … 定期実行タスクを登録
//    ScaffoldAlert.Batch.exe uninstall-task  … 定期実行タスクを解除
//
//  終了コード
//    0 … うまくいった
//    1 … 途中で止まった (bat 側で気づけるようにするため)
//
//  ★なぜ登録機能をここに入れているか
//    インストーラー(.msi)から呼ぶため。
//
//    はじめは VBScript で登録していたが、Windows 11 24H2 以降は
//    VBScript が非推奨になり、無効になっている環境がある。
//    会社のPCでは実行できないことが多い。
//
//    この exe は .NET ごと1つにまとまっているので、
//    追加で何かを入れる必要がない。ここに持たせるのが確実。
// ============================================================

// 日本語が化けないようにする。
// bat 側でも chcp 65001 を先に実行しておくこと。
Console.OutputEncoding = Encoding.UTF8;

// --- インストーラーから呼ばれる登録・解除 ---
// 取得も判定もせず、タスクの登録だけをして終わる。
if (args.Length > 0)
{
    if (args[0].Equals("install-task", StringComparison.OrdinalIgnoreCase))
    {
        return TaskRegistration.Install();
    }

    if (args[0].Equals("uninstall-task", StringComparison.OrdinalIgnoreCase))
    {
        return TaskRegistration.Uninstall();
    }
}

bool judgeToo = !(args.Length > 0
                  && args[0].Equals("fetch", StringComparison.OrdinalIgnoreCase));

var started = DateTime.Now;

Console.WriteLine(new string('=', 60));
Console.WriteLine(judgeToo
    ? $"  取得と判定  {started:yyyy-MM-dd HH:mm:ss}"
    : $"  取得のみ    {started:yyyy-MM-dd HH:mm:ss}");
Console.WriteLine(new string('=', 60));

// --- 設定を読む ---
// 実行ファイルと同じ場所の appsettings.json を読む。
// bat をどこから呼んでも同じ場所を見るようにするため、
// 実行ファイルの場所を基準にする。
//
// ★接続先はここでしか決まらない
//   プログラム(.cs)にサーバー名は1つも書いていない。
//   つなぐ先を変えるときは、設定ファイルだけを直す。
//
// ★環境ごとに設定を分けられるようにしてある
//   appsettings.json                 … 会社支給PC（開発用）
//   appsettings.Production.json      … 共有PC（本番）
//
//   共有PCのタスクは run_batch_取得と判定.bat から動き、
//   その中で DOTNET_ENVIRONMENT=Production を指定している。
//   そのため共有PCでは Production のほうが上書きで使われる。
//
//   この作りにしておくと、あとでアプリを直して publish し直しても、
//   接続先が開発用に戻ってしまう事故が起きない。
string 環境 = Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT")
              ?? string.Empty;

var configuration = new ConfigurationBuilder()
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: false)
    .AddJsonFile($"appsettings.{環境}.json", optional: true)
    .Build();

Console.WriteLine(string.IsNullOrEmpty(環境)
    ? "  設定        appsettings.json"
    : $"  設定        appsettings.json ＋ appsettings.{環境}.json");

string connectionString =
    configuration.GetConnectionString("ScaffoldAlert") ?? string.Empty;

if (string.IsNullOrWhiteSpace(connectionString))
{
    Console.WriteLine("[NG] appsettings.json に接続先が書かれていません。");
    return 1;
}

// --- 部品を組み立てる ---
// Web アプリの Program と同じ組み合わせにする。
var services = new ServiceCollection();

services.AddSingleton(new DatabaseSettings(connectionString));
services.AddScoped<SqlConnectionFactory>();

// データベースを読み書きする係
services.AddScoped<StationRepository>();
services.AddScoped<ThresholdRepository>();
services.AddScoped<AlertRepository>();
services.AddScoped<EarthquakeRepository>();
services.AddScoped<SiteAreaRepository>();
services.AddScoped<ObservationRepository>();
services.AddScoped<ForecastSaveRepository>();
services.AddScoped<FetchLogRepository>();

// 気象庁から取ってくる係
services.AddHttpClient<IHttpClientFactoryLike, HttpAccess>();
services.AddScoped<EarthquakeFetcher>();
services.AddScoped<PrecipitationFetcher>();
services.AddScoped<WindFetcher>();
services.AddScoped<SnowFetcher>();
services.AddScoped<WeatherService>();
services.AddScoped<FeedReader>();

// 風の予報を取り出す係。
// 以前は降水確率(PopsFetcher)を取っていたが、
// 「警報級の可能性」に降水確率を混ぜると読み違えるためやめた。
// いまは府県天気予報の風の文章を取っている。
services.AddScoped<WindTextFetcher>();

services.AddScoped<ForecastService>();

// 点検義務の判定
services.AddScoped<RainJudge>();
services.AddScoped<WindJudge>();
services.AddScoped<GustJudge>();
services.AddScoped<SnowJudge>();
services.AddScoped<EarthquakeJudge>();
services.AddScoped<JudgeService>();

// とりまとめ
services.AddScoped<BatchService>();

using var provider = services.BuildServiceProvider();
using var scope = provider.CreateScope();

BatchResult result;

try
{
    var batch = scope.ServiceProvider.GetRequiredService<BatchService>();
    result = batch.Run(judgeToo);
}
catch (Exception ex)
{
    Console.WriteLine($"[NG] 実行できませんでした: {ex.Message}");
    Console.WriteLine();
    Console.WriteLine("確認すること");
    Console.WriteLine("  ・SQL Server が動いているか");
    Console.WriteLine("  ・appsettings.json のサーバー名が正しいか");
    Console.WriteLine("  ・このPCからインターネットに出られるか");
    return 1;
}

// --- 結果を出す ---
foreach (var step in result.Steps)
{
    Console.WriteLine();
    Console.WriteLine($"[{(step.Succeeded ? "OK" : "NG")}] {step.Name}"
        + (string.IsNullOrEmpty(step.Summary) ? "" : $" — {step.Summary}"));

    foreach (var line in step.Details)
    {
        Console.WriteLine("     " + line);
    }

    if (!string.IsNullOrEmpty(step.ErrorMessage))
    {
        Console.WriteLine("     理由: " + step.ErrorMessage);
    }
}

Console.WriteLine();
Console.WriteLine(new string('=', 60));

if (result.Succeeded)
{
    Console.WriteLine($"  完了しました (処理時間 {result.Seconds:0.0} 秒)");
    if (result.NewAlerts > 0)
    {
        Console.WriteLine($"  新しいアラート: {result.NewAlerts} 件");
    }
    Console.WriteLine(new string('=', 60));
    return 0;
}

Console.WriteLine("  途中で止まりました。上の [NG] の行を確認してください。");
Console.WriteLine(new string('=', 60));
return 1;
