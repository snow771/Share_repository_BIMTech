using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using ExcelDataReader;

namespace SimilaritySearchApp.Excel;

/// <summary>
/// シート1枚分のセル値（Excel に保存されている計算済みの値。数式は再計算しない＝openpyxl の data_only=True 相当）。
/// 行・列は 1 始まり。
/// </summary>
public sealed class SheetData
{
    private readonly List<object?[]> _rows;

    public SheetData(string name, List<object?[]> rows)
    {
        Name = name;
        _rows = rows;
    }

    public string Name { get; }

    public int RowCount => _rows.Count;

    public int ColumnCount => _rows.Count == 0 ? 0 : _rows.Max(r => r.Length);

    /// <summary>セル値（範囲外・空は null）。</summary>
    public object? this[int row, int col]
    {
        get
        {
            if (row < 1 || row > _rows.Count) return null;
            var r = _rows[row - 1];
            if (col < 1 || col > r.Length) return null;
            var v = r[col - 1];
            return v is DBNull || (v is string s && s.Length == 0) ? null : v;
        }
    }

    /// <summary>"C1" のような番地で取得。番地が不正なら null。</summary>
    public object? Get(string address) =>
        CellAddress.TryParse(address, out var row, out var col) ? this[row, col] : null;

    /// <summary>テスト・変換用: 2次元配列から作る（rows[0] が 1 行目）。</summary>
    public static SheetData FromRows(string name, params object?[][] rows) => new(name, rows.ToList());
}

/// <summary>"C1" / "AT2" ⇔ (行, 列)</summary>
public static class CellAddress
{
    private static readonly Regex Pattern = new(@"^\$?([A-Za-z]{1,3})\$?(\d{1,7})$", RegexOptions.Compiled);

    public static bool TryParse(string? address, out int row, out int col)
    {
        row = col = 0;
        var m = Pattern.Match((address ?? "").Trim());
        if (!m.Success) return false;
        foreach (var ch in m.Groups[1].Value.ToUpperInvariant()) col = col * 26 + (ch - 'A' + 1);
        row = int.Parse(m.Groups[2].Value);
        return row > 0 && col > 0;
    }

    public static string ColumnLetter(int col)
    {
        var s = "";
        while (col > 0)
        {
            var m = (col - 1) % 26;
            s = (char)('A' + m) + s;
            col = (col - 1) / 26;
        }
        return s;
    }
}

/// <summary>
/// Excel（.xlsx / .xlsm / .xls）の読込。ExcelDataReader で保存済みの値を読む（数式の再計算はしない）。
/// Excel で開いたままのファイルも読めるよう、共有モードで開く。
/// </summary>
public static class ExcelWorkbookReader
{
    static ExcelWorkbookReader()
    {
        // .xls（旧形式）の文字コード用
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    /// <summary>指定シート（null なら全シート）を読む。シート名 → 内容。</summary>
    public static Dictionary<string, SheetData> Read(string path, IReadOnlyCollection<string>? sheetNames = null)
    {
        using var stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = ExcelReaderFactory.CreateReader(stream);
        var result = new Dictionary<string, SheetData>(StringComparer.Ordinal);
        do
        {
            var name = reader.Name ?? "";
            if (sheetNames is not null && !sheetNames.Contains(name)) continue;
            var rows = new List<object?[]>();
            while (reader.Read())
            {
                var values = new object?[reader.FieldCount];
                for (var i = 0; i < reader.FieldCount; i++) values[i] = reader.GetValue(i);
                rows.Add(values);
            }
            result[name] = new SheetData(name, TrimTrailingEmptyRows(rows));
        } while (reader.NextResult());
        return result;
    }

    /// <summary>シート名の一覧（先頭から順）。</summary>
    public static List<string> SheetNames(string path)
    {
        using var stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = ExcelReaderFactory.CreateReader(stream);
        var names = new List<string>();
        do names.Add(reader.Name ?? ""); while (reader.NextResult());
        return names;
    }

    private static List<object?[]> TrimTrailingEmptyRows(List<object?[]> rows)
    {
        var last = rows.Count - 1;
        while (last >= 0 && rows[last].All(v => v is null || v is DBNull || (v is string s && s.Trim().Length == 0))) last--;
        return rows.GetRange(0, last + 1);
    }
}
