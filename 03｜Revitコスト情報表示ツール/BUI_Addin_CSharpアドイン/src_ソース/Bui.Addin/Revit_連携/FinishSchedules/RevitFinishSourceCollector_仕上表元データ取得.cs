// このファイルは、仕上表出力のためにRevitドキュメントから元データを一括収集するために存在します。
// Revit 2025 DB API、固定GUID(Identifiers.LayerProductIdParameterGuids)、LayerSlotMapper、
// ExteriorSideResolverに依存します。CompoundStructureReader（旧・単一選択の層読取フロー）と
// 同じ「層スロット割当→固定GUIDパラメータ読取」規則を、全文書スイープへ展開したものです。
//
// 部屋名の読み方（設計上の取り決め、GUIDではなく表示名一致）:
//   壁       : st_部屋名（内側） → InnerRoomName、st_部屋名（外側） → OuterRoomName
//   床・天井 : st_天井・床部屋要素ID → HorizontalRoomName（名前に反して部屋名の文字列が入る）
//   屋根     : 部屋名パラメータを持たない（常に外部候補）
//
// 部屋一覧は「部屋集計表」という名前のスケジュール（ViewSchedule）の「名前」列から読み取る。
// スケジュールまたは列が見つからない場合は、誤った（空の）部屋表を静かに作らないよう
// InvalidOperationExceptionで処理を停止する。呼出元（Task 7のコントローラー）が捕捉し、
// 利用者向けメッセージへ翻訳する。
//
// 複合構造を持たない要素（GetCompoundStructureがnullを返す）は、分類対象が無いため
// 警告なしで静かに除外する（1要素の欠落であり、部屋集計表の読み取り失敗のような
// 全体停止条件ではない）。
//
// 修正時の影響:
//   室内側の判定やスロット割当がずれると、仕上のProduct_IDが下地の欄へ書き込まれます。
//   モデルを見ても間違いに気づけないため、CompoundStructureReaderと同じ規則を必ず維持してください。
using Autodesk.Revit.DB;
using Bui.Assemblies;
using Bui.FinishSchedules;
using Bui.Resources;

namespace Bui.Revit.FinishSchedules;

/// <summary>Revitドキュメントから部屋集計表の部屋一覧と、壁・床・天井・屋根の層スナップショットを収集します。</summary>
public sealed class RevitFinishSourceCollector
{
    private const string RoomScheduleName = "部屋集計表";
    private const string RoomNameHeading = "名前";
    private const string InnerRoomParameter = "st_部屋名（内側）";
    private const string OuterRoomParameter = "st_部屋名（外側）";
    private const string HorizontalRoomParameter = "st_天井・床部屋要素ID";

    public FinishSourceSnapshot Collect(Document document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var rooms = ReadScheduleRoomNames(document, RoomScheduleName, RoomNameHeading);
        var hosts = CollectHosts(document);
        // LAYER_OVERFLOW警告はFinishLayerClassifier.Classify(Bui.Shared)がHostFinishInput.LayerWarningから
        // 生成する。ここで同じ警告をhosts[].LayerWarningから重複して組み立てると、
        // FinishScheduleExportService.PrepareAsyncのConcatで確認ダイアログに二重表示されてしまうため、
        // 収集段階(このクラス)では警告を作らない。現時点で収集段階固有の警告は無いため空配列を返す。
        return new FinishSourceSnapshot(rooms, hosts, Array.Empty<FinishScheduleWarning>());
    }

    // --- 部屋集計表 ---

    /// <summary>
    /// 「部屋集計表」スケジュールの「名前」列を本文行ぶん読み、正規化キーが同じ行は
    /// 最初に現れた原文へ統合する（RoomNameNormalizerTestsで固定した規則と同じ）。
    /// スケジュール自体、または「名前」列が見つからない場合は処理を停止する。
    /// </summary>
    private static IReadOnlyList<NormalizedRoomName> ReadScheduleRoomNames(
        Document document, string scheduleName, string headingName)
    {
        var schedule = new FilteredElementCollector(document)
            .OfClass(typeof(ViewSchedule))
            .Cast<ViewSchedule>()
            .FirstOrDefault(vs => !vs.IsTemplate && vs.Name == scheduleName);

        if (schedule is null)
        {
            throw new InvalidOperationException(
                $"「{scheduleName}」という名前の集計表が見つかりません。部屋集計表を作成してから再実行してください。");
        }

        var tableData = schedule.GetTableData();
        var header = tableData.GetSectionData(SectionType.Header);
        var body = tableData.GetSectionData(SectionType.Body);

        // まずスケジュール定義(ScheduleDefinition)の可視フィールド列から探す。
        // ヘッダー行の表示・非表示設定に左右されない、本来あるべき照合方法。
        var columnIndex = FindHeadingColumnFromDefinition(schedule, headingName);
        // 定義側で見つからない場合のみ、描画されたヘッダーセルのテキストを走査する
        // (旧来の方法。ヘッダー行が実際に表示されているスケジュールでの保険)。
        columnIndex = columnIndex >= 0 ? columnIndex : FindHeadingColumn(schedule, header, headingName);
        if (columnIndex < 0)
        {
            throw new InvalidOperationException(
                $"「{scheduleName}」に「{headingName}」列が見つかりません。集計表のフィールド構成を確認してください。\n"
                + DescribeHeaderForDiagnostics(schedule, header)
                + "\n" + DescribeDefinitionForDiagnostics(schedule));
        }

        var rooms = new List<NormalizedRoomName>();
        var seenKeys = new HashSet<string>(StringComparer.Ordinal);
        for (var row = 0; row < body.NumberOfRows; row++)
        {
            var text = schedule.GetCellText(SectionType.Body, row, columnIndex)?.Trim() ?? string.Empty;
            if (string.IsNullOrEmpty(text))
            {
                continue;
            }

            // NormalizeForRoomMatchingを使うのは、壁等のst_部屋名（内側/外側）が
            // 「名前 部屋番号」形式で入力される運用のため、FinishLayerClassifier側と
            // 同じキー生成規則で揃える必要があるため(RoomNameNormalizer参照)。
            // 部屋集計表側の名前は番号を含まないため、通常は素通り(no-op)になる。
            var normalized = RoomNameNormalizer.NormalizeForRoomMatching(text);
            if (seenKeys.Add(normalized.Key))
            {
                rooms.Add(normalized);
            }
        }

        return rooms;
    }

    /// <summary>
    /// スケジュール定義(ScheduleDefinition)の可視フィールドを表示順に数え、
    /// 列見出し(カスタム指定が無ければパラメーター名)が完全一致するフィールドの
    /// 本文列番号を返す。非表示フィールドは本文に列を持たないため数えない。
    /// ヘッダー行の表示/非表示設定に依存しないため、こちらを優先する。
    /// </summary>
    private static int FindHeadingColumnFromDefinition(ViewSchedule schedule, string headingName)
    {
        var definition = schedule.Definition;
        var bodyColumnIndex = 0;
        foreach (var fieldId in definition.GetFieldOrder())
        {
            var field = definition.GetField(fieldId);
            if (field.IsHidden)
            {
                continue;
            }

            var heading = string.IsNullOrEmpty(field.ColumnHeading) ? field.GetName() : field.ColumnHeading;
            if (heading?.Trim() == headingName)
            {
                return bodyColumnIndex;
            }

            bodyColumnIndex++;
        }

        return -1;
    }

    /// <summary>
    /// 定義側の探索も失敗した場合の切り分け用に、可視フィールドの見出し一覧を
    /// エラーメッセージへ含める。
    /// </summary>
    private static string DescribeDefinitionForDiagnostics(ViewSchedule schedule)
    {
        var definition = schedule.Definition;
        var lines = new List<string> { "[診断] スケジュール定義の可視フィールド:" };
        var bodyColumnIndex = 0;
        foreach (var fieldId in definition.GetFieldOrder())
        {
            var field = definition.GetField(fieldId);
            if (field.IsHidden)
            {
                lines.Add($"[診断]   (非表示) 名前=\"{field.GetName()}\" 見出し=\"{field.ColumnHeading}\"");
                continue;
            }

            lines.Add(
                $"[診断]   列{bodyColumnIndex}: 名前=\"{field.GetName()}\" 見出し=\"{field.ColumnHeading}\"");
            bodyColumnIndex++;
        }

        return string.Join("\n", lines);
    }

    /// <summary>ヘッダー区画の全行・全列を走査し、表示テキストが完全一致する列番号を探す。</summary>
    private static int FindHeadingColumn(ViewSchedule schedule, TableSectionData header, string headingName)
    {
        for (var row = 0; row < header.NumberOfRows; row++)
        {
            for (var column = 0; column < header.NumberOfColumns; column++)
            {
                var text = schedule.GetCellText(SectionType.Header, row, column)?.Trim();
                if (text == headingName)
                {
                    return column;
                }
            }
        }

        return -1;
    }

    /// <summary>
    /// 「名前」列が見つからなかった場合の切り分け用に、実際にAPIが読み取った
    /// ヘッダー区画の行数・列数と、各セルのテキストをそのままエラーメッセージへ含める。
    /// 会社PCでしかRevit実機確認ができないため、推測より実際の値を画面に出す方が早い
    /// （bui-surface-the-reasonの教訓と同じ方針）。
    /// </summary>
    private static string DescribeHeaderForDiagnostics(ViewSchedule schedule, TableSectionData header)
    {
        var lines = new List<string>
        {
            $"[診断] ヘッダー区画: {header.NumberOfRows}行 × {header.NumberOfColumns}列"
        };

        for (var row = 0; row < header.NumberOfRows; row++)
        {
            var cells = new List<string>();
            for (var column = 0; column < header.NumberOfColumns; column++)
            {
                var text = schedule.GetCellText(SectionType.Header, row, column) ?? string.Empty;
                cells.Add($"[{column}]=\"{text}\"");
            }
            lines.Add($"[診断] {row}行目: {string.Join(" ", cells)}");
        }

        return string.Join("\n", lines);
    }

    // --- 壁・床・天井・屋根 ---

    private static IReadOnlyList<HostFinishInput> CollectHosts(Document document)
    {
        var hosts = new List<HostFinishInput>();
        hosts.AddRange(CollectByKind<Wall>(document, FinishHostKind.Wall));
        hosts.AddRange(CollectByKind<Floor>(document, FinishHostKind.Floor));
        hosts.AddRange(CollectByKind<Ceiling>(document, FinishHostKind.Ceiling));
        hosts.AddRange(CollectByKind<RoofBase>(document, FinishHostKind.Roof));
        return hosts;
    }

    private static IEnumerable<HostFinishInput> CollectByKind<THost>(Document document, FinishHostKind kind)
        where THost : HostObject
    {
        var elements = new FilteredElementCollector(document)
            .OfClass(typeof(THost))
            .WhereElementIsNotElementType();

        foreach (var element in elements)
        {
            var built = TryBuildHostFinishInput(document, element, kind);
            if (built is not null)
            {
                yield return built;
            }
        }
    }

    private static HostFinishInput? TryBuildHostFinishInput(Document document, Element element, FinishHostKind kind)
    {
        if (element is not HostObject host)
        {
            return null;
        }

        var typeId = host.GetTypeId();
        if (typeId == ElementId.InvalidElementId
            || document.GetElement(typeId) is not HostObjAttributes hostType)
        {
            return null;
        }

        var compound = hostType.GetCompoundStructure();
        if (compound is null)
        {
            // 複合構造を持たない要素は分類対象が無いため、警告なしで静かに除外する。
            return null;
        }

        var rawLayers = compound.GetLayers();
        var layerCount = rawLayers.Count;
        var interiorSide = ExteriorSideResolver.InteriorFor(kind);

        var layersForMapping = rawLayers
            .Select((layer, index) => new AssemblyLayerRecord(
                index, layer.Function.ToString(), 0m, string.Empty, string.Empty))
            .ToArray();
        var plan = LayerSlotMapper.Map(layersForMapping, interiorSide);
        var slotByOrder = plan.Assigned.ToDictionary(
            assignment => assignment.Layer.Order,
            assignment => assignment.Slot);

        var layers = rawLayers
            .Select((layer, index) => new FinishLayerInput(
                index,
                layer.Function.ToString(),
                ComputeDepthFromInterior(index, layerCount, interiorSide),
                slotByOrder.TryGetValue(index, out var slot)
                    ? ReadElementLayerProductId(host, slot)
                    : string.Empty))
            .ToArray();

        var (innerRoom, outerRoom, horizontalRoom) = ReadRoomNames(host, kind);
        var category = element.Category?.Name ?? hostType.Category?.Name ?? string.Empty;
        var familyName = hostType.FamilyName ?? string.Empty;
        var typeName = hostType.Name ?? string.Empty;

        return new HostFinishInput(
            element.Id.Value,
            kind,
            category,
            familyName,
            typeName,
            innerRoom,
            outerRoom,
            horizontalRoom,
            layers,
            plan.Warning);
    }

    /// <summary>
    /// Task 1の契約どおり、DepthFromInterior==0が常に室内側の層になるよう、
    /// Revit生の層順(Order)を室内側からの深度へ変換する。
    /// </summary>
    private static int ComputeDepthFromInterior(int order, int layerCount, InteriorSide interiorSide) =>
        interiorSide == InteriorSide.FirstLayer ? order : layerCount - 1 - order;

    private static (string Inner, string Outer, string Horizontal) ReadRoomNames(Element element, FinishHostKind kind) =>
        kind switch
        {
            FinishHostKind.Wall => (
                ReadRoomParameter(element, InnerRoomParameter),
                ReadRoomParameter(element, OuterRoomParameter),
                string.Empty),
            FinishHostKind.Floor or FinishHostKind.Ceiling => (
                string.Empty,
                string.Empty,
                ReadRoomParameter(element, HorizontalRoomParameter)),
            FinishHostKind.Roof => (string.Empty, string.Empty, string.Empty),
            _ => (string.Empty, string.Empty, string.Empty)
        };

    /// <summary>
    /// LookupParameterは表示名の完全一致で検索する標準APIだが、同名パラメータが複数付いた要素では
    /// どれが返るか保証がない（Revit APIの既知の注意点）。st_部屋名（内側）等はBUI管理の共有パラメータで
    /// 重複付与を想定していないが、モデル側の事情で重複した場合は意図しない値を読む可能性がある。
    /// </summary>
    private static string ReadRoomParameter(Element element, string parameterName) =>
        element.LookupParameter(parameterName)?.AsString()?.Trim() ?? string.Empty;

    private static string ReadElementLayerProductId(Element element, LayerSlot slot)
    {
        var index = (int)slot;
        if (index < 0 || index >= Identifiers.LayerProductIdParameterGuids.Count)
        {
            return string.Empty;
        }

        return element.get_Parameter(Identifiers.LayerProductIdParameterGuids[index])?.AsString()?.Trim()
            ?? string.Empty;
    }
}
