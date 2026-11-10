// このファイルの目的:
//   FinishScheduleOutput(内部仕上表・外部仕上表)を、雛型(仕上表_雛型.xlsx)のコピーへ
//   Open XML SDKで直接書き込みます。Excel COMは使いません(会社PC以外でも動かすため)。
//
// 雛型の構造（詳細はtask-5-brief.mdおよび仕上表_雛型.xlsxそのものを参照）:
//   - シート「仕上表」: A1:J102。1～2行目がヘッダー(部屋名/床(仕上/下地)/壁(仕上/下地)/巾木/
//     天井(仕上/下地)/廻縁/備考)、3～102行目が100部屋分のデータ行。
//     F列(巾木)・I列(廻縁)はヘッダーはあるが列の運用が未定のため、データは常に空欄のままにする。
//     102行目は「雛型の最終行」であり、101部屋目以降はこの行の列ごとのスタイル(罫線・行高含む)を
//     複製して拡張する。5行ごとのthickBot帯模様はテンプレートの100行分だけの装飾であり、
//     101行目以降まで再現する要求はない(task-5-brief.mdの百一部屋目テストが根拠)。
//   - シート「外部仕上表」は雛型に存在しない。新規WorksheetPartとして作成し、
//     ヘッダー(カテゴリ/ファミリ/タイプ/外側仕上/外側下地/要素数/備考)を1行目に、
//     データを2行目以降に書く。ヘッダーは「仕上表」シート1行目と同じCellFormat(青背景・白文字)を
//     そのまま使う(スタイルパートはワークブックで1つだけ共有されるため、同じStyleIndexを
//     指定すれば別シートでも同じ見た目になる)。
//
// 一般名称未登録セルの赤色化:
//   FinishScheduleCell.HighlightMissing=trueのセルは、既存の罫線・配置・フォント・数値形式を
//   保ったまま、FillだけFFF4CCCC(薄赤色)へ差し替える。同じ元スタイルへ複数回赤色化が要求されたら
//   複製済みのCellFormatを使い回す(スタイルパートを不要に肥大化させない)。
//   CellFormatClonerはWrite()で1つだけ作り、内部仕上表・外部仕上表の両方の書き込みへ渡す。
//   シートごとに別インスタンスを作ると、赤色化用のFillやCellFormat複製がシートの数だけ
//   重複して追加されてしまう(スタイルパートはワークブックで1つしか無いため、
//   「使い回す」はワークブック全体で言えて初めて意味を持つ)。
//
// セル内改行とWrapText:
//   セルの文字列にEnvironment.NewLineが含まれる場合、そのセルのCellFormatでWrapTextを
//   有効にしないとExcel上で1行に潰れて見える。赤色化とは独立した軸なので、
//   (元スタイル, 赤色有無, 折返し有無)の組み合わせ単位でCellFormatをキャッシュする。
//
// 文字列の書き方について（判断メモ）:
//   共有文字列テーブル(sharedStrings.xml)を伸長させる代わりに、データセルは常にインライン文字列
//   (t="inlineStr")として書く。雛型のヘッダーは元々共有文字列を使っているが、データ行はどのセルも
//   1回しか使われない文章(部屋名・仕上材名の改行連結・備考)であり、共有文字列化のメリットが薄い上、
//   sst.xmlのcount/uniqueCount更新や重複検出といった複雑さを避けられる。t="inlineStr"と
//   t="s"はOOXML上同じシート内に混在してよく、Excelで問題なく開ける。
//
// 修正時の影響:
//   ここは雛型ファイルを直接編集する最後の砦です。列の割り当てやスタイル複製を間違えると
//   仕上表のExcel出力そのものが壊れます(セル参照の重複やスタイルインデックス範囲外は
//   Excelが「修復」ダイアログを出す/開けない、という壊れ方をします)。必ずテストで固定してから
//   変更してください。

using System.Globalization;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;

namespace Bui.FinishSchedules;

public sealed class FinishScheduleWorkbookWriter
{
    private const string MissingFillRgb = "FFF4CCCC";

    private const string InternalSheetName = "仕上表";
    private const string ExternalSheetName = "外部仕上表";

    private const uint FirstDataRowNumber = 3;
    private const uint LastTemplateRowNumber = 102;

    // 内部仕上表の列。F(巾木)・I(廻縁)はヘッダーのみで、データは書かない。
    private static readonly string[] InternalColumns = { "A", "B", "C", "D", "E", "F", "G", "H", "I", "J" };
    private static readonly string[] ExternalHeaders = { "カテゴリ", "ファミリ", "タイプ", "外側仕上", "外側下地", "要素数", "備考" };

    public void Write(string templatePath, string outputPath, FinishScheduleOutput output)
    {
        ArgumentNullException.ThrowIfNull(output);
        var directory = Path.GetDirectoryName(outputPath)
            ?? throw new InvalidOperationException("出力先フォルダーを取得できません。");
        Directory.CreateDirectory(directory);
        var temporaryPath = Path.Combine(directory, $".{Path.GetFileName(outputPath)}.{Guid.NewGuid():N}.tmp.xlsx");
        try
        {
            File.Copy(templatePath, temporaryPath, true);
            using (var document = SpreadsheetDocument.Open(temporaryPath, true))
            {
                var stylesPart = document.WorkbookPart!.WorkbookStylesPart
                    ?? throw new InvalidOperationException("スタイル定義(styles.xml)がありません。");
                // 赤色化用Fill・複製CellFormatを内部/外部シートで共有するため、
                // CellFormatClonerはここで1つだけ作る。
                var formatCloner = new CellFormatCloner(stylesPart.Stylesheet!, MissingFillRgb);
                var headerStyles = ReadInternalHeaderStyles(document);

                WriteInternalSheet(document, output.InternalRows, formatCloner);
                WriteExternalSheet(document, output.ExternalRows, formatCloner, headerStyles);

                stylesPart.Stylesheet!.Save();
            }
            // 保存した一時ファイルが壊れていないか、最終パスへ移す前に開き直して確かめる。
            // ZIPコンテナが開けるだけでは不十分なので、Workbook・各シート・スタイル定義の
            // XMLを実際にパースさせ、壊れた断片があればここで例外を投げさせる。
            using (var document = SpreadsheetDocument.Open(temporaryPath, false))
            {
                var workbookPart = document.WorkbookPart!;
                var workbook = workbookPart.Workbook!;
                foreach (var sheet in workbook.Descendants<Sheet>())
                {
                    var worksheetPart = (WorksheetPart)workbookPart.GetPartById(sheet.Id!);
                    _ = worksheetPart.Worksheet!;
                }
                _ = workbookPart.WorkbookStylesPart!.Stylesheet!;
            }
            File.Move(temporaryPath, outputPath, true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    // --- 内部仕上表(仕上表シート) ---

    private static void WriteInternalSheet(SpreadsheetDocument document, IReadOnlyList<InternalFinishRow> rows, CellFormatCloner formatCloner)
    {
        var worksheetPart = GetWorksheetPart(document, InternalSheetName);
        var sheetData = worksheetPart.Worksheet!.GetFirstChild<SheetData>()
            ?? throw new InvalidOperationException("仕上表シートにsheetDataがありません。");

        var lastTemplateRow = sheetData.Elements<Row>().FirstOrDefault(r => r.RowIndex?.Value == LastTemplateRowNumber)
            ?? throw new InvalidOperationException("雛型の最終データ行(102行目)が見つかりません。");
        var overflowStyles = ReadColumnStyles(lastTemplateRow, InternalColumns);

        var rowNumber = FirstDataRowNumber;
        foreach (var row in rows)
        {
            var rowElement = rowNumber <= LastTemplateRowNumber
                ? sheetData.Elements<Row>().First(r => r.RowIndex!.Value == rowNumber)
                : CreateOverflowRow(rowNumber, overflowStyles, InternalColumns, lastTemplateRow);

            if (rowNumber > LastTemplateRowNumber)
            {
                sheetData.Append(rowElement);
            }

            SetInlineText(GetColumnCell(rowElement, "A", rowNumber), row.RoomName, formatCloner, highlight: false);
            SetInlineText(GetColumnCell(rowElement, "B", rowNumber), row.FloorFinish.Text, formatCloner, row.FloorFinish.HighlightMissing);
            SetInlineText(GetColumnCell(rowElement, "C", rowNumber), row.FloorSubstrate.Text, formatCloner, row.FloorSubstrate.HighlightMissing);
            SetInlineText(GetColumnCell(rowElement, "D", rowNumber), row.WallFinish.Text, formatCloner, row.WallFinish.HighlightMissing);
            SetInlineText(GetColumnCell(rowElement, "E", rowNumber), row.WallSubstrate.Text, formatCloner, row.WallSubstrate.HighlightMissing);
            // F列(巾木)は空欄のまま(スタイルだけ保持)。
            SetInlineText(GetColumnCell(rowElement, "G", rowNumber), row.CeilingFinish.Text, formatCloner, row.CeilingFinish.HighlightMissing);
            SetInlineText(GetColumnCell(rowElement, "H", rowNumber), row.CeilingSubstrate.Text, formatCloner, row.CeilingSubstrate.HighlightMissing);
            // I列(廻縁)は空欄のまま(スタイルだけ保持)。
            SetInlineText(GetColumnCell(rowElement, "J", rowNumber), row.Notes, formatCloner, highlight: false);

            rowNumber++;
        }

        UpdateDimension(worksheetPart.Worksheet!, Math.Max(rowNumber - 1, LastTemplateRowNumber), "J");
        worksheetPart.Worksheet!.Save();
    }

    private static Row CreateOverflowRow(uint rowNumber, IReadOnlyDictionary<string, uint> columnStyles, IReadOnlyList<string> columns, Row templateRow)
    {
        var row = new Row { RowIndex = rowNumber };
        if (templateRow.Height is not null) row.Height = templateRow.Height.Value;
        if (templateRow.CustomHeight is not null) row.CustomHeight = templateRow.CustomHeight.Value;
        if (templateRow.ThickBot is not null) row.ThickBot = templateRow.ThickBot.Value;
        if (templateRow.ThickTop is not null) row.ThickTop = templateRow.ThickTop.Value;

        foreach (var column in columns)
        {
            row.Append(new Cell { CellReference = $"{column}{rowNumber}", StyleIndex = columnStyles[column] });
        }
        return row;
    }

    private static IReadOnlyDictionary<string, uint> ReadColumnStyles(Row templateRow, IReadOnlyList<string> columns)
    {
        var cells = templateRow.Elements<Cell>().ToArray();
        if (cells.Length != columns.Count)
        {
            throw new InvalidOperationException("雛型の最終データ行の列数が想定と異なります。");
        }
        var result = new Dictionary<string, uint>(columns.Count);
        for (var i = 0; i < columns.Count; i++)
        {
            result[columns[i]] = cells[i].StyleIndex?.Value ?? 0u;
        }
        return result;
    }

    private static void UpdateDimension(Worksheet worksheet, uint lastDataRow, string lastColumn)
    {
        var dimension = worksheet.GetFirstChild<SheetDimension>();
        if (dimension is null)
        {
            return;
        }
        dimension.Reference = $"A1:{lastColumn}{lastDataRow}";
    }

    // --- 外部仕上表(新規シート) ---

    private static void WriteExternalSheet(SpreadsheetDocument document, IReadOnlyList<ExternalFinishRow> rows, CellFormatCloner formatCloner, InternalHeaderStyles headerStyles)
    {
        var workbookPart = document.WorkbookPart!;

        var worksheetPart = workbookPart.AddNewPart<WorksheetPart>();
        var sheetData = new SheetData();
        worksheetPart.Worksheet = new Worksheet(sheetData);

        var sheets = workbookPart.Workbook!.GetFirstChild<Sheets>()
            ?? throw new InvalidOperationException("workbook.xmlにsheetsがありません。");
        var newSheetId = sheets.Elements<Sheet>().Select(s => s.SheetId?.Value ?? 0u).DefaultIfEmpty(0u).Max() + 1;
        var relationshipId = workbookPart.GetIdOfPart(worksheetPart);
        sheets.Append(new Sheet { Name = ExternalSheetName, SheetId = newSheetId, Id = relationshipId });

        // ヘッダーは「仕上表」シート1行目と同じCellFormat(青背景・白文字)を、StyleIndexをそのまま
        // 流用することで再現する。styles.xmlはワークブックで1つしか無いため、これで足りる。
        // どのStyleIndexが該当するかは決め打ちにせず、実際の「仕上表」シートA1/B1/J1から
        // 読み取ったもの(headerStyles、Write()内のReadInternalHeaderStylesで取得)を使う。
        // 雛型が再生成されてスタイルの並びが変わっても、ここは追従する。
        var headerRow = new Row { RowIndex = 1 };
        for (var i = 0; i < ExternalHeaders.Length; i++)
        {
            var column = ExternalColumnLetters[i];
            var headerStyleIndex = i == 0
                ? headerStyles.First
                : (i == ExternalHeaders.Length - 1 ? headerStyles.Last : headerStyles.Middle);
            headerRow.Append(CreateCell($"{column}1", headerStyleIndex, ExternalHeaders[i]));
        }
        sheetData.Append(headerRow);

        var rowNumber = 2u;
        foreach (var row in rows)
        {
            var rowElement = new Row { RowIndex = rowNumber };
            rowElement.Append(CreateInlineTextCell($"A{rowNumber}", 0u, row.Category, formatCloner, highlight: false));
            rowElement.Append(CreateInlineTextCell($"B{rowNumber}", 0u, row.FamilyName, formatCloner, highlight: false));
            rowElement.Append(CreateInlineTextCell($"C{rowNumber}", 0u, row.TypeName, formatCloner, highlight: false));
            rowElement.Append(CreateInlineTextCell($"D{rowNumber}", 0u, row.Finish.Text, formatCloner, row.Finish.HighlightMissing));
            rowElement.Append(CreateInlineTextCell($"E{rowNumber}", 0u, row.Substrate.Text, formatCloner, row.Substrate.HighlightMissing));
            rowElement.Append(CreateNumberCell($"F{rowNumber}", 0u, row.ElementCount));
            rowElement.Append(CreateInlineTextCell($"G{rowNumber}", 0u, row.Notes, formatCloner, highlight: false));
            sheetData.Append(rowElement);
            rowNumber++;
        }

        workbookPart.Workbook!.Save();
        worksheetPart.Worksheet!.Save();
    }

    private static readonly string[] ExternalColumnLetters = { "A", "B", "C", "D", "E", "F", "G" };

    /// <summary>
    /// 外部仕上表のヘッダーへ流用する、「仕上表」シート1行目のStyleIndex。
    /// A1(先頭列)・J1(最終列/備考)は他と異なる見た目のため個別に持ち、
    /// それ以外(床・壁・天井など)はB1のスタイルを共通(Middle)として使う。
    /// </summary>
    private readonly record struct InternalHeaderStyles(uint First, uint Middle, uint Last);

    private static InternalHeaderStyles ReadInternalHeaderStyles(SpreadsheetDocument document)
    {
        var worksheetPart = GetWorksheetPart(document, InternalSheetName);
        var sheetData = worksheetPart.Worksheet!.GetFirstChild<SheetData>()
            ?? throw new InvalidOperationException("仕上表シートにsheetDataがありません。");
        var headerRow = sheetData.Elements<Row>().FirstOrDefault(r => r.RowIndex?.Value == 1)
            ?? throw new InvalidOperationException("仕上表シートのヘッダー行(1行目)が見つかりません。");

        var first = GetColumnCell(headerRow, "A", 1u).StyleIndex?.Value ?? 0u;
        var middle = GetColumnCell(headerRow, "B", 1u).StyleIndex?.Value ?? 0u;
        var last = GetColumnCell(headerRow, "J", 1u).StyleIndex?.Value ?? 0u;
        return new InternalHeaderStyles(first, middle, last);
    }

    // --- セル組み立ての共通処理 ---

    private static Cell GetColumnCell(Row row, string column, uint rowNumber)
    {
        var reference = $"{column}{rowNumber}";
        var cell = row.Elements<Cell>().FirstOrDefault(c => c.CellReference == reference);
        if (cell is not null)
        {
            return cell;
        }
        // 雛型の行に想定した列セルが無い場合の保険(通常は発生しない)。
        cell = new Cell { CellReference = reference };
        row.Append(cell);
        return cell;
    }

    private static void SetInlineText(Cell cell, string text, CellFormatCloner formatCloner, bool highlight)
    {
        var baseStyleIndex = cell.StyleIndex?.Value ?? 0u;
        var wrap = text.Contains('\n') || text.Contains('\r');
        cell.StyleIndex = formatCloner.Resolve(baseStyleIndex, highlight, wrap);

        cell.RemoveAllChildren<CellValue>();
        cell.RemoveAllChildren<InlineString>();
        cell.RemoveAllChildren<CellFormula>();

        if (string.IsNullOrEmpty(text))
        {
            cell.DataType = null;
            return;
        }

        cell.DataType = CellValues.InlineString;
        cell.AppendChild(new InlineString(
            new DocumentFormat.OpenXml.Spreadsheet.Text(text) { Space = SpaceProcessingModeValues.Preserve }));
    }

    private static Cell CreateCell(string cellReference, uint styleIndex, string text)
    {
        var cell = new Cell { CellReference = cellReference, StyleIndex = styleIndex };
        if (!string.IsNullOrEmpty(text))
        {
            cell.DataType = CellValues.InlineString;
            cell.AppendChild(new InlineString(
                new DocumentFormat.OpenXml.Spreadsheet.Text(text) { Space = SpaceProcessingModeValues.Preserve }));
        }
        return cell;
    }

    private static Cell CreateInlineTextCell(string cellReference, uint baseStyleIndex, string text, CellFormatCloner formatCloner, bool highlight)
    {
        var wrap = text.Contains('\n') || text.Contains('\r');
        var styleIndex = formatCloner.Resolve(baseStyleIndex, highlight, wrap);
        return CreateCell(cellReference, styleIndex, text);
    }

    private static Cell CreateNumberCell(string cellReference, uint styleIndex, int value) =>
        new()
        {
            CellReference = cellReference,
            StyleIndex = styleIndex,
            DataType = CellValues.Number,
            CellValue = new CellValue(value.ToString(CultureInfo.InvariantCulture))
        };

    private static WorksheetPart GetWorksheetPart(SpreadsheetDocument document, string sheetName)
    {
        var sheet = document.WorkbookPart!.Workbook!.Descendants<Sheet>().FirstOrDefault(s => s.Name == sheetName)
            ?? throw new InvalidOperationException($"シート「{sheetName}」が雛型に見つかりません。");
        return (WorksheetPart)document.WorkbookPart.GetPartById(sheet.Id!);
    }

    /// <summary>
    /// 元のCellFormat(罫線・配置・フォント・数値形式)を保ったまま、
    /// 一般名称未登録の薄赤色Fillや、セル内改行時のWrapTextだけを適用したCellFormatを
    /// 複製・再利用します。(元スタイル, 赤色有無, 折返し有無)の組み合わせ単位で1つだけ作り、使い回します。
    /// </summary>
    private sealed class CellFormatCloner
    {
        private readonly Stylesheet _stylesheet;
        private readonly string _missingFillRgb;
        private readonly Dictionary<(uint BaseStyleIndex, bool Highlight, bool Wrap), uint> _cache = new();
        private uint? _highlightFillId;

        public CellFormatCloner(Stylesheet stylesheet, string missingFillRgb)
        {
            _stylesheet = stylesheet;
            _missingFillRgb = missingFillRgb;
        }

        public uint Resolve(uint baseStyleIndex, bool highlight, bool wrap)
        {
            if (!highlight && !wrap)
            {
                return baseStyleIndex;
            }

            var key = (baseStyleIndex, highlight, wrap);
            if (_cache.TryGetValue(key, out var cached))
            {
                return cached;
            }

            var cellFormats = _stylesheet.CellFormats
                ?? throw new InvalidOperationException("styles.xmlにcellXfsがありません。");
            var baseFormat = cellFormats.Elements<CellFormat>().ElementAt((int)baseStyleIndex);
            var clone = (CellFormat)baseFormat.CloneNode(true);

            if (highlight)
            {
                clone.FillId = GetOrCreateHighlightFillId();
                clone.ApplyFill = true;
            }
            if (wrap)
            {
                var alignment = clone.Alignment;
                if (alignment is null)
                {
                    alignment = new Alignment();
                    clone.Alignment = alignment;
                }
                alignment.WrapText = true;
                clone.ApplyAlignment = true;
            }

            var newIndex = (uint)cellFormats.ChildElements.Count;
            cellFormats.Append(clone);
            cellFormats.Count = (uint)cellFormats.ChildElements.Count;

            _cache[key] = newIndex;
            return newIndex;
        }

        private uint GetOrCreateHighlightFillId()
        {
            if (_highlightFillId is uint existing)
            {
                return existing;
            }

            var fills = _stylesheet.Fills
                ?? throw new InvalidOperationException("styles.xmlにfillsがありません。");
            var fill = new Fill(new PatternFill(
                new ForegroundColor { Rgb = _missingFillRgb },
                new BackgroundColor { Indexed = 64 })
            {
                PatternType = PatternValues.Solid
            });

            var newFillIndex = (uint)fills.ChildElements.Count;
            fills.Append(fill);
            fills.Count = (uint)fills.ChildElements.Count;

            _highlightFillId = newFillIndex;
            return newFillIndex;
        }
    }
}
