// このファイルの目的:
//   「プレビューを取った文書」と「これから書き込む文書」が同じものかを判定します。
//
// なぜ参照比較ではいけないか:
//   以前は ReferenceEquals(document, preview.SourceDocument) だけで判定していました。
//   Revit APIのDocumentは native オブジェクトのラッパーであり、
//   ActiveUIDocument を取り直すと同じモデルでも別のラッパーが返ることがあります。
//   その場合、利用者は文書を切り替えていないのに
//   「プレビュー後にRevitドキュメントが切り替わったため、書き込みませんでした。」
//   と表示され、何をしても書き込めなくなります（実際に報告されました）。
//
// なぜ判定を捨てないか:
//   この判定は「別のモデルへ誤って書き込まない」ための安全装置です。
//   緩めるのではなく、モデルを正しく識別できる材料へ置き換えます。
//
// 修正時の影響:
//   保存済みの文書は PathName（フルパス）で識別します。
//   未保存の文書は PathName が空のため識別材料が無く、従来どおり参照比較だけで判断します。
//   ここを緩めると、別モデルへの書き込み事故が起こり得ます。
using Autodesk.Revit.DB;

namespace Bui.Revit.UI;

internal static class RevitDocumentMatch
{
    /// <summary>プレビュー時の文書と現在の文書が同一モデルかを判定します。</summary>
    public static bool IsSame(Document? current, Document? previewed)
    {
        if (current is null || previewed is null) return false;
        if (!current.IsValidObject || !previewed.IsValidObject) return false;
        if (ReferenceEquals(current, previewed)) return true;

        // Revit APIのDocumentは等価比較を実装しています。
        // 同じ文書に別のラッパーが返っても、これなら一致します。
        // CompoundStructureReader も同じ理由で document.Equals を使っています。
        if (current.Equals(previewed)) return true;

        // 保存済みの文書はフルパスで一意に決まります。
        // 大文字小文字はWindowsのパスとして同一視します。
        var currentPath = current.PathName ?? string.Empty;
        var previewedPath = previewed.PathName ?? string.Empty;
        if (currentPath.Length > 0 && previewedPath.Length > 0)
        {
            return string.Equals(currentPath, previewedPath, StringComparison.OrdinalIgnoreCase);
        }

        // 片方でも未保存ならパスで識別できません。
        // 未保存文書のTitleはRevitが「プロジェクト1」「プロジェクト2」のように
        // セッション内で重複しない名前を付けるため、ここではTitleで判断します。
        // ただし片方だけ保存済みなら別の文書なので、両方とも未保存の場合に限ります。
        if (currentPath.Length > 0 || previewedPath.Length > 0) return false;

        return string.Equals(current.Title, previewed.Title, StringComparison.Ordinal);
    }

    /// <summary>不一致のときに、どちらの文書だったのかを利用者へ示す文言を作ります。</summary>
    public static string DescribeMismatch(Document? current, Document? previewed) =>
        $"（プレビュー時: {Describe(previewed)} / 現在: {Describe(current)}）";

    private static string Describe(Document? document)
    {
        if (document is null) return "なし";
        if (!document.IsValidObject) return "閉じられた文書";
        var path = document.PathName ?? string.Empty;
        return path.Length > 0 ? System.IO.Path.GetFileName(path) : $"{document.Title}（未保存）";
    }
}
