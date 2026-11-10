// このファイルの目的:
//   仮登録レビューツールが作業中に持つ1件分の状態です。
//   System.Text.Jsonでそのまま読み書きできるよう、単純な可変クラスにします
//   （record + 位置引数コンストラクタだと、JSON側のキー名との対応で
//   余計な注意が必要になるため、プロパティ名だけで素直に対応させます）。

namespace Bui.Curation;

/// <summary>1件の仮登録レビュー項目です。</summary>
public sealed class CurationQueueItem
{
    public string SourceFolder { get; set; } = string.Empty;
    public string ProvisionalId { get; set; } = string.Empty;
    public Dictionary<string, string> Values { get; set; } = new();
    public List<Dictionary<string, string>> SpecRows { get; set; } = new();
    public bool IsCompleted { get; set; }
    public string? OfficialId { get; set; }
    public List<string> Warnings { get; set; } = new();
}
