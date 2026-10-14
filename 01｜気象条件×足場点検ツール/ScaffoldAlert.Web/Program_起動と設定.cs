using ScaffoldAlert.Core.Batch;

using ScaffoldAlert.Core.Data;
using ScaffoldAlert.Core.Forecast;

using ScaffoldAlert.Core.Judgement;
using ScaffoldAlert.Core.Weather;

// ============================================================
//  アプリの起動と設定
// ============================================================
//  ここが一番最初に動く場所。
//  「どんな部品を使うか」を登録してから、アプリを起動する。
// ============================================================

var builder = WebApplication.CreateBuilder(args);

// --- Windows のサービスとして動けるようにする ---
// 共有PCではサービスとして登録し、常時待ち受ける。
// 誰かがログインしていなくても動き、PCを再起動しても自動で戻る。
//
// ふだんの開発 (Visual Studio の緑ボタン) では、
// サービスとして動いていないことをこれが自分で見分けるので、
// 書いたままで困らない。
builder.Host.UseWindowsService();

// --- 画面(コントローラーとビュー)を使えるようにする ---
builder.Services.AddControllersWithViews();

// --- データベースの設定を読み込む ---
// appsettings.json の ConnectionStrings:ScaffoldAlert を読む。
// ここが唯一の接続先の指定。プログラムの他の場所には書かない。
var connectionString =
    builder.Configuration.GetConnectionString("ScaffoldAlert") ?? string.Empty;

// --- 部品を登録する ---
// AddSingleton … アプリ全体で1つだけ作る (設定のように変わらないもの)
// AddScoped    … 画面を1回表示するたびに作る (データベースを読む係など)
builder.Services.AddSingleton(new DatabaseSettings(connectionString));
builder.Services.AddScoped<SqlConnectionFactory>();

// データベースを読む係
builder.Services.AddScoped<SiteRepository>();

builder.Services.AddScoped<DashboardRepository>();

builder.Services.AddScoped<SiteDetailRepository>();

builder.Services.AddScoped<InspectionRepository>();

builder.Services.AddScoped<ObservationRepository>();

builder.Services.AddScoped<ForecastSaveRepository>();

builder.Services.AddScoped<FetchLogRepository>();
builder.Services.AddScoped<ThresholdRepository>();
builder.Services.AddScoped<AlertRepository>();
builder.Services.AddScoped<EarthquakeRepository>();
builder.Services.AddScoped<StationRepository>();

builder.Services.AddScoped<SiteAreaRepository>();

builder.Services.AddScoped<ForecastViewRepository>();

// 気象庁からデータを取ってくる係
// AddHttpClient を使うと、繋ぎ口を使い回す仕組みが用意される。
// 毎回新しく作ると、繋ぎ口を使い切って繋がらなくなることがある。
builder.Services.AddHttpClient<IHttpClientFactoryLike, HttpAccess>();
builder.Services.AddScoped<EarthquakeFetcher>();
builder.Services.AddScoped<PrecipitationFetcher>();
builder.Services.AddScoped<WindFetcher>();
builder.Services.AddScoped<SnowFetcher>();
builder.Services.AddScoped<WeatherService>();



// 気象予測を取ってくる係

builder.Services.AddScoped<FeedReader>();

builder.Services.AddScoped<WindTextFetcher>();

builder.Services.AddScoped<ForecastService>();



// 区域の形は数MBあり、年に何度も変わらない。

// アプリ全体で1つだけ作り、一度取ってきたら覚えておく。

builder.Services.AddSingleton<AreaGeoJsonCache>();

// 点検義務の判定 (事象ごとに1つ)
builder.Services.AddScoped<RainJudge>();
builder.Services.AddScoped<WindJudge>();
builder.Services.AddScoped<GustJudge>();
builder.Services.AddScoped<SnowJudge>();
builder.Services.AddScoped<EarthquakeJudge>();
builder.Services.AddScoped<JudgeService>();



// 取得と判定をまとめて動かす係

builder.Services.AddScoped<BatchService>();

var app = builder.Build();

// --- エラーの見せ方 ---
// 開発中は詳しい内容を画面に出す。原因を突き止めやすくするため。
//
// ★デモ版でも詳しい内容を出す
//   デモ版は社内で意見をもらうために配るもの。
//   「なにか失敗しました」だけだと、受け取った人が
//   何を伝えればよいのか分からず、こちらも直しようがない。
//   本物のデータは1件も入っていないので、
//   中身が画面に出ても差し支えない。
//
//   共有PC(Production)では、これまでどおり詳細を隠す。
bool 詳しく出す = app.Environment.IsDevelopment()
                  || app.Environment.IsEnvironment("Demo");

if (!詳しく出す)
{
    app.UseExceptionHandler("/Home/Error");
}

// --- CSS や画像を返せるようにする (wwwroot フォルダの中身) ---
app.UseStaticFiles();

app.UseRouting();

// --- どのURLでどの画面を出すか ---
// 例: http://localhost:5080/            → HomeController の Index
//     http://localhost:5080/Judgement   → JudgementController の Index
app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();
