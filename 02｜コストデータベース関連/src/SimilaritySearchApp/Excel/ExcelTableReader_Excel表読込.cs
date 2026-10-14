using System.IO;
using SimilaritySearchApp.Services;

namespace SimilaritySearchApp.Excel;

/// <summary>Excel の1シートを「1行目＝見出し、2行目以降＝データ」の表として読んだ結果。値は画面用の文字列。</summary>
public sealed record ExcelTable(string Path, List<string> Sheets, string Sheet, List<string> Headers, List<string?[]> Rows);

/// <summary>DB編集の「Excel取込」用。指定シート（省略時は先頭）を表として読む。</summary>
public static class ExcelTableReader
{
    public sealed class Request
    {
        public string Path { get; set; } = "";
        public string? Sheet { get; set; }
    }

    public static ExcelTable Read(string path, string? sheet)
    {
        var names = ExcelWorkbookReader.SheetNames(path);
        if (names.Count == 0) throw new InvalidDataException("シートがありません。");
        var target = !string.IsNullOrEmpty(sheet) && names.Contains(sheet) ? sheet : names[0];
        var data = ExcelWorkbookReader.Read(path, new[] { target })[target];

        var width = data.ColumnCount;
        var headers = Enumerable.Range(1, width)
            .Select(c => DbValueConverter.Format(data[1, c])?.Trim() is { Length: > 0 } h ? h : $"列{CellAddress.ColumnLetter(c)}")
            .ToList();
        var rows = new List<string?[]>();
        for (var r = 2; r <= data.RowCount; r++)
        {
            var row = new string?[width];
            var any = false;
            for (var c = 1; c <= width; c++)
            {
                row[c - 1] = DbValueConverter.Format(data[r, c]);
                any |= row[c - 1] is not null;
            }
            if (any) rows.Add(row);
        }
        return new ExcelTable(path, names, target, headers, rows);
    }
}
