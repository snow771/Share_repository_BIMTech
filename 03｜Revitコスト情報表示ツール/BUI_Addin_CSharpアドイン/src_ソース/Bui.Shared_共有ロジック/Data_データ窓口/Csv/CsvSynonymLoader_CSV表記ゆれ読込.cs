// このファイルの目的:
//   表記ゆれ.csv を読み、SynonymTable にします。
//
// なぜ読めなくても止めないのか:
//   表記ゆれは検索の精度を上げる補助であり、無くても検索は動きます。
//   ここで例外を投げると、CSVの1行のせいでチャットが使えなくなります。
//   読めなかった理由は画面へ出し、動きは続けます。
//
// 修正時の影響:
//   ヘッダーは 言い方,項目,値 の3列です。変えると既存の表が読めなくなります。

using System.IO;

namespace Bui.Data.Csv;

/// <summary>表記ゆれ.csv を読み込みます。読めなくても空の表を返します。</summary>
public sealed class CsvSynonymLoader
{
    public IReadOnlyList<string> LastProblems { get; private set; } = Array.Empty<string>();

    public SynonymTable Load(string? path)
    {
        LastProblems = Array.Empty<string>();

        // 未設定は異常ではありません。表を使わないだけです。
        if (string.IsNullOrWhiteSpace(path)) return SynonymTable.Empty;
        if (!File.Exists(path)) return SynonymTable.Empty;

        try
        {
            var document = CsvDocumentCodec.Read(path);
            if (document.Headers.Count < 3
                || !string.Equals(document.Headers[0], "言い方", StringComparison.Ordinal)
                || !string.Equals(document.Headers[1], "項目", StringComparison.Ordinal)
                || !string.Equals(document.Headers[2], "値", StringComparison.Ordinal))
            {
                LastProblems = new[] { "表記ゆれCSVの見出しは 言い方,項目,値 にしてください。" };
                return SynonymTable.Empty;
            }

            var entries = new List<SynonymEntry>();
            var problems = new List<string>();
            foreach (var row in document.Rows)
            {
                var spoken = row["言い方"].Trim();
                var column = row["項目"].Trim();
                var value = row["値"].Trim();

                if (spoken.Length == 0 || value.Length == 0)
                {
                    problems.Add($"表記ゆれCSVに、言い方か値が空の行があります（言い方: {spoken}）。");
                    continue;
                }

                entries.Add(new SynonymEntry(spoken, column, value));
            }

            LastProblems = problems;
            return new SynonymTable(entries);
        }
        catch (Exception)
        {
            // 例外メッセージにはパスが混じり得るため、画面へは定型文だけを出します。
            LastProblems = new[] { "表記ゆれCSVを読み取れませんでした。ファイルの形式を確認してください。" };
            return SynonymTable.Empty;
        }
    }
}
