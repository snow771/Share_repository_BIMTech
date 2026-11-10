// このファイルはRevitリボンにBUIタブ、BUIパネル、利用者向けコマンドボタンを作成します。
// Revit UI APIと各IExternalCommandの完全修飾名に依存し、名前を変えるとリボン表示やコマンド起動に影響します。
//
// なぜアイコンを設定するのか:
//   文字間隔を直接指定するAPIはありません。アイコンが無いとボタンが小文字幅のまま並び、
//   文字が詰まって見えます。LargeImageを設定するとボタンが大ボタン形式になり、間隔が空きます。
//   つまりアイコン設置が、文字間隔の対策そのものです。
//
// 修正時の影響:
//   アイコンが読めなくてもボタンは必ず作ってください。ここで例外を投げると
//   BUIのボタンが1つも出ず、利用者にはアドインが壊れたようにしか見えません。
using System.IO;
using System.Reflection;
using System.Windows.Media.Imaging;
using Autodesk.Revit.UI;
using Bui.Revit;
using Bui.Revit.FinishSchedules;

namespace Bui.Application;

/// <summary>BUIリボンを一貫した名前で作成します。</summary>
internal static class RibbonService
{
    private const string TabName = "BUI";
    private const string PanelName = "BUI";
    private const string InstallIdentifiersCommandClassName =
        "Bui.Revit.SharedParameters.InstallIdentifiersCommand";

    private const string ResourceFolderName = "Resources_画像と定義";

    public static void Create(UIControlledApplication application)
    {
        application.CreateRibbonTab(TabName);
        var panel = application.CreateRibbonPanel(TabName, PanelName);
        var assemblyPath = Assembly.GetExecutingAssembly().Location;

        var showPaneData = new PushButtonData(
            "BuiShowPane",
            "パネル表示",
            assemblyPath,
            typeof(ShowBuiPaneCommand).FullName!);
        var showPaneButton = panel.AddItem(showPaneData) as PushButton
            ?? throw new InvalidOperationException("パネル表示ボタンを作成できませんでした。");
        showPaneButton.ToolTip = "BUIのドッキングパネルを表示します。";
        ApplyIcon(showPaneButton, "リボン_01_パネル表示.ico");

        panel.AddSeparator();

        // リボンから安定した完全修飾名で、明示操作のID初期設定コマンドを呼び出します。
        var installIdentifiersData = new PushButtonData(
            "BuiInstallIdentifiers",
            "パラメータ作成",
            assemblyPath,
            InstallIdentifiersCommandClassName);
        var installIdentifiersButton = panel.AddItem(installIdentifiersData) as PushButton
            ?? throw new InvalidOperationException("パラメータ作成ボタンを作成できませんでした。");
        installIdentifiersButton.ToolTip = "Product_IDとAssembly_IDの共有パラメータを初期設定します。";
        ApplyIcon(installIdentifiersButton, "リボン_02_パラメータ作成.ico");

        panel.AddSeparator();

        // AIチャットは別ウィンドウで開きます。BUIパネルは細長く、会話には狭いためです。
        var askAiData = new PushButtonData(
            "BuiAskAi",
            "AI",
            assemblyPath,
            typeof(AskBuiAiCommand).FullName!);
        var askAiButton = panel.AddItem(askAiData) as PushButton
            ?? throw new InvalidOperationException("AIボタンを作成できませんでした。");
        askAiButton.ToolTip = "自然言語で建材を検索・比較・提案できるチャットウィンドウを開きます。";
        ApplyIcon(askAiButton, "リボン_03_AI.ico");

        panel.AddSeparator();

        var exportData = new PushButtonData(
            "BuiExportFinishSchedule",
            "仕上表出力",
            assemblyPath,
            typeof(ExportFinishScheduleCommand).FullName!);
        var exportButton = panel.AddItem(exportData) as PushButton
            ?? throw new InvalidOperationException("仕上表出力ボタンを作成できませんでした。");
        exportButton.ToolTip = "部屋とProduct_IDから内部・外部仕上表をExcelへ出力します。";
        ApplyIcon(exportButton, "リボン_04_仕上表出力.ico");
    }

    /// <summary>
    /// ボタンへアイコンを設定します。読めなければ何もしません。
    /// 例外を投げるとリボンが作られず、BUIのボタンが1つも出なくなるためです。
    /// LargeImage（32×32）だけでなくImage（16×16）も設定します。
    /// Imageが無いと、リボンが縮んだ状態でアイコンが消えます。
    /// </summary>
    private static void ApplyIcon(PushButton button, string fileName)
    {
        try
        {
            var assemblyDirectory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            if (assemblyDirectory is null) return;

            var path = Path.Combine(assemblyDirectory, ResourceFolderName, fileName);
            if (!File.Exists(path)) return;

            button.LargeImage = LoadIcon(path, 32);
            button.Image = LoadIcon(path, 16);
        }
        catch (Exception)
        {
            // アイコンが無くてもボタンは使えます。ここで止めません。
        }
    }

    private static BitmapImage LoadIcon(string path, int pixelWidth)
    {
        var image = new BitmapImage();
        image.BeginInit();
        // ファイルを掴んだままにすると、次のビルドで上書きできなくなります。
        // WPFの既定手順どおりCacheOptionをUriSourceより先に設定します。逆順だと
        // OnLoadが効かずファイルが開いたままになることがあります。
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.UriSource = new Uri(path, UriKind.Absolute);
        image.DecodePixelWidth = pixelWidth;
        image.EndInit();
        image.Freeze();
        return image;
    }
}
