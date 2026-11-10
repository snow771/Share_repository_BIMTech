// このファイルの目的:
//   新規登録の画面が、メーカー名や建材分類を「選択肢」として出せるようにします。
//   一覧に無い名前を本当に登録したいときだけ、マスタ表へ1行足せるようにします。
//
// なぜ必要か:
//   Products.csv が持つのは manufacturer_id と material_category_id です。
//   名称からIDを決められない値は書けば必ず参照切れになるので、書き込み前に断っています。
//   ところが画面は自由入力の欄だったため、利用者は全部入力し終えてから
//   「Manufacturers.csvに登録されていません」と断られ、進む道がありませんでした。
//   選べるようにして誤字を防ぎ、本当に新しいときだけ明示的に足せるようにします。
//
// 修正時の影響:
//   AddValueAsync はマスタ表（顧客の実データ）へ書きます。
//   呼び出し側は、後続の処理が失敗したら RemoveValueAsync で必ず取り消してください。
//   取り消さないと、実体の無いメーカーがマスタに残ります。

namespace Bui.Data;

/// <summary>名称からIDを引くマスタ表の種類です。</summary>
public enum ProductMasterKind
{
    /// <summary>Manufacturers.csv（メーカー名）</summary>
    Manufacturer,

    /// <summary>Material_Categories.csv（建材分類）</summary>
    MaterialCategory
}

/// <summary>マスタ表の1行分です。画面には Name だけを出します。</summary>
public sealed record ProductMasterValue(string Id, string Name);

/// <summary>マスタ表の値を読み、必要なら1行足せる窓口です。</summary>
public interface IProductMasterSource
{
    /// <summary>選択肢に出す一覧です。読めない場合は空を返し、例外は投げません。</summary>
    Task<IReadOnlyList<ProductMasterValue>> GetValuesAsync(ProductMasterKind kind, CancellationToken cancellationToken);

    /// <summary>
    /// マスタ表へ1行足し、採番したIDを返します。
    /// 同じ名称が既にある場合は足さず、その既存IDを返します（二重登録を作らないため）。
    /// </summary>
    Task<string> AddValueAsync(ProductMasterKind kind, string name, CancellationToken cancellationToken);

    /// <summary>AddValueAsync で足した行を取り消します。後続処理が失敗したときに使います。</summary>
    Task RemoveValueAsync(ProductMasterKind kind, string id, CancellationToken cancellationToken);
}

/// <summary>
/// 既存IDの並びから、採番の接頭辞と桁数を読み取ります。
/// 顧客ごとにIDの付け方が違うため（MFR-0001 / M001 / MAKER_12 など）、
/// 設定に固定値を持たせず、実データの多数派に合わせます。
/// </summary>
public static class MasterIdPattern
{
    /// <summary>該当する形式が1つも無いときに使う桁数です。</summary>
    public const int FallbackDigits = 4;

    /// <param name="fallbackPrefix">既存IDから形式を読み取れない場合に使う接頭辞。</param>
    public static (string Prefix, int Digits) Derive(IEnumerable<string> existingIds, string fallbackPrefix)
    {
        ArgumentNullException.ThrowIfNull(existingIds);
        ArgumentException.ThrowIfNullOrWhiteSpace(fallbackPrefix);

        var counts = new Dictionary<(string Prefix, int Digits), int>();
        foreach (var raw in existingIds)
        {
            var id = (raw ?? string.Empty).Trim();
            if (id.Length == 0) continue;

            var digits = 0;
            while (digits < id.Length && char.IsAsciiDigit(id[id.Length - 1 - digits])) digits++;
            // 数字が無い、全部数字、18桁を超える、のいずれもProductIdAllocatorが扱えません。
            if (digits == 0 || digits == id.Length || digits > 18) continue;

            var key = (id[..(id.Length - digits)], digits);
            counts[key] = counts.GetValueOrDefault(key) + 1;
        }

        if (counts.Count == 0) return (fallbackPrefix, FallbackDigits);

        // 多数派を採ります。同数なら接頭辞の順序で決めて、実行のたびに変わらないようにします。
        return counts
            .OrderByDescending(pair => pair.Value)
            .ThenBy(pair => pair.Key.Prefix, StringComparer.Ordinal)
            .ThenBy(pair => pair.Key.Digits)
            .First()
            .Key;
    }
}
