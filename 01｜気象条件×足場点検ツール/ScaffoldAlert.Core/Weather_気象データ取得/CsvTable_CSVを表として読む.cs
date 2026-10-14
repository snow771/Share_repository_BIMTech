using System.Text;

namespace ScaffoldAlert.Core.Weather;

/// <summary>
/// 気象庁のCSVを、列名で引ける表として読む。
///
/// 気象庁のCSVは Shift_JIS (cp932) で配信されている。
/// UTF-8 として読むと文字化けするため、必ず指定して読む。
///
/// 列の順番や名前は気象庁の都合で変わることがあるため、
/// 列番号ではなく列名で引けるようにしている。
/// </summary>
public class CsvTable
{
    /// <summary>1行目に書かれている列名。</summary>
    public List<string> Columns { get; } = new();

    /// <summary>2行目以降のデータ。</summary>
    public List<string[]> Rows { get; } = new();

    /// <summary>列名から、その列が何番目かを引く表。</summary>
    private readonly Dictionary<string, int> _index = new();

    /// <summary>
    /// Shift_JIS のバイト列を読み込んで表にする。
    /// </summary>
    public static CsvTable Parse(byte[] shiftJisBytes)
    {
        // .NET は既定で Shift_JIS を扱えないため、使えるように登録する。
        // 登録は何度呼んでも問題ない。
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var text = Encoding.GetEncoding(932).GetString(shiftJisBytes);

        var table = new CsvTable();
        var lines = text.Split('\n');

        bool first = true;
        foreach (var raw in lines)
        {
            var line = raw.TrimEnd('\r');
            if (line.Length == 0)
            {
                continue;
            }

            var cells = SplitLine(line);

            if (first)
            {
                first = false;
                for (int i = 0; i < cells.Length; i++)
                {
                    string name = cells[i].Trim();
                    table.Columns.Add(name);
                    // 同じ名前の列があっても、最初のものを採る。
                    if (!table._index.ContainsKey(name))
                    {
                        table._index[name] = i;
                    }
                }
            }
            else
            {
                table.Rows.Add(cells);
            }
        }

        return table;
    }

    /// <summary>
    /// 1行をカンマで分ける。二重引用符で囲まれた中のカンマは区切りにしない。
    /// 気象庁のCSVは地点名に「、」を含むことがあるため。
    /// </summary>
    private static string[] SplitLine(string line)
    {
        var cells = new List<string>();
        var current = new StringBuilder();
        bool inQuote = false;

        foreach (char c in line)
        {
            if (c == '"')
            {
                inQuote = !inQuote;
            }
            else if (c == ',' && !inQuote)
            {
                cells.Add(current.ToString());
                current.Clear();
            }
            else
            {
                current.Append(c);
            }
        }
        cells.Add(current.ToString());

        return cells.ToArray();
    }

    /// <summary>
    /// キーワードをすべて含む列名を探す。見つからなければ空を返す。
    ///
    /// 「12時間降水量」「現在値」のように複数語で指定する。
    /// 列名が少し変わっても動くようにするため、完全一致では探さない。
    /// </summary>
    public string? FindColumn(params string[] keywords)
    {
        foreach (var name in Columns)
        {
            bool all = true;
            foreach (var k in keywords)
            {
                if (!name.Contains(k))
                {
                    all = false;
                    break;
                }
            }
            if (all)
            {
                return name;
            }
        }
        return null;
    }

    /// <summary>列名が表にあるか。</summary>
    public bool HasColumn(string name) => _index.ContainsKey(name);

    /// <summary>指定した行の、指定した列名の値を取り出す。</summary>
    public string? Value(string[] row, string? columnName)
    {
        if (columnName is null || !_index.TryGetValue(columnName, out int i))
        {
            return null;
        }
        return i < row.Length ? row[i].Trim() : null;
    }

    /// <summary>指定した行の、1列目(観測所番号)を取り出す。</summary>
    public string FirstCell(string[] row)
        => row.Length > 0 ? row[0].Trim() : string.Empty;
}
