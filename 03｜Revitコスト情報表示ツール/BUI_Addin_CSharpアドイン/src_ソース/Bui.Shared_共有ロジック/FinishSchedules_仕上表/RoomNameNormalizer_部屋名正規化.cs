using System.Text;
using System.Text.RegularExpressions;

namespace Bui.FinishSchedules;

public static class RoomNameNormalizer
{
    /// <summary>
    /// 末尾の「半角スペース+数字」（部屋番号サフィックス）を取り除くパターンです。
    /// Normalize()が既に全角→半角変換・空白統一・大文字化を済ませたKeyに対してだけ使うため、
    /// 半角スペースと半角数字だけを見れば足ります。
    /// </summary>
    private static readonly Regex TrailingRoomNumberPattern = new(@" [0-9]+$", RegexOptions.Compiled);

    /// <summary>
    /// Normalize()と同じ表示名を保ちつつ、照合キーからさらに末尾の部屋番号
    /// （例:「トイレ 021」の「 021」）を取り除きます。
    ///
    /// なぜ必要か: このプロジェクトの壁等のst_部屋名（内側／外側）パラメーターは
    /// 「名前 部屋番号」（半角スペース区切り、番号は常に末尾）の形式で入力される運用です。
    /// 一方、部屋集計表の「名前」列は番号を含まない名前（例:「トイレ」）だけで、
    /// 利用者は意図的に「トイレ」「洗面所」のように部屋番号単位ではなく名前単位で
    /// 仕上表をまとめたいと明言しています（同じ仕様の部屋が番号ごとに別行へ分かれると
    /// 困るため）。Normalize()自体は「部屋番号を削除しない」という別の契約
    /// （RoomNameNormalizerTests.部屋番号は削除しない）を保つ必要があるため、
    /// この照合専用の挙動は別メソッドとして追加し、Normalize()は変更しません。
    ///
    /// 部屋集計表由来の名前には番号サフィックスが無いため、このメソッドを両方の
    /// 突合対象（部屋集計表側・壁等のパラメーター側）へ一貫して使っても、
    /// 部屋集計表側は素通り（no-op）になるだけで副作用はありません。
    /// </summary>
    public static NormalizedRoomName NormalizeForRoomMatching(string value)
    {
        var normalized = Normalize(value);
        var strippedKey = TrailingRoomNumberPattern.Replace(normalized.Key, string.Empty);
        return normalized with { Key = strippedKey };
    }

    public static NormalizedRoomName Normalize(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var displayName = value.Trim();

        // Step 1: Convert full-width space (U+3000) to regular space
        var normalized = displayName.Replace('　', ' ');

        // Step 2: Replace multiple spaces with single space
        normalized = Regex.Replace(normalized, @"\s+", " ");

        // Step 3: Convert full-width digits (FF10-FF19) to ASCII (0-9)
        normalized = ConvertFullWidthDigits(normalized);

        // Step 4: Convert full-width letters (FF21-FF3A, FF41-FF5A) to ASCII
        normalized = ConvertFullWidthLetters(normalized);

        // Step 5: Convert to uppercase
        var key = normalized.ToUpperInvariant();

        return new NormalizedRoomName(displayName, key);
    }

    private static string ConvertFullWidthDigits(string text)
    {
        var result = new StringBuilder();
        foreach (var c in text)
        {
            if (c >= '０' && c <= '９')
            {
                result.Append((char)('0' + (c - '０')));
            }
            else
            {
                result.Append(c);
            }
        }
        return result.ToString();
    }

    private static string ConvertFullWidthLetters(string text)
    {
        var result = new StringBuilder();
        foreach (var c in text)
        {
            if (c >= 'Ａ' && c <= 'Ｚ')
            {
                // Full-width uppercase A-Z to ASCII uppercase
                result.Append((char)('A' + (c - 'Ａ')));
            }
            else if (c >= 'ａ' && c <= 'ｚ')
            {
                // Full-width lowercase a-z to ASCII lowercase
                result.Append((char)('a' + (c - 'ａ')));
            }
            else
            {
                result.Append(c);
            }
        }
        return result.ToString();
    }
}
