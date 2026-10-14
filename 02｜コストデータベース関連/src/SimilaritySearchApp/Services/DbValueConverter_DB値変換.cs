using System.Data;
using System.Globalization;
using System.Text;
using SimilaritySearchApp.Models;

namespace SimilaritySearchApp.Services;

/// <summary>
/// DB 値 ⇔ 画面の文字列 の変換（DB編集画面用）。
/// Format は「読み直して Parse すると元と同じ値になる」書式にしてあり、保存時に行を特定するキーとしても使う。
/// </summary>
public static class DbValueConverter
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    private static readonly CultureInfo Ja = CultureInfo.GetCultureInfo("ja-JP");

    private static readonly HashSet<string> BinaryTypes = new(StringComparer.OrdinalIgnoreCase)
        { "binary", "varbinary", "image", "timestamp", "rowversion" };

    /// <summary>= で比較できない型（主キーが無い表の行特定から外す）</summary>
    private static readonly HashSet<string> NonComparableTypes = new(StringComparer.OrdinalIgnoreCase)
        { "text", "ntext", "image", "xml", "geography", "geometry", "hierarchyid", "sql_variant", "timestamp", "rowversion" };

    private static readonly HashSet<string> SupportedTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "bit", "tinyint", "smallint", "int", "bigint", "decimal", "numeric", "money", "smallmoney", "float", "real",
        "date", "datetime", "datetime2", "smalldatetime", "datetimeoffset", "time",
        "char", "varchar", "nchar", "nvarchar", "text", "ntext", "uniqueidentifier",
    };

    public static bool IsBinary(string typeName) => BinaryTypes.Contains(typeName);

    public static bool IsComparable(string typeName) => !NonComparableTypes.Contains(typeName) && !IsBinary(typeName);

    /// <summary>画面から編集できる型か</summary>
    public static bool IsSupported(string typeName) => SupportedTypes.Contains(typeName);

    /// <summary>DB 値 → 画面の文字列。NULL は null。</summary>
    public static string? Format(object? value)
    {
        switch (value)
        {
            case null:
            case DBNull:
                return null;
            case DateTime dt:
                return dt.TimeOfDay == TimeSpan.Zero
                    ? dt.ToString("yyyy-MM-dd", Inv)
                    : dt.ToString("yyyy-MM-dd HH:mm:ss.FFFFFFF", Inv).TrimEnd('.');
            case DateTimeOffset dto:
                return dto.ToString("yyyy-MM-dd HH:mm:ss.FFFFFFF", Inv).TrimEnd('.') + dto.ToString(" zzz", Inv);
            case TimeSpan ts:
                return ts.ToString("c", Inv);
            case double d:
                return d.ToString("R", Inv);
            case float f:
                return f.ToString("R", Inv);
            case decimal m:
                return m.ToString("G29", Inv);   // 末尾の 0 を落とす（100.0000 → 100）。値は変わらない
            case bool b:
                return b ? "1" : "0";
            case byte[] bytes:
                return $"(バイナリ {bytes.Length:N0} bytes)";
            case IFormattable fmt:
                return fmt.ToString(null, Inv);
            default:
                return value.ToString();
        }
    }

    /// <summary>
    /// 画面の文字列 → DB に渡す値。空文字は NULL（NULL 不可の文字列列は空文字）。
    /// 変換できないときは FormatException（日本語メッセージ）。
    /// </summary>
    public static object Parse(string? text, DbColumnInfo col)
    {
        var type = col.TypeName.ToLowerInvariant();
        var isText = type is "char" or "varchar" or "nchar" or "nvarchar" or "text" or "ntext";

        if (string.IsNullOrEmpty(text) || (!isText && string.IsNullOrWhiteSpace(text)))
        {
            if (col.IsNullable) return DBNull.Value;
            if (isText) return "";
            throw new FormatException($"「{col.Name}」は必須です（空にできません）。");
        }

        if (isText)
        {
            if (col.MaxLength is int max && max > 0 && text.Length > max)
                throw new FormatException($"「{col.Name}」は {max} 文字までです（入力 {text.Length} 文字）。");
            return text;
        }

        // 全角数字・全角記号を半角に（日本語入力のまま打っても通るように）
        var s = text.Normalize(NormalizationForm.FormKC).Trim();
        try
        {
            switch (type)
            {
                case "bit":
                    return s.ToLowerInvariant() switch
                    {
                        "1" or "true" or "はい" or "yes" or "on" => true,
                        "0" or "false" or "いいえ" or "no" or "off" => false,
                        _ => throw new FormatException(),
                    };
                case "tinyint":
                    return byte.Parse(Num(s), NumberStyles.Integer, Inv);
                case "smallint":
                    return short.Parse(Num(s), NumberStyles.Integer, Inv);
                case "int":
                    return int.Parse(Num(s), NumberStyles.Integer, Inv);
                case "bigint":
                    return long.Parse(Num(s), NumberStyles.Integer, Inv);
                case "decimal" or "numeric" or "money" or "smallmoney":
                    return decimal.Parse(Num(s), NumberStyles.Number | NumberStyles.AllowExponent, Inv);
                case "float":
                    return double.Parse(Num(s), NumberStyles.Float, Inv);
                case "real":
                    return float.Parse(Num(s), NumberStyles.Float, Inv);
                case "date" or "datetime" or "datetime2" or "smalldatetime":
                    return ParseDate(s);
                case "datetimeoffset":
                    return DateTimeOffset.Parse(s, Inv);
                case "time":
                    return TimeSpan.Parse(s, Inv);
                case "uniqueidentifier":
                    return Guid.Parse(s);
            }
        }
        catch (Exception e) when (e is FormatException or OverflowException)
        {
            throw new FormatException($"「{col.Name}」に「{text}」は入力できません（型: {col.TypeName}）。");
        }
        throw new FormatException($"「{col.Name}」（型: {col.TypeName}）は画面から編集できません。");
    }

    public static SqlDbType SqlType(string typeName) => typeName.ToLowerInvariant() switch
    {
        "bit" => SqlDbType.Bit,
        "tinyint" => SqlDbType.TinyInt,
        "smallint" => SqlDbType.SmallInt,
        "int" => SqlDbType.Int,
        "bigint" => SqlDbType.BigInt,
        "decimal" or "numeric" => SqlDbType.Decimal,
        "money" => SqlDbType.Money,
        "smallmoney" => SqlDbType.SmallMoney,
        "float" => SqlDbType.Float,
        "real" => SqlDbType.Real,
        "date" => SqlDbType.Date,
        "datetime" => SqlDbType.DateTime,
        "datetime2" => SqlDbType.DateTime2,
        "smalldatetime" => SqlDbType.SmallDateTime,
        "datetimeoffset" => SqlDbType.DateTimeOffset,
        "time" => SqlDbType.Time,
        "char" => SqlDbType.Char,
        "varchar" => SqlDbType.VarChar,
        "nchar" => SqlDbType.NChar,
        "text" => SqlDbType.Text,
        "ntext" => SqlDbType.NText,
        "uniqueidentifier" => SqlDbType.UniqueIdentifier,
        _ => SqlDbType.NVarChar,
    };

    // ------------------------------------------------------------------

    private static string Num(string s) => s.Replace(",", "").Replace("¥", "").Replace("\\", "").Trim();

    private static DateTime ParseDate(string s)
    {
        string[] formats =
        {
            "yyyy-MM-dd", "yyyy/MM/dd", "yyyy/M/d", "yyyy-M-d", "yyyyMMdd",
            "yyyy-MM-dd HH:mm:ss.FFFFFFF", "yyyy-MM-dd HH:mm:ss", "yyyy-MM-dd HH:mm",
            "yyyy/MM/dd HH:mm:ss", "yyyy/M/d H:mm:ss", "yyyy/M/d H:mm", "yyyy-MM-ddTHH:mm:ss.FFFFFFF",
        };
        if (DateTime.TryParseExact(s, formats, Inv, DateTimeStyles.None, out var d)) return d;
        if (DateTime.TryParse(s, Ja, DateTimeStyles.None, out d)) return d;   // 「2024年4月1日」など
        throw new FormatException();
    }
}
