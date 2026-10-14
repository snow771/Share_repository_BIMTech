using SimilaritySearchApp.Models;

namespace SimilaritySearchApp.Services;

/// <summary>
/// テーブル同士の関連を求める（DB編集画面の関連図用）。
///   1. DB に外部キー制約があればそれを使う（実線）。
///   2. 制約が無くても、同じ意味のキー列（例: Work_Type_Code と Work_Type_Code_工種コード）を
///      持つテーブル同士は関連があると推定する（点線）。ER 図の多くの関連は、まだ制約として登録されていないため。
/// 推定の向き（親/子）は、ER 図に合わせた「キー列の持ち主テーブル」表から決める。
/// </summary>
public static class RelationInferenceService
{
    public const string Parent = "parent";
    public const string Child = "child";
    public const string Peer = "peer";

    /// <summary>
    /// キー列 → そのキーの「持ち主（親）」テーブルの英字名。先に書いたものが優先（存在するテーブルだけが対象）。
    /// ER 図（E-R図_CostDisply.txt）の参照関係に合わせている。
    /// </summary>
    private static readonly Dictionary<string, string[]> KnownOwners = new(StringComparer.OrdinalIgnoreCase)
    {
        ["project_id"] = new[] { "ProjectInformation" },
        ["work_type_code"] = new[] { "Category" },
        ["category_code"] = new[] { "CategoryGroup", "Category" },
        ["term"] = new[] { "Term" },
        ["rep_year"] = new[] { "Index" },
        ["index_allocation"] = new[] { "IndexType", "Index_Allocation" },
        ["material_code"] = new[] { "StandardSpec" },
        ["supplier_id"] = new[] { "SupplierMaster" },
    };

    /// <summary>関連のキーとみなす固有名（_id / code で終わらないもの）</summary>
    private static readonly HashSet<string> ExtraKeys = new(StringComparer.OrdinalIgnoreCase)
        { "term", "rep_year", "index_allocation" };

    /// <summary>
    /// 名前の英字部分（最初の「_＋日本語」より前）。
    /// 例: MaterialMaster_建築材料マスタ → MaterialMaster、Work_Type_Code_工種コード → Work_Type_Code
    /// </summary>
    public static string BaseName(string name)
    {
        for (var i = 1; i < name.Length; i++)
        {
            if (name[i - 1] == '_' && name[i] > 0x7F) return name[..(i - 1)];
        }
        return name;
    }

    /// <summary>日本語部分（無ければ空）。例: MaterialMaster_建築材料マスタ → 建築材料マスタ</summary>
    public static string JapaneseLabel(string name)
    {
        var b = BaseName(name);
        return b.Length < name.Length ? name[(b.Length + 1)..] : "";
    }

    /// <summary>関連推定用に正規化した列名（英字部分を小文字に）。</summary>
    public static string KeyOf(string column) => BaseName(column).Trim('_').ToLowerInvariant();

    /// <summary>テーブル間を結ぶキー列らしいか（xxx_id / xxx_code / term など）。単なる "id" は除く。</summary>
    public static bool IsJoinKey(string key) =>
        key != "id" && (key.EndsWith("_id", StringComparison.Ordinal) || key.EndsWith("_code", StringComparison.Ordinal) || ExtraKeys.Contains(key));

    public static DbNeighborhood Build(
        DbTableInfo center,
        IReadOnlyList<DbTableInfo> tables,
        IReadOnlyList<ColumnRef> columns,
        IReadOnlyList<ForeignKeyRef> foreignKeys)
    {
        var byFull = tables.ToDictionary(t => t.FullName, StringComparer.OrdinalIgnoreCase);
        var found = new Dictionary<string, (string Direction, bool IsFk, List<string> Links)>(StringComparer.OrdinalIgnoreCase);
        var fkPairs = new HashSet<(string Table, string Key)>();

        void Add(string full, string direction, bool isFk, string link)
        {
            if (!found.TryGetValue(full, out var cur))
            {
                found[full] = (direction, isFk, new List<string> { link });
                return;
            }
            if (!cur.Links.Contains(link)) cur.Links.Add(link);
            // 制約による関連を推定より優先。向きは最初に決まったものを使う（制約があればそれ）
            if (isFk && !cur.IsFk) found[full] = (direction, true, cur.Links);
        }

        // ── 1. 外部キー制約 ──
        foreach (var fk in foreignKeys)
        {
            var from = $"{fk.FromSchema}.{fk.FromTable}";
            var to = $"{fk.ToSchema}.{fk.ToTable}";
            if (from.Equals(to, StringComparison.OrdinalIgnoreCase)) continue;
            if (from.Equals(center.FullName, StringComparison.OrdinalIgnoreCase))
            {
                Add(to, Parent, true, $"{fk.FromColumn} → {fk.ToColumn}");
                fkPairs.Add((to, KeyOf(fk.FromColumn)));
            }
            else if (to.Equals(center.FullName, StringComparison.OrdinalIgnoreCase))
            {
                Add(from, Child, true, $"{fk.FromColumn} → {fk.ToColumn}");
                fkPairs.Add((from, KeyOf(fk.FromColumn)));
            }
        }

        // ── 2. 同名キー列からの推定 ──
        var colsByTable = columns
            .GroupBy(c => $"{c.Schema}.{c.Table}", StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

        if (colsByTable.TryGetValue(center.FullName, out var centerCols))
        {
            foreach (var cc in centerCols)
            {
                var key = KeyOf(cc.Column);
                if (!IsJoinKey(key)) continue;
                var owner = OwnerOf(key, tables);

                foreach (var (full, otherCols) in colsByTable)
                {
                    if (full.Equals(center.FullName, StringComparison.OrdinalIgnoreCase) || !byFull.ContainsKey(full)) continue;
                    var oc = otherCols.FirstOrDefault(c => KeyOf(c.Column) == key);
                    if (oc is null || fkPairs.Contains((full, key))) continue;

                    var other = byFull[full];
                    string direction;
                    if (owner is not null && owner.FullName.Equals(full, StringComparison.OrdinalIgnoreCase)) direction = Parent;
                    else if (owner is not null && owner.FullName.Equals(center.FullName, StringComparison.OrdinalIgnoreCase)) direction = Child;
                    else if (owner is not null) continue;            // 持ち主が別にいるキー（兄弟同士）は持ち主経由で見せる
                    else if (oc.IsPrimaryKey && !cc.IsPrimaryKey) direction = Parent;
                    else if (cc.IsPrimaryKey && !oc.IsPrimaryKey) direction = Child;
                    else direction = Peer;

                    Add(other.FullName, direction, false, $"{cc.Column} ⇄ {oc.Column}");
                }
            }
        }

        var order = new Dictionary<string, int> { [Parent] = 0, [Peer] = 1, [Child] = 2 };
        var neighbors = found
            .Where(kv => byFull.ContainsKey(kv.Key))
            .Select(kv => new DbNeighbor(byFull[kv.Key], kv.Value.Direction, kv.Value.IsFk, kv.Value.Links))
            .OrderBy(n => order[n.Direction])
            .ThenBy(n => n.Table.Name, StringComparer.Ordinal)
            .ToList();
        return new DbNeighborhood(center, neighbors);
    }

    /// <summary>キー列の持ち主テーブル（表にあり、DB に存在する最初のもの）。無ければ null。</summary>
    private static DbTableInfo? OwnerOf(string key, IReadOnlyList<DbTableInfo> tables)
    {
        if (KnownOwners.TryGetValue(key, out var owners))
        {
            foreach (var o in owners)
            {
                var t = tables.FirstOrDefault(x => x.BaseName.Equals(o, StringComparison.OrdinalIgnoreCase));
                if (t is not null) return t;
            }
        }
        // xxx_id は、英字名が xxx のテーブルが持ち主（例: ocrestimateheader_id → OcrEstimateHeader）
        if (key.EndsWith("_id", StringComparison.Ordinal))
        {
            var stem = key[..^3];
            return tables.FirstOrDefault(x => x.BaseName.Equals(stem, StringComparison.OrdinalIgnoreCase)
                                           || x.BaseName.Replace("_", "").Equals(stem.Replace("_", ""), StringComparison.OrdinalIgnoreCase));
        }
        return null;
    }
}
