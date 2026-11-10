// このファイルは、利用者ごとの保存先(%LOCALAPPDATA%\PoC\BUI)を一か所で定義します。
// 個別設定・認証情報・ログ・既定の構成CSV・DB管理ツールの作業用データがすべてここを使います。
// フォルダ名を変えると既存利用者の設定とログが見えなくなるため、変更時は移行手順も案内してください。
namespace Bui.Configuration;

public static class BuiLocalPaths
{
    /// <summary>
    /// %LOCALAPPDATA%直下のフォルダ名です。他社の利用者にも配るため、特定の企業名は入れません。
    /// </summary>
    public const string VendorFolderName = "PoC";

    public const string ProductFolderName = "BUI";

    /// <summary>%LOCALAPPDATA%\PoC\BUI の絶対パスです。</summary>
    public static string Root => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        VendorFolderName,
        ProductFolderName);

    /// <summary>%LOCALAPPDATA%\PoC\BUI\Logs の絶対パスです。</summary>
    public static string Logs => Path.Combine(Root, "Logs");
}
