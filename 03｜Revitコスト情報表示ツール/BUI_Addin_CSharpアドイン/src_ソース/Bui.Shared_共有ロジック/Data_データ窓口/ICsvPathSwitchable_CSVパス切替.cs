// このファイルの目的:
//   「内部で読んでいるCSVのパスを、実体を作り直さずに差し替えられる」
//   という契約を、単価・構成の両方の窓口クラスで共有します。
//
// なぜ必要か:
//   RevitDataSourceConnector（Bui.Addin側）は、単価・構成それぞれの
//   具象クラスを知る必要なく「パスを渡して切り替える」操作だけを
//   呼べれば十分です。具象型への依存を増やさないためのインターフェイスです。

namespace Bui.Data;

/// <summary>内部で読んでいるCSVのパスを、実体を作り直さずに差し替えられることを表します。</summary>
public interface ICsvPathSwitchable
{
    /// <summary>読み込み先を新しいパスへ切り替えます。次回の読み込みから新しいパスを使います。</summary>
    void SwitchCsvPath(string csvPath);
}
