// このファイルは既存Assembly_IDを確認し、設定可能な接頭辞・桁数で次の候補を作るために存在します。
// CSVから取得したID一覧に依存し、接頭辞または桁数の判定を変えると旧形式IDが誤って新規採番へ混入します。
using System.Globalization;

namespace Bui.Assemblies;

/// <summary>既存IDと衝突しない、連番のAssembly_ID候補を作成します。</summary>
public static class AssemblyIdAllocator
{
    /// <summary>完全一致する接頭辞と数字桁数を持つIDだけを対象に、次の連番を返します。</summary>
    public static string Next(IEnumerable<string> existingIds, string prefix, int digits)
    {
        ArgumentNullException.ThrowIfNull(existingIds);
        ArgumentException.ThrowIfNullOrWhiteSpace(prefix);
        if (digits is < 1 or > 18)
        {
            throw new ArgumentOutOfRangeException(nameof(digits), "桁数は1から18の範囲で指定してください。");
        }

        long maximum = 0;
        foreach (var existingId in existingIds)
        {
            // 旧IDや見た目だけ似たIDを含めると連番の意味が崩れるため、接頭辞は大文字小文字も含めて完全一致にします。
            if (existingId is null || !existingId.StartsWith(prefix, StringComparison.Ordinal))
            {
                continue;
            }

            var numericPart = existingId[prefix.Length..];
            if (numericPart.Length != digits || numericPart.Any(character => character is < '0' or > '9'))
            {
                continue;
            }

            if (long.TryParse(numericPart, NumberStyles.None, CultureInfo.InvariantCulture, out var value))
            {
                maximum = Math.Max(maximum, value);
            }
        }

        var next = checked(maximum + 1);
        var limit = Pow10(digits) - 1;
        // 桁あふれを別形式IDへ黙って拡張せず、利用者が設定を見直せるよう明示的に停止します。
        if (next > limit)
        {
            throw new InvalidOperationException("指定したAssembly_IDの桁数では次の番号を割り当てられません。");
        }

        return prefix + next.ToString($"D{digits}", CultureInfo.InvariantCulture);
    }

    private static long Pow10(int digits)
    {
        long value = 1;
        for (var index = 0; index < digits; index++)
        {
            value = checked(value * 10);
        }

        return value;
    }
}
