// このファイルの目的:
//   部屋集計表由来の部屋一覧(NormalizedRoomName)と、Product_IDを一般名称へ解決済みの配置
//   (ResolvedProductPlacement)を、内部仕上表(部屋単位)・外部仕上表(カテゴリ・ファミリ・タイプ単位)
//   の行へ集約します。
//
// 集約規則の要点（詳細は仕上表出力の設計ドキュメント／task-4-brief.mdを参照）:
//   内部仕上表:
//     - 1行は1つの正規化済み部屋名(RoomKey)。部屋集計表由来の全部屋を行として出力し、
//       配置が1件もない部屋も空行として残してROOM_NO_FINISH警告を1件付ける。
//     - セルの文字列は解決済み(Resolved)の一般名称だけを重複排除してEnvironment.NewLineで連結する。
//       仕上列は名称の安定ソート、下地列は最小Depth優先→同深度なら名称の安定ソート。
//     - セルのHighlightMissingは、そのセルにProductMissingまたはGeneralNameMissingが1件でもあればtrue。
//     - 備考(Notes)は部屋(行)全体で未解決Product_IDを重複させずに列挙する。
//   外部仕上表:
//     - 1行は1つの(Category, FamilyName, TypeName)。ElementIdの種類数(Distinct)をElementCountとする。
//     - 同じ組の中で要素ごとの(Column, ProductId)構成が異なる場合は構成ごとに行を分け、
//       グループへEXTERNAL_TYPE_MISMATCH警告を1件追加する。
//     - 並び順はCategory→FamilyName→TypeNameの安定ソート。
//
// なぜBuildの単一配置オーバーロードがあるのか:
//   task-4-brief.mdの検証済みテストは、配置1件だけの場合に配列でラップせず直接渡す形を使う。
//   複数件は配列/IReadOnlyListで渡す既存オーバーロードへ委譲する。
//
// 修正時の影響:
//   ここがずれると、後続のExcel出力(Task 5)が誤った集約結果をそのまま帳票へ書き出します。
//   必ずテストで固定してから変更してください。

namespace Bui.FinishSchedules;

public static class FinishScheduleAggregator
{
    private const string RoomNoFinishWarningCode = "ROOM_NO_FINISH";
    private const string ExternalTypeMismatchWarningCode = "EXTERNAL_TYPE_MISMATCH";

    /// <summary>内部配置のRoomKeyが部屋集計表由来の部屋一覧に無く、行として出力できなかったときの警告コードです。</summary>
    private const string RoomNotInScheduleWarningCode = "ROOM_NOT_IN_SCHEDULE";

    /// <summary>
    /// FormatNoteが備考へ書く接頭辞です。FinishSchedulePreparedResult.CountDistinctMissingIds
    /// (Bui.Addin, FinishScheduleComposition_仕上表構成.cs)がこの接頭辞と一致するテキストを
    /// 探して未解決Product_IDを数えるため、文言を変えるときは両方を必ず一緒に直してください。
    /// </summary>
    public const string GeneralNameMissingNotePrefix = "一般名称未登録: ";
    public const string ProductMissingNotePrefix = "Product_ID未登録: ";

    /// <summary>配置1件だけを集約する便宜オーバーロードです。内部で配列へラップして本体へ委譲します。</summary>
    public static FinishScheduleOutput Build(
        IReadOnlyList<NormalizedRoomName> rooms, ResolvedProductPlacement placement, IReadOnlyList<FinishScheduleWarning> warnings) =>
        Build(rooms, new[] { placement }, warnings);

    public static FinishScheduleOutput Build(
        IReadOnlyList<NormalizedRoomName> rooms,
        IReadOnlyList<ResolvedProductPlacement> placements,
        IReadOnlyList<FinishScheduleWarning> warnings)
    {
        ArgumentNullException.ThrowIfNull(rooms);
        ArgumentNullException.ThrowIfNull(placements);
        ArgumentNullException.ThrowIfNull(warnings);

        var internalPlacements = placements.Where(p => IsInternalColumn(p.Placement.Column)).ToArray();
        var externalPlacements = placements.Where(p => !IsInternalColumn(p.Placement.Column)).ToArray();

        var (internalRows, internalWarnings) = BuildInternalRows(rooms, internalPlacements);
        var (externalRows, externalWarnings) = BuildExternalRows(externalPlacements);

        var allWarnings = warnings.Concat(internalWarnings).Concat(externalWarnings).ToArray();
        return new FinishScheduleOutput(internalRows, externalRows, allWarnings);
    }

    /// <summary>外部仕上候補だけを渡し、外部行一覧だけを受け取る便宜メソッドです（警告はBuildを使ってください）。</summary>
    public static IReadOnlyList<ExternalFinishRow> BuildExternal(params ResolvedProductPlacement[] placements) =>
        Build(Array.Empty<NormalizedRoomName>(), placements, Array.Empty<FinishScheduleWarning>()).ExternalRows;

    private static bool IsInternalColumn(FinishColumn column) => column switch
    {
        FinishColumn.FloorFinish or FinishColumn.FloorSubstrate or
        FinishColumn.WallFinish or FinishColumn.WallSubstrate or
        FinishColumn.CeilingFinish or FinishColumn.CeilingSubstrate => true,
        _ => false
    };

    // --- 内部仕上表 ---

    private static (IReadOnlyList<InternalFinishRow> Rows, IReadOnlyList<FinishScheduleWarning> Warnings) BuildInternalRows(
        IReadOnlyList<NormalizedRoomName> rooms, IReadOnlyList<ResolvedProductPlacement> placements)
    {
        var byRoomKey = placements.ToLookup(p => p.Placement.RoomKey);

        // 同じ正規化キーの部屋が複数渡された場合に備え、キー単位で1件へ集約する（最初に現れた表示名を採用）。
        var orderedRooms = rooms
            .GroupBy(r => r.Key, StringComparer.Ordinal)
            .Select(g => g.First())
            .OrderBy(r => r.Key, StringComparer.Ordinal)
            .ToArray();

        var rowList = new List<InternalFinishRow>(orderedRooms.Length);
        var warnings = new List<FinishScheduleWarning>();

        foreach (var room in orderedRooms)
        {
            var roomPlacements = byRoomKey[room.Key].ToArray();
            var noteEntries = new List<(string Id, ProductResolution Resolution)>();
            var seenNoteIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            FinishScheduleCell CellFor(FinishColumn column, bool sortByDepth) =>
                BuildCell(
                    roomPlacements.Where(p => p.Placement.Column == column).ToArray(),
                    sortByDepth, noteEntries, seenNoteIds);

            var floorFinish = CellFor(FinishColumn.FloorFinish, sortByDepth: false);
            var floorSubstrate = CellFor(FinishColumn.FloorSubstrate, sortByDepth: true);
            var wallFinish = CellFor(FinishColumn.WallFinish, sortByDepth: false);
            var wallSubstrate = CellFor(FinishColumn.WallSubstrate, sortByDepth: true);
            var ceilingFinish = CellFor(FinishColumn.CeilingFinish, sortByDepth: false);
            var ceilingSubstrate = CellFor(FinishColumn.CeilingSubstrate, sortByDepth: true);

            string notes;
            if (roomPlacements.Length == 0)
            {
                notes = "仕上要素が見つかりませんでした。";
                warnings.Add(new FinishScheduleWarning(RoomNoFinishWarningCode, $"部屋「{room.DisplayName}」の仕上要素を1件も取得できませんでした。"));
            }
            else
            {
                notes = string.Join(Environment.NewLine, noteEntries.Select(FormatNote));
            }

            rowList.Add(new InternalFinishRow(
                room.DisplayName, floorFinish, floorSubstrate, wallFinish, wallSubstrate, ceilingFinish, ceilingSubstrate, notes));
        }

        // 部屋集計表由来の部屋一覧に無いRoomKeyへ紐づく配置(壁のst_部屋名等がスケジュールのどの行とも
        // 一致しない場合)は、行として出力しない(部屋一覧はあくまで部屋集計表が定義する)。
        // ただし出力から静かに消えると利用者は欠落に気づけないため、キーごとに1件警告を出す。
        var scheduleRoomKeys = new HashSet<string>(orderedRooms.Select(r => r.Key), StringComparer.Ordinal);
        foreach (var group in byRoomKey)
        {
            if (scheduleRoomKeys.Contains(group.Key))
            {
                continue;
            }

            var unmatched = group.ToArray();
            var displayName = unmatched[0].Placement.RoomName;
            warnings.Add(new FinishScheduleWarning(
                RoomNotInScheduleWarningCode,
                $"部屋「{displayName}」(キー:{group.Key})は部屋集計表に見つからないため、{unmatched.Length}件の配置を出力から除外しました。"));
        }

        return (rowList, warnings);
    }

    // --- 外部仕上表 ---

    private static (IReadOnlyList<ExternalFinishRow> Rows, IReadOnlyList<FinishScheduleWarning> Warnings) BuildExternalRows(
        IReadOnlyList<ResolvedProductPlacement> placements)
    {
        var rows = new List<ExternalFinishRow>();
        var warnings = new List<FinishScheduleWarning>();

        var groups = placements
            .GroupBy(p => (p.Placement.Category, p.Placement.FamilyName, p.Placement.TypeName))
            .OrderBy(g => g.Key.Category, StringComparer.Ordinal)
            .ThenBy(g => g.Key.FamilyName, StringComparer.Ordinal)
            .ThenBy(g => g.Key.TypeName, StringComparer.Ordinal);

        foreach (var group in groups)
        {
            var byElement = group.GroupBy(p => p.Placement.ElementId).ToArray();

            // 要素ごとの(Column, ProductId)構成を「署名」として、同じ署名を持つ要素をまとめる。
            var buckets = new List<(HashSet<(FinishColumn Column, string ProductId)> Signature, List<IGrouping<long, ResolvedProductPlacement>> Elements)>();
            foreach (var element in byElement)
            {
                var signature = new HashSet<(FinishColumn, string)>(element.Select(p => (p.Placement.Column, p.Placement.ProductId)));
                var bucket = buckets.FirstOrDefault(b => b.Signature.SetEquals(signature));
                if (bucket.Elements is null)
                {
                    buckets.Add((signature, new List<IGrouping<long, ResolvedProductPlacement>> { element }));
                }
                else
                {
                    bucket.Elements.Add(element);
                }
            }

            if (buckets.Count > 1)
            {
                warnings.Add(new FinishScheduleWarning(
                    ExternalTypeMismatchWarningCode,
                    $"{group.Key.Category}/{group.Key.FamilyName}/{group.Key.TypeName} でProduct_ID構成が一致しない要素があります。"));
            }

            foreach (var bucket in buckets)
            {
                var bucketPlacements = bucket.Elements.SelectMany(e => e).ToArray();
                var elementCount = bucket.Elements.Count;
                rows.Add(BuildExternalRow(group.Key.Category, group.Key.FamilyName, group.Key.TypeName, bucketPlacements, elementCount));
            }
        }

        return (rows, warnings);
    }

    private static ExternalFinishRow BuildExternalRow(
        string category, string familyName, string typeName, IReadOnlyList<ResolvedProductPlacement> placements, int elementCount)
    {
        var noteEntries = new List<(string Id, ProductResolution Resolution)>();
        var seenNoteIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var finish = BuildCell(
            placements.Where(p => p.Placement.Column == FinishColumn.ExteriorFinish).ToArray(),
            sortByDepth: false, noteEntries, seenNoteIds);
        var substrate = BuildCell(
            placements.Where(p => p.Placement.Column == FinishColumn.ExteriorSubstrate).ToArray(),
            sortByDepth: true, noteEntries, seenNoteIds);

        var notes = string.Join(Environment.NewLine, noteEntries.Select(FormatNote));
        return new ExternalFinishRow(category, familyName, typeName, finish, substrate, elementCount, notes);
    }

    // --- 共通: セル組み立て ---

    /// <summary>
    /// セルへ入るResolvedProductPlacement一式から、解決済み一般名称だけを重複排除・整列してセルを組み立てます。
    /// 未解決(ProductMissing/GeneralNameMissing)は、備考へ出すためnoteEntriesへ追記します(同じProduct_IDは1回だけ)。
    /// </summary>
    private static FinishScheduleCell BuildCell(
        IReadOnlyList<ResolvedProductPlacement> cellPlacements,
        bool sortByDepth,
        List<(string Id, ProductResolution Resolution)> noteEntries,
        HashSet<string> seenNoteIds)
    {
        var resolved = cellPlacements.Where(p => p.Resolution == ProductResolution.Resolved).ToArray();

        string[] names;
        if (sortByDepth)
        {
            names = resolved
                .GroupBy(p => p.GeneralName, StringComparer.Ordinal)
                .Select(g => (Name: g.Key, MinDepth: g.Min(x => x.Placement.Depth)))
                .OrderBy(x => x.MinDepth)
                .ThenBy(x => x.Name, StringComparer.Ordinal)
                .Select(x => x.Name)
                .ToArray();
        }
        else
        {
            names = resolved
                .Select(p => p.GeneralName)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(n => n, StringComparer.Ordinal)
                .ToArray();
        }

        var highlightMissing = cellPlacements.Any(p => p.Resolution != ProductResolution.Resolved);

        foreach (var unresolved in cellPlacements.Where(p => p.Resolution != ProductResolution.Resolved))
        {
            var id = unresolved.Placement.ProductId;
            if (seenNoteIds.Add(id))
            {
                noteEntries.Add((id, unresolved.Resolution));
            }
        }

        return new FinishScheduleCell(string.Join(Environment.NewLine, names), highlightMissing);
    }

    private static string FormatNote((string Id, ProductResolution Resolution) note) =>
        note.Resolution == ProductResolution.GeneralNameMissing
            ? $"{GeneralNameMissingNotePrefix}{note.Id}"
            : $"{ProductMissingNotePrefix}{note.Id}";
}
